using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Extraction;
using PdfSharpCore.Pdf.IO;
using Xunit;

// This namespace has a PdfReader of its own, so the one that opens documents needs saying in full.
using Reader = PdfSharpCore.Pdf.IO.PdfReader;

namespace PdfSharpCore.Test.IO;

/// <summary>
///   The content-stream parser has been here all along — <c>CParser</c>, <c>ContentReader</c> and a
///   full operator table — and nothing turned what it produced into text. So a library that wrote
///   PDFs could not read back a word of its own output, and anyone needing that reached for a second
///   library.
///
///   These tests round-trip: draw text, save, reopen, extract. That is the strongest check available
///   here, because a font embedded as Identity-H writes glyph indices rather than characters, and
///   getting the text back means the <c>/ToUnicode</c> map this library writes was read correctly by
///   the map reader — two independent pieces agreeing.
/// </summary>
public class TextExtractionTests
{
    [Fact]
    public void TextDrawnOnAPageIsReadBack()
    {
        var page = Reopen(Draw(gfx => gfx.DrawString("Hello world", Font, XBrushes.Black, 40, 100)));

        PdfTextExtractor.ExtractText(page).Should().Be("Hello world");
    }

    [Fact]
    public void EachRunSaysWhereItsBaselineStarts()
    {
        var page = Reopen(Draw(gfx => gfx.DrawString("Positioned", Font, XBrushes.Black, 72, 200)));

        var run = PdfTextExtractor.ExtractRuns(page).Single();

        run.Origin.X.Should().BeApproximately(72, 1);

        // User space has its origin at the bottom-left and Y growing upwards, while DrawString was
        // given a Y measured down from the top. On an A4 page 200 down is 842 - 200 up.
        run.Origin.Y.Should().BeApproximately(842 - 200, 1);
    }

    [Fact]
    public void ARunKnowsTheSizeItWasDrawnAt()
    {
        var page = Reopen(Draw(gfx => gfx.DrawString("Large", new XFont("Arial", 24), XBrushes.Black, 40, 100)));

        PdfTextExtractor.ExtractRuns(page).Single().FontSize.Should().BeApproximately(24, 0.5);
    }

    [Fact]
    public void ARunIsAsWideAsTheTextItHolds()
    {
        var page = Reopen(Draw(gfx => gfx.DrawString("Wide enough to measure", Font, XBrushes.Black, 40, 100)));

        var run = PdfTextExtractor.ExtractRuns(page).Single();

        // Compared against what the library itself says the string measures, so this asserts the
        // extractor's width arithmetic against the writer's rather than against a number typed in.
        var expected = Measure("Wide enough to measure");
        run.Width.Should().BeApproximately(expected, expected * 0.05);
    }

    [Fact]
    public void TextOnSeparateBaselinesComesBackOnSeparateLines()
    {
        var page = Reopen(Draw(gfx =>
        {
            gfx.DrawString("First line", Font, XBrushes.Black, 40, 100);
            gfx.DrawString("Second line", Font, XBrushes.Black, 40, 130);
        }));

        PdfTextExtractor.ExtractText(page).Should().Be("First line\nSecond line");
    }

    [Fact]
    public void TwoRunsOnOneBaselineAreOneLine()
    {
        var page = Reopen(Draw(gfx =>
        {
            gfx.DrawString("Left", Font, XBrushes.Black, 40, 100);
            gfx.DrawString("Right", Font, XBrushes.Black, 300, 100);
        }));

        var text = PdfTextExtractor.ExtractText(page);

        text.Should().NotContain("\n");
        text.Should().StartWith("Left").And.EndWith("Right");
        text.Should().Contain(" ", "a gap the pen jumped is a space, even though none was drawn");
    }

    [Fact]
    public void AccentedTextSurvivesTheRoundTrip()
    {
        // The point of reading /ToUnicode rather than guessing: these are not code points the glyph
        // indices bear any relation to.
        var page = Reopen(Draw(gfx => gfx.DrawString("Ångström café", Font, XBrushes.Black, 40, 100)));

        PdfTextExtractor.ExtractText(page).Should().Be("Ångström café");
    }

    [Fact]
    public void TextIsReadBackFromEveryPage()
    {
        var document = new PdfDocument();
        foreach (var word in new[] { "One", "Two", "Three" })
        {
            var gfx = XGraphics.FromPdfPage(document.AddPage());
            gfx.DrawString(word, Font, XBrushes.Black, 40, 100);
            gfx.Dispose();
        }

        var reopened = Reopen(Save(document));

        reopened.Owner.Pages.Cast<PdfPage>().Select(PdfTextExtractor.ExtractText)
            .Should().Equal("One", "Two", "Three");
    }

    [Fact]
    public void APageWithNoTextYieldsNoRuns()
    {
        var page = Reopen(Draw(gfx => gfx.DrawRectangle(XBrushes.LightGray, 10, 10, 100, 50)));

        PdfTextExtractor.ExtractRuns(page).Should().BeEmpty();
    }

    [Fact]
    public void TextDrawnUnderATransformIsReportedWhereItLanded()
    {
        // The extractor has to keep the current transformation matrix as well as the text matrix,
        // or everything drawn inside a translated container is reported at the wrong place.
        var page = Reopen(Draw(gfx =>
        {
            gfx.TranslateTransform(100, 50);
            gfx.DrawString("Shifted", Font, XBrushes.Black, 0, 100);
        }));

        var run = PdfTextExtractor.ExtractRuns(page).Single();

        run.Origin.X.Should().BeApproximately(100, 1);
        run.Origin.Y.Should().BeApproximately(842 - 150, 1);
    }

    [Fact]
    public void ARunNamesTheFontResourceItWasDrawnWith()
    {
        var page = Reopen(Draw(gfx => gfx.DrawString("Named", Font, XBrushes.Black, 40, 100)));

        PdfTextExtractor.ExtractRuns(page).Single().FontName.Should().StartWith("/F");
    }

    [Fact]
    public void RunsComeBackInTheOrderTheyWereDrawn()
    {
        var page = Reopen(Draw(gfx =>
        {
            gfx.DrawString("Third", Font, XBrushes.Black, 40, 300);
            gfx.DrawString("First", Font, XBrushes.Black, 40, 100);
            gfx.DrawString("Second", Font, XBrushes.Black, 40, 200);
        }));

        // Drawing order, not reading order. Saying so is the honest contract: sorting these into
        // the order a person would read them is layout analysis and is not what this does.
        PdfTextExtractor.ExtractRuns(page).Select(run => run.Text)
            .Should().Equal("Third", "First", "Second");
    }

    [Fact]
    public void ExtractingFromNothingIsRefusedRatherThanReturningNothing()
    {
        var extracting = () => PdfTextExtractor.ExtractRuns(null);

        extracting.Should().Throw<ArgumentNullException>();
    }

    private static XFont Font => new("Arial", 12);

    private static double Measure(string text)
    {
        var document = new PdfDocument();
        var gfx = XGraphics.FromPdfPage(document.AddPage());
        return gfx.MeasureString(text, Font).Width;
    }

    private static byte[] Draw(Action<XGraphics> draw)
    {
        var document = new PdfDocument();
        var gfx = XGraphics.FromPdfPage(document.AddPage());
        draw(gfx);
        gfx.Dispose();
        return Save(document);
    }

    private static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static PdfPage Reopen(byte[] bytes)
    {
        var saved = new MemoryStream(bytes);
        return Reader.Open(saved, PdfDocumentOpenMode.Modify).Pages[0];
    }
}
