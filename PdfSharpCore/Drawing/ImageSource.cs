
using System;
using System.IO;


namespace MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;

public abstract class ImageSource
{
    /// <summary>
    /// Gets or sets the image source implementation to use for reading images.
    /// </summary>
    /// <value>The image source impl.</value>
    public static ImageSource ImageSourceImpl { get; set; }

    public interface IImageSource
    {
        int Width { get; }
        int Height { get; }
        string Name { get; }
        void SaveAsJpeg(MemoryStream ms);
        bool Transparent { get; }
        void SaveAsPdfBitmap(MemoryStream ms);
    }

    protected abstract IImageSource FromFileImpl(string path, int? quality = 75);
    protected abstract IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75);
    protected abstract IImageSource FromStreamImpl(string name, Func<Stream> imageStream, int? quality = 75);


    private static ImageSource RequireImpl()
    {
        if (ImageSourceImpl == null)
            throw new InvalidOperationException(
                "No ImageSource implementation has been configured. Set ImageSource.ImageSourceImpl before "
                + "loading images, e.g. 'ImageSource.ImageSourceImpl = new SkiaImageSource();' from the "
                + "PdfSharpCore.Skia package.");

        return ImageSourceImpl;
    }

    public static IImageSource FromFile(string path, int? quality = 75)
    {
        return RequireImpl().FromFileImpl(path, quality);
    }

    public static IImageSource FromBinary(string name, Func<byte[]> imageSource, int? quality = 75)
    {
        return RequireImpl().FromBinaryImpl(name, imageSource, quality);
    }

    public static IImageSource FromStream(string name, Func<Stream> imageStream, int? quality = 75)
    {
        return RequireImpl().FromStreamImpl(name, imageStream, quality);
    }
}
