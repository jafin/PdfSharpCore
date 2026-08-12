using System.Collections.Generic;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Drawing.Layout.enums;
using PdfSharpCore.Pdf;
using SampleApp.Infrastructure;

namespace SampleApp.Demos;

/// <summary>
///   A feature opener and its continuation: a full bleed photograph, a drop cap, and a pull quote.
/// </summary>
/// <remarks>
///   Everything here that looks like a typographic feature is arithmetic. There is no drop cap, no
///   full bleed and no pull quote in the library - there is a rectangle, a transform, and
///   <c>MeasureString</c> to work out where things go.
/// </remarks>
internal sealed class MagazineDemo : PdfDemo
{
    public MagazineDemo() : base() { }

    public override string Name => "Magazine";

    public override string Summary => "A feature opener with a drop cap and a pull quote.";

    public override IReadOnlyList<string> Shows => new[]
    {
        "A photograph bled off three edges, clipped to its band",
        "A scrim that fades out as well as down, from a gradient with alpha in its colours",
        "A drop cap sized by MeasureString, with the text broken to fit beside it",
        "A title turned into a path by AddString and filled with a gradient",
        "A pull quote slanted with ObliqueAngle",
    };

    public override int PageCount => 2;

    protected override PdfDocument Build(DemoContext context)
    {
        #region example
        const string Serif = "Liberation Serif";
        const string Sans = "Liberation Sans";

        const string Opening =
            "here is no drop cap in this library, and no pull quote, and nothing that bleeds "
            + "a photograph off the edge of a page. There is a rectangle, a transform, and a "
            + "way to ask how wide a piece of text will be. Everything on this spread is one "
            + "of those three things used carefully, which is the point worth taking away "
            + "from it. ";

        const string Body =
            "The first letter below is drawn on its own at four times the body size, and the "
            + "opening lines are broken to fit the space left beside it. Nothing decides that "
            + "break except a loop that adds one word at a time and asks the formatter how "
            + "tall the result would be. When the answer stops fitting, the previous word was "
            + "the last one. The remainder is flowed into two columns underneath, which the "
            + "formatter will do in one call. ";

        PdfDocument document = new PdfDocument();
        document.Info.Title = "Feature";

        // ---- Page one: the opener --------------------------------------------------------
        PdfPage page = document.AddPage();
        XGraphics gfx = XGraphics.FromPdfPage(page);
        XTextFormatter formatter = new XTextFormatter(gfx);

        double width = page.Width.Point;
        double height = page.Height.Point;
        const double margin = 46;

        void RunningFoot(XGraphics target, string folio) =>
            target.DrawString(folio, new XFont(Sans, 8), XBrushes.Gray,
                new XRect(margin, height - margin, width - margin * 2, 12),
                XStringFormats.TopCenter);

        using XImage photograph = XImage.FromStream(
            () => Assets.Open(Assets.ImagePrefix + "frog-and-toad.jpg"));

        // A full bleed is a rectangle that starts at the page edge and finishes past it.
        // The image is scaled to cover, exactly as in the Images demo - but covering means
        // one dimension overflows, and an overflow off the bottom lands on the article
        // rather than off the page. So it is clipped to the band it belongs in, and the
        // clip is undone by restoring the state, there being no ResetClip.
        double bleedHeight = height * 0.42;
        double cover = System.Math.Max(
            width / photograph.PointWidth, bleedHeight / photograph.PointHeight);

        XGraphicsState bleed = gfx.Save();
        gfx.IntersectClip(new XRect(0, 0, width, bleedHeight));
        gfx.DrawImage(photograph, 0, 0,
            photograph.PointWidth * cover, photograph.PointHeight * cover);

        // A scrim, so white type has something to sit on whatever the photograph happens to
        // be doing underneath it. One gradient, from nothing at the top to nearly opaque at
        // the foot of the band: a gradient honours the alpha of its colours, so this fades out
        // as well as down and the picture shows through the top of it.
        XRect scrim = new XRect(0, bleedHeight * 0.45, width, bleedHeight * 0.55);
        gfx.DrawRectangle(
            new XLinearGradientBrush(scrim,
                XColor.FromArgb(0, 12, 14, 10), XColor.FromArgb(190, 12, 14, 10),
                XLinearGradientMode.Vertical),
            scrim);

        gfx.Restore(bleed);

        gfx.DrawString("FEATURE", new XFont(Sans, 9, XFontStyle.Bold), XBrushes.White,
            new XRect(margin, bleedHeight - 128, width - margin * 2, 14),
            new XStringFormat { CharacterSpacing = 3 });

        formatter.DrawString("The arithmetic behind\na page that looks designed",
            new XFont(Serif, 30, XFontStyle.Bold), XBrushes.White,
            new XRect(margin, bleedHeight - 108, width - margin * 2, 84));

        gfx.DrawString("Photographs by the test suite · Words by nobody",
            new XFont(Sans, 8), new XSolidBrush(XColor.FromArgb(230, 255, 255, 255)),
            new XPoint(margin, bleedHeight - 20));

        // ---- The drop cap ----------------------------------------------------------------
        XFont body = new XFont(Serif, 10);
        double textTop = bleedHeight + 30;
        double measure = width - margin * 2;

        XFont capFont = new XFont(Serif, 10 * 4, XFontStyle.Bold);
        const string cap = "T";
        XSize capSize = gfx.MeasureString(cap, capFont);

        // The cap is drawn from its baseline, which is roughly its height below the top of
        // the block it belongs to. Three body lines is the conventional depth and the one
        // the height below is worked back from.
        double capDepth = capSize.Height * 0.78;
        gfx.DrawString(cap, capFont, XBrushes.Black, new XPoint(margin, textTop + capDepth));

        XRect besideCap = new XRect(
            margin + capSize.Width + 4, textTop, measure - capSize.Width - 4, capDepth);

        // How much of the opening fits beside the cap is decided by measuring, one word at
        // a time, rather than by counting characters and hoping. GetLayout reports what the
        // formatter would need without drawing anything.
        string[] words = Opening.Split(' ');
        int taken = 0;
        for (int count = 1; count <= words.Length; count++)
        {
            string candidate = string.Join(" ", words, 0, count);
            XRect needed = formatter.GetLayout(candidate, body, XBrushes.Black,
                new XRect(besideCap.X, besideCap.Y, besideCap.Width, double.MaxValue));

            if (needed.Height > besideCap.Height)
                break;

            taken = count;
        }

        formatter.Alignment = XParagraphAlignment.Justify;
        formatter.DrawString(string.Join(" ", words, 0, taken), body, XBrushes.Black, besideCap);

        // The rest runs full width in two columns, in one call.
        string remainder = string.Join(" ", words, taken, words.Length - taken) + " " + Body;

        double columnsTop = textTop + capDepth + 8;
        formatter.Columns = 2;
        formatter.ColumnGap = 18;
        formatter.DrawString(string.Concat(remainder, Body, Body, Body, Body, Body), body, XBrushes.Black,
            new XRect(margin, columnsTop, measure, height - columnsTop - margin - 20));
        formatter.Columns = 1;
        formatter.Alignment = XParagraphAlignment.Left;

        RunningFoot(gfx, "1");

        // ---- Page two: the continuation and the pull quote --------------------------------
        page = document.AddPage();
        gfx = XGraphics.FromPdfPage(page);
        formatter = new XTextFormatter(gfx);

        gfx.DrawString("THE ARITHMETIC BEHIND A PAGE", new XFont(Sans, 8, XFontStyle.Bold),
            new XSolidBrush(XColor.FromArgb(140, 140, 140)),
            new XRect(margin, margin, measure, 12),
            new XStringFormat { CharacterSpacing = 2 });
        gfx.DrawLine(new XPen(XColors.Gainsboro, 0.6), margin, margin + 18, width - margin,
            margin + 18);

        // A title as geometry rather than as text. AddString puts the glyph outlines into a
        // path, which can then be filled with anything a shape can be filled with - here a
        // gradient across the word, which no DrawString overload can produce.
        //
        // It needs a glyph outline provider registered, which the runner does along with the
        // other backends. To stroke a title and nothing more you would not come here at all:
        // DrawString takes a pen as well as a brush, which is cheaper and stays searchable.
        XRect titleBox = new XRect(margin, margin + 30, measure, 44);
        XGraphicsPath title = new XGraphicsPath();
        title.AddString("Continued", new XFontFamily(Serif), XFontStyle.Bold, 34, titleBox,
            XStringFormats.TopLeft);

        gfx.DrawPath(
            new XLinearGradientBrush(titleBox, XColors.DarkSlateGray, XColors.CadetBlue,
                XLinearGradientMode.Horizontal),
            title);

        double upperTop = margin + 86;
        formatter.Alignment = XParagraphAlignment.Justify;
        formatter.Columns = 2;
        formatter.ColumnGap = 18;
        formatter.DrawString(string.Concat(Body, Body, Body), body, XBrushes.Black,
            new XRect(margin, upperTop, measure, 210));
        formatter.Columns = 1;

        // ---- The pull quote ---------------------------------------------------------------
        // Set on a tint, at a slight slant. ObliqueAngle skews the glyphs where a real
        // italic would redraw them, which is the honest tool for display type that has no
        // italic of its own to reach for.
        double quoteTop = upperTop + 232;
        XRect quote = new XRect(margin + 40, quoteTop, measure - 80, 96);

        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 248, 232)), quote);
        gfx.DrawLine(new XPen(XColors.DarkSlateGray, 2), quote.X, quote.Y,
            quote.X, quote.Y + quote.Height);

        // Drawn a line at a time with XGraphics rather than flowed, because the slant lives
        // on XStringFormat and XTextFormatter's own DrawString does not take one.
        XStringFormat slanted = new XStringFormat
        {
            ObliqueAngle = 8,
            LineAlignment = XLineAlignment.Near,
        };

        XFont quoteFont = new XFont(Serif, 16);
        string[] quoteLines =
        {
            "“Nothing decides that break except a loop",
            "that adds one word at a time.”",
        };

        for (int line = 0; line < quoteLines.Length; line++)
        {
            gfx.DrawString(quoteLines[line], quoteFont, XBrushes.DarkSlateGray,
                new XRect(quote.X + 20, quote.Y + 22 + line * 24, quote.Width - 36, 22),
                slanted);
        }

        gfx.DrawString("Set with ObliqueAngle, on a tint", new XFont(Sans, 7),
            XBrushes.Gray, new XPoint(quote.X + 20, quote.Y + quote.Height + 14));

        double lowerTop = quoteTop + 130;
        formatter.Alignment = XParagraphAlignment.Justify;
        formatter.Columns = 2;
        formatter.DrawString(string.Concat(Body, Body, Body), body, XBrushes.Black,
            new XRect(margin, lowerTop, measure, height - lowerTop - margin - 20));
        formatter.Columns = 1;
        formatter.Alignment = XParagraphAlignment.Left;

        RunningFoot(gfx, "2");
        #endregion

        return document;
    }
}
