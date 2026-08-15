using System;
using System.Linq;
using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel.IO;
using MigraDocCore.DocumentObjectModel.Shapes;
using MigraDocCore.DocumentObjectModel.Shapes.Charts;
using Xunit;

namespace MigraDocCore.DocumentObjectModel.Tests;

/// <summary>
///   Reading a chart out of MDDDL. A chart is the deepest thing the grammar allows - a chart holds
///   seven areas, each of which holds paragraphs, tables, images and legends; three axes; and any
///   number of series, each of which holds numbers, gaps, and points with formatting of their own.
///   The parser has a method per level and they are among the longest in the file.
///   <para>
///   Only round trips would not do here. The serializer writes a chart one way, so a round trip
///   exercises one path through each of these methods and leaves the others - an area given
///   attributes as well as contents, a series written as bare numbers, a legend with nothing after
///   it - unread. The DDL below is written out by hand for that reason.
///   </para>
/// </summary>
public class DdlChartReadingTests
{
    static Chart ChartFrom(string chartDdl) =>
        (Chart)DdlReader.DocumentFromString("\\document{\\section{" + chartDdl + "}}")
            .LastSection.Elements[0];

    // ----- the chart itself ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Line")]
    [InlineData("Column2D")]
    [InlineData("Pie2D")]
    [InlineData("Bar2D")]
    public void AChartIsReadAsTheKindItNames(string type)
    {
        ChartFrom("\\chart(" + type + "){}").Type.Should().Be(Enum.Parse<ChartType>(type));
    }

    [Fact]
    public void AChartCanCarryAttributesAsWellAsContents()
    {
        var chart = ChartFrom("\\chart(Line)[Style = \"Normal\" DisplayBlanksAs = Zero]{}");

        chart.Style.Should().Be("Normal");
        chart.DisplayBlanksAs.Should().Be(BlankType.Zero);
    }

    [Fact]
    public void AChartNamingAKindThatIsNotOneIsRefused()
    {
        var act = () => ChartFrom("\\chart(Hexagonal){}");

        act.Should().Throw<Exception>();
    }

    // ----- the areas ------------------------------------------------------------------------------------

    public static TheoryData<string> EveryArea => new()
    {
        "headerarea", "footerarea", "toparea", "bottomarea", "leftarea", "rightarea",
    };

    [Theory]
    [MemberData(nameof(EveryArea))]
    public void EachAreaOfAChartIsReadOntoTheAreaItNames(string keyword)
    {
        var chart = ChartFrom("\\chart(Line){\\" + keyword + "{\\paragraph{inside}}}");

        var area = keyword switch
        {
            "headerarea" => chart.HeaderArea,
            "footerarea" => chart.FooterArea,
            "toparea" => chart.TopArea,
            "bottomarea" => chart.BottomArea,
            "leftarea" => chart.LeftArea,
            _ => chart.RightArea,
        };
        area.Elements.Count.Should().Be(1);
        (area.Elements[0] as Paragraph).Elements.OfType<Text>().Single().Content.Should().Be("inside");
    }

    [Fact]
    public void AnAreaCanCarryAttributesAsWellAsContents()
    {
        var chart = ChartFrom(
            "\\chart(Line){\\headerarea[Height = \"2cm\" Width = \"4cm\"]{\\paragraph{t}}}");

        chart.HeaderArea.Height.Centimeter.Should().BeApproximately(2, 1e-4);
        chart.HeaderArea.Width.Centimeter.Should().BeApproximately(4, 1e-4);
        chart.HeaderArea.Elements.Count.Should().Be(1);
    }

    [Fact]
    public void AnAreaCanCarryAttributesAndNothingElse()
    {
        // The brace block is optional, which is a separate arm from the one above.
        var chart = ChartFrom("\\chart(Line){\\plotarea[LeftPadding = \"5mm\"]}");

        chart.PlotArea.LeftPadding.Millimeter.Should().BeApproximately(5, 1e-4);
    }

    [Fact]
    public void AnAreaTakesTheSameThingsAnySequenceOfElementsDoes()
    {
        var chart = ChartFrom(
            "\\chart(Line){\\rightarea{"
            + "\\paragraph{words}"
            + "\\table{\\columns{\\column[Width = \"1cm\"]}\\rows{\\row{\\cell{c}}}}"
            + "\\legend"
            + "}}");

        chart.RightArea.Elements.Count.Should().Be(3);
        chart.RightArea.Elements.OfType<Legend>().Should().ContainSingle();
    }

