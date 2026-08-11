using System;
using System.Linq;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Content.Objects;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Drawing.Layout;

/// <summary>
///   <see cref="XTextSegmentFormatter"/> lays out runs of differing font and colour as one flow of
///   text. It is what stands in this library for PDFKit's <c>continued</c> option, which exists so
///   that the styling can be changed in the middle of a paragraph: there the runs are chained by
///   calling <c>text</c> again, here they are handed over together.
///   <para>
///   The class shipped without a test of any kind. These are here because the parity checklist
///   closes <c>continued</c> on the claim that this covers it, and a claim like that is worth no
///   more than what holds it up.
///   </para>
/// </summary>
public class XTextSegmentFormatterTests
{
    static XFont Plain => new XFont("Arial", 12, XFontStyle.Regular, XPdfFontOptions.WinAnsiDefault);
    static XFont Bold => new XFont("Arial", 12, XFontStyle.Bold, XPdfFontOptions.WinAnsiDefault);

    static PdfPage PageShowing(params TextSegment[] segments)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            new XTextSegmentFormatter(gfx).DrawString(segments, new XRect(20, 20, 220, 200));
        return page;
    }

    static TextSegment Segment(string text, XFont font, XBrush brush)
    {
        return new TextSegment { Text = text, Font = font, Brush = brush };
    }

    [Fact]
    public void TwoSegmentsOfDifferentStyleRunOnAsOneLine()
    {
        var page = PageShowing(
            Segment("Hello", Plain, XBrushes.Black),
            Segment("world", Bold, XBrushes.Red));

        var runs = TextBaselines.PositionsOf(page);

        // The second run picks up where the first left off rather than starting a line of its own.
        // That is the whole of what continued is for.
        runs.Should().HaveCountGreaterThanOrEqualTo(2);
        runs[1].Y.Should().BeApproximately(runs[0].Y, 0.01);
        runs[1].X.Should().BeGreaterThan(runs[0].X);
    }

    [Fact]
    public void EachSegmentIsDrawnInItsOwnColour()
    {
        var page = PageShowing(
            Segment("Hello", Plain, XBrushes.Black),
            Segment("world", Bold, XBrushes.Red));

        // rg is the non-stroking colour, which is what a brush sets.
        var colours = TextOperators.OperandsGivenTo(page, OpCodeName.rg);

        colours.Should().ContainEquivalentOf(new[] { 0d, 0d, 0d });
        colours.Should().ContainEquivalentOf(new[] { 1d, 0d, 0d });
    }

    [Fact]
    public void SegmentsWrapTogetherRatherThanEachToItself()
    {
        var page = PageShowing(
            Segment("The quick brown fox jumps over", Plain, XBrushes.Black),
            Segment("the lazy dog and keeps on going", Bold, XBrushes.Black));

        // One flow of text, so the wrapping happens where the words run out of room and not at the
        // seam between the two segments. A formatter that started each segment on a line of its own
        // would also give more than one line, so the seam is what has to be checked: the run that
        // opens the second segment must sit on a line that already has something on it.
        var runs = TextBaselines.PositionsOf(page);
        TextBaselines.LinesOf(page).Count.Should().BeGreaterThan(1);

        var lineOfEachRun = runs.Select(run => Math.Round(run.Y, 3)).ToList();
        lineOfEachRun.Distinct().Count().Should().BeLessThan(runs.Count,
            "some line must carry more than one run, or every segment began a line of its own");
    }

    [Fact]
    public void ASingleSegmentLaysOutLikePlainText()
    {
        var page = PageShowing(Segment("The quick brown fox jumps over the lazy dog", Plain, XBrushes.Black));

        TextBaselines.PositionsOf(page).Should().NotBeEmpty();
    }
}
