using System;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Content.Objects;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Drawing;

/// <summary>
///   The text state a caller sets on an <see cref="XStringFormat"/> reaches the content stream:
///   character spacing as Tc, word spacing as Tw, horizontal scaling as Tz.
///   <para>
///   Before this the renderer wrote Tc only to fake bold and wrote Tw and Tz never, so the three
///   properties measured for in <see cref="TextStateMeasurementTests"/> had no effect on what was
///   drawn. These check the other half of that: that what is measured is what is written.
///   </para>
/// </summary>
public class TextStateOperatorTests
{
    const double FontSize = 12;

    /// <summary>Liberation Sans, encoded as WinAnsi - the default, and what Tw can speak for.</summary>
    static XFont WinAnsiFont => new XFont("Arial", FontSize, XFontStyle.Regular, XPdfFontOptions.WinAnsiDefault);

    /// <summary>The same face embedded as Identity-H, whose two-byte codes Tw cannot reach.</summary>
    static XFont UnicodeFont => new XFont("Arial", FontSize, XFontStyle.Regular, XPdfFontOptions.UnicodeDefault);

    /// <summary>
    ///   Source Code Pro ships only a regular face, so asking for bold gets bold simulation -
    ///   which draws its own character spacing, and is the thing a caller's spacing has to
    ///   compose with rather than replace.
    /// </summary>
    static XFont BoldSimulatedFont =>
        new XFont(PinnedFontResolver.CffFamilyName, FontSize, XFontStyle.Bold, XPdfFontOptions.WinAnsiDefault);

