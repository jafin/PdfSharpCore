using System;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Charting.Tests.Helpers;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Charting.Tests;

/// <summary>
///   The frame a chart is drawn into, and the renderer it picks to draw it.
/// </summary>
/// <remarks>
///   <c>ChartFrame</c> is the whole public surface of the charting package's drawing side: it
///   chooses a renderer from the chart's type, or a combination renderer if the series disagree
///   about what type they are, and runs Init, Format and Draw over it. Everything else the package
///   does is behind that choice.
/// </remarks>
public class ChartFrameTests
{
    [Theory]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Column2D)]
    [InlineData(ChartType.ColumnStacked2D)]
    [InlineData(ChartType.Bar2D)]
    [InlineData(ChartType.BarStacked2D)]
    [InlineData(ChartType.Area2D)]
    [InlineData(ChartType.Pie2D)]
    [InlineData(ChartType.PieExploded2D)]
    public void EveryChartTypeDrawsSomething(ChartType type)
    {
        var page = Drawn.Page(Charts.Of(type, 1.0, 5.0, 3.0));

        PageContent.Of(page).Should().NotBeEmpty();
        var drewSomething = StrokedLines.Of(page).Any()
            || PaintedRectangles.On(page).Any()
            || ShownText.On(page).Any();
        drewSomething.Should().BeTrue();
    }

    /// <summary>
    ///   A chart whose series do not all agree with it about their type is drawn by the
    ///   combination renderer instead - which is chosen from the series rather than from the
    ///   chart, so a chart of type Column2D holding one line series is not a column chart.
    /// </summary>
    [Fact]
    public void SeriesDisagreeingAboutTheirTypeAreDrawnTogether()
    {
        var chart = Charts.Empty(ChartType.Column2D);
        chart.XValues.AddXSeries().Add("A", "B");
        chart.SeriesCollection.AddSeries().Add(1.0, 2.0);
        var line = chart.SeriesCollection.AddSeries();
        line.ChartType = ChartType.Line;
        line.Add(3.0, 1.0);

        var page = Drawn.Page(chart);

        // The column series is filled and the line series is stroked, so the page carries both -
        // which a chart drawn by either renderer alone would not.
        PaintedRectangles.FilledOn(page).Should().HaveCount(2);
        StrokedLines.Of(page).Should().Contain(segment => !segment.IsHorizontal && !segment.IsVertical);
    }

    /// <summary>
    ///   Drawing the frame rather than the chart alone puts a rounded, shadowed, gradient-filled
    ///   border around it first. Nothing else in the package draws a rounded rectangle, so the
    ///   curves are the signature.
    /// </summary>
    [Fact]
    public void DrawingTheFrameAddsABorderThatDrawingTheChartAloneDoesNot()
    {
        var chartOnly = Content(frame => frame.DrawChart);
        var framed = Content(frame => frame.Draw);

        Curves(framed).Should().BeGreaterThan(0);
        Curves(chartOnly).Should().Be(0);
    }

    [Fact]
    public void AFrameHoldingTwoChartsDrawsThemOneAboveTheOther()
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = 400;
        page.Height = 600;

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var frame = new ChartFrame(new XRect(0, 0, 400, 600));
            frame.Add(Charts.Of(ChartType.Column2D, 1.0, 2.0));
            frame.Add(Charts.Of(ChartType.Column2D, 3.0, 4.0));
            frame.Draw(gfx);
        }

        var columns = PaintedRectangles.FilledOn(Reopened(document));

        columns.Should().HaveCount(4);

        // Two charts, each with two columns standing on its own baseline, and the two baselines
        // are not the same one.
        columns.Select(column => column.Y).Distinct().Should().HaveCount(2);
    }

    /// <summary>
    ///   A chart whose X axis was never asked for draws its columns at NaN.
    /// </summary>
    /// <remarks>
    ///   <see cref="Chart.XAxis"/> creates the axis the first time it is read, so a chart nothing
    ///   configured has none at all. The X axis renderer answers that by returning renderer info
    ///   it has calculated nothing into, which leaves the maximum scale at its default of zero -
    ///   and the plot area builds its matrix by dividing its own width by that scale. The result
    ///   is infinite, every coordinate derived from it is NaN, and NaN is what is written to the
    ///   page: <c>NaN NaN NaN 200 re</c>. The height survives because it comes from the value
    ///   axis, which needs no such object.
    ///
    ///   Recorded rather than endorsed. It is a defect, and one with no warning attached - the
    ///   draw succeeds, the file is written, and a reader is left with a page it cannot parse.
    ///   It is also why <see cref="Charts.Empty"/> reads both axes.
    /// </remarks>
    [Fact]
    public void AChartWithNoXAxisDrawsItsColumnsNowhere()
    {
        var chart = new Chart(ChartType.Column2D);
        chart.XValues.AddXSeries().Add("A", "B");
        chart.SeriesCollection.AddSeries().Add(1.0, 2.0);

        var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var frame = new ChartFrame(new XRect(0, 0, 400, 300));
            frame.Add(chart);
            frame.DrawChart(gfx);
        }

        var content = Encoding.ASCII.GetString(PageContent.Of(Reopened(document)));

        content.Should().Contain("NaN");
    }

    /// <summary>
    ///   The same chart with its axis present draws in numbers, which is the other half of the
    ///   above: nothing about the data changed, only whether the axis object existed.
    /// </summary>
    [Fact]
    public void TheSameChartWithAnXAxisDrawsInNumbers()
    {
        var page = Drawn.Page(Charts.Of(ChartType.Column2D, 1.0, 2.0));

        var content = Encoding.ASCII.GetString(PageContent.Of(page));

        content.Should().NotContain("NaN");
        PaintedRectangles.FilledOn(page).Should().HaveCount(2);
    }

    private static byte[] Content(Func<ChartFrame, Action<XGraphics>> draw)
    {
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var frame = new ChartFrame(new XRect(20, 20, 400, 300));
            frame.Add(Charts.Of(ChartType.Column2D, 1.0, 5.0, 3.0));
            draw(frame)(gfx);
        }

        return PageContent.Of(Reopened(document));
    }

    /// <summary>How many Bézier segments the page draws, which for a chart means rounded corners.</summary>
    private static int Curves(byte[] content) =>
        Encoding.ASCII.GetString(content)
            .Split('\n')
            .Count(line => line.EndsWith(" c") || line == "c");

    private static PdfPage Reopened(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, false);
        stream.Position = 0;
        return PdfReader.Open(stream, PdfDocumentOpenMode.Modify).Pages[0];
    }
}
