using System.Collections.Generic;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Drawing.Layout.enums;
using PdfSharpCore.Pdf;
using SampleApp.Infrastructure;

namespace SampleApp.Demos;

/// <summary>
///   Flowing text into rectangles with <see cref="XTextFormatter"/>: wrapping, alignment, and
///   measuring a box before anything is drawn in it.
/// </summary>
internal sealed class LayoutDemo : PdfDemo
{
    public LayoutDemo() : base() { }

    public override string Name => "Layout";

    public override string Summary => "Wrapping text into rectangles, and measuring one first.";

    public override IReadOnlyList<string> Shows => new[]
    {
        "Text wrapped to a rectangle, which DrawString will not do",
        "GetLayout measuring the box the text will need",
        "Horizontal and vertical alignment within that box",
    };

    public override int PageCount => 1;

    protected override PdfDocument Build(DemoContext context)
    {
        #region example
        PdfDocument document = new PdfDocument();
        PdfPage page = document.AddPage();

        XGraphics gfx = XGraphics.FromPdfPage(page);
        XFont font = new XFont("Liberation Sans", 12);
        XBrush brush = XBrushes.Black;

        // XGraphics.DrawString does not wrap. It draws one line, from a point, and a newline in the
        // string comes out as a literal character. Wrapping is XTextFormatter's job.
        gfx.DrawString("DrawString draws one line and does not wrap.", font, brush, new XPoint(12, 12));

        XTextFormatter formatter = new XTextFormatter(gfx);

        const string text = "More and more text boxes to show alignment capabilities";
        const string anotherText =
            "Text to determine the size of the box I would like to place the text I'm going to test";

        // GetLayout answers "how much room would this need" without drawing anything, so a box can
        // be sized to its text rather than the text squeezed into a guess. Vertical overflow is
        // allowed while measuring - the point is to find out how tall it wants to be - and turned
        // back off before anything is drawn.
        formatter.AllowVerticalOverflow = true;
        XRect rect = formatter.GetLayout(anotherText, font, brush, new XRect(0, 30, 120, 120));
        formatter.AllowVerticalOverflow = false;

        // A wash over each box, so it is visible that the text really does fit the measured space.
        XSolidBrush translucent = new XSolidBrush(XColor.FromArgb(20, 0, 0, 0));

        rect.Location = new XPoint(50, 50);
        formatter.DrawString(text, font, brush, rect);
        gfx.DrawRectangle(translucent, rect);

        rect.Location = new XPoint(300, 50);
        formatter.DrawString(text, font, brush, rect, new TextFormatAlignment
        {
            Horizontal = XParagraphAlignment.Center,
            Vertical = XVerticalAlignment.Middle,
        });
        gfx.DrawRectangle(translucent, rect);
        #endregion

        return document;
    }
}
