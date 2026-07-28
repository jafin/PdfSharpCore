using System;
using System.IO;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace PdfSharpCore.Test.IO
{
    /// <summary>
    ///   A document states its dates as local time and an offset from Universal Time. The offset
    ///   members carry both through a save and back; the DateTime members answer in Universal Time,
    ///   which is the same instant and not the same value.
    ///   See docs/specs/pdf-date-round-trip.md.
    /// </summary>
    public class DocumentDateRoundTripTests
    {
        [Theory]
        [InlineData(10, 0)]
        [InlineData(-5, -30)]
        [InlineData(0, 0)]
        [InlineData(5, 45)]
        public void ADateWrittenIntoADocumentIsTheDateReadBackOutOfIt(int hours, int minutes)
        {
            var written = new DateTimeOffset(2024, 6, 1, 12, 34, 56, new TimeSpan(hours, minutes, 0));

            var document = new PdfDocument();
            document.AddPage();
            document.Info.CreationDateOffset = written;
            document.Info.ModificationDateOffset = written;

            var reopened = Reopen(document);

            reopened.Info.CreationDateOffset.Should().Be(written);
            reopened.Info.ModificationDateOffset.Should().Be(written);
            reopened.Info.CreationDateOffset.Offset.Should().Be(written.Offset);
        }

        [Fact]
        public void TheDateTimeMembersAnswerInUniversalTime()
        {
            var written = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.FromHours(10));

            var document = new PdfDocument();
            document.AddPage();
            document.Info.CreationDateOffset = written;

            var readBack = Reopen(document).Info.CreationDate;

            readBack.Kind.Should().Be(DateTimeKind.Utc);
            readBack.Should().Be(new DateTime(2024, 6, 1, 2, 0, 0, DateTimeKind.Utc));
        }

        /// <summary>
        ///   A date the caller chose through the offset member is as much the caller's own as one
        ///   chosen through the DateTime member, and is not stamped over when the document is saved.
        /// </summary>
        [Fact]
        public void SavingDoesNotStampOverAModificationDateChosenThroughTheOffsetMember()
        {
            var pdf = ADocumentToModify();
            var document = Pdf.IO.PdfReader.Open(new MemoryStream(pdf, false), PdfDocumentOpenMode.Modify);

            var chosen = new DateTimeOffset(1999, 12, 31, 23, 59, 58, TimeSpan.FromHours(-5));
            document.Info.ModificationDateOffset = chosen;
            using var written = new MemoryStream();
            document.Save(written, false);

            document.Info.ModificationDateOffset.Should().Be(chosen);
        }

        private static PdfDocument Reopen(PdfDocument document)
        {
            using var pdf = new MemoryStream();
            document.Save(pdf, false);
            return Pdf.IO.PdfReader.Open(new MemoryStream(pdf.ToArray(), false), PdfDocumentOpenMode.Import);
        }

        private static byte[] ADocumentToModify()
        {
            var document = new PdfDocument();
            document.AddPage();

            using var pdf = new MemoryStream();
            document.Save(pdf, false);
            return pdf.ToArray();
        }
    }
}
