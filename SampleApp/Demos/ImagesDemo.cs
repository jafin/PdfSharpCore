using System;
using System.Collections.Generic;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SampleApp.Infrastructure;

namespace SampleApp.Demos;

/// <summary>
///   One photograph, placed every way there is to place it.
/// </summary>
internal sealed class ImagesDemo : PdfDemo
{
    public ImagesDemo() : base() { }

    public override string Name => "Images";

    public override string Summary => "Sizing, stretching, fitting, cropping and rotating one image.";

    public override IReadOnlyList<string> Shows => new[]
    {
        "Natural size, and what PointWidth means next to PixelWidth",
        "Fit and fill worked out by hand - the library has no helper",
        "Cropping through the source-rectangle overload of DrawImage",
        "Rotation about a point, inside Save and Restore",
    };

    public override int PageCount => 2;

    protected override PdfDocument Build(DemoContext context)
    {
        #region example
        PdfDocument document = new PdfDocument();

        XFont label = new XFont("Liberation Sans", 8);
        XFont heading = new XFont("Liberation Sans", 9, XFontStyle.Bold);
        XPen boxPen = new XPen(XColors.Crimson, 0.5) { DashStyle = XDashStyle.Dot };

        // The image is embedded in this assembly rather than read from disk, so it is found
        // wherever the app runs. FromStream takes a factory rather than a stream: the
        // library opens it when it needs it and may do so more than once.
        using XImage photograph = XImage.FromStream(
            () => Assets.Open(Assets.ImagePrefix + "frog-and-toad.jpg"));

        // ---- Page one: sizing ---------------------------------------------------------
        PdfPage page = document.AddPage();
        XGraphics gfx = XGraphics.FromPdfPage(page);

        void Caption(string text, double x, double y) =>
            gfx.DrawString(text, label, XBrushes.DimGray, new XPoint(x, y));

        void Heading(string text, double y)
        {
            gfx.DrawString(text.ToUpperInvariant(), heading, XBrushes.SteelBlue, new XPoint(48, y));
            gfx.DrawLine(XPens.LightGray, 48, y + 5, 548, y + 5);
        }

        Heading("Natural size, and scaled", 56);

        // PointWidth is the pixel count converted at 96 dpi, which is what the image is
        // worth on the page if nothing scales it. The pixels themselves are unchanged
        // whatever rectangle it is drawn into - drawing it smaller does not resample it.
        Caption($"{photograph.PixelWidth} x {photograph.PixelHeight} pixels, "
              + $"{photograph.PointWidth:0.#} x {photograph.PointHeight:0.#} points at 96 dpi",
            48, 78);

        double naturalWidth = photograph.PointWidth;
        double naturalHeight = photograph.PointHeight;

        // Natural size gets a row to itself: at 96 dpi this photograph is most of the width
        // of an A4 page, which is the point worth making about drawing one unscaled.
        double y = 92;
        gfx.DrawImage(photograph, 48, y, naturalWidth, naturalHeight);
        Caption("natural size, from PointWidth and PointHeight", 48, y + naturalHeight + 12);

        // Everything below is placed from the sizes above rather than from numbers typed in,
        // so changing the photograph cannot silently make the page overlap itself.
        y += naturalHeight + 34;
        gfx.DrawImage(photograph, 48, y, naturalWidth / 2, naturalHeight / 2);
        Caption("half", 48, y + naturalHeight / 2 + 12);

        double quarterX = 48 + naturalWidth / 2 + 24;
        gfx.DrawImage(photograph, quarterX, y, naturalWidth / 4, naturalHeight / 4);
        Caption("quarter", quarterX, y + naturalHeight / 4 + 12);

        y += naturalHeight / 2 + 40;
        Heading("Stretched out of proportion", y);
        y += 20;
        gfx.DrawImage(photograph, 48, y, 300, 84);
        Caption("a rectangle the image does not share the shape of", 48, y + 96);

        // ---- Page two: fitting, cropping and turning -----------------------------------
        page = document.AddPage();
        gfx = XGraphics.FromPdfPage(page);

        Heading("Fit and fill", 56);
        Caption("There is no fit or cover helper. The arithmetic below is the whole of it.",
            48, 78);

        XRect box = new XRect(48, 92, 200, 200);

        // Fit, or "contain": the largest scale at which the whole image is inside the box,
        // so the box shows through on two sides. Min of the two ratios.
        double fit = Math.Min(box.Width / naturalWidth, box.Height / naturalHeight);
        XRect fitted = new XRect(
            box.X + (box.Width - naturalWidth * fit) / 2,
            box.Y + (box.Height - naturalHeight * fit) / 2,
            naturalWidth * fit,
            naturalHeight * fit);

        gfx.DrawImage(photograph, fitted);
        gfx.DrawRectangle(boxPen, box);
        Caption("fit: Math.Min, the whole image, letterboxed", 48, 306);

        // Fill, or "cover": the smallest scale at which the image covers the box, so the
        // overflow has to be cut off. Max of the two ratios, and then the part of the
        // image to keep is given as a source rectangle in the image's own pixels.
        XRect coverBox = new XRect(300, 92, 200, 200);
        double cover = Math.Max(coverBox.Width / naturalWidth, coverBox.Height / naturalHeight);
        double sourceWidth = coverBox.Width / cover * photograph.PixelWidth / naturalWidth;
        double sourceHeight = coverBox.Height / cover * photograph.PixelHeight / naturalHeight;

        gfx.DrawImage(photograph, coverBox,
            new XRect(
                (photograph.PixelWidth - sourceWidth) / 2,
                (photograph.PixelHeight - sourceHeight) / 2,
                sourceWidth,
                sourceHeight),
            XGraphicsUnit.Point);

        gfx.DrawRectangle(boxPen, coverBox);
        Caption("fill: Math.Max, centre kept, edges cropped away", 300, 306);

        Heading("Turned", 340);

        // Every transform is undone by restoring the state that was saved before it. There
        // is no ResetTransform, so a Save that is not Restored leaks into everything drawn
        // afterwards.
        double[] angles = { 0, 15, 30, 45 };
        double x = 110;
        foreach (double angle in angles)
        {
            XGraphicsState state = gfx.Save();
            gfx.RotateAtTransform(angle, new XPoint(x, 430));
            gfx.DrawImage(photograph, x - 45, 430 - 30, 90, 60);
            gfx.Restore(state);

            Caption($"{angle:0}°", x - 6, 500);
            x += 130;
        }

        Caption("RotateAtTransform turns the page about a point, then the image is drawn "
              + "square onto it.", 48, 520);
        #endregion

        return document;
    }
}
