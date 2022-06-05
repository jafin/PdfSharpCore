using MigraDocCore.DocumentObjectModel;
using MigraDocCore.Rendering;
using Xunit;

namespace MigraDocCore.Tests.Rendering;

/// <summary>
/// Summary description for TestLayout.
/// </summary>
public class TestLayout
{
    [Fact]
    public static void TwoParagraphs()
    {
        var outputFile = "test-twoparagraphs.pdf";
        Document doc = new Document();
        Section sec = doc.Sections.AddSection();

        sec.PageSetup.TopMargin = 0;
        sec.PageSetup.BottomMargin = 0;

        Paragraph par1 = sec.AddParagraph();
        TestParagraphRenderer.FillFormattedParagraph(par1);
        TestParagraphRenderer.GiveBorders(par1);
        par1.Format.SpaceAfter = "2cm";
        par1.Format.SpaceBefore = "3cm";
        Paragraph par2 = sec.AddParagraph();
        TestParagraphRenderer.FillFormattedParagraph(par2);
        TestParagraphRenderer.GiveBorders(par2);
        par2.Format.SpaceBefore = "3cm";

        PdfDocumentRenderer printer = new PdfDocumentRenderer()
        {
            Document = doc
        };
        printer.RenderDocument();
        printer.PdfDocument.Save(outputFile);
    }

    [Fact]
    public static void A1000Paragraphs()
    {
        const string outputFile = "test-a1000Paragraphs.pdf";
        Document doc = new Document();
        Section sec = doc.Sections.AddSection();

        sec.PageSetup.TopMargin = 0;
        sec.PageSetup.BottomMargin = 0;

        for (int idx = 1; idx <= 1000; ++idx)
        {
            Paragraph par = sec.AddParagraph();
            par.AddText("Paragraph " + idx + ": ");
            TestParagraphRenderer.FillFormattedParagraph(par);
            TestParagraphRenderer.GiveBorders(par);
        }

        PdfDocumentRenderer printer = new PdfDocumentRenderer()
        {
            Document = doc
        };
        printer.RenderDocument();
        printer.PdfDocument.Save(outputFile);
    }
}