using System;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Charting.Tests.Helpers;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Charting.Tests;

/// <summary>
///   The caption written alongside an axis.
/// </summary>
/// <remarks>
///   <c>AxisTitleRenderer</c> draws the caption twice over: upright, where it goes into a rectangle
///   with its alignment handed to a string format, and rotated, where it goes into a rectangle
///   centred on the origin and the surface is turned under it. The rotated path is the longer of
///   the two and is what a chart with a caption up its side takes, which is the ordinary case.
///
///   It serves the value axis only, despite its name. The category axis renderers draw their own
///   caption inline at the end of their Draw, reading neither the alignment nor the orientation -
///   so half the tests here are about which of the two ways a caption is drawn, and what is lost
///   by taking the other one.
///
///   The other half of the renderer is that a title takes room. It is measured during Format and
///   the axis subtracts what it measured from what is left for the plot area - so the surest sign
///   that a title was laid out at all is that the columns moved.
/// </remarks>
public class AxisTitleTests
{
    [Fact]
    public void ACaptionOnEitherAxisIsWrittenOnThePage()
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        chart.XAxis.Title.Caption = "Across";
        chart.YAxis.Title.Caption = "Up";

        ShownText.On(Drawn.Page(chart)).Should().Contain("Across").And.Contain("Up");
    }

    [Fact]
    public void ACaptionOnTheCategoryAxisTakesRoomFromThePlotArea()
    {
        var untitled = Charts.Of(ChartType.Column2D, 1.0, 3.0);

        var titled = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        titled.XAxis.Title.Caption = "Across";

        var withoutTitle = PaintedRectangles.FilledOn(Drawn.Page(untitled))[0];
        var withTitle = PaintedRectangles.FilledOn(Drawn.Page(titled))[0];

        // The title is written under the category labels, so the plot area loses height from the
        // bottom and the columns start higher up the page and are shorter.
        withTitle.Y.Should().BeGreaterThan(withoutTitle.Y);
        withTitle.Height.Should().BeLessThan(withoutTitle.Height);
    }

    [Fact]
    public void ACaptionOnTheValueAxisTakesRoomFromThePlotArea()
    {
        var untitled = Charts.Of(ChartType.Column2D, 1.0, 3.0);

        var titled = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        titled.YAxis.Title.Caption = "Up";

        var withoutTitle = PaintedRectangles.FilledOn(Drawn.Page(untitled))[0];
        var withTitle = PaintedRectangles.FilledOn(Drawn.Page(titled))[0];

        // The value axis runs up the left, so its title costs width and the columns move right.
        withTitle.X.Should().BeGreaterThan(withoutTitle.X);
        withTitle.Width.Should().BeLessThan(withoutTitle.Width);
    }

    /// <summary>
    ///   A rotated title is measured across its turned extent rather than its written one, so a
    ///   long caption turned on its side costs the plot area its height rather than its width.
    ///   That is the whole point of turning it, and it is the branch of Format that does the
    ///   turning.
    /// </summary>
    [Fact]
    public void ARotatedCaptionCostsThePlotAreaLessWidthThanAnUprightOne()
    {
        var upright = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        upright.YAxis.Title.Caption = "A rather long caption";

        var rotated = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        rotated.YAxis.Title.Caption = "A rather long caption";
        rotated.YAxis.Title.Orientation = 90;

        var written = PaintedRectangles.FilledOn(Drawn.Page(upright))[0];
        var turned = PaintedRectangles.FilledOn(Drawn.Page(rotated))[0];

        turned.X.Should().BeLessThan(written.X);
        turned.Width.Should().BeGreaterThan(written.Width);
    }

    [Fact]
    public void ARotatedCaptionIsStillWrittenOnThePage()
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        chart.YAxis.Title.Caption = "Rotated";
        chart.YAxis.Title.Orientation = 90;

        ShownText.On(Drawn.Page(chart)).Should().Contain("Rotated");
    }

    [Fact]
    public void AligningARotatedCaptionToOneEndMovesIt()
    {
        RotatedCaption(HorizontalAlignment.Left, VerticalAlignment.Center)
            .Should().NotBe(RotatedCaption(HorizontalAlignment.Center, VerticalAlignment.Center));

        RotatedCaption(HorizontalAlignment.Center, VerticalAlignment.Top)
            .Should().NotBe(RotatedCaption(HorizontalAlignment.Center, VerticalAlignment.Center));
    }

    /// <summary>
    ///   Centring a rotated caption and pushing it to the far end put it in the same place. The
    ///   two cases are written separately - <c>x + width / 2</c> against
    ///   <c>x + width - layout.Width / 2</c> - but the layout rectangle a rotated title is drawn
    ///   into is the title's own rectangle, so <c>layout.Width</c> is that same width and the
    ///   second expression reduces to the first. The same holds of Center against Bottom. Only
    ///   Left and Top come out anywhere else, and only because they subtract nothing.
    /// </summary>
    /// <remarks>
    ///   Recorded rather than endorsed. Two of the three alignments a caller can ask for do the
    ///   same thing, and nothing but this says so.
    /// </remarks>
    [Fact]
    public void CentringARotatedCaptionAndAligningItFarComeToTheSamePlace()
    {
        RotatedCaption(HorizontalAlignment.Right, VerticalAlignment.Center)
            .Should().Be(RotatedCaption(HorizontalAlignment.Center, VerticalAlignment.Center));

        RotatedCaption(HorizontalAlignment.Center, VerticalAlignment.Bottom)
            .Should().Be(RotatedCaption(HorizontalAlignment.Center, VerticalAlignment.Center));
    }

    /// <summary>
    ///   An upright caption is placed by handing its alignment to a string format instead of by
    ///   turning the surface, and vertically the three answers are distinct: the value axis's
    ///   title is drawn into a rectangle as tall as the axis, so there is room in it to move up
    ///   and down.
    /// </summary>
    [Theory]
    [InlineData(VerticalAlignment.Top)]
    [InlineData(VerticalAlignment.Bottom)]
    public void AligningAnUprightCaptionVerticallyMovesItUpOrDownTheAxis(VerticalAlignment alignment)
    {
        UprightCaptionPosition(alignment)
            .Should().NotBe(UprightCaptionPosition(VerticalAlignment.Center));
    }

    /// <summary>
    ///   Horizontally it moves nowhere: the same rectangle is only as wide as the caption
    ///   measured, so there is no slack across it to align within and all three answers land on
    ///   the same point.
    /// </summary>
    /// <remarks>
    ///   Recorded rather than endorsed. A caller can move a value-axis caption up and down but not
    ///   side to side, and nothing distinguishes the setting that works from the one that does
    ///   not.
    /// </remarks>
    [Theory]
    [InlineData(HorizontalAlignment.Left)]
    [InlineData(HorizontalAlignment.Right)]
    public void AligningAnUprightCaptionAcrossMovesItNowhere(HorizontalAlignment alignment)
    {
        UprightCaptionPosition(alignment)
            .Should().Be(UprightCaptionPosition(HorizontalAlignment.Center));
    }

    /// <summary>
    ///   The category axis does not use this renderer at all. It draws its own caption inline, at
    ///   the middle of the axis, and never looks at the alignment - so the alignment on a category
    ///   axis title is not merely ineffective, as it is on a value axis title, but unread.
    /// </summary>
    /// <remarks>
    ///   Recorded rather than endorsed. The two axes take different code paths to draw the same
    ///   kind of object, and only one of them can honour an orientation: a caption on the category
    ///   axis is written flat whatever it was asked for.
    /// </remarks>
    [Fact]
    public void TheCategoryAxisDrawsItsOwnCaptionAndReadsNeitherAlignmentNorOrientation()
    {
        var plain = CategoryCaption(title => { });
        var aligned = CategoryCaption(title => title.Alignment = HorizontalAlignment.Right);
        var rotated = CategoryCaption(title => title.Orientation = 90);

        aligned.Should().Be(plain);
        rotated.Should().Be(plain);
    }

    [Fact]
    public void AnEmptyCaptionCostsThePlotAreaNothing()
    {
        var untitled = Charts.Of(ChartType.Column2D, 1.0, 3.0);

        var emptyTitle = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        emptyTitle.YAxis.Title.Caption = "";

        var withoutTitle = PaintedRectangles.FilledOn(Drawn.Page(untitled))[0];
        var withEmptyTitle = PaintedRectangles.FilledOn(Drawn.Page(emptyTitle))[0];

        withEmptyTitle.X.Should().Be(withoutTitle.X);
        withEmptyTitle.Width.Should().Be(withoutTitle.Width);
    }

    [Fact]
    public void ABarChartsAxisTitlesAreWrittenTheSameWay()
    {
        var chart = Charts.Of(ChartType.Bar2D, 3.0, 6.0);
        chart.XAxis.Title.Caption = "Categories";
        chart.YAxis.Title.Caption = "Values";

        ShownText.On(Drawn.Page(chart)).Should().Contain("Categories").And.Contain("Values");
    }

    /// <summary>
    ///   The page a rotated caption is drawn on, as bytes.
    /// </summary>
    /// <remarks>
    ///   A rotated caption is positioned by transforming the surface under it rather than by
    ///   moving the text on it, and PdfSharpCore may realize a transform either as a <c>cm</c> or
    ///   by folding it into the coordinates it then writes. Reading the position out of the text
    ///   operators alone would therefore see the move in one case and miss it in the other. The
    ///   whole content stream sees it in both, and since the alignment is the only thing these
    ///   comparisons vary, a difference anywhere in it came from the alignment.
    /// </remarks>
    private static string RotatedCaption(
        HorizontalAlignment alignment, VerticalAlignment verticalAlignment)
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        chart.YAxis.Title.Caption = "Rotated";
        chart.YAxis.Title.Orientation = 90;
        chart.YAxis.Title.Alignment = alignment;
        chart.YAxis.Title.VerticalAlignment = verticalAlignment;

        return Encoding.ASCII.GetString(PageContent.Of(Drawn.Page(chart)));
    }

    /// <summary>
    ///   The value axis's caption, left upright, which is the branch of this renderer that hands
    ///   the alignment to a string format rather than transforming the surface.
    /// </summary>
    private static (double X, double Y) UprightCaptionPosition(HorizontalAlignment alignment)
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        chart.YAxis.Title.Caption = "Up";
        chart.YAxis.Title.Alignment = alignment;

        return PositionOfCaption(chart, "Up");
    }

    private static (double X, double Y) UprightCaptionPosition(VerticalAlignment alignment)
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        chart.YAxis.Title.Caption = "Up";
        chart.YAxis.Title.VerticalAlignment = alignment;

        return PositionOfCaption(chart, "Up");
    }

    private static (double X, double Y) CategoryCaption(Action<AxisTitle> arrange)
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        chart.XAxis.Title.Caption = "Across";
        arrange(chart.XAxis.Title);

        return PositionOfCaption(chart, "Across");
    }

    private static (double X, double Y) PositionOfCaption(Chart chart, string caption)
    {
        var run = ShownText.RunsOn(Drawn.Page(chart)).Single(shown => shown.Text == caption);
        return (run.X, run.Y);
    }
}