    [Fact]
    public void ALegendCanBeBareOrCarryAttributes()
    {
        var bare = ChartFrom("\\chart(Line){\\rightarea{\\legend}}");
        var dressed = ChartFrom("\\chart(Line){\\rightarea{\\legend[Style = \"Normal\"]}}");

        bare.RightArea.Elements.OfType<Legend>().Should().ContainSingle();
        dressed.RightArea.Elements.OfType<Legend>().Single().Style.Should().Be("Normal");
    }

    // ----- axes -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("xaxis")]
    [InlineData("yaxis")]
    [InlineData("zaxis")]
    public void EachAxisIsReadOntoTheAxisItNames(string keyword)
    {
        var chart = ChartFrom("\\chart(Line){\\" + keyword + "[MajorTick = 5]}");

        var axis = keyword switch
        {
            "xaxis" => chart.XAxis,
            "yaxis" => chart.YAxis,
            _ => chart.ZAxis,
        };
        axis.MajorTick.Should().Be(5);
    }

    [Fact]
    public void AnAxisReadsTheWholeOfWhatCanBeSaidAboutIt()
    {
        var chart = ChartFrom(
            "\\chart(Line){\\yaxis[MinimumScale = 0 MaximumScale = 100 MajorTick = 25 "
            + "MajorTickMark = Outside HasMajorGridLines = true TickLabels{Format = \"0.0\"}]}");

        chart.YAxis.MinimumScale.Should().Be(0);
        chart.YAxis.MaximumScale.Should().Be(100);
        chart.YAxis.MajorTick.Should().Be(25);
        chart.YAxis.MajorTickMark.Should().Be(TickMarkType.Outside);
        chart.YAxis.HasMajorGridlines.Should().BeTrue();
        chart.YAxis.TickLabels.Format.Should().Be("0.0");
    }

    // ----- series ------------------------------------------------------------------------------------------

    [Fact]
    public void ASeriesOfBareNumbersIsRead()
    {
        var chart = ChartFrom("\\chart(Line){\\series{1, 2, 3}}");

        var series = chart.SeriesCollection[0];
        series.Count.Should().Be(3);
        ((Shapes.Charts.Point)series.Elements[0]).Value.Should().Be(1);
        ((Shapes.Charts.Point)series.Elements[2]).Value.Should().Be(3);
    }

    [Fact]
    public void ANumberInASeriesCanHaveADecimalPointAndASign()
    {
        var chart = ChartFrom("\\chart(Line){\\series{-1.5, 0, 2.25}}");

        var series = chart.SeriesCollection[0];
        ((Shapes.Charts.Point)series.Elements[0]).Value.Should().Be(-1.5);
        ((Shapes.Charts.Point)series.Elements[2]).Value.Should().Be(2.25);
    }

    [Fact]
    public void ANullInASeriesIsAGapRatherThanAZero()
    {
        var chart = ChartFrom("\\chart(Line){\\series{1, null, 3}}");

        var series = chart.SeriesCollection[0];
        series.Count.Should().Be(3);
        series.Elements[1].Should().BeNull();
    }

    [Fact]
    public void ASeriesCanCarryAttributesAsWellAsNumbers()
    {
        var chart = ChartFrom(
            "\\chart(Line){\\series[Name = \"sales\" MarkerStyle = Diamond ChartType = Line]{1, 2}}");

        var series = chart.SeriesCollection[0];
        series.Name.Should().Be("sales");
        series.MarkerStyle.Should().Be(MarkerStyle.Diamond);
        series.ChartType.Should().Be(ChartType.Line);
    }

    [Fact]
    public void APointInASeriesCanBeDressedUpOnItsOwn()
    {
        // Which is the arm that makes one column a different colour from the rest, and the only
        // place a point is written as anything other than a number.
        var chart = ChartFrom(
            "\\chart(Bar2D){\\series{1, \\point[FillFormat{Color = Red}]{2}, 3}}");

        var series = chart.SeriesCollection[0];
        series.Count.Should().Be(3);
        var point = (Shapes.Charts.Point)series.Elements[1];
        point.Value.Should().Be(2);
        point.FillFormat.Color.Should().Be(Colors.Red);
        ((Shapes.Charts.Point)series.Elements[0]).FillFormat.Color.Should().Be(Color.Empty);
    }

    [Fact]
    public void MoreThanOneSeriesIsReadAsMoreThanOneSeries()
    {
        var chart = ChartFrom("\\chart(Line){\\series[Name = \"a\"]{1}\\series[Name = \"b\"]{2}}");

        chart.SeriesCollection.Count.Should().Be(2);
        chart.SeriesCollection[0].Name.Should().Be("a");
        chart.SeriesCollection[1].Name.Should().Be("b");
    }

