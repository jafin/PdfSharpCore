
using System;
using System.IO;
using System.Text;

using SkiaSharp;


namespace PdfSharpCore.Utils;

/// <summary>
/// Writes the 32bpp bottom-up DIB that PdfSharpCore.Pdf.Advanced.PdfImage expects.
/// Skia has no BMP encoder - SKImage.Encode(SKEncodedImageFormat.Bmp, ...) returns null -
/// so the one bitmap layout the PDF writer consumes is produced here directly.
/// </summary>
internal static class PdfBitmapWriter
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;
    private const short BitsPerPixel = 32;
    private const int BiRgb = 0;


    /// <summary>
    /// Writes <paramref name="bitmap"/> as an uncompressed 32bpp BGRA bottom-up bitmap.
    /// </summary>
    public static void Write(SKBitmap bitmap, MemoryStream ms)
    {
        if (bitmap.ColorType != SKColorType.Bgra8888)
            throw new InvalidOperationException(
                $"Expected a Bgra8888 bitmap but got {bitmap.ColorType}.");

        int width = bitmap.Width;
        int height = bitmap.Height;

        // At 32bpp a row is inherently a multiple of 4 bytes, so no padding is ever required.
        int stride = width * 4;
        int pixelDataSize = stride * height;
        int pixelDataOffset = FileHeaderSize + InfoHeaderSize;

        using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8, true))
        {
            // BITMAPFILEHEADER
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(pixelDataOffset + pixelDataSize); // PdfImage checks this against the stream length
            writer.Write((short)0);                        // bfReserved1
            writer.Write((short)0);                        // bfReserved2
            writer.Write(pixelDataOffset);

            // BITMAPINFOHEADER
            writer.Write(InfoHeaderSize);
            writer.Write(width);
            writer.Write(height);
            writer.Write((short)1);            // biPlanes
            writer.Write(BitsPerPixel);
            writer.Write(BiRgb);               // biCompression - PdfImage rejects anything else
            writer.Write(pixelDataSize);
            writer.Write(0);                   // biXPelsPerMeter
            writer.Write(0);                   // biYPelsPerMeter
            writer.Write(0);                   // biClrUsed
            writer.Write(0);                   // biClrImportant

            // Bottom-up: PdfImage maps the first row it reads to the bottom of the image.
            ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
            int rowBytes = bitmap.RowBytes;
            for (int y = height - 1; y >= 0; y--)
                writer.Write(pixels.Slice(y * rowBytes, stride));
        }
    }
}
