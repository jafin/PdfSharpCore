using System;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.BarCodes;
using PdfSharpCore.Pdf;
using Xunit;

namespace PdfSharpCore.Test.Drawing
{
    /// <summary>
    ///   PdfSharpCore does not produce the image of a DataMatrix code: the open source version of
    ///   PDFsharp left the ecc200 encoding out over the copyright on ISO/IEC 16022, and this is a
    ///   port of it. What it did was hand back no image, which drawing then reported as a null
    ///   argument named "image" - a complaint about a parameter the caller never passed, and
    ///   nothing at all about the code it asked for.
    ///   See https://github.com/ststeiger/PdfSharpCore/issues/361.
    /// </summary>
    public class MatrixCodeTests
    {
        [Fact]
        public void DrawingADataMatrixSaysThatProducingOneIsNotImplemented()
        {
            var code = new CodeDataMatrix("HELLO-DATAMATRIX-1234", 21);

            var drawing = () => Draw(gfx => gfx.DrawMatrixCode(code, new XPoint(100, 100)));

            drawing.Should().Throw<NotImplementedException>()
                .WithMessage("*DataMatrix*not implemented*");
        }

        [Fact]
        public void TheComplaintIsNoLongerAboutANullImage()
        {
            var code = new CodeDataMatrix("HELLO-DATAMATRIX-1234", 21);

            var drawing = () => Draw(gfx => gfx.DrawMatrixCode(code, new XPoint(100, 100)));

            // What the issue reports: an ArgumentNullException naming a parameter the caller has
            // no say over, which sent people looking at their own arguments for the fault.
            drawing.Should().NotThrow<ArgumentNullException>();
        }

        [Fact]
        public void TheMessageSaysWhatToDoInstead()
        {
            var code = new CodeDataMatrix("HELLO-DATAMATRIX-1234", 21);

            var drawing = () => Draw(gfx => gfx.DrawMatrixCode(code, new XPoint(100, 100)));

            drawing.Should().Throw<NotImplementedException>()
                .WithMessage("*DrawImage*");
        }

        [Fact]
        public void TheBarCodesThatAreImplementedAreStillDrawn()
        {
            // The change is to the one code that was never produced, and says nothing about the
            // rest, which are implemented and drawn as they were.
            var drawing = () => Draw(gfx =>
            {
                gfx.DrawBarCode(new Code3of9Standard("ABC123", new XSize(120, 40)), new XPoint(20, 20));
                gfx.DrawBarCode(new Code2of5Interleaved("123456", new XSize(120, 40)), new XPoint(20, 100));
            });

            drawing.Should().NotThrow();
        }

        static void Draw(Action<XGraphics> draw)
        {
            var document = new PdfDocument();
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            draw(gfx);
        }
    }
}
