using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Rendering
{
    /// <summary>
    ///   A path says how it is painted only at the end, so the segments of one cannot be counted as
    ///   drawn until the operator that closes it says it is stroked. These check that the helper
    ///   the border tests read pages with keeps that straight.
    /// </summary>
    public class StrokedLinesTests
    {
        const string Triangle = "10 10 m 50 10 l 50 50 l ";

        [Fact]
        public void APathThatIsStrokedIsReported()
        {
            var lines = StrokedLines.Of(PageShowing("1 w 10 10 m 50 10 l S"));

            lines.Should().ContainSingle();
            lines[0].X1.Should().Be(10);
            lines[0].X2.Should().Be(50);
            lines[0].Width.Should().Be(1);
        }

        [Theory]
        [InlineData("f")]   // filled
        [InlineData("F")]   // filled, the older spelling
        [InlineData("f*")]  // filled, even-odd
        [InlineData("n")]   // painted no way at all, as a clipping path is
        public void APathThatIsNeverStrokedIsNotReported(string paint)
        {
            // The shading behind a table cell is filled, not stroked, and used to be counted as
            // three drawn lines.
            var lines = StrokedLines.Of(PageShowing(Triangle + paint));

            lines.Should().BeEmpty();
        }

        [Theory]
        [InlineData("B")]   // filled and stroked
        [InlineData("B*")]  // filled even-odd and stroked
        public void APathThatIsFilledAndStrokedIsReported(string paint)
        {
            var lines = StrokedLines.Of(PageShowing(Triangle + paint));

            lines.Should().HaveCount(2);
        }

        [Theory]
        [InlineData("h S")] // closed, then stroked
        [InlineData("s")]   // closed and stroked in one
        [InlineData("b")]   // closed, filled and stroked
        [InlineData("b*")]
        public void ClosingAPathDrawsTheSegmentBackToWhereItBegan(string paint)
        {
            var lines = StrokedLines.Of(PageShowing(Triangle + paint));

            // Two sides drawn and a third closing the triangle, which used to be dropped.
            lines.Should().HaveCount(3);
            lines[2].X1.Should().Be(50);
            lines[2].Y1.Should().Be(50);
            lines[2].X2.Should().Be(10);
            lines[2].Y2.Should().Be(10);
        }

        [Fact]
        public void ClosingAPathThatNeverLeftItsStartDrawsNothingExtra()
        {
            var lines = StrokedLines.Of(PageShowing("10 10 m h S"));

            lines.Should().BeEmpty();
        }

        [Fact]
        public void OneFilledPathDoesNotCarryIntoTheStrokedPathAfterIt()
        {
            var lines = StrokedLines.Of(PageShowing(Triangle + "f 60 60 m 90 60 l S"));

            lines.Should().ContainSingle();
            lines[0].X1.Should().Be(60);
            lines[0].X2.Should().Be(90);
        }

        [Fact]
        public void EachPathIsReportedInTheOrderItIsDrawn()
        {
            var lines = StrokedLines.Of(PageShowing("10 10 m 20 10 l S 30 30 m 40 30 l S"));

            lines.Select(line => line.X1).Should().Equal(10, 30);
        }

        /// <summary>A page whose content stream is exactly the given operators.</summary>
        static PdfPage PageShowing(string content)
        {
            var document = new PdfDocument();
            var page = document.AddPage();
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content + "\n"));

            using var stream = new MemoryStream();
            document.Save(stream, false);
            stream.Position = 0;

            return Pdf.IO.PdfReader.Open(stream, PdfDocumentOpenMode.Modify).Pages[0];
        }
    }
}
