using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Pdf;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Drawing.Layout;

/// <summary>
///   How far right-to-left text gets up the layout engine, now that <c>DrawString</c> reorders.
/// </summary>
/// <remarks>
///   <para>
///     Reordering lives in the renderer rather than in any layout engine, so a formatter that
///     hands a whole line to <c>DrawString</c> gets it right without having been changed at all,
///     and one that places each word itself does not. This class is where that line is drawn: what
///     works today, and the case that still does not.
///   </para>
/// </remarks>
public class BidirectionalLayoutTests
{
    const string ArabicFamily = "Noto Sans Arabic";

    // Three Arabic words - "one two three" would be misleading, so: three short words of two
    // letters each, kept as escapes so that a source file mixing right-to-left text with
    // left-to-right code cannot be misread.
    const string First = "\u0645\u0646";
    const string Second = "\u0628\u0644";
    const string Third = "\u0642\u062F";

    static XFont Font()
    {
        PinnedFontResolver.Register(ArabicFamily, File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "NotoSansArabic-Regular.ttf")));

        return new XFont(ArabicFamily, 12);
    }

    static (PdfPage Page, XTextFormatter Formatter, XGraphics Graphics) Sheet()
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Size = PageSize.A6;
        var graphics = XGraphics.FromPdfPage(page);

        return (page, new XTextFormatter(graphics), graphics);
    }

    /// <summary>The glyph a single character draws as, for reading an order back.</summary>
    static int GlyphOf(char letter, XFont font)
        => DrawnText.Glyphs(DrawnText.Page(letter.ToString(), font)).Single();

    [Fact]
    public void AFormatterLineOfRightToLeftTextComesOutInVisualOrder()
    {
        var font = Font();
        var (page, formatter, graphics) = Sheet();

        using (graphics)
            formatter.DrawString(First + " " + Second, font, XBrushes.Black,
                new XRect(12, 12, 200, 50));

        // The formatter joins a line back into one string and draws it in one go, so the whole
        // line goes through the bidirectional algorithm together: the word written first ends up
        // rightmost, and its letters turn round inside it. The space between two right-to-left
        // words is right-to-left too, so the line reverses whole.
        DrawnText.Glyphs(page).Should().Equal(
            (First + " " + Second).Reverse().Select(letter => GlyphOf(letter, font)),
            "the line is reordered across the words in it and not only inside each of them");
    }

    [Fact]
    public void ARightToLeftLineIsTheReverseOfWhatItWouldHaveBeenDrawnAsBefore()
    {
        var font = Font();
        var (page, formatter, graphics) = Sheet();

        using (graphics)
            formatter.DrawString(First, font, XBrushes.Black, new XRect(12, 12, 200, 50));

        DrawnText.Glyphs(page).Should().Equal(
            First.Reverse().Select(letter => GlyphOf(letter, font)),
            "no shaper is registered, so the letters do not join - but they are in the order "
            + "they are read, which is all reordering has to do");
    }

    [Fact]
    public void JustifiedRightToLeftTextIsNotReorderedAcrossWordsYet()
    {
        // The one case the formatter does not get for free. Justifying places each word at an x
        // of its own and draws it on its own, so the words stay in the order they were written
        // while each of them is individually turned round. A right-to-left paragraph justified to
        // both margins therefore reads inside out.
        //
        // Fixing it means laying the words of a line out in visual order rather than logical, which
        // is a change to the formatter and not to the renderer, and it is written down in
        // docs/specs/text-shaping-and-bidi.md rather than fixed here. This test exists to fail
        // when somebody does fix it.
        var font = Font();
        var (page, formatter, graphics) = Sheet();

        formatter.Alignment = XParagraphAlignment.Justify;
        using (graphics)
            formatter.DrawString(First + " " + Second + " " + Third + " " + First + " " + Second,
                font, XBrushes.Black, new XRect(12, 12, 60, 100));

        var runs = DrawnText.GlyphRuns(page);

        runs.Should().HaveCountGreaterThan(1,
            "each word is drawn on its own, which is what stops the line being reordered as one");
        runs[0].Should().Equal(First.Reverse().Select(letter => GlyphOf(letter, font)),
            "the first word written is still drawn leftmost, though its own letters are the "
            + "right way round");
    }
}