    static PdfPage PageShowing(Action<XGraphics> draw)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            draw(gfx);
        return page;
    }

    static PdfPage PageShowing(string text, XFont font, XStringFormat format)
    {
        return PageShowing(gfx => gfx.DrawString(text, font, XBrushes.Black, 20, 40, format));
    }

    // ----- nothing asked for, nothing written ---------------------------------------------------

    [Fact]
    public void AFormatThatAsksForNothingWritesNoTextStateAtAll()
    {
        // The guard on every existing document: a default format has to leave the content stream
        // exactly as it was before any of this existed.
        var page = PageShowing("Hello world", WinAnsiFont, XStringFormats.Default);

        TextOperators.NumbersGivenTo(page, OpCodeName.Tc).Should().BeEmpty();
        TextOperators.NumbersGivenTo(page, OpCodeName.Tw).Should().BeEmpty();
        TextOperators.NumbersGivenTo(page, OpCodeName.Tz).Should().BeEmpty();
        TextOperators.ShowTextOperators(page).Should().Equal(OpCodeName.Tj);
    }

    // ----- character spacing, A3 ----------------------------------------------------------------

    [Fact]
    public void CharacterSpacingIsWrittenAsTc()
    {
        var format = XStringFormats.Default;
        format.CharacterSpacing = 1.5;

        TextOperators.NumbersGivenTo(PageShowing("Hello", WinAnsiFont, format), OpCodeName.Tc)
            .Should().Equal(1.5);
    }

    [Fact]
    public void ANegativeCharacterSpacingIsWrittenAsItIs()
    {
        var format = XStringFormats.Default;
        format.CharacterSpacing = -0.75;

        TextOperators.NumbersGivenTo(PageShowing("Hello", WinAnsiFont, format), OpCodeName.Tc)
            .Should().Equal(-0.75);
    }

    [Fact]
    public void CharacterSpacingAddsToBoldSimulationRatherThanReplacingIt()
    {
        // Bold simulation strokes the glyphs and spaces them out to match. Overwriting that
        // spacing with the caller's would quietly un-bolden the text, so the two are added.
        var simulatedOnly = TextOperators.NumbersGivenTo(
            PageShowing("Hello", BoldSimulatedFont, XStringFormats.Default), OpCodeName.Tc);

        simulatedOnly.Should().ContainSingle().Which.Should().BeGreaterThan(0);

        var format = XStringFormats.Default;
        format.CharacterSpacing = 2;
        var withBoth = TextOperators.NumbersGivenTo(
            PageShowing("Hello", BoldSimulatedFont, format), OpCodeName.Tc);

        withBoth.Should().ContainSingle().Which.Should().BeApproximately(simulatedOnly[0] + 2, 1e-6);
    }

    // ----- word spacing, A4 ---------------------------------------------------------------------

    [Fact]
    public void WordSpacingIsWrittenAsTwForAFontEncodedAsWinAnsi()
    {
        var format = XStringFormats.Default;
        format.WordSpacing = 4;

        var page = PageShowing("a b c", WinAnsiFont, format);

        TextOperators.NumbersGivenTo(page, OpCodeName.Tw).Should().Equal(4);
        // One byte per code, so Tw reaches the spaces and the run stays in one piece.
        TextOperators.ShowTextOperators(page).Should().Equal(OpCodeName.Tj);
    }

    [Fact]
    public void WordSpacingIsDrawnByHandForAFontEncodedAsUnicode()
    {
        var format = XStringFormats.Default;
        format.WordSpacing = 5;

        var page = PageShowing("a b c", UnicodeFont, format);

        // Tw counts single-byte code 32 only, and Identity-H writes two bytes per code, so a Tw
        // here would be accepted and ignored. The run is broken up and moved apart instead.
        TextOperators.NumbersGivenTo(page, OpCodeName.Tw).Should().NotContain(5);
        TextOperators.ShowTextOperators(page).Should().Equal(OpCodeName.TJ);

        // -wordSpacing * 1000 / fontSize, the number that buys one word spacing back.
        var expected = -5 * 1000 / FontSize;
        TextOperators.TJAdjustments(page).Should().HaveCount(2)
            .And.AllSatisfy(adjustment => adjustment.Should().BeApproximately(expected, 0.001));
    }

    [Fact]
    public void AUnicodeRunIsBrokenIntoOnePieceMoreThanItHasSpaces()
    {
        var format = XStringFormats.Default;
        format.WordSpacing = 5;

        // "a b c" - the space stays with the word in front of it, so the pieces are "a ", "b "
        // and "c", with the gap opened up after each of the first two.
        TextOperators.TJRunCounts(PageShowing("a b c", UnicodeFont, format)).Should().Equal(3);
    }

    [Fact]
    public void ATrailingSpaceStillGetsItsWordSpacing()
    {
        var format = XStringFormats.Default;
        format.WordSpacing = 5;

        // Nothing follows the space, so there is no run after the gap - but the gap is still
        // owed, because measurement counted it.
        var page = PageShowing("a ", UnicodeFont, format);

        TextOperators.TJRunCounts(page).Should().Equal(1);
        TextOperators.TJAdjustments(page).Should().HaveCount(1);
    }

    [Fact]
    public void AUnicodeRunWithNoWordSpacingIsStillDrawnInOneGo()
    {
        // A TJ array of one run would be correct but wasteful, and would change every existing
        // document that uses a Unicode font.
        var page = PageShowing("a b c", UnicodeFont, XStringFormats.Default);

        TextOperators.ShowTextOperators(page).Should().Equal(OpCodeName.Tj);
    }

    [Fact]
    public void AUnicodeRunWithoutSpacesIsDrawnInOneGoEvenWhenWordSpacingIsAskedFor()
    {
        var format = XStringFormats.Default;
        format.WordSpacing = 5;

        var page = PageShowing("abc", UnicodeFont, format);

        TextOperators.TJRunCounts(page).Should().Equal(1);
        TextOperators.TJAdjustments(page).Should().BeEmpty();
    }

    // ----- horizontal scaling, A5 ---------------------------------------------------------------

    [Fact]
    public void HorizontalScalingIsWrittenAsTz()
    {
        var format = XStringFormats.Default;
        format.HorizontalScaling = 75;

        TextOperators.NumbersGivenTo(PageShowing("Hello", WinAnsiFont, format), OpCodeName.Tz)
            .Should().Equal(75);
    }

    [Fact]
    public void AScalingOfAHundredIsNotWrittenBecauseThatIsWhereItStarts()
    {
        var format = XStringFormats.Default;
        format.HorizontalScaling = 100;

        TextOperators.NumbersGivenTo(PageShowing("Hello", WinAnsiFont, format), OpCodeName.Tz)
            .Should().BeEmpty();
    }

    // ----- the state is state -------------------------------------------------------------------

    [Fact]
    public void TheTextStateIsWrittenOnceForTwoStringsThatShareIt()
    {
        var format = XStringFormats.Default;
        format.CharacterSpacing = 1;
        format.HorizontalScaling = 80;

        var page = PageShowing(gfx =>
        {
            gfx.DrawString("first", WinAnsiFont, XBrushes.Black, 20, 40, format);
            gfx.DrawString("second", WinAnsiFont, XBrushes.Black, 20, 60, format);
        });

        // These are graphics state parameters, not something each string carries with it.
        TextOperators.NumbersGivenTo(page, OpCodeName.Tc).Should().Equal(1);
        TextOperators.NumbersGivenTo(page, OpCodeName.Tz).Should().Equal(80);
    }

    [Fact]
    public void TheTextStateIsWrittenAgainWhenItChanges()
    {
        var spaced = XStringFormats.Default;
        spaced.CharacterSpacing = 3;

        var page = PageShowing(gfx =>
        {
            gfx.DrawString("spaced", WinAnsiFont, XBrushes.Black, 20, 40, spaced);
            gfx.DrawString("plain", WinAnsiFont, XBrushes.Black, 20, 60, XStringFormats.Default);
        });

        // Back to nothing has to be said out loud, or the second string keeps the first's spacing.
        TextOperators.NumbersGivenTo(page, OpCodeName.Tc).Should().Equal(3, 0);
    }

    [Fact]
    public void TheTextStateGoesBackWithTheGraphicsState()
    {
        var spaced = XStringFormats.Default;
        spaced.CharacterSpacing = 3;

        var page = PageShowing(gfx =>
        {
            var state = gfx.Save();
            gfx.DrawString("spaced", WinAnsiFont, XBrushes.Black, 20, 40, spaced);
            gfx.Restore(state);
            gfx.DrawString("plain", WinAnsiFont, XBrushes.Black, 20, 60, XStringFormats.Default);
        });

        // Q puts Tc back to 0 along with everything else, so the renderer must not think it is
        // still 3 and write a 0 Tc that is already true - nor think it is 0 and write nothing
        // when it is not. One Tc, for the string that asked for one.
        TextOperators.NumbersGivenTo(page, OpCodeName.Tc).Should().Equal(3);
    }
}
