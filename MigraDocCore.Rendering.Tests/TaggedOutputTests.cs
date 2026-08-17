using System;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using MigraDocCore.DocumentObjectModel.Shapes;
using MigraDocCore.DocumentObjectModel.Shapes.Charts;
using MigraDocCore.DocumentObjectModel.Tables;
using MigraDocCore.Rendering.Tests.Helpers;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
// The content-stream readers are linked in from the other test project and keep their namespace.
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace MigraDocCore.Rendering.Tests;

/// <summary>
///   MigraDoc knows what it is drawing — a <c>Paragraph</c> whose style is <c>Heading1</c>, a
///   <c>Row</c> flagged as a heading, an <c>Image</c> — and used to throw all of it away on the way
///   to the page, where a heading and a caption are both a <c>Tj</c>. These are the assertions that
///   it now keeps it.
/// </summary>
/// <remarks>
///   Stage A gave the library a hand-driven tagging API, which is plumbing rather than a feature:
///   almost nobody tags a document by hand, so almost no document was accessible. This is Stage B,
///   where the renderer does it, and Stage C, where a document may claim PDF/UA and be held to it.
///   <para>
///   The tree rather than the content stream, because the tree is what a screen reader walks.
///   </para>
/// </remarks>
public class TaggedOutputTests
{
    [Fact]
    public void AParagraphIsAParagraphAndAHeadingIsAHeading()
    {
        var document = new Document();
        var section = document.AddSection();
        section.AddParagraph("Invoice", "Heading1");
        section.AddParagraph("Amounts are in pounds sterling.");

        var tree = Structure.Of(document);

        tree.Tag.Should().Be("Document");
        tree.Single("Sect").ChildTags().Should().Equal("H1", "P");
    }

    [Theory]
    [InlineData("Heading1", "H1")]
    [InlineData("Heading2", "H2")]
    [InlineData("Heading3", "H3")]
    [InlineData("Heading4", "H4")]
    [InlineData("Heading5", "H5")]
    // MigraDoc has nine heading levels and PDF has six, so the last four land on the deepest one
    // PDF has. A heading too deep to name exactly is still a heading, and calling it a paragraph
    // would lose more than calling it an H6 does.
    [InlineData("Heading6", "H6")]
    [InlineData("Heading7", "H6")]
    [InlineData("Heading9", "H6")]
    public void AHeadingIsTaggedAtItsOwnLevel(string style, string expected)
    {
        var document = new Document();
        document.AddSection().AddParagraph("Heading", style);

        Structure.Of(document).Single("Sect").ChildTags().Should().Equal(expected);
    }

    [Fact]
    public void ATableIsRowsAndCellsAndItsHeadingRowSaysWhichWayItReaches()
    {
        var document = new Document();
        var table = document.AddSection().AddTable();
        table.Summary = "Charges for the period, by service.";
        table.AddColumn("6cm");
        table.AddColumn("3cm");

        var heading = table.AddRow();
        heading.HeadingFormat = true;
        heading.Cells[0].AddParagraph("Service");
        heading.Cells[1].AddParagraph("Amount");

        var row = table.AddRow();
        row.Cells[0].AddParagraph("Support");
        row.Cells[1].AddParagraph("49.20");

        var tagged = Structure.Of(document).Single("Table");

        tagged.Summary.Should().Be("Charges for the period, by service.");
        tagged.ChildTags().Should().Equal("TR", "TR");
        tagged.Children[0].ChildTags().Should().Equal("TH", "TH");
        tagged.Children[1].ChildTags().Should().Equal("TD", "TD");

        // Without the scope a reader cannot say which heading a cell answers to, and a table reads
        // as a stream of values with nothing to attach them to.
        tagged.Children[0].Children.Should().AllSatisfy(cell => cell.Scope.Should().Be("Column"));
    }

    [Fact]
    public void AMergedCellSaysHowFarItReaches()
    {
        var document = new Document();
        var table = document.AddSection().AddTable();
        table.AddColumn("4cm");
        table.AddColumn("4cm");
        table.AddColumn("4cm");

        var row = table.AddRow();
        row.Cells[0].MergeRight = 2;
        row.Cells[0].AddParagraph("Everything");

        table.AddRow().Cells[0].AddParagraph("One");

        // Three columns and one cell. Without the span a reader would put "Everything" in the first
        // column and expect two more that are not there.
        var merged = Structure.Of(document).Single("Table").Children[0].Children[0];
        merged.ColumnSpan.Should().Be(3);
    }

