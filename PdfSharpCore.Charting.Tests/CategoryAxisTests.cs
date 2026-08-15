using System;
using System.Linq;
using AwesomeAssertions;
using PdfSharpCore.Charting.Tests.Helpers;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Charting.Tests;

/// <summary>
///   The axis the categories are written along - across the bottom of a column chart, up the side
///   of a bar chart.
/// </summary>
/// <remarks>
///   Two renderers, <c>HorizontalXAxisRenderer</c> and <c>VerticalXAxisRenderer</c>, each with a
///   Draw of its own. They are near-copies of one another, and the interesting part of these tests
///   is where the copies have drifted: the horizontal one skips a category with no value and stops
///   at the end of the series, and the vertical one does neither. Both facts are pinned down
///   below, the second as the exception it currently throws.
/// </remarks>
public class CategoryAxisTests
{
    [Fact]
    public void AColumnChartWritesItsCategoriesLeftToRightInOrder()
    {
        var page = Drawn.Page(Charts.Of(ChartType.Column2D, 1.0, 5.0, 3.0));

        var categories = ShownText.RunsOn(page).Where(run => run.Text.Length == 1).ToList();

        categories.Select(run => run.Text).Should().Equal("A", "B", "C");
        categories.Should().BeInAscendingOrder(run => run.X);
    }

    [Fact]
    public void AColumnChartPutsEachCategoryUnderItsOwnColumn()
    {
        var page = Drawn.Page(Charts.Of(ChartType.Column2D, 1.0, 5.0, 3.0));

        var categories = ShownText.RunsOn(page).Where(run => run.Text.Length == 1).ToList();
        var columns = PaintedRectangles.FilledOn(page);

        categories.Should().HaveCount(columns.Count);
        for (var idx = 0; idx < columns.Count; idx++)
            categories[idx].X.Should().BeApproximately(columns[idx].CentreX, LabelTolerance);
    }

    [Fact]
    public void ABarChartWritesItsCategoriesUpTheSideInReverse()
    {
        var page = Drawn.Page(Charts.Of(ChartType.Bar2D, 1.0, 5.0, 3.0));

        var categories = ShownText.RunsOn(page).Where(run => run.Text.Length == 1).ToList();

        // The last category is drawn first because it belongs at the top: a bar chart's first
        // category is the bottom bar.
        categories.Select(run => run.Text).Should().Equal("C", "B", "A");
        categories.Should().BeInDescendingOrder(run => run.Y);
    }

    [Fact]
    public void ABarChartPutsEachCategoryBesideItsOwnBar()
    {
        var page = Drawn.Page(Charts.Of(ChartType.Bar2D, 1.0, 5.0, 3.0));

        var categories = ShownText.RunsOn(page).Where(run => run.Text.Length == 1).ToList();
        var bars = PaintedRectangles.FilledOn(page);

        // Bars come out in category order and labels in reverse, so the first label belongs to
        // the last bar. A label is placed by its baseline rather than by the middle of its
        // glyphs, so what is asserted is that it falls within the bar's own band rather than at
        // any particular point in it.
        categories.Should().HaveCount(bars.Count);
        for (var idx = 0; idx < bars.Count; idx++)
        {
            categories[bars.Count - 1 - idx].Y
                .Should().BeInRange(bars[idx].Y, bars[idx].Top);
        }
    }

    /// <summary>
    ///   A chart given no categories of its own is labelled 1, 2, 3 - the axis renderer fills them
    ///   in from the scale rather than leaving the axis blank.
    /// </summary>
    [Theory]
    [InlineData(ChartType.Column2D)]
    [InlineData(ChartType.Bar2D)]
    public void AChartWithNoCategoriesIsLabelledByPosition(ChartType type)
    {
        var chart = Charts.Empty(type);
        chart.SeriesCollection.AddSeries().Add(1.0, 5.0, 3.0);

        var shown = ShownText.On(Drawn.Page(chart));

        shown.Should().Contain("1").And.Contain("2").And.Contain("3");
    }

