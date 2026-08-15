namespace PdfSharpCore.Charting.Tests.Helpers;

/// <summary>
///   Charts arranged the way the tests need them, so that each test says only what it varies.
/// </summary>
internal static class Charts
{
    /// <summary>
    ///   A chart of the given type with one category per value, plotting <paramref name="values"/>
    ///   as its only series.
    /// </summary>
    internal static Chart Of(ChartType type, params double[] values)
    {
        var chart = Empty(type);

        var categories = chart.XValues.AddXSeries();
        for (var idx = 0; idx < values.Length; idx++)
            categories.Add(CategoryNames[idx % CategoryNames.Length]);

        chart.SeriesCollection.AddSeries().Add(values);
        return chart;
    }

    /// <summary>
    ///   A chart of the given type with one category per value of the first series, plotting each
    ///   array given as a series of its own.
    /// </summary>
    internal static Chart OfSeries(ChartType type, params double[][] series)
    {
        var chart = Empty(type);

        var categories = chart.XValues.AddXSeries();
        for (var idx = 0; idx < series[0].Length; idx++)
            categories.Add(CategoryNames[idx % CategoryNames.Length]);

        foreach (var values in series)
            chart.SeriesCollection.AddSeries().Add(values);

        return chart;
    }

    /// <summary>
    ///   A chart of the given type with both axes present and nothing plotted.
    /// </summary>
    /// <remarks>
    ///   Both axes are read, which is not idle: <see cref="Chart.XAxis"/> creates the axis the
    ///   first time it is asked for, and a chart that was never asked has none. The X axis
    ///   renderer answers a chart with no X axis by returning renderer info it has calculated
    ///   nothing into, leaving the maximum scale at zero - and the plot area's matrix is built by
    ///   dividing the plot area's width by that scale. Every column then lands at an infinite
    ///   coordinate and is written to the page as NaN. See
    ///   <c>ChartFrameTests.AChartWithNoXAxisDrawsItsColumnsNowhere</c>, which pins that down.
    ///
    ///   So the axes are created here rather than left to the tests that happen to configure one.
    ///   A chart drawn by anything else - MigraDoc's chart mapper, or any caller following the
    ///   package's own samples - has them, and a test arranged without them would be measuring
    ///   that defect instead of the renderer it names.
    /// </remarks>
    internal static Chart Empty(ChartType type)
    {
        var chart = new Chart(type);
        _ = chart.XAxis;
        _ = chart.YAxis;
        return chart;
    }

    /// <summary>
    ///   One-character category names, so that a tick label measures the same width whichever
    ///   category it belongs to and a test comparing two of them is comparing their positions.
    /// </summary>
    private static readonly string[] CategoryNames = { "A", "B", "C", "D", "E", "F", "G", "H" };
}
