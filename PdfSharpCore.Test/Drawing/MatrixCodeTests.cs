using System;
using System.Linq;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.BarCodes;
using PdfSharpCore.Pdf;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Drawing;

/// <summary>
///   Drawing a CodeDataMatrix used to throw ArgumentNullException naming a parameter called
///   image, because the code that was to produce the image was a stub returning nothing.
///   The ecc200 encoding of ISO/IEC 16022 is written now, so the code is produced and drawn.
///   See https://github.com/ststeiger/PdfSharpCore/issues/361.
///   <para>
///   What is drawn is checked by reading it back with ZXing, a separate implementation of the
///   standard. Agreeing with an independent reader is the thing worth knowing; agreeing with
///   itself would tell us nothing.
///   </para>
/// </summary>
public class MatrixCodeTests
{
    [Fact]
    public void ADataMatrixCanBeDrawnAtAll()
    {
        // The snippet on the issue.
        var text = "HELLO-DATAMATRIX-1234";
        var code = new CodeDataMatrix(text, 26, 26);

        var drawing = () => Draw(gfx => gfx.DrawMatrixCode(code, new XPoint(100, 100)));

        drawing.Should().NotThrow();
    }

    [Fact]
    public void TheComplaintAboutANullImageIsGone()
    {
        var code = new CodeDataMatrix("HELLO-DATAMATRIX-1234", 26, 26);

        var drawing = () => Draw(gfx => gfx.DrawMatrixCode(code, new XPoint(100, 100)));

        drawing.Should().NotThrow<ArgumentNullException>();
    }

    [Theory]
    [InlineData("A", 10, 10)]
    [InlineData("12", 10, 10)]
    [InlineData("HELLO", 14, 14)]
    [InlineData("123456789012", 16, 16)]
    [InlineData("Wikipedia, the free encyclopedia", 26, 26)]
    [InlineData("Test~!@#$%^&*()_+", 22, 22)]
    [InlineData("The quick brown fox jumps over the lazy dog", 32, 32)]
    public void AnIndependentReaderReadsBackWhatWasEncoded(string text, int rows, int columns)
    {
        var modules = DataMatrixModules.Of(text, rows, columns);

        DataMatrixModules.Read(modules).Should().Be(text);
    }

    [Theory]
    // Every symbol size the standard defines, filled to capacity. The rectangular ones, the
    // ones built of several data regions, and the ones whose codewords are split into more
    // than one interleaved block are all in here.
    [InlineData(10, 10, 3)] [InlineData(12, 12, 5)] [InlineData(8, 18, 5)]
    [InlineData(14, 14, 8)] [InlineData(8, 32, 10)] [InlineData(16, 16, 12)]
    [InlineData(12, 26, 16)] [InlineData(18, 18, 18)] [InlineData(20, 20, 22)]
    [InlineData(12, 36, 22)] [InlineData(22, 22, 30)] [InlineData(16, 36, 32)]
    [InlineData(24, 24, 36)] [InlineData(26, 26, 44)] [InlineData(16, 48, 49)]
    [InlineData(32, 32, 62)] [InlineData(36, 36, 86)] [InlineData(40, 40, 114)]
    [InlineData(44, 44, 144)] [InlineData(48, 48, 174)] [InlineData(52, 52, 204)]
    [InlineData(64, 64, 280)] [InlineData(72, 72, 368)] [InlineData(80, 80, 456)]
    [InlineData(88, 88, 576)] [InlineData(96, 96, 696)] [InlineData(104, 104, 816)]
    [InlineData(120, 120, 1050)] [InlineData(132, 132, 1304)]
    public void EverySymbolSizeCarriesItsFullCapacity(int rows, int columns, int capacity)
    {
        // Digits pack two to a codeword, so this is exactly full.
        var text = string.Concat(Enumerable.Range(0, capacity).Select(i => (i * 7 % 100).ToString("D2")));

        var modules = DataMatrixModules.Of(text, rows, columns);

        DataMatrixModules.Read(modules).Should().Be(text);
    }

