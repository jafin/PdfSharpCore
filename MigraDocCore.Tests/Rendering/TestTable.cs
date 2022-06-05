using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Tables;
using MigraDocCore.Tests.Rendering;
using Xunit;

namespace MigraDocCore.Rendering.Tests;

/// <summary>
/// Summary description for TestTable.
/// </summary>
public class TestTable
{
    [Fact]
    public static void Borders()
    {
        Document document = new Document();
        var sec = document.Sections.AddSection();
        sec.AddParagraph("A paragraph before.");
        Table table = sec.AddTable();
        table.Borders.Visible = true;
        table.AddColumn();
        table.AddColumn();
        table.Rows.HeightRule = RowHeightRule.Exactly;
        table.Rows.Height = 14;
        Row row = table.AddRow();
        Cell cell = row.Cells[0];
        cell.Borders.Visible = true;
        cell.Borders.Left.Width = 8;
        cell.Borders.Right.Width = 2;
        cell.AddParagraph("First Cell");

        row = table.AddRow();
        cell = row.Cells[1];
        cell.AddParagraph("Last Cell within this table");
        cell.Borders.Bottom.Width = 15;
        cell.Shading.Color = Colors.LightBlue;
        sec.AddParagraph("A Paragraph afterwards");
        PdfDocumentRenderer printer = new PdfDocumentRenderer
        {
            Document = document
        };
        printer.RenderDocument();
        printer.PdfDocument.Save("migradoc-test-borders.pdf");
    }

    [Fact]
    public static void CellMerge()
    {
        Document document = new Document();
        var sec = document.Sections.AddSection();
        sec.AddParagraph("A paragraph before.");
        Table table = sec.AddTable();
        table.Borders.Visible = true;
        table.AddColumn();
        table.AddColumn();
        Row row = table.AddRow();
        Cell cell = row.Cells[0];
        cell.MergeRight = 1;
        cell.Borders.Visible = true;
        cell.Borders.Left.Width = 8;
        cell.Borders.Right.Width = 2;
        cell.AddParagraph("First Cell");

        row = table.AddRow();
        cell = row.Cells[1];
        cell.AddParagraph("Last Cell within this row");
        cell.MergeDown = 1;
        cell.Borders.Bottom.Width = 15;
        cell.Borders.Right.Width = 30;
        cell.Shading.Color = Colors.LightBlue;
        row = table.AddRow();
        sec.AddParagraph("A Paragraph afterwards");
        PdfDocumentRenderer printer = new PdfDocumentRenderer()
        {
            Document = document
        };
        printer.RenderDocument();
        printer.PdfDocument.Save("migradoc-test-cellmerge.pdf");
    }

    [Fact]
    public static void VerticalAlign()
    {
        Document document = new Document();
        var sec = document.Sections.AddSection();
        sec.AddParagraph("A paragraph before.");
        Table table = sec.AddTable();
        table.Borders.Visible = true;
        table.AddColumn();
        table.AddColumn();
        Row row = table.AddRow();
        row.HeightRule = RowHeightRule.Exactly;
        row.Height = 70;
        row.VerticalAlignment = VerticalAlignment.Center;
        row[0].AddParagraph("First Cell");
        row[1].AddParagraph("Second Cell");
        sec.AddParagraph("A Paragraph afterwards.");

        PdfDocumentRenderer printer = new PdfDocumentRenderer()
        {
            Document = document
        };
        printer.RenderDocument();
        printer.PdfDocument.Save("migradoc-test-VerticalAlign.pdf");
    }


    [Fact]
    public void BigTables()
    {
        Document doc = new Document();
        Section sec = doc.Sections.AddSection();
        sec.PageSetup.PageFormat = PageFormat.A4;
        sec.PageSetup.LeftMargin = Unit.FromMillimeter(5);
        sec.PageSetup.RightMargin = Unit.FromMillimeter(5);
        sec.PageSetup.BottomMargin = Unit.FromMillimeter(5);
        sec.PageSetup.TopMargin = Unit.FromMillimeter(5);
        var style = doc.Styles.AddStyle("TableFont","Normal");
        style.Font.Size = Unit.FromPoint(8);

        var table = sec.AddTable();
        table.Borders.Visible = true;
        for (var colIdx = 1; colIdx <= 10; ++colIdx)
        {
            var col = table.AddColumn();
            col.Width = Unit.FromMillimeter(15);
        }

        for (int rowIdx = 1; rowIdx <= 1000; ++rowIdx)
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