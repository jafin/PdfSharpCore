using System;
using System.Collections.Generic;
using AwesomeAssertions;
using ImageMagick;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Drawing;

/// <summary>
///   That a band drawn into the bleed actually puts ink on the outermost row of the sheet.
/// </summary>
/// <remarks>
///   Reading the content stream proves the operators were written; only rasterizing proves
///   nothing clipped them away between the operator and the paper. A bleed that is silently
///   trimmed back to the page is exactly the failure this feature exists to prevent, and it is
///   invisible to every structural assertion.
/// </remarks>
[Collection(RasterizingCollection.Name)]
public class PageBleedRenderingTests : IDisposable
{
    const string OutDir = "Out/PageBleed";

    readonly List<MagickImageCollection> _rasterized = new List<MagickImageCollection>();

    public void Dispose()
    {
        foreach (var collection in _rasterized)
            collection.Dispose();

        _rasterized.Clear();
    }

    static PageBleedRenderingTests()
    {
        GhostscriptSetup.Configure();
    }

    static readonly XUnit Bleed = XUnit.FromMillimeter(3);

    [GoldenImageFact]
    public void ABandBledOffTheTopReachesTheOutermostPixelsOfTheSheet()
    {
        var sheet = Rasterize("band_off_the_top", (gfx, page) =>
            gfx.DrawRectangle(XBrushes.Black,
                new XRect(-Bleed.Point, -Bleed.Point, page.Width.Point + 2 * Bleed.Point, 60)));

        // The very first row of the sheet, which is one bleed above the trim and would be blank
        // if anything had clipped the band back to the page.
        ColourAt(sheet, 0.5, 0).Should().Be("black");
        ColourAt(sheet, 0.02, 0).Should().Be("black");
        ColourAt(sheet, 0.98, 0).Should().Be("black");

        // ...and the paper still shows below it, so "black at the top" is not "black everywhere".
        ColourAt(sheet, 0.5, 0.5).Should().Be("white");
    }

    [GoldenImageFact]
    public void ABandBledOffTheLeftReachesTheOutermostColumnOfTheSheet()
    {
        var sheet = Rasterize("band_off_the_left", (gfx, page) =>
            gfx.DrawRectangle(XBrushes.Black,
                new XRect(-Bleed.Point, -Bleed.Point, 60, page.Height.Point + 2 * Bleed.Point)));

        ColourAt(sheet, 0, 0.5).Should().Be("black");
        ColourAt(sheet, 0, 0.02).Should().Be("black");
        ColourAt(sheet, 0, 0.98).Should().Be("black");

        ColourAt(sheet, 0.5, 0.5).Should().Be("white");
    }

    [GoldenImageFact]
    public void WithoutABleedTheSameDrawingLeavesTheEdgeOfTheSheetBlank()
    {
        // The same band, on an untrimmed page of the same size, drawn from the same coordinates.
        // Its negative corner now falls off the sheet entirely rather than into a bleed.
        var sheet = Rasterize("band_with_no_bleed", (gfx, page) =>
            gfx.DrawRectangle(XBrushes.Black, new XRect(20, 20, page.Width.Point - 40, 60)),
            trimmed: false);

        ColourAt(sheet, 0.5, 0).Should().Be("white");
        ColourAt(sheet, 0, 0.02).Should().Be("white");
    }

    // ----- rasterizing and reading pixels ---------------------------------------------------------

    IMagickImage<byte> Rasterize(string name, Action<XGraphics, PdfPage> draw, bool trimmed = true)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Size = PageSize.A5;
        if (trimmed)
            page.TrimMargins.All = Bleed;

        using (var gfx = XGraphics.FromPdfPage(page))
            draw(gfx, page);

        var images = PdfHelper.Rasterize(document).ImageCollection;
        _rasterized.Add(images);
        PdfHelper.WriteImageCollection(images, OutDir, name);
        return images[0];
    }

    /// <summary>
    ///   Whether the sheet is inked or blank a fraction of the way across and down it, where 0
    ///   and 1 mean the outermost pixel on that axis.
    /// </summary>
    static string ColourAt(IMagickImage<byte> sheet, double across, double down)
    {
        var x = (int)Math.Round(across * (sheet.Width - 1));
        var y = (int)Math.Round(down * (sheet.Height - 1));

        using var pixels = sheet.GetPixels();
        var colour = pixels.GetPixel(x, y).ToColor();
        var luminance = 0.299 * colour.R + 0.587 * colour.G + 0.114 * colour.B;

        // Named rather than numeric so a failure reads as "white where black was wanted" rather
        // than as two numbers the reader has to interpret.
        return luminance < 128 ? "black" : "white";
    }
}
