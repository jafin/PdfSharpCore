using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using ImageMagick;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Drawing;

/// <summary>
///   <see cref="TextStateOperatorTests"/> checks that Tc, Tw and Tz are written and what they are
///   given. These check that they do something: the page is rasterized and the ink measured.
///   <para>
///   Worth the Ghostscript dependency for one reason above the others. The word spacing a Unicode
///   font gets is drawn by hand, as numbers inside a TJ array, and a number there is
///   <em>subtracted</em> from the horizontal position - so the number that opens a gap is a
///   negative one. That sign is read off the specification and is exactly the kind of thing that
///   is easy to have backwards; a test that only checks the number it wrote would agree with
///   itself either way.
///   </para>
/// </summary>
[Collection(RasterizingCollection.Name)]
public class TextStateRenderingTests : IDisposable
{
    const double FontSize = 24;
    const double PageWidth = 400;
    const double PageHeight = 80;

    /// <summary>Rasterization is at 300 dpi, and PDF measures in 72nds of an inch.</summary>
    const double PixelsPerPoint = 300.0 / 72.0;

    readonly List<MagickImageCollection> _rasterized = new();

    static XFont WinAnsiFont => new XFont("Arial", FontSize, XFontStyle.Regular, XPdfFontOptions.WinAnsiDefault);
    static XFont UnicodeFont => new XFont("Arial", FontSize, XFontStyle.Regular, XPdfFontOptions.UnicodeDefault);

    /// <summary>
    ///   How wide the inked part of the page is, in points. Not the measured width of the string:
    ///   the trailing spacing after the last glyph moves the pen without marking the page, so this
    ///   is one gap short of what MeasureString answers, by design.
    /// </summary>
    double InkedWidthOf(string text, XFont font, XStringFormat format)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = PageWidth;
        page.Height = PageHeight;

        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString(text, font, XBrushes.Black, 20, 50, format);

        var images = PdfHelper.Rasterize(document).ImageCollection;
        _rasterized.Add(images);

        using var pixels = images[0].GetPixels();
        var inked = pixels
            .Where(pixel =>
            {
                var colour = pixel.ToColor();
                return colour != null && colour.R < 128 && colour.G < 128 && colour.B < 128;
            })
            .Select(pixel => pixel.X)
            .ToList();

        inked.Should().NotBeEmpty("the page should have text drawn on it");
        return (inked.Max() - inked.Min()) / PixelsPerPoint;
    }

    [GoldenImageFact]
    public void CharacterSpacingPushesTheGlyphsApartOnThePage()
    {
        const double spacing = 4;

        var plain = InkedWidthOf("abc", WinAnsiFont, XStringFormats.Default);

        var format = XStringFormats.Default;
        format.CharacterSpacing = spacing;
        var spaced = InkedWidthOf("abc", WinAnsiFont, format);

        // Three glyphs, so two gaps between them. The spacing after the last one moves the pen
        // past the end and leaves no ink to find.
        (spaced - plain).Should().BeApproximately(2 * spacing, 1.5);
    }

    [GoldenImageFact]
    public void WordSpacingPushesTheWordsApartForAFontEncodedAsWinAnsi()
    {
        const double spacing = 6;

        var plain = InkedWidthOf("a b c", WinAnsiFont, XStringFormats.Default);

        var format = XStringFormats.Default;
        format.WordSpacing = spacing;
        var spaced = InkedWidthOf("a b c", WinAnsiFont, format);

        // Two spaces, each with ink after it, so the run grows by both.
        (spaced - plain).Should().BeApproximately(2 * spacing, 1.5);
    }

    [GoldenImageFact]
    public void WordSpacingPushesTheWordsApartForAFontEncodedAsUnicode()
    {
        const double spacing = 6;

        var plain = InkedWidthOf("a b c", UnicodeFont, XStringFormats.Default);

        var format = XStringFormats.Default;
        format.WordSpacing = spacing;
        var spaced = InkedWidthOf("a b c", UnicodeFont, format);

        // The one that goes through the TJ array. Apart, not together - a sign the wrong way
        // round would bring the words closer and this would come out negative.
        (spaced - plain).Should().BeApproximately(2 * spacing, 1.5);
    }

    [GoldenImageFact]
    public void TheTwoEncodingsSpaceTheirWordsOutTheSameAmount()
    {
        var format = XStringFormats.Default;
        format.WordSpacing = 6;

        // Tw for one, a TJ array for the other, and the reader must not be able to tell.
        var winAnsi = InkedWidthOf("a b c", WinAnsiFont, format);
        var unicode = InkedWidthOf("a b c", UnicodeFont, format);

        unicode.Should().BeApproximately(winAnsi, 1.5);
    }

    [GoldenImageFact]
    public void HorizontalScalingSquashesTheTextOnThePage()
    {
        var plain = InkedWidthOf("abcdef", WinAnsiFont, XStringFormats.Default);

        var format = XStringFormats.Default;
        format.HorizontalScaling = 50;
        var squashed = InkedWidthOf("abcdef", WinAnsiFont, format);

        squashed.Should().BeApproximately(plain / 2, 1.5);
    }

    public void Dispose()
    {
        foreach (var images in _rasterized)
            images.Dispose();
    }
}
