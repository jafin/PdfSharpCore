using System;
using System.Collections.Generic;
using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SampleApp.Infrastructure;

namespace SampleApp.Demos;

/// <summary>
///   A cover whose photograph runs off three edges of the paper, drawn on a sheet larger than the
///   page it will be cut down to.
/// </summary>
internal sealed class BleedDemo : PdfDemo
{
    public BleedDemo() : base() { }

    public override string Name => "Bleed";

    public override string Summary => "A photograph bled off three edges of a page that will be trimmed.";

    public override IReadOnlyList<string> Shows => new[]
    {
        "PdfPage.TrimMargins: a sheet larger than the page, with the origin still on the page",
        "Drawing at negative coordinates to reach past the trim and onto the bleed",
        "Width and Height still reporting the trimmed size while the document is being built",
        "The five page boxes a trimmed page is saved with, listed on the page itself",
    };

    public override int PageCount => 1;

    protected override PdfDocument Build(DemoContext context)
    {
        #region example
        PdfDocument document = new PdfDocument();

        // Three millimetres is what a printer asks for and what page-layout applications
        // default to: enough that a cut landing slightly off the mark still falls on ink.
        XUnit bleed = XUnit.FromMillimeter(3);

        PdfPage page = document.AddPage();
        page.Size = PageSize.A5;

        // The one line that makes this a bleeding page. The sheet written to the file grows by
        // the margin on each edge, but the page a caller draws on does not: the origin moves to
        // the corner of the *trimmed* page, so every coordinate below is measured from where the
        // paper will be cut. Nothing in this demo would have to change if the margin were
        // removed - it would simply lose its bleed.
        page.TrimMargins.All = bleed;

        // Points, because that is the only unit TrimMargins supports. XGraphics asserts it.
        XGraphics gfx = XGraphics.FromPdfPage(page);

        // Still A5. The extra sheet is not part of the page, and reading Width here rather than
        // hard-coding 420 is what keeps the layout right if the page size is changed.
        double width = page.Width.Point;
        double height = page.Height.Point;
        double over = bleed.Point;

        using XImage photograph = XImage.FromStream(
            () => Assets.Open(Assets.ImagePrefix + "frog-and-toad.jpg"));

        // ---- The photograph, off three edges ------------------------------------------------
        //
        // Negative coordinates are the whole technique. The rectangle starts one bleed above and
        // one bleed left of the origin and is two bleeds wider than the page, so it covers the
        // sheet on the top, left and right and stops short of the bottom.
        XRect bled = new XRect(-over, -over, width + 2 * over, height * 0.62 + over);

        // Filled rather than fitted, so the photograph covers every point of that rectangle
        // instead of leaving paper showing at two edges. The scale is the larger of the two
        // ratios, and the part of the image that survives is given as a source rectangle in the
        // image's own pixels - the same arithmetic the Images demo works through.
        double cover = Math.Max(bled.Width / photograph.PointWidth, bled.Height / photograph.PointHeight);
        double sourceWidth = bled.Width / cover * photograph.PixelWidth / photograph.PointWidth;
        double sourceHeight = bled.Height / cover * photograph.PixelHeight / photograph.PointHeight;

        gfx.DrawImage(photograph, bled,
            new XRect(
                (photograph.PixelWidth - sourceWidth) / 2,
                (photograph.PixelHeight - sourceHeight) / 2,
                sourceWidth,
                sourceHeight),
            XGraphicsUnit.Point);

        // ---- The words, safely inside the trim ----------------------------------------------
        XFont title = new XFont("Liberation Sans", 34, XFontStyle.Bold);
        XFont body = new XFont("Liberation Sans", 10);
        XFont note = new XFont("Liberation Sans", 7);

        double textTop = bled.Bottom + 28;
        gfx.DrawString("Bleed", title, XBrushes.Black, new XPoint(40, textTop));

        string[] paragraph =
        {
            "The photograph above runs off the top, left and right of this page. It was drawn",
            "from (-3mm, -3mm) onto a sheet three millimetres larger than the page on every",
            "edge, and the guillotine cuts along the dashed rule below - through the middle of",
            "the ink, so that a cut a fraction off the mark still lands on the picture rather",
            "than on white paper.",
        };

        double y = textTop + 26;
        foreach (string line in paragraph)
        {
            gfx.DrawString(line, body, XBrushes.Black, new XPoint(40, y));
            y += 14;
        }

        // ---- The trim boundary, drawn so the bleed can be seen on screen --------------------
        //
        // This rule is part of the demonstration and not part of the artwork. On the printed
        // sheet the cut is where it is whether or not anything is drawn there; on screen the
        // bleed is invisible without it, because a reader shows the whole sheet and nothing
        // marks which part of it survives.
        XPen cut = new XPen(XColors.Crimson, 0.5) { DashStyle = XDashStyle.Dash };
        gfx.DrawRectangle(cut, new XRect(0, 0, width, height));

        // ---- What the file will say ---------------------------------------------------------
        //
        // Worked out here rather than read back from the saved page, because the boxes are
        // written during the save and this demo hands the document to its caller unsaved.
        double sheetWidth = width + 2 * over;
        double sheetHeight = height + 2 * over;

        (string Box, string Value)[] boxes =
        {
            ("MediaBox", $"[0 0 {sheetWidth:0.###} {sheetHeight:0.###}]  the sheet"),
            ("CropBox",  $"[0 0 {sheetWidth:0.###} {sheetHeight:0.###}]  what a reader shows"),
            ("BleedBox", $"[0 0 {sheetWidth:0.###} {sheetHeight:0.###}]  how far the ink may run"),
            ("TrimBox",  $"[{over:0.###} {over:0.###} {sheetWidth - over:0.###} {sheetHeight - over:0.###}]  where it is cut"),
            ("ArtBox",   $"[{over:0.###} {over:0.###} {sheetWidth - over:0.###} {sheetHeight - over:0.###}]  the meaningful content"),
        };

        y += 18;
        foreach ((string Box, string Value) row in boxes)
        {
            gfx.DrawString(row.Box, note, XBrushes.DimGray, new XPoint(40, y));
            gfx.DrawString(row.Value, note, XBrushes.DimGray, new XPoint(92, y));
            y += 10;
        }

        gfx.DrawString("the dashed rule is the trim edge - it is drawn by this demo, and would "
                     + "not be on a page going to press",
            note, XBrushes.Crimson, new XPoint(40, y + 12));
        #endregion

        return document;
    }
}