    [Fact]
    public void TurningOffEveryTickMarkLeavesNothingStrokedOnTheAxes()
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 3.0);

        StrokedLines.Of(Drawn.Page(chart)).Should().NotBeEmpty("the default axes draw tick marks");

        chart.XAxis.MajorTickMark = TickMarkType.None;
        chart.XAxis.MinorTickMark = TickMarkType.None;
        chart.YAxis.MajorTickMark = TickMarkType.None;
        chart.YAxis.MinorTickMark = TickMarkType.None;

        // No gridlines, no axis line format and now no tick marks: an axis with nothing to stroke
        // strokes nothing, rather than drawing a zero-length segment for each tick.
        StrokedLines.Of(Drawn.Page(chart)).Should().BeEmpty();
    }

    /// <summary>
    ///   The category axis draws nothing at all until its line format says something. Its tick
    ///   marks and its axis line are both stroked with a renderer built from
    ///   <see cref="Axis.LineFormat"/>, and that renderer holds no pen when the format is absent,
    ///   so every line it is asked for is discarded. The value axis has a pen of its own and draws
    ///   its tick marks either way, which is why a default chart has horizontal tick marks up the
    ///   side and nothing along the bottom.
    /// </summary>
    [Fact]
    public void TheCategoryAxisStrokesNothingUntilItIsGivenALineFormat()
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 3.0, 2.0, 4.0);

        StrokedLines.Of(Drawn.Page(chart)).Should().OnlyContain(line => line.IsHorizontal,
            "only the value axis, which runs up the side, has drawn anything");

        chart.XAxis.LineFormat.Visible = true;

        StrokedLines.Of(Drawn.Page(chart)).Should().Contain(line => line.IsVertical,
            "the category axis now has a pen for its tick marks");
    }

    [Fact]
    public void AskingForMinorTickMarksDrawsMoreOfThemThanMajorOnesAlone()
    {
        var majorOnly = Charts.Of(ChartType.Column2D, 1.0, 3.0, 2.0, 4.0);
        majorOnly.XAxis.LineFormat.Visible = true;
        majorOnly.XAxis.MinorTickMark = TickMarkType.None;

        var withMinor = Charts.Of(ChartType.Column2D, 1.0, 3.0, 2.0, 4.0);
        withMinor.XAxis.LineFormat.Visible = true;
        withMinor.XAxis.MinorTickMark = TickMarkType.Outside;

        // A minor tick every half category against a major tick every whole one, so asking for
        // them roughly doubles what the axis strokes.
        StrokedLines.Of(Drawn.Page(withMinor)).Count
            .Should().BeGreaterThan(StrokedLines.Of(Drawn.Page(majorOnly)).Count);
    }

    /// <summary>
    ///   The bar chart's category axis is the same story: nothing is stroked for it until it has a
    ///   line format, and then its tick marks run across rather than up.
    /// </summary>
    [Fact]
    public void ABarChartsCategoryAxisStrokesNothingUntilItIsGivenALineFormat()
    {
        var chart = Charts.Of(ChartType.Bar2D, 3.0, 6.0);
        chart.YAxis.MajorTickMark = TickMarkType.None;
        chart.YAxis.MinorTickMark = TickMarkType.None;

        StrokedLines.Of(Drawn.Page(chart)).Should().BeEmpty();

        chart.XAxis.LineFormat.Visible = true;
        chart.XAxis.MinorTickMark = TickMarkType.Outside;

        var lines = StrokedLines.Of(Drawn.Page(chart));

        // A bar chart's categories run up the side, so the axis itself is the one vertical line
        // and every tick mark sticks out sideways from it.
        lines.Should().ContainSingle(line => line.IsVertical);
        lines.Should().Contain(line => line.IsHorizontal);
    }

    [Fact]
    public void ABarChartsCategoryAxisDrawsAnAxisLineWhenItHasALineFormat()
    {
        var chart = Charts.Of(ChartType.Bar2D, 3.0, 6.0);
        chart.YAxis.MajorTickMark = TickMarkType.None;
        chart.YAxis.MinorTickMark = TickMarkType.None;
        chart.XAxis.MajorTickMark = TickMarkType.None;
        chart.XAxis.MinorTickMark = TickMarkType.None;
        chart.XAxis.LineFormat.Visible = true;

        // Every tick mark is off, so what is left is the axis itself - one line, up the side.
        var lines = StrokedLines.Of(Drawn.Page(chart));

        lines.Should().ContainSingle();
        lines[0].IsVertical.Should().BeTrue();
    }

    [Theory]
    [InlineData(TickMarkType.Inside)]
    [InlineData(TickMarkType.Outside)]
    [InlineData(TickMarkType.Cross)]
    public void EachKindOfTickMarkIsDrawnSomewhereOfItsOwn(TickMarkType kind)
    {
        var lines = TickMarks(kind);
        var others = new[] { TickMarkType.Inside, TickMarkType.Outside, TickMarkType.Cross }
            .Where(other => other != kind)
            .Select(TickMarks);

        others.Should().NotContain(lines);
    }

    /// <summary>
    ///   A category with no value is skipped rather than drawn: the horizontal renderer tests each
    ///   X value for null before it measures it.
    /// </summary>
    [Fact]
    public void AColumnChartSkipsACategoryWithNoValue()
    {
        var chart = Charts.Empty(ChartType.Column2D);
        var categories = chart.XValues.AddXSeries();
        categories.Add("A");
        categories.AddBlank();
        categories.Add("C");
        chart.SeriesCollection.AddSeries().Add(1.0, 2.0, 3.0);

        var shown = ShownText.On(Drawn.Page(chart));

        shown.Should().Contain("A").And.Contain("C");
    }

    /// <summary>
    ///   The same chart drawn as bars throws, because the vertical renderer reads the X value
    ///   without testing it first. The two renderers are otherwise the same method, and this is
    ///   the guard one of them is missing rather than a difference either was designed to have.
    /// </summary>
    [Fact]
    public void ABarChartThrowsOnACategoryWithNoValue()
    {
        var chart = Charts.Empty(ChartType.Bar2D);
        var categories = chart.XValues.AddXSeries();
        categories.Add("A");
        categories.AddBlank();
        categories.Add("C");
        chart.SeriesCollection.AddSeries().Add(1.0, 2.0, 3.0);

        var drawing = () => Drawn.Page(chart);

        drawing.Should().Throw<NullReferenceException>();
    }

    /// <summary>
    ///   Fewer categories than there are values is the same story from the other side. The scale
    ///   runs to the longest series, so the horizontal renderer's second condition - stop at the
    ///   end of the category list - is what keeps it in bounds, and the vertical renderer has no
    ///   such condition.
    /// </summary>
    [Fact]
    public void AColumnChartDrawsTheCategoriesItHasWhenThereAreFewerThanValues()
    {
        var chart = Charts.Empty(ChartType.Column2D);
        chart.XValues.AddXSeries().Add("A", "B");
        chart.SeriesCollection.AddSeries().Add(1.0, 2.0, 3.0);

        ShownText.On(Drawn.Page(chart)).Should().Contain("A").And.Contain("B");
    }

    [Fact]
    public void ABarChartThrowsWhenThereAreFewerCategoriesThanValues()
    {
        var chart = Charts.Empty(ChartType.Bar2D);
        chart.XValues.AddXSeries().Add("A", "B");
        chart.SeriesCollection.AddSeries().Add(1.0, 2.0, 3.0);

        var drawing = () => Drawn.Page(chart);

        drawing.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///   Where a column chart's category tick marks are drawn, as text, so that two kinds can be
    ///   compared for having come out anywhere different.
    /// </summary>
    private static string TickMarks(TickMarkType kind)
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        chart.XAxis.LineFormat.Visible = true;
        chart.XAxis.MajorTickMark = kind;
        chart.XAxis.MinorTickMark = TickMarkType.None;
        chart.YAxis.MajorTickMark = TickMarkType.None;
        chart.YAxis.MinorTickMark = TickMarkType.None;

        return string.Join("; ", StrokedLines.Of(Drawn.Page(chart)).Select(line => line.ToString()));
    }

    /// <summary>
    ///   A label is centred on its column by its measured width, so its left edge lands half a
    ///   character to the left of the centre it is aligned to.
    /// </summary>
    private const double LabelTolerance = 5;
}