    [Fact]
    public void AListIsAListAndItsBulletIsALabel()
    {
        var document = new Document();
        var section = document.AddSection();
        foreach (var item in new[] { "First", "Second", "Third" })
            section.AddParagraph(item).Format.ListInfo.ListType = ListType.BulletList1;

        var list = Structure.Of(document).Single("L");

        list.ChildTags().Should().Equal("LI", "LI", "LI");
        list.Children[0].ChildTags().Should().Equal("Lbl", "LBody");

        // The bullet is a label and not part of the sentence. Read as one, every item begins with
        // the word the bullet is pronounced as.
        list.Children[0].Single("Lbl").MarkCount.Should().Be(1);
    }

    [Fact]
    public void ARunOfListItemsIsOneListAndAChangeOfKindStartsAnother()
    {
        var document = new Document();
        var section = document.AddSection();

        section.AddParagraph("First").Format.ListInfo.ListType = ListType.BulletList1;
        section.AddParagraph("Second").Format.ListInfo.ListType = ListType.BulletList1;
        section.AddParagraph("Interrupted.");
        section.AddParagraph("Third").Format.ListInfo.ListType = ListType.NumberList1;

        var section1 = Structure.Of(document).Single("Sect");

        section1.ChildTags().Should().Equal("L", "P", "L");
        section1.Children[0].Children.Should().HaveCount(2);
        section1.Children[2].Children.Should().HaveCount(1);
    }

    [Fact]
    public void AnImageWithAlternativeTextIsAFigureThatSaysWhatItShows()
    {
        var document = new Document();
        var image = document.AddSection().AddImage(AnImage());
        image.Width = "3cm";
        image.AlternativeText = "The company logo.";

        var figure = Structure.Of(document).Single("Figure");
        figure.AlternateText.Should().Be("The company logo.");
    }

    [Fact]
    public void AnImageWithNothingToSayIsDrawnAsDecorationRatherThanAsAFigureWithNothingToSay()
    {
        var document = new Document();
        document.AddSection().AddImage(AnImage()).Width = "3cm";

        // Not a Figure with an empty /Alt, which announces to a reader that something is there and
        // then cannot say what — leaving them knowing only that they have missed something. An
        // artifact is passed over in silence, which for an undescribed image is the honest answer.
        Structure.Of(document).OfTag("Figure").Should().BeEmpty();
    }

    [Fact]
    public void AChartIsAFigureToo()
    {
        var document = new Document();
        var chart = document.AddSection().AddChart(ChartType.Column2D);
        chart.Width = "8cm";
        chart.Height = "5cm";
        chart.SeriesCollection.AddSeries().Add(3.0, 5.0, 4.0);
        chart.AlternativeText = "Sales by quarter, rising through the year.";

        // For the same reason an image is. Axis labels and data labels read out in drawing order say
        // nothing about the shape they describe, so the description is the whole of what a reader
        // who cannot see it gets.
        Structure.Of(document).Single("Figure").AlternateText
            .Should().Be("Sales by quarter, rising through the year.");
    }

    [Fact]
    public void AHyperlinkIsALinkThatReachesItsAnnotation()
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph("See ");
        paragraph.AddHyperlink("https://example.org/terms", HyperlinkType.Web)
            .AddText("the terms");
        paragraph.AddText(".");

        var link = Structure.Of(document).Single("Link");

