using System;
using System.IO;
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PdfSharpCore.Utils;
using SkiaSharp;

namespace PdfSharpCore.Skia;

/// <summary>
/// Decodes images with SkiaSharp for use by PdfSharpCore.
/// Register it once before loading any image:
/// <c>ImageSource.ImageSourceImpl = new SkiaImageSource();</c>
/// </summary>
public class SkiaImageSource
    : ImageSource
{

    /// <summary>
    /// Wraps an already decoded SkiaSharp bitmap. The bitmap must be Bgra8888 with
    /// unpremultiplied alpha, and ownership passes to the returned image source.
    /// </summary>
    public static IImageSource FromSkiaBitmap(SKBitmap bitmap, bool transparent, int? quality = 75)
    {
        if (bitmap == null)
            throw new ArgumentNullException(nameof(bitmap));

        string name = "*" + Guid.NewGuid().ToString("B");
        return new SkiaImageSourceImpl(name, bitmap, (int)quality, transparent);
    }


    protected override IImageSource FromFileImpl(string path, int? quality = 75)
    {
        using SKData data = SKData.Create(path);
        return Decode(path, data, quality);
    }


    protected override IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75)
    {
        using SKData data = SKData.CreateCopy(imageSource.Invoke());
        return Decode(name, data, quality);
    }


    protected override IImageSource FromStreamImpl(string name, Func<Stream> imageStream, int? quality = 75)
    {
        using Stream stream = imageStream.Invoke();
        using SKData data = SKData.Create(stream);
        return Decode(name, data, quality);
    }


    private static IImageSource Decode(string name, SKData data, int? quality)
    {
        if (data == null)
            throw new InvalidOperationException("Unable to read image data for '" + name + "'.");

        using SKCodec codec = SKCodec.Create(data);
        if (codec == null)
            throw new InvalidOperationException("Unsupported or corrupt image format for '" + name + "'.");

        // Decode to unpremultiplied BGRA. Skia premultiplies by default, which would darken
        // semi-transparent pixels once PdfImage reads the colour and alpha channels separately.
        // Bgra8888 also matches the byte order PdfImage expects: B, G, R, A.
        SKImageInfo info = new SKImageInfo(
            codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        SKBitmap bitmap = new SKBitmap(info);
        try
        {
            SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                throw new InvalidOperationException(
                    "Failed to decode image '" + name + "': " + result + ".");
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        // Mirrors the previous ImageSharp behaviour: PNG sources keep their alpha and take the
        // FLATE path, everything else is re-encoded as JPEG.
        bool transparent = codec.EncodedFormat == SKEncodedImageFormat.Png;

        return new SkiaImageSourceImpl(name, bitmap, (int)quality, transparent);
    }


    private sealed class SkiaImageSourceImpl
        : IImageSource, IDisposable
    {
        private readonly SKBitmap _bitmap;
        private readonly int _quality;

        public int Width => _bitmap.Width;

        public int Height => _bitmap.Height;

        public string Name { get; }

        public bool Transparent { get; }


        public SkiaImageSourceImpl(string name, SKBitmap bitmap, int quality, bool transparent)
        {
            Name = name;
            _bitmap = bitmap;
            _quality = quality;
            Transparent = transparent;
        }


        public void SaveAsJpeg(MemoryStream ms)
        {
            using SKImage image = SKImage.FromBitmap(_bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, _quality);
            if (data == null)
                throw new InvalidOperationException("JPEG encoding failed for '" + Name + "'.");

            data.SaveTo(ms);
        }


        public void SaveAsPdfBitmap(MemoryStream ms)
        {
            PdfBitmapWriter.Write(_bitmap, ms);
        }


        public void Dispose()
        {
            _bitmap.Dispose();
        }
    }
}
