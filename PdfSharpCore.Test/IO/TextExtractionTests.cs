using System;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Extraction;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Test.Helpers;
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
    public void ARunUnderAScaledTransformIsAsWideAsItLooks()
    {
        // The width and the size have to be measured through the same matrix. Measuring the width
        // through the text matrix alone leaves the run reported in text space while its size is
        // reported in user space, and a test that only translates cannot tell, because translating
        // scales by one.
        var page = Reopen(Draw(gfx =>
        {
            gfx.ScaleTransform(2);
            gfx.DrawString("Wide", Font, XBrushes.Black, 20, 50);
        }));

        var run = PdfTextExtractor.ExtractRuns(page).Single();
        var unscaled = Measure("Wide");

        run.FontSize.Should().BeApproximately(24, 0.5);
        run.Width.Should().BeApproximately(unscaled * 2, unscaled * 0.1);
    }

    [Fact]
    public void ADestinationOfMoreThanOneCharacterDoesNotAbortExtraction()
    {
        // A /ToUnicode destination is a string of UTF-16 code units, not a scalar. Reading it as one
        // number and converting threw for anything longer than a single unit — a ligature such as
        // <00660069> for "fi" reads as 6684777 — and the exception came out of extraction rather
        // than out of the map, taking the whole page with it.
        var page = Reopen(WithToUnicode(Draw(gfx => gfx.DrawString("Hello", Font, XBrushes.Black, 40, 100)),
            cmap => cmap + "\n1 beginbfrange\n<F000> <F001> <00660069>\nendbfrange\n"));

        PdfTextExtractor.ExtractText(page).Should().Be("Hello");
    }

    [Fact]
    public void AnArrayOfDestinationsDoesNotShiftTheEntriesAfterIt()
    {
        // The array form — <lo> <hi> [<d1> <d2> …] — was documented as not read. It was not skipped
        // either: collecting every hexadecimal string in the block and stepping through them three
        // at a time swallows the array's elements into the same stream and shifts everything after
        // it, so the real entries below map the wrong codes to the wrong text.
        var page = Reopen(WithToUnicode(Draw(gfx => gfx.DrawString("Hello", Font, XBrushes.Black, 40, 100)),
            // Matched without a line break, because the CMap is written with StreamWriter.WriteLine
            // and so carries whatever newline the machine that wrote it uses. Matching "\n" made
            // this pass by doing nothing on Windows.
            cmap => cmap.Replace("beginbfrange", "beginbfrange\n<F000> <F002> [<0041> <0042> <0043>]")));

        PdfTextExtractor.ExtractText(page).Should().Be("Hello");
    }

    [Fact]
    public void TheSpacingOperandsOfAQuotedShowAreNotReadAsKerning()
    {
        // " takes aw ac string. Showing from operand zero reads the two spacing numbers as though
        // they were the kerning adjustments of a TJ array, so they are applied twice — once as the
        // spacing they are, and once as a displacement they are not.
        var page = Reopen(WithTwoShows());

        var runs = PdfTextExtractor.ExtractRuns(page);

        // The same string, the same spacing, shown two ways. Whatever the number is, it is the same
        // number.
        runs[runs.Count - 1].Width.Should().BeApproximately(runs[runs.Count - 2].Width, 0.01);
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

    /// <summary>
    ///   Rewrites the font's <c>/ToUnicode</c> map, so that a CMap this library would never write
    ///   can still be put in front of the reader that has to cope with one.
    /// </summary>
    private static byte[] WithToUnicode(byte[] bytes, Func<string, string> rewrite)
    {
        var document = Reader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        var map = FontOf(document.Pages[0]).Elements.GetDictionary("/ToUnicode");

        map.Stream.TryUnfilter();
        var original = Encoding.Latin1.GetString(map.Stream.Value);
        var replaced = Encoding.Latin1.GetBytes(rewrite(original));

        // So that a rewrite matching nothing fails here rather than leaving a test that passes
        // because it tested nothing.
        Encoding.Latin1.GetString(replaced).Should().NotBe(original,
            "the rewrite has to have changed the map for the test to be testing anything");

        map.Stream.Value = replaced;
        map.Elements.Remove("/Filter");
        map.Elements.SetInteger("/Length", replaced.Length);

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    /// <summary>
    ///   A page showing the same string twice with the same spacing, once through <c>Tj</c> with the
    ///   spacing set by <c>Tw</c> and <c>Tc</c>, and once through <c>"</c>, which carries it.
    /// </summary>
    private static byte[] WithTwoShows()
    {
        var document = Reader.Open(new MemoryStream(
            Draw(gfx => gfx.DrawString("Quoted", Font, XBrushes.Black, 40, 100))), PdfDocumentOpenMode.Modify);

        var page = document.Pages[0];
        var content = Encoding.Latin1.GetString(PageContent.Of(page));

        var font = page.Elements.GetDictionary("/Resources").Elements.GetDictionary("/Font")
            .Elements.KeyNames[0].Value;
        var shown = content.Substring(content.IndexOf('<') + 1,
            content.IndexOf('>') - content.IndexOf('<') - 1);

        // Spacing large enough that reading it twice cannot be mistaken for rounding.
        var added = content
            + "\nBT " + font + " 12 Tf 0 Tw -3000 Tc 1 0 0 1 40 700 Tm <" + shown + "> Tj ET\n"
            + "BT " + font + " 12 Tf 1 0 0 1 40 650 Tm 0 -3000 <" + shown + "> \" ET\n";

        var bytes = Encoding.Latin1.GetBytes(added);
        var stream = (PdfDictionary)page.Elements.GetValue("/Contents");
        stream.Stream.Value = bytes;
        stream.Elements.Remove("/Filter");
        stream.Elements.SetInteger("/Length", bytes.Length);

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static PdfDictionary FontOf(PdfPage page)
    {
        var fonts = page.Elements.GetDictionary("/Resources").Elements.GetDictionary("/Font");
        return fonts.Elements.GetDictionary(fonts.Elements.KeyNames[0].Value);
    }
}