        // Both halves. The marks are the words a reader announces as the link; the object reference
        // is the annotation, which is content as much as anything drawn is and is otherwise reachable
        // only by hit-testing rectangles.
        link.MarkCount.Should().BeGreaterThan(0);
        link.AnnotationCount.Should().Be(1);
    }

    [Fact]
    public void AWordBrokenAtAHyphenSaysWhatItReallyIs()
    {
        var tree = Structure.Of(Hyphenated());
        var span = tree.Single("Span");

        // The page says "demon-" and "strate". The word is neither, and /ActualText is the only
        // thing that can say so — without it a reader announces two fragments, a search for the
        // word fails, and copying the paragraph out pastes the hyphen the typesetter added.
        span.ActualText.Should().Be("demonstrate");

        // Two runs of marks, one per line, on the one element. Not one sequence spanning the break:
        // a marked-content sequence cannot cross a page boundary, and this has to work when the
        // break happens to be one.
        span.MarkCount.Should().Be(2);
    }

    [Fact]
    public void AHyphenThatBreaksNothingIsNotTaggedAtAll()
    {
        var document = new Document();
        var section = document.AddSection();

        // The same word, with room for it. MigraDoc draws no hyphen when it does not need one, so
        // there is no word to put back together and nothing to say about it.
        section.AddParagraph("A demon­strate of it.");

        Structure.Of(document).OfTag("Span").Should().BeEmpty();
    }

    [Fact]
    public void AWordBrokenAcrossAPageIsStillOneWord()
    {
        var document = new Document();
        var section = document.AddSection();

        // Both dimensions, because FlattenPageSetup overwrites whichever one is set on its own with
        // the document default — so a page sized by width alone is silently still A4. Room for one
        // line and no more, so the break at the hyphen is also the break between pages.
        section.PageSetup.PageWidth = "21cm";
        section.PageSetup.PageHeight = "2.5cm";
        section.PageSetup.TopMargin = "1cm";
        section.PageSetup.BottomMargin = "1cm";

        var paragraph = section.AddParagraph("In demon­strate");
        paragraph.Format.RightIndent = "14.2cm";
        paragraph.Format.WidowControl = false;

        var rendered = Rendered.Of(document);
        rendered.PageCount.Should().Be(2, "the arrangement is only interesting if it splits");

        // The hard case, and the reason the element is keyed by the soft hyphen rather than by
        // anything belonging to a renderer: the two halves are drawn by two different renderers, on
        // two different pages, into two different content streams. One element, two runs of marks,
        // one word.
        var span = Structure.RootOf(rendered).Single("Span");
        span.ActualText.Should().Be("demonstrate");
        span.MarkCount.Should().Be(2);
    }

    [Fact]
    public void SayingWhatTheWordIsDoesNotChangeWhatIsDrawn()
    {
        var tagged = Glyphs.On(Rendered.FirstPageOf(Hyphenated()));
        var plain = Glyphs.On(Untagged(Hyphenated()).Pages[0]);

        // /ActualText changes what the word is read as. The page still shows "demon-" and "strate",
        // hyphen and all, because that is what the typesetter decided and none of this is about
        // typesetting.
        tagged.Should().Equal(plain);
    }

    [Fact]
    public void EveryMarkedContentSequenceOnAPageIsClosed()
    {
        var document = new Document();
        var section = document.AddSection();
        section.Headers.Primary.AddParagraph("Running head");
        section.AddParagraph("Statement", "Heading1");

        // Everything that opens a scope, on one page and interleaved: a heading, a list, a link
        // inside a paragraph, a word broken at a hyphen, and a table whose cells hold paragraphs.
        foreach (var item in new[] { "First", "Second" })
            section.AddParagraph(item).Format.ListInfo.ListType = ListType.NumberList1;

        var paragraph = section.AddParagraph("See ");
        paragraph.AddHyperlink("https://example.org", HyperlinkType.Web).AddText("the terms");
        paragraph.AddText(" for demon­strate of it");
        paragraph.Format.RightIndent = "14.2cm";

        var table = section.AddTable();
        table.AddColumn("4cm");
        table.AddRow().Cells[0].AddParagraph("Cell");

        var content = Encoding.ASCII.GetString(PageContent.Of(Rendered.Of(document).Pages[0]));

        // An unbalanced BDC does not fail loudly — it corrupts every mark after it on the page, and
        // the page goes on drawing perfectly while the tree describes the wrong things.
        var opened = Occurrences(content, "BDC") + Occurrences(content, "BMC");
        opened.Should().BeGreaterThan(0, "the arrangement is only interesting if it marks anything");
        opened.Should().Be(Occurrences(content, "EMC"));
    }

    static int Occurrences(string content, string token)
    {
        var found = 0;
        for (var at = content.IndexOf(token, StringComparison.Ordinal); at >= 0;
             at = content.IndexOf(token, at + token.Length, StringComparison.Ordinal))
            found++;

        return found;
    }

    [Fact]
    public void ARunningHeadIsFurnitureAndNotSomethingToReadOut()
    {
        var document = new Document();
        var section = document.AddSection();
        section.AddParagraph("Body text.");
        section.Headers.Primary.AddParagraph("Quarterly report");
        section.Footers.Primary.AddParagraph("Page 1");

        var tree = Structure.Of(document);

        // Nothing from the header or the footer is anywhere in the tree — they are drawn inside an
        // artifact scope instead. A page number read out between every paragraph is worse than no
        // page number.
        tree.Descendants().Should().HaveCount(3, "Document, Sect and the one paragraph");
    }

    [Fact]
    public void APageThatDrawsNothingIsStillInTheTree()
    {
        var document = new Document();
        var section = document.AddSection();
        section.AddParagraph("First page.");
        section.AddPageBreak();
        section.AddPageBreak();
        section.AddParagraph("Third page.");

        var rendered = Rendered.Of(document);

        // Every page, including the blank one. A page with no /StructParents cannot be told apart
        // from one imported out of an untagged document, and a validator is right to refuse both.
        for (var page = 0; page < rendered.PageCount; page++)
        {
            rendered.Pages[page].Elements.ContainsKey("/StructParents")
                .Should().BeTrue("page " + (page + 1) + " has to be in the tree");
        }
    }

    [Fact]
    public void EveryTaggedPageOrdersItsTabsByStructure()
    {
        var document = new Document();
        document.AddSection().AddParagraph("Body text.");

        var rendered = Rendered.Of(document);
        rendered.Pages[0].Elements.GetName("/Tabs").Should().Be("/S");
    }

    [Fact]
    public void ADocumentThatAsksNotToBeTaggedIsWrittenAsItAlwaysWas()
    {
        var document = new Document();
        document.AddSection().AddParagraph("Body text.");

        var renderer = new PdfDocumentRenderer(true) { Document = document, TagContent = false };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        stream.Position = 0;

        var saved = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        saved.Internals.Catalog.Elements.ContainsKey("/StructTreeRoot").Should().BeFalse();
        saved.Internals.Catalog.Elements.ContainsKey("/MarkInfo").Should().BeFalse();
        saved.Pages[0].Elements.ContainsKey("/StructParents").Should().BeFalse();
    }

    [Fact]
    public void TheDocumentSaysWhatLanguageItIsIn()
    {
        var document = new Document();
        document.AddSection().AddParagraph("Body text.");

        var renderer = new PdfDocumentRenderer(true) { Document = document, Language = "en-GB" };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        stream.Position = 0;

        PdfReader.Open(stream, PdfDocumentOpenMode.Modify)
            .Internals.Catalog.Elements.GetString("/Lang").Should().Be("en-GB");
    }

    /// <summary>
    ///   A document narrow enough that "demonstrate" cannot finish the line it starts on, so
    ///   MigraDoc breaks it at the soft hyphen in the middle of it.
    /// </summary>
    /// <remarks>
    ///   Narrowed with an indent rather than with a page size, which is both what the other
    ///   soft-hyphen tests do and the only one of the two that works: <c>PageSetup.PageWidth</c> on
    ///   its own leaves the page A4 and the measure unchanged, so a document set up that way breaks
    ///   nothing and the test passes for the wrong reason.
    /// </remarks>
    static Document Hyphenated()
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph("In demon­strate");

        // 16cm of measure less 14.2cm leaves 1.8cm: room for "In demon-" and not for
        // "In demonstrate".
        paragraph.Format.RightIndent = "14.2cm";
        return document;
    }

    /// <summary>The same document rendered with tagging off.</summary>
    static PdfDocument Untagged(Document document)
    {
        var renderer = new PdfDocumentRenderer(true) { Document = document, TagContent = false };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        stream.Position = 0;
        return PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
    }

    /// <summary>
    ///   An image the backend will really decode, because an image that fails to load is drawn as a
    ///   grey placeholder and would answer these tests without ever having been an image.
    /// </summary>
    static ImageSource.IImageSource AnImage() =>
        ImageSource.FromFile(Path.Combine(AppContext.BaseDirectory, "Assets", "lenna.png"));
}