    [Fact]
    public void TheNamesAlongTheAxisAreReadAsAnXSeries()
    {
        var chart = ChartFrom("\\chart(Column2D){\\series{1, 2}\\xvalues{\"Jan\", \"Feb\"}}");

        // An XSeries is not indexable from outside, so what it holds is read back through the
        // serializer, which is also how the file spells it.
        DdlWriter.WriteToString(chart).Should().Contain("\"Jan\", \"Feb\"");
    }

    [Fact]
    public void AnXSeriesTakesGapsToo()
    {
        var chart = ChartFrom("\\chart(Column2D){\\xvalues{\"Jan\", null, \"Mar\"}}");

        DdlWriter.WriteToString(chart).Should().Contain("\"Jan\", null, \"Mar\"");
    }

    // ----- everything at once --------------------------------------------------------------------------------

    [Fact]
    public void AChartWithSomethingOfEveryKindInItIsReadWhole()
    {
        var chart = ChartFrom(
            "\\chart(Column2D)[DisplayBlanksAs = Interpolated]{"
            + "\\plotarea[LeftPadding = \"5mm\" FillFormat{Color = Beige}]"
            + "\\headerarea{\\paragraph{Sales}}"
            + "\\rightarea{\\legend}"
            + "\\xaxis[HasMajorGridLines = true]"
            + "\\yaxis[MinimumScale = 0]"
            + "\\series[Name = \"actual\"]{1, null, \\point[FillFormat{Color = Red}]{3}}"
            + "\\xvalues{\"Jan\", \"Feb\", \"Mar\"}"
            + "}");

        chart.Type.Should().Be(ChartType.Column2D);
        chart.DisplayBlanksAs.Should().Be(BlankType.Interpolated);
        chart.PlotArea.LeftPadding.Millimeter.Should().BeApproximately(5, 1e-4);
        chart.PlotArea.FillFormat.Color.Should().Be(Colors.Beige);
        chart.HeaderArea.Elements.Count.Should().Be(1);
        chart.RightArea.Elements.OfType<Legend>().Should().ContainSingle();
        chart.XAxis.HasMajorGridlines.Should().BeTrue();
        chart.YAxis.MinimumScale.Should().Be(0);
        chart.SeriesCollection[0].Name.Should().Be("actual");
        chart.SeriesCollection[0].Count.Should().Be(3);
        chart.SeriesCollection[0].Elements[1].Should().BeNull();
    }

    /// <summary>
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   A chart whose axis has a title cannot be written out at all.
    ///   <c>AxisTitle.Serialize</c> asks <c>if (this.orientation != null)</c>, and
    ///   <c>orientation</c> is a <see cref="Unit"/> - a value type. The comparison compiles only
    ///   because a string converts to a Unit implicitly: <c>null</c> becomes a string, the string
    ///   becomes a Unit, and the comparison binds to <c>operator !=(Unit, Unit)</c> rather than
    ///   lifting to <c>Unit?</c>. So it is not a null check at all, and the conversion throws.
    ///   </para>
    ///   <para>
    ///   This is the exact mistake the guard inside that conversion was added to name - its message
    ///   says so, and this is the one call site in the whole assembly still making it. The effect
    ///   is that a document with a labelled axis throws on <c>DdlWriter.WriteToString</c>, so it
    ///   cannot be saved as MDDDL. Reading one is fine; it is only writing that fails.
    ///   </para>
    /// </remarks>
    [Fact]
    public void AChartWithATitleOnItsAxisCannotBeWrittenOut()
    {
        var document = new Document();
        var chart = document.AddSection().AddChart(ChartType.Line);
        chart.SeriesCollection.AddSeries().Add(1.0);
        chart.XAxis.Title.Caption = "months";

        var act = () => DdlWriter.WriteToString(document);

        act.Should().Throw<ArgumentNullException>()
            .WithMessage("*IsEmpty*", "the guard inside the Unit conversion names the mistake");
    }

    [Fact]
    public void AChartWhoseAxisWasNeverGivenATitleIsWrittenOutFine()
    {
        // The same chart without the title, to show that it is the title and not the axis.
        var document = new Document();
        var chart = document.AddSection().AddChart(ChartType.Line);
        chart.SeriesCollection.AddSeries().Add(1.0);
        chart.XAxis.MajorTick = 5;

        var act = () => DdlWriter.WriteToString(document);

        act.Should().NotThrow();
    }
}
