using System;
using System.IO;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace PdfSharpCore.Test.IO
{
    /// <summary>
    ///   Opening a document for modification stamped it with the current time there and then, before
    ///   anything had been modified and whether or not it was ever written. Reading a document to look
    ///   at its dates changed the date being looked at, which left no way to tell a document that had
    ///   been edited from one that had only been opened.
    ///   See https://github.com/ststeiger/PdfSharpCore/issues/365.
    /// </summary>
    public class ModificationDateTests
    {
        private static readonly DateTime ADateInTheFile = new DateTime(2001, 2, 3, 4, 5, 6);

        [Fact]
        public void OpeningADocumentForModificationReportsTheDateTheFileCarries()
        {
            var pdf = ADocumentModifiedOn(ADateInTheFile);

            var opened = Pdf.IO.PdfReader.Open(new MemoryStream(pdf, false), PdfDocumentOpenMode.Modify);

            // Reading the same file without asking to modify it never stamped anything, so it says what
            // the file says. Comparing the two keeps the question away from how a date is spelled in a
            // file and asks only whether opening for modification answers differently.
            var readOnly = Pdf.IO.PdfReader.Open(new MemoryStream(pdf, false), PdfDocumentOpenMode.ReadOnly);
            opened.Info.ModificationDate.Should().Be(readOnly.Info.ModificationDate);
        }

        [Fact]
        public void OpeningADocumentWithNoModificationDateForModificationDoesNotGiveItOne()
        {
            var pdf = ADocumentModifiedOn(null);

            var opened = Pdf.IO.PdfReader.Open(new MemoryStream(pdf, false), PdfDocumentOpenMode.Modify);

            opened.Info.ModificationDate.Should().Be(DateTime.MinValue);
        }

        [Fact]
        public void WritingADocumentOpenedForModificationStampsItWithTheTimeItWasWritten()
        {
            var pdf = ADocumentModifiedOn(ADateInTheFile);
            var document = Pdf.IO.PdfReader.Open(new MemoryStream(pdf, false), PdfDocumentOpenMode.Modify);

            // The property answers in Universal Time, since a DateTime cannot hold the offset a
            // document states its dates with. See docs/specs/pdf-date-round-trip.md.
            // A PDF date carries whole seconds, so what reaches the file is the moment of the save
            // rounded down. The bound has to be rounded down with it, or the two sit a fraction apart.
            var before = TruncatedToSeconds(DateTime.UtcNow);
            using var written = new MemoryStream();
            document.Save(written, false);
            var after = DateTime.UtcNow;

            document.Info.ModificationDate.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
            Encoding.Latin1.GetString(written.ToArray()).Should().Contain("/ModDate");
            ModificationDateWrittenTo(written).Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        }

        /// <summary>
        ///   The date is stamped by setting the element rather than the property, so that the stamp of
        ///   one save is not mistaken for a date the caller chose when the next save comes around.
        /// </summary>
        [Fact]
        public void WritingADocumentTwiceStampsItTwice()
        {
            var pdf = ADocumentModifiedOn(ADateInTheFile);
            var document = Pdf.IO.PdfReader.Open(new MemoryStream(pdf, false), PdfDocumentOpenMode.Modify);

            using var first = new MemoryStream();
            document.Save(first, false);
            var afterTheFirstSave = document.Info.ModificationDate;

            var between = DateTime.UtcNow;
            using var second = new MemoryStream();
            document.Save(second, false);

            document.Info.ModificationDate.Should().BeOnOrAfter(between);
            afterTheFirstSave.Should().BeOnOrBefore(between);
        }

        [Fact]
        public void WritingADocumentDoesNotStampOverAModificationDateTheCallerChose()
        {
            var pdf = ADocumentModifiedOn(ADateInTheFile);
            var document = Pdf.IO.PdfReader.Open(new MemoryStream(pdf, false), PdfDocumentOpenMode.Modify);

            var chosen = new DateTime(1999, 12, 31, 23, 59, 58);
            document.Info.ModificationDate = chosen;
            using var written = new MemoryStream();
            document.Save(written, false);

            document.Info.ModificationDate.Should().Be(chosen.ToUniversalTime());
            ModificationDateWrittenTo(written).Should().Be(chosen.ToUniversalTime());
        }

        /// <summary>
        ///   A document that was never read from a file is dated by its creation date alone, as it
        ///   always has been. Nothing about it has been modified, so there is no modification to date.
        /// </summary>
        [Fact]
        public void WritingANewlyAuthoredDocumentDoesNotStampIt()
        {
            var document = new PdfDocument();
            document.AddPage();

            using var written = new MemoryStream();
            document.Save(written, false);

            document.Info.ModificationDate.Should().Be(DateTime.MinValue);
            Encoding.Latin1.GetString(written.ToArray()).Should().NotContain("/ModDate");
        }

        /// <summary>
        ///   The date the saved bytes carry, which is the one that outlives the document in memory.
        /// </summary>
        private static DateTime ModificationDateWrittenTo(MemoryStream pdf)
        {
            return Pdf.IO.PdfReader.Open(new MemoryStream(pdf.ToArray(), false), PdfDocumentOpenMode.Import)
                .Info.ModificationDate;
        }

        private static DateTime TruncatedToSeconds(DateTime value)
        {
            return new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);
        }

        private static byte[] ADocumentModifiedOn(DateTime? modificationDate)
        {
            var document = new PdfDocument();
            document.AddPage();
            if (modificationDate.HasValue)
                document.Info.ModificationDate = modificationDate.Value;

            using var pdf = new MemoryStream();
            document.Save(pdf, false);
            return pdf.ToArray();
        }
    }
}