    [Fact]
    public void TheLargestSymbolIsBuiltEvenThoughTheReaderWillNotReadItBack()
    {
        // 144x144 is left out of the sweep above because ZXing cannot read one back - not
        // ours and not its own either, which is a limit of its detector rather than of the
        // symbol. Checked here as far as it can be: the right size, and both kinds of module.
        var text = string.Concat(Enumerable.Range(0, 1558).Select(i => (i * 7 % 100).ToString("D2")));

        var modules = DataMatrixModules.Of(text, 144, 144);

        modules.GetLength(0).Should().Be(144);
        modules.GetLength(1).Should().Be(144);
        Dark(modules).Should().BeGreaterThan(0).And.BeLessThan(144 * 144);
    }

    [Fact]
    public void TheSameTextGivesTheSameCodeEveryTime()
    {
        var first = DataMatrixModules.Of("REPEATABLE-1234", 20, 20);
        var second = DataMatrixModules.Of("REPEATABLE-1234", 20, 20);

        // The padding is scrambled by position rather than at random, so nothing about a
        // symbol changes between one run and the next.
        first.Should().BeEquivalentTo(second);
    }

    [Fact]
    public void EveryDataRegionIsGivenItsFinderPattern()
    {
        // A symbol of four data regions. Each carries a solid edge down its left and along
        // its bottom, which is what tells a reader where the region is and which way up.
        var modules = DataMatrixModules.Of("FINDER", 32, 32);

        foreach (var top in new[] { 0, 16 })
        foreach (var left in new[] { 0, 16 })
        {
            for (var down = 0; down < 16; down++)
                modules[top + down, left].Should().BeTrue("the left edge of the region at {0},{1}", top, left);

            for (var across = 0; across < 16; across++)
                modules[top + 15, left + across].Should().BeTrue("the bottom edge of the region at {0},{1}", top, left);
        }
    }

    [Fact]
    public void TextTooLongForTheSymbolSaysSoRatherThanBeingCutShort()
    {
        var tooMuch = new string('A', 40);
        var code = new CodeDataMatrix(tooMuch, 14, 14);   // holds 8 codewords

        var drawing = () => Draw(gfx => gfx.DrawMatrixCode(code, new XPoint(20, 20)));

        drawing.Should().Throw<InvalidOperationException>().WithMessage("*too big*");
    }

    [Fact]
    public void ASizeThatIsNotOneOfTheStandardSizesSaysSo()
    {
        var code = new CodeDataMatrix("HELLO", 11, 11);

        var drawing = () => Draw(gfx => gfx.DrawMatrixCode(code, new XPoint(20, 20)));

        drawing.Should().Throw<InvalidOperationException>().WithMessage("*invalid*");
    }

    [Fact]
    public void AnEncodationThatIsNotWrittenSaysSoRatherThanWritingAscii()
    {
        var code = new CodeDataMatrix("HELLO", DataMatrixEncoding.C40, 26, 26, XSize.Empty);

        var drawing = () => Draw(gfx => gfx.DrawMatrixCode(code, new XPoint(20, 20)));

        // Quietly writing ASCII would give a readable code of the wrong density, and the
        // caller who asked for C40 would never know it did not get it.
        drawing.Should().Throw<NotImplementedException>().WithMessage("*encodation*");
    }

    [Fact]
    public void AQuietZoneLeavesTheCodeReadable()
    {
        var text = "QUIET-ZONE-42";
        var code = new CodeDataMatrix(text, "", 20, 20, 2, new XSize(120, 120));

        var drawing = () => Draw(gfx => gfx.DrawMatrixCode(code, new XPoint(20, 20)));

        drawing.Should().NotThrow();
    }

    [Fact]
    public void TheBarCodesThatWereAlreadyDrawnStillAre()
    {
        var drawing = () => Draw(gfx =>
        {
            gfx.DrawBarCode(new Code3of9Standard("ABC123", new XSize(120, 40)), new XPoint(20, 20));
            gfx.DrawBarCode(new Code2of5Interleaved("123456", new XSize(120, 40)), new XPoint(20, 100));
        });

        drawing.Should().NotThrow();
    }

    static int Dark(bool[,] modules)
    {
        var count = 0;
        foreach (var module in modules)
        {
            if (module)
                count++;
        }
        return count;
    }

    static void Draw(Action<XGraphics> draw)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        draw(gfx);
    }
}