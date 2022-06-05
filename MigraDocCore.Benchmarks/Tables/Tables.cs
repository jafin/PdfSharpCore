using BenchmarkDotNet.Attributes;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Tables;
using MigraDocCore.Rendering;

namespace MyBenchmarks.Tables;

[HtmlExporter]
[MemoryDiagnoser]
public class Tables
{
    [Benchmark]
    public void BigTables()
    {
        Document doc = new Document();
        Section sec = doc.Sections.AddSection();
        sec.PageSetup.PageFormat = PageFormat.A4;
        sec.PageSetup.LeftMargin = Unit.FromMillimeter(5);
        sec.PageSetup.RightMargin = Unit.FromMillimeter(5);
        sec.PageSetup.BottomMargin = Unit.FromMillimeter(5);
        sec.PageSetup.TopMargin = Unit.FromMillimeter(5);
        var style = doc.Styles.AddStyle("TableFont", "Normal");
        style.Font.Size = Unit.FromPoint(8);

        var table = sec.AddTable();
        table.Borders.Visible = true;
        for (var colIdx = 1; colIdx <= 10; ++colIdx)
        {
            var col = table.AddColumn();
            col.Width = Unit.FromMillimeter(15);
        }

        //700ms for 1000, 32s for 10000
        for (int rowIdx = 1; rowIdx <= 10000; ++rowIdx)
        {
            AddRow(table);
        }

        var printer = new PdfDocumentRenderer
        {
            Document = doc
        };
        printer.RenderDocument();
        printer.PdfDocument.Save($"migradoc-test-{nameof(BigTables)}.pdf");
    }

    private void AddRow(Table table)
    {
        Row row = table.AddRow();
        row.HeightRule = RowHeightRule.Exactly;
        row.Height = 20;
        row.VerticalAlignment = VerticalAlignment.Center;
        for (int i = 0; i < 10; i++)
        {
            var para = row[i].AddParagraph($"Cell {i}");
            para.Style = "TableFont";
        }
    }
}