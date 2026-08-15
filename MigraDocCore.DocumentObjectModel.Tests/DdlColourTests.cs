using System;
using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel.IO;
using Xunit;

namespace MigraDocCore.DocumentObjectModel.Tests;

/// <summary>
///   Reading a colour out of MDDDL. The grammar allows six spellings and the parser has a method
///   for each: a name, a number, a hexadecimal number, <c>RGB(r, g, b)</c>,
///   <c>CMYK(c, m, y, k)</c> with an optional alpha in front, and <c>GRAY(g)</c>. Two more,
///   <c>HSB</c> and <c>Lab</c>, are named in the switch and throw.
///   <para>
///   Worth its own file because a colour is the one value in the format whose spelling a person
///   chooses rather than the serializer: PDFsharp writes a name or a hex number and never writes
///   RGB, CMYK or GRAY, so those three arms are reached only by a file somebody wrote by hand -
///   and were reached by nothing at all until these tests.
///   </para>
/// </summary>
public class DdlColourTests
{
    static Color ColourFrom(string spelling)
    {
        var document = DdlReader.DocumentFromString(
            "\\document{\\section{\\paragraph[Format{Font{Color = " + spelling + "}}]{t}}}");

        // Cast rather than "as": a paragraph that came back as something else would otherwise
        // become an empty colour, and every assertion below would be comparing two blanks.
        return ((Paragraph)document.LastSection.Elements[0]).Format.Font.Color;
    }

    // ----- the spellings the serializer writes ------------------------------------------------------

    [Theory]
    [InlineData("Red")]
    [InlineData("Navy")]
    [InlineData("DarkSeaGreen")]
    public void AColourCanBeNamed(string name)
    {
        ColourFrom(name).Should().Be(Color.Parse(name));
    }

    [Fact]
    public void AColourCanBeAHexadecimalNumberWithItsAlphaInFront()
    {
        ColourFrom("0xFF0000FF").Should().Be(Colors.Blue);
    }

    [Fact]
    public void AColourCanBeAPlainNumber()
    {
        // The same value written in decimal. 255 with no alpha byte is a colour that is entirely
        // transparent blue, which is a legal thing for a file to say.
        ColourFrom("255").Argb.Should().Be(255);
    }

    // ----- the spellings only a person writes ---------------------------------------------------------

    [Fact]
    public void AColourCanBeGivenAsRedGreenAndBlue()
    {
        ColourFrom("RGB(255, 128, 0)").Should().Be(new Color(255, 128, 0));
    }

    [Theory]
    [InlineData("RGB(256, 0, 0)")]
    [InlineData("RGB(0, 300, 0)")]
    public void EachPartOfAnRgbColourHasToBeAByte(string spelling)
    {
        var act = () => ColourFrom(spelling);

        act.Should().Throw<Exception>("the range is 0 to 255");
    }

    [Fact]
    public void AColourCanBeGivenInTheFourInksOfPrinting()
    {
        // CMYK is what a printer works in, and the DOM keeps it as such rather than converting it
        // away: a colour given in inks reports itself as a CMYK colour afterwards.
        var colour = ColourFrom("CMYK(0, 100, 100, 0)");

        colour.IsCmyk.Should().BeTrue();
        colour.M.Should().BeApproximately(100, 0.5);
        colour.Y.Should().BeApproximately(100, 0.5);
        colour.C.Should().BeApproximately(0, 0.5);
        colour.K.Should().BeApproximately(0, 0.5);
    }

    [Fact]
    public void ACmykColourCanCarryItsOwnTransparencyInFront()
    {
        // Five numbers rather than four, and the first is the alpha - the one arm of this parser
        // that changes what the other four mean.
        var colour = ColourFrom("CMYK(50, 0, 100, 100, 0)");

        colour.IsCmyk.Should().BeTrue();
        colour.Alpha.Should().BeApproximately(50, 0.5);
        colour.M.Should().BeApproximately(100, 0.5);
    }

    [Fact]
    public void TheInksAreGivenAsPercentagesAndNothingElseIsAllowed()
    {
        var tooHigh = () => ColourFrom("CMYK(0, 0, 0, 101)");
        var negative = () => ColourFrom("CMYK(0, 0, 0, -1)");

        tooHigh.Should().Throw<Exception>();
        negative.Should().Throw<Exception>();
    }

    [Fact]
    public void TheInksCanBeGivenWithADecimalPoint()
    {
        var colour = ColourFrom("CMYK(12.5, 0, 0, 0)");

        colour.C.Should().BeApproximately(12.5, 0.5);
    }

    [Fact]
    public void AShadeOfGreyIsHowMuchInkRatherThanHowMuchLight()
    {
        // GRAY(100) is a hundred per cent ink, which is black. The printer's way round, and the
        // opposite of the one a screen suggests.
        ColourFrom("GRAY(100)").Should().Be(Colors.Black);
        ColourFrom("GRAY(0)").Should().Be(Colors.White);
        ColourFrom("GRAY(50)").Argb.Should().Be(0xFF808080);
    }

    [Fact]
    public void AShadeOfGreyIsAPercentageToo()
    {
        var act = () => ColourFrom("GRAY(101)");

        act.Should().Throw<Exception>();
    }

    // ----- the spellings that are named but not written -------------------------------------------------

    /// <summary>
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   <c>HSB</c> and <c>Lab</c> are named in the parser's switch over colour spaces and both
    ///   raise <see cref="NotImplementedException"/>. Neither reaches the caller. The document
    ///   reads, the colour comes out empty, and the error list the caller supplied is untouched -
    ///   so a file asking for a colour this reader cannot work out loses it without saying so, and
    ///   the text is drawn in whatever it would have inherited.
    ///   </para>
    ///   <para>
    ///   The contrast with a misspelt colour <em>name</em> is what makes it a defect rather than a
    ///   policy: <c>Chartreuse2000</c> is refused outright, as the test below shows. Two ways of
    ///   getting a colour wrong, one loud and one silent.
    ///   </para>
    /// </remarks>
    [Theory]
    [InlineData("HSB(0, 0, 0)")]
    [InlineData("Lab(0, 0, 0)")]
    public void AColourSpaceTheReaderCannotWorkOutIsLostWithoutAWord(string spelling)
    {
        var errors = new DdlReaderErrors();

        var read = DdlReader.ObjectFromString(
            "\\document{\\section{\\paragraph[Format{Font{Color = " + spelling + "}}]{t}}}", errors);

        read.Should().NotBeNull("the document still reads");
        ColourFrom(spelling).Should().Be(Color.Empty, "and the colour is gone");
        errors.ErrorCount.Should().Be(0, "with nothing to say it was dropped");
    }

    [Theory]
    [InlineData("Chartreuse2000", "a name that is no colour")]
    [InlineData("\"MyColor\"", "a quoted name, which the grammar comment offers and the code does not read")]
    public void SomethingThatIsNoColourAtAllIsRefused(string spelling, string why)
    {
        var act = () => ColourFrom(spelling);

        act.Should().Throw<Exception>(why);
    }

    // ----- where else a colour can appear ------------------------------------------------------------------

    [Fact]
    public void EveryPlaceThatTakesAColourReadsTheSameGrammar()
    {
        // One parser, reached from the borders, the shading and the font alike - so a spelling that
        // works in one place works in all of them.
        var document = DdlReader.DocumentFromString(
            "\\document{\\section{\\paragraph[Format{"
            + "Borders{Color = RGB(1, 2, 3)} "
            + "Shading{Color = GRAY(50)} "
            + "Font{Color = CMYK(0, 0, 0, 100)}}]{t}}}");

        var format = (document.LastSection.Elements[0] as Paragraph).Format;
        format.Borders.Color.Should().Be(new Color(1, 2, 3));
        format.Shading.Color.R.Should().Be(format.Shading.Color.G, "grey is equal in every channel");
        format.Font.Color.IsCmyk.Should().BeTrue();
    }
}
