using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Images
{
    public class DrawImageTest
    {
        double borderWidth = 4.5;
        private XColor shadowColor = XColors.Gainsboro;
        private XColor backColor = XColor.FromArgb(212, 224, 240);
        private XColor backColor2 = XColor.FromArgb(253, 254, 254);
        private XPen borderPen;
        XGraphicsState _state;

        public DrawImageTest()
        {
            borderPen = new XPen(XColor.FromArgb(94, 118, 151), borderWidth);
        }

        [Fact]
        public void Foo()
        {
            var document = new PdfDocument();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            //BeginBox(gfx, 1, "DrawImage (original)");
            XImage image = XImage.FromFile(PathHelper.GetInstance().GetAssetPath("lenna.png"));
            XImage imageJpg = XImage.FromFile(PathHelper.GetInstance().GetAssetPath("image.jpg"));
            // Left position in point
            double x = (250 - image.PixelWidth * 72 / image.HorizontalResolution) / 2;
            gfx.DrawImage(image, x, 0, 200,200);
            gfx.DrawImage(imageJpg, x, 210,500,200);

            
            document.Save("C:\\temp\\gfxtest.pdf");
        }

        public void BeginBox(XGraphics gfx, int number, string title)
        {
            const int dEllipse = 15;
            XRect rect = new XRect(0, 20, 300, 200);
            if (number % 2 == 0)
                rect.X = 300 - 5;
            rect.Y = 40 + ((number - 1) / 2) * (200 - 5);
            rect.Inflate(-10, -10);
            XRect rect2 = rect;
            rect2.Offset(this.borderWidth, this.borderWidth);
            gfx.DrawRoundedRectangle(new XSolidBrush(this.shadowColor), rect2, new XSize(dEllipse + 8, dEllipse + 8));
            XLinearGradientBrush brush = new XLinearGradientBrush(rect, this.backColor, this.backColor2, XLinearGradientMode.Vertical);
            gfx.DrawRoundedRectangle(this.borderPen, brush, rect, new XSize(dEllipse, dEllipse));
            rect.Inflate(-5, -5);

            XFont font = new XFont("Verdana", 12, XFontStyle.Regular);
            gfx.DrawString(title, font, XBrushes.Navy, rect, XStringFormats.TopCenter);

            rect.Inflate(-10, -5);
            rect.Y += 20;
            rect.Height -= 20;

            _state = gfx.Save();
            gfx.TranslateTransform(rect.X, rect.Y);
        }

        public void EndBox(XGraphics gfx)
        {
            gfx.Restore(_state);
        }
    }
}