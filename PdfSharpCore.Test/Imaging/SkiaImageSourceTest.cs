using System.IO;
using FluentAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Utils;
using SkiaSharp;
using Xunit;

namespace PdfSharpCore.Test.Imaging
{
    /// <summary>
    /// Skia cannot encode BMP, so PdfSharpCore.Skia writes the 32bpp bottom-up DIB that
    /// PdfSharpCore.Pdf.Advanced.PdfImage parses. These tests pin that byte layout, because a
    /// mismatch surfaces only as a NotImplementedException deep inside PDF generation.
    /// </summary>
    public class SkiaImageSourceTest
    {
        private static int ReadWord(byte[] b, int offset) => b[offset] + 256 * b[offset + 1];

        private static int ReadDWord(byte[] b, int offset) => ReadWord(b, offset) + 0x10000 * ReadWord(b, offset + 2);

        private const int PixelDataOffset = 54; // 14 byte file header + 40 byte info header

        private static byte[] SaveAsPdfBitmap(SKBitmap bitmap)
        {
            var source = SkiaImageSource.FromSkiaBitmap(bitmap, transparent: true);
            using var ms = new MemoryStream();
            source.SaveAsPdfBitmap(ms);
            return ms.ToArray();
        }

        private static SKBitmap CreateBitmap(int width, int height)
            => new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));

        [Fact]
        public void SaveAsPdfBitmap_WritesHeaderPdfImageAccepts()
        {
            const int width = 3;
            const int height = 2;
            var bitmap = CreateBitmap(width, height);
            bitmap.Erase(SKColors.Red);

            var bytes = SaveAsPdfBitmap(bitmap);

            // Exactly the fields PdfImage.ReadTrueColorMemoryBitmap validates.
            ReadWord(bytes, 0).Should().Be(0x4d42, "the magic must be 'BM'");
            ReadDWord(bytes, 2).Should().Be(bytes.Length, "PdfImage compares this against the stream length");
            ReadDWord(bytes, 10).Should().Be(PixelDataOffset);
            ReadDWord(bytes, 14).Should().Be(40, "PdfImage only accepts a 40 or 108 byte info header");
            ReadDWord(bytes, 18).Should().Be(width);
            ReadDWord(bytes, 22).Should().Be(height);
            ReadWord(bytes, 26).Should().Be(1, "biPlanes");
            ReadWord(bytes, 28).Should().Be(32, "PdfImage requires (components + 1) * bits == 32");
            ReadDWord(bytes, 30).Should().Be(0, "PdfImage rejects any compression other than BI_RGB");

            bytes.Length.Should().Be(PixelDataOffset + width * height * 4);
        }

        [Fact]
        public void SaveAsPdfBitmap_WritesRowsBottomUp()
        {
            var bitmap = CreateBitmap(1, 2);
            bitmap.SetPixel(0, 0, new SKColor(10, 20, 30, 255));    // top row
            bitmap.SetPixel(0, 1, new SKColor(40, 50, 60, 255));    // bottom row

            var bytes = SaveAsPdfBitmap(bitmap);

            // PdfImage maps the first row it reads onto the bottom of the image, so the bottom
            // row must be written first. Byte order within a pixel is B, G, R, A.
            bytes[PixelDataOffset + 0].Should().Be(60, "blue of the bottom row comes first");
            bytes[PixelDataOffset + 1].Should().Be(50);
            bytes[PixelDataOffset + 2].Should().Be(40);

            bytes[PixelDataOffset + 4].Should().Be(30, "the top row is written last");
            bytes[PixelDataOffset + 5].Should().Be(20);
            bytes[PixelDataOffset + 6].Should().Be(10);
        }

        [Fact]
        public void SaveAsPdfBitmap_KeepsStraightAlpha()
        {
            var bitmap = CreateBitmap(1, 1);
            bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 128));

            var bytes = SaveAsPdfBitmap(bitmap);

            // Skia premultiplies by default; that would store red as ~128 and darken the image.
            bytes[PixelDataOffset + 2].Should().Be(255, "colour channels must stay unpremultiplied");
            bytes[PixelDataOffset + 3].Should().Be(128, "alpha is carried in the fourth byte");
        }

        [Fact]
        public void TransparentPngRoundTripsThroughPdfImage()
        {
            // End-to-end proof that PdfImage accepts the bitmap we produce: it throws
            // NotImplementedException on any header field it does not recognise.
            var bitmap = CreateBitmap(8, 8);
            bitmap.Erase(new SKColor(0, 128, 255, 100));

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            var png = encoded.ToArray();

            var document = new PdfDocument();
            var renderer = XGraphics.FromPdfPage(document.AddPage());
            renderer.DrawImage(XImage.FromStream(() => new MemoryStream(png)), new XPoint(0, 0));

            using var ms = new MemoryStream();
            document.Save(ms);

            ms.Length.Should().BeGreaterThan(0);
        }

        [Fact]
        public void OpaqueJpegTakesTheJpegPath()
        {
            var bitmap = CreateBitmap(8, 8);
            bitmap.Erase(SKColors.Green);

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            var jpeg = encoded.ToArray();

            var xImage = XImage.FromStream(() => new MemoryStream(jpeg));

            // Preserves the previous ImageSharp behaviour: only PNG sources are treated as transparent.
            xImage.Format.Should().Be(XImageFormat.Jpeg);
        }
    }
}
