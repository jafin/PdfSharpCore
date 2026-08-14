using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using ImageMagick;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Annotations;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Annotations;

/// <summary>
///   <see cref="PdfCircleAnnotation"/>, which shares everything but its shape with
///   <see cref="PdfSquareAnnotation"/>.
/// </summary>
/// <remarks>
///   The tests that matter here are the ones a square would fail. Everything else - the interior
///   colour, the border, <c>/RD</c>, rebuilding the appearance when something changes - belongs to
///   <see cref="PdfSquareCircleAnnotation"/> and is covered by <see cref="SquareAnnotationTests"/>;
///   repeating it would only pin the same code twice.
/// </remarks>
[Collection(RasterizingCollection.Name)]
public class CircleAnnotationTests : IDisposable
{
    const string OutDir = "Out/CircleAnnotations";

    readonly List<MagickImageCollection> _rasterized = new List<MagickImageCollection>();

    public void Dispose()
    {
        foreach (MagickImageCollection collection in _rasterized)
            collection.Dispose();

        _rasterized.Clear();
    }

    static CircleAnnotationTests()
    {
        GhostscriptSetup.Configure();
    }

    /// <summary>A wide rectangle, so that "circle" is visibly an ellipse inscribed in it.</summary>
    static readonly XRect Where = new XRect(60, 60, 200, 100);

    [Fact]
    public void ACircleNamesItsSubtype()
    {
        PdfDocument document = new PdfDocument();
        PdfCircleAnnotation circle = new PdfCircleAnnotation();
        document.AddPage().Annotations.Add(circle);

        circle.Elements.GetName("/Subtype").Should().Be("/Circle");
    }

    [Fact]
    public void ACircleCarriesTheSameInteriorAndBorderAsASquare()
    {
        PdfDocument document = new PdfDocument();
        PdfCircleAnnotation circle = new PdfCircleAnnotation();
        document.AddPage().Annotations.Add(circle);
        circle.Rectangle = new PdfRectangle(Where);

        circle.Interior = XColors.SeaGreen;
        circle.BorderWidth = 8;

        // Written by the shared base, so this is a check that a circle really does inherit it
        // rather than a second test of the same code.
        circle.Elements.GetArray("/IC").Elements.Count.Should().Be(3);
        circle.Elements.GetArray("/RD").Elements.GetReal(0).Should().Be(4);
        circle.Elements.GetDictionary("/AP").Should().NotBeNull();
    }

    [GoldenImageFact]
    public void AFilledCircleIsAnEllipseInscribedInTheRectangle()
    {
        IMagickImage<byte> page = Rasterize("filled", circle =>
        {
            circle.Interior = XColors.SeaGreen;
            circle.BorderWidth = 0;
        });

        // The middle is inside the ellipse.
        IsGreen(At(page, Where.X + Where.Width / 2, Where.Y + Where.Height / 2)).Should().BeTrue();

        // And the corners of the rectangle are outside it, which is the whole of the difference
        // between this and PdfSquareAnnotation - a square would have painted all four.
        foreach ((double x, double y) in new[]
                 {
                     (Where.X + 4, Where.Y + 4),
                     (Where.Right - 4, Where.Y + 4),
                     (Where.X + 4, Where.Bottom - 4),
                     (Where.Right - 4, Where.Bottom - 4),
                 })
        {
            IMagickColor<byte> corner = At(page, x, y);
            IsGreen(corner).Should().BeFalse("the corner at {0},{1} is outside the ellipse", x, y);
        }
    }

    [GoldenImageFact]
    public void AnUnfilledCircleIsAnOutlineWithAnEmptyMiddle()
    {
        IMagickImage<byte> page = Rasterize("outline", circle =>
        {
            circle.Color = XColors.SeaGreen;
            circle.BorderWidth = 5;
        });

        Count(page, IsGreen).Should().BeGreaterThan(200);
        IsWhite(At(page, Where.X + Where.Width / 2, Where.Y + Where.Height / 2)).Should().BeTrue();
    }

    IMagickImage<byte> Rasterize(string name, Action<PdfCircleAnnotation> arrange)
    {
        GlobalFontSettings.FontResolver ??= new PinnedFontResolver();

        PdfDocument document = new PdfDocument();
        PdfPage page = document.AddPage();
        XGraphics gfx = XGraphics.FromPdfPage(page);

        PdfCircleAnnotation circle = new PdfCircleAnnotation();
        page.Annotations.Add(circle);
        circle.Rectangle = new PdfRectangle(gfx.Transformer.WorldToDefaultPage(Where));

        arrange(circle);

        MagickImageCollection images = PdfHelper.Rasterize(document).ImageCollection;
        _rasterized.Add(images);
        PdfHelper.WriteImageCollection(images, OutDir, name);
        return images[0];
    }

    /// <summary>
    ///   The pixel at a place on the page, given in the same world coordinates the drawing uses.
    /// </summary>
    static IMagickColor<byte> At(IMagickImage<byte> image, double x, double y)
    {
        double scale = image.Width / PageSizeConverter.ToSize(PageSize.A4).Width;

        using IPixelCollection<byte> pixels = image.GetPixels();
        return pixels.GetPixel((int)(x * scale), (int)(y * scale)).ToColor();
    }

    static bool IsGreen(IMagickColor<byte> c) => c.G > 90 && c.R < 120 && c.B < 140;

    static bool IsWhite(IMagickColor<byte> c) => c.R > 240 && c.G > 240 && c.B > 240;

    static int Count(IMagickImage<byte> image, Func<IMagickColor<byte>, bool> match)
    {
        using IPixelCollection<byte> pixels = image.GetPixels();
        return pixels.Count(p =>
        {
            IMagickColor<byte> c = p.ToColor();
            return c != null && match(c);
        });
    }
}
