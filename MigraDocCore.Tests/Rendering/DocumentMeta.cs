using MigraDocCore.DocumentObjectModel;
using MigraDocCore.Rendering;
using Xunit;

namespace MigraDocCore.Tests.Rendering;

public class DocumentMeta
{
    [Fact]
    public void CanRenderMetaData()
    {
        var document = new Document();
        document.Info.Author = "A Author";
        document.Info.Subject = "Document Subject";
        document.Info.Title = "Document Title";
        document.Info.Keywords = "keyword1 keyword2 anotherkeyword";

        var sec = document.Sections.AddSection();

        sec.PageSetup = new PageSetup
            { StartingNumber = 1, BottomMargin = Unit.FromMillimeter(2), PageFormat = PageFormat.A4, LeftMargin = Unit.FromCentimeter(1) };

        sec.AddParagraph("A paragraph before.");

        var printer = new PdfDocumentRenderer
        {
            Document = document,
            Language = "English",
        };

        printer.RenderDocument();
        printer.PdfDocument.Save($"migradoc-test-{nameof(CanRenderMetaData)}.pdf");
    }
}