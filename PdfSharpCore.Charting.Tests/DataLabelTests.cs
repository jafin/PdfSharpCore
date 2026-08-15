using System;
using System.Linq;
using AwesomeAssertions;
using PdfSharpCore.Charting.Tests.Helpers;
using Xunit;

namespace PdfSharpCore.Charting.Tests;

/// <summary>
///   The numbers written on the data itself.
/// </summary>
/// <remarks>
///   <c>DataLabelRenderer.Init</c> decides, for each series, whether it has labels at all and what
///   they say. There are four ways to ask for them - a flag or a settings object, on the chart or
///   on one series - and the settings are inherited from the chart by a series that has none of
///   its own. Where nothing says otherwise the format is "0" and the type is the value, except on
///   a pie, where it is the percentage. Every one of those is a branch, and none of them shows
///   anywhere but in the text drawn on the page.
///
///   <c>PieDataLabelRenderer.CalcPositions</c> then decides where a pie's labels go, which is a
///   harder question than for a column: there is no rectangle to sit on top of, only a wedge and
///   an angle, and the answer depends on which of the three label positions was asked for.
/// </remarks>
public class DataLabelTests
{
    [Fact]
    public void AChartWithNoDataLabelsWritesNoneOfTheValues()
    {
        var page = Drawn.Page(Charts.Of(ChartType.Column2D, 12.0, 34.0));

        ShownText.On(page).Should().NotContain("12").And.NotContain("34");
    }

    [Fact]
    public void AskingTheChartForDataLabelsLabelsEverySeries()
    {
        var chart = Charts.OfSeries(ChartType.Column2D, new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 });
        chart.HasDataLabel = true;

        var page = Drawn.Page(chart);

        ShownText.On(page).Take(4).Should().Equal("1", "2", "3", "4");
    }

    /// <summary>
    ///   Settings on the chart are enough on their own: a chart that was given a data label object
    ///   has labels whether or not anything set the flag, because the renderer treats the object's
    ///   existence as the request.
    /// </summary>
    [Fact]
    public void GivingTheChartADataLabelFormatIsItselfAskingForLabels()
    {
        var chart = Charts.Of(ChartType.Column2D, 1.5, 2.5);
        chart.DataLabel.Format = "0.00";

        ShownText.On(Drawn.Page(chart)).Take(2).Should().Equal("1.50", "2.50");
    }

    [Fact]
    public void TheDefaultFormatRoundsToAWholeNumber()
    {
        var chart = Charts.Of(ChartType.Column2D, 1.4, 2.5);
        chart.HasDataLabel = true;

        ShownText.On(Drawn.Page(chart)).Take(2).Should().Equal("1", "3");
    }

    [Fact]
    public void AskingOneSeriesForLabelsLeavesTheOthersUnlabelled()
    {
        var chart = Charts.OfSeries(ChartType.Column2D, new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 });
        chart.SeriesCollection[1].HasDataLabel = true;

        var shown = ShownText.On(Drawn.Page(chart));

        shown.Take(2).Should().Equal("3", "4");
        shown.Should().NotContain("1").And.NotContain("2");
    }

    [Fact]
    public void ASeriesWithItsOwnFormatUsesItRatherThanTheCharts()
    {
        var chart = Charts.OfSeries(ChartType.Column2D, new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 });
        chart.DataLabel.Format = "0.0";
        chart.SeriesCollection[1].DataLabel.Format = "0.000";

        var shown = ShownText.On(Drawn.Page(chart));

        shown.Take(4).Should().Equal("1.0", "2.0", "3.000", "4.000");
    }

    [Fact]
    public void ADataLabelIsDrawnOverTheColumnItBelongsTo()
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 5.0, 3.0);
        chart.HasDataLabel = true;

        var page = Drawn.Page(chart);
        var labels = ShownText.RunsOn(page).Take(3).ToList();
        var columns = PaintedRectangles.FilledOn(page);

        for (var idx = 0; idx < columns.Count; idx++)
        {
            labels[idx].X.Should().BeInRange(columns[idx].X, columns[idx].Right);
            labels[idx].Y.Should().BeInRange(columns[idx].Y, columns[idx].Top);
        }
    }

    /// <summary>
    ///   A pie is labelled with percentages rather than values unless it is told otherwise - the
    ///   one place where the default type depends on the kind of chart being drawn.
    /// </summary>
    [Fact]
    public void APieIsLabelledWithPercentagesByDefault()
    {
        var chart = Charts.Of(ChartType.Pie2D, 1.0, 2.0, 1.0);
        chart.HasDataLabel = true;

        ShownText.On(Drawn.Page(chart)).Should().Equal("25%", "50%", "25%");
    }

    [Fact]
    public void APieAskedForValuesIsLabelledWithThem()
    {
        var chart = Charts.Of(ChartType.Pie2D, 1.0, 2.0, 1.0);
        chart.DataLabel.Type = DataLabelType.Value;

        ShownText.On(Drawn.Page(chart)).Should().Equal("1", "2", "1");
    }

    /// <summary>
    ///   Percentages are a pie's answer and no other chart's. A column asked for them says so
    ///   rather than drawing something wrong, which is worth pinning down because the type is
    ///   accepted by the object model without complaint and only refused at the point of drawing.
    /// </summary>
    [Fact]
    public void AColumnChartRefusesToLabelItselfWithPercentages()
    {
        var chart = Charts.Of(ChartType.Column2D, 1.0, 3.0);
        chart.DataLabel.Type = DataLabelType.Percent;

        var drawing = () => Drawn.Page(chart);

        drawing.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be set to 'Percent'*");
    }

    [Fact]
    public void AnExplodedPieIsLabelledLikeAClosedOne()
    {
        var chart = Charts.Of(ChartType.PieExploded2D, 1.0, 1.0, 1.0, 1.0);
        chart.HasDataLabel = true;

        ShownText.On(Drawn.Page(chart)).Should().Equal("25%", "25%", "25%", "25%");
    }

    /// <summary>
    ///   Where a pie's labels sit depends on the position asked for, and the three answers are
    ///   distinct: outside the wedge is further from the middle of the pie than inside its end,
    ///   which is further than the centre.
    /// </summary>
    [Fact]
    public void APiesLabelPositionDecidesHowFarFromTheMiddleTheLabelsSit()
    {
        var centre = SpreadOfLabels(DataLabelPosition.Center);
        var insideEnd = SpreadOfLabels(DataLabelPosition.InsideEnd);
        var outsideEnd = SpreadOfLabels(DataLabelPosition.OutsideEnd);

        insideEnd.Should().BeGreaterThan(centre);
        outsideEnd.Should().BeGreaterThan(insideEnd);
    }

    /// <summary>
    ///   The fourth position a pie label can be given puts every label at the middle of the pie,
    ///   one on top of another.
    /// </summary>
    /// <remarks>
    ///   Recorded rather than endorsed. The case sets the label's corner to the centre of the pie
    ///   and then tests whether that corner is left of, or above, the centre of the pie - which it
    ///   cannot be, being the same point - so the two adjustments meant to pull the label back by
    ///   its own width and height never run. Four labels asked for InsideBase are drawn at one
    ///   point and read as one illegible overlap.
    /// </remarks>
    [Fact]
    public void APieLabelledAtItsBaseStacksEveryLabelOnOneSpot()
    {
        var chart = Charts.Of(ChartType.Pie2D, 1.0, 1.0, 1.0, 1.0);
        chart.HasDataLabel = true;
        chart.DataLabel.Position = DataLabelPosition.InsideBase;

        var labels = ShownText.RunsOn(Drawn.Page(chart)).ToList();

        labels.Should().HaveCount(4);
        labels.Select(label => (label.X, label.Y)).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void APieAskedForNoLabelTypeIsLeftUnlabelled()
    {
        var chart = Charts.Of(ChartType.Pie2D, 1.0, 2.0, 1.0);
        chart.DataLabel.Type = DataLabelType.None;

        ShownText.On(Drawn.Page(chart)).Should().BeEmpty();
    }

    [Fact]
    public void EveryWedgeOfAPieIsLabelledSomewhereDifferent()
    {
        var chart = Charts.Of(ChartType.Pie2D, 1.0, 1.0, 1.0, 1.0);
        chart.HasDataLabel = true;

        var labels = ShownText.RunsOn(Drawn.Page(chart)).ToList();

        labels.Should().HaveCount(4);
        labels.Select(label => (label.X, label.Y)).Distinct().Should().HaveCount(4);
    }

    /// <summary>
    ///   How far apart the four labels of an equal four-wedge pie are drawn, measured as the width
    ///   of the box they all fall inside. The pie itself does not move, so a position that puts
    ///   the labels further out puts them further apart.
    /// </summary>
    private static double SpreadOfLabels(DataLabelPosition position)
    {
        var chart = Charts.Of(ChartType.Pie2D, 1.0, 1.0, 1.0, 1.0);
        chart.HasDataLabel = true;
        chart.DataLabel.Position = position;

        var labels = ShownText.RunsOn(Drawn.Page(chart)).ToList();

        return labels.Max(label => label.X) - labels.Min(label => label.X);
    }
}
