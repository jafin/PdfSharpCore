using MigraDocCore.DocumentObjectModel;
using MigraDocCore.Rendering;
using MigraDocCore.Tests.Helpers;
using PdfSharpCore.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace MigraDocCore.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            // Create a new MigraDoc document
            Document document = new Document();
            // Add a section to the document
            Section section = document.AddSection();
            // Add a paragraph to the section
            Paragraph paragraph = section.AddParagraph();
            // Add some text to the paragraph
            paragraph.AddFormattedText("Hello, World!", TextFormat.Italic);
            var filePath = PathHelper.GetInstance().GetAssetPath("image.jpg");
            // paragraph.AddImage(filePath);
            var image = Image.Load<Rgb24>(PathHelper.GetInstance().GetAssetPath("image.jpg"), out var format);
            //image.Mutate(ctx => ctx.Grayscale());

            // create XImage from that same ImageSharp image:
            var source = ImageSharpImageSource<Rgb24>.FromImageSharpImage(image, format);
            var img = paragraph.AddImage(source);
            var img2 = img.Clone();
            img.ScaleHeight = 0.1;

            var img2x = paragraph.AddImage(img2.Source);
            //you have to set scale AFTER AddImage, it will not inherit from source.
            img2x.ScaleWidth = 0.1;
            img2x.ScaleHeight= 0.2;
            img2x.LockAspectRatio = false;
            //img.LockAspectRatio = true;

            PdfDocumentRenderer renderer = new PdfDocumentRenderer(true);
            renderer.Document = document;
            renderer.RenderDocument();
            renderer.PdfDocument.Save("test-migradoc.pdf");
        }
    }
}