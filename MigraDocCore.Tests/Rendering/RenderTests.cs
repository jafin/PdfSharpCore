using MigraDocCore.DocumentObjectModel;
using MigraDocCore.Rendering;
using Xunit;

namespace MigraDocCore.Tests.Rendering;

public class RenderTests
{
    [Fact]
    public void RenderStyles()
    {
        var document = new Document();
        var sec = document.Sections.AddSection();
        var style = document.Styles.AddStyle("Foo", "Normal");
        style.Font.Color = Color.Parse("#002AFF");

        var para = sec.AddParagraph("A paragraph before.");
        para.Style = "Foo";
        sec.AddParagraph("A Paragraph afterwards");
        var printer = new PdfDocumentRenderer
        {
            Document = document
        };
        printer.RenderDocument();
        printer.PdfDocument.Save($"migradoc-test-{nameof(RenderStyles)}.pdf");
    }

    [Fact]
    public void RenderFootnote()
    {
        var document = new Document();
        var sec = document.Sections.AddSection();
        var para = sec.AddParagraph("A paragraph before.");
        sec.AddParagraph("A Paragraph afterwards");
        var footNote = para.AddFootnote("This is a footnote");
        footNote.Reference = "X";
        footNote.AddParagraph("Foot");

        var printer = new PdfDocumentRenderer
        {
            Document = document
        };
        printer.RenderDocument();
        printer.PdfDocument.Save($"migradoc-test-{nameof(RenderFootnote)}.pdf");
    }

    [Fact]
    public void RenderHyperlink()
    {
        var document = new Document();

        Section section = document.AddSection();

        section.AddPageBreak();
        Paragraph paragraph = section.AddParagraph("Table of Contents");
        paragraph.Format.Font.Size = 14;
        paragraph.Format.Font.Bold = true;
        paragraph.Format.SpaceAfter = 24;
        paragraph.Format.OutlineLevel = OutlineLevel.Level1;

        paragraph = section.AddParagraph();
        paragraph.Style = "TOC";
        Hyperlink hyperlink = paragraph.AddHyperlink("Paragraphs");
        hyperlink.AddText("Paragraphs\t");
        hyperlink.AddPageRefField("Paragraphs");

        paragraph = section.AddParagraph();
        paragraph.Style = "TOC";
        hyperlink = paragraph.AddHyperlink("Tables");
        hyperlink.AddText("Tables\t");
        hyperlink.AddPageRefField("Tables");

        paragraph = section.AddParagraph();
        paragraph.Style = "TOC";
        hyperlink = paragraph.AddHyperlink("Charts");
        hyperlink.AddText("Charts\t");
        hyperlink.AddPageRefField("Charts");


        var sec = document.Sections.AddSection();
        var para = sec.AddParagraph("Paragraphs");
        para.AddBookmark("Paragraphs");
        var secTables = sec.AddParagraph("Tables");
        secTables.AddBookmark("Tables");

        var secCharts = sec.AddParagraph("Charts");
        secCharts.AddBookmark("Charts");

        sec.AddParagraph("A Paragraph afterwards");
        var hyper = para.AddHyperlink("https://www.google.com", HyperlinkType.Web);
        var txt = hyper.AddText("Visit Google");
        hyper.Font.Underline = Underline.Dash;

        var printer = new PdfDocumentRenderer
        {
            Document = document
        };
        printer.RenderDocument();
        printer.PdfDocument.Save($"migradoc-test-{nameof(RenderHyperlink)}.pdf");
    }


    [Fact]
    public void RenderHeaderFooter()
    {
        var document = new Document();
        var sec = document.Sections.AddSection();
        sec.PageSetup.OddAndEvenPagesHeaderFooter = true;
        sec.PageSetup.StartingNumber = 1;

        var header = sec.Headers.Primary;
        header.AddParagraph("Page header");

        var footer = sec.Footers.Primary;
        footer.AddParagraph("Page footer");

        var para = sec.AddParagraph("A paragraph before.");
        var printer = new PdfDocumentRenderer
        {
            Document = document
        };
        printer.RenderDocument();
        printer.PdfDocument.Save($"migradoc-test-{nameof(RenderHeaderFooter)}.pdf");
    }
}