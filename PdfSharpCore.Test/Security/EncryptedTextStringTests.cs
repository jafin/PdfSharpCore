using System.IO;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Security;
using Xunit;

namespace PdfSharpCore.Test.Security
{
    /// <summary>
    ///   The byte order mark of a UTF-16BE text string belongs to the value of the string, so an
    ///   encrypted document has to encrypt it along with the text. Written outside the ciphertext it
    ///   is decrypted as if it were text, which shifts everything after it and leaves the string
    ///   unreadable: that is why the document properties of protected documents came out scrambled in
    ///   Acrobat and Firefox. See https://github.com/ststeiger/PdfSharpCore/issues/460.
    ///
    ///   What the writer produces is checked with <see cref="StandardSecurity"/> rather than by
    ///   reading it back, because a reader that shares the writer's mistake reads the writer's output
    ///   back perfectly, which is how this survived the existing round trip test.
    /// </summary>
    public class EncryptedTextStringTests
    {
        // Characters that cannot be written in a single byte, so the string has to go out as UTF-16BE.
        private const string Title = "Tîtlé wíth àccents";
        private const string Author = "Ångström";
        private const string OwnerPassword = "12343";

        [Theory]
        [InlineData(PdfDocumentSecurityLevel.Encrypted40Bit)]
        [InlineData(PdfDocumentSecurityLevel.Encrypted128Bit)]
        public void ADocumentPropertyOfAnEncryptedDocumentCanBeReadByAConformingReader(
            PdfDocumentSecurityLevel level)
        {
            var document = new StandardSecurity(SaveEncryptedDocument(level));

            document.DerivedKeyMatchesTheDocument.Should()
                .BeTrue("this test can only judge the strings if it agrees with the document about the key");
            document.DecryptInfoString("/Title").Should().Be(Title);
            document.DecryptInfoString("/Author").Should().Be(Author);
        }

        [Theory]
        [InlineData(PdfDocumentSecurityLevel.Encrypted40Bit)]
        [InlineData(PdfDocumentSecurityLevel.Encrypted128Bit)]
        public void TheByteOrderMarkIsNotWrittenOutsideTheCipherText(PdfDocumentSecurityLevel level)
        {
            var document = new StandardSecurity(SaveEncryptedDocument(level));

            // The mark is encrypted with the text now, so it is no longer spelled out in the file.
            document.RawInfoString("/Title").Should().NotStartWith("<FEFF");
        }

        [Theory]
        [InlineData(PdfDocumentSecurityLevel.Encrypted40Bit)]
        [InlineData(PdfDocumentSecurityLevel.Encrypted128Bit)]
        public void ADocumentPropertyOfAnEncryptedDocumentIsReadBack(PdfDocumentSecurityLevel level)
        {
            using var saved = new MemoryStream(SaveEncryptedDocument(level));

            var reread = Pdf.IO.PdfReader.Open(saved, OwnerPassword, PdfDocumentOpenMode.Modify);

            reread.Info.Title.Should().Be(Title);
            reread.Info.Author.Should().Be(Author);
        }

        [Theory]
        [InlineData(PdfDocumentSecurityLevel.Encrypted40Bit)]
        [InlineData(PdfDocumentSecurityLevel.Encrypted128Bit)]
        public void ADocumentWrittenBeforeTheFixIsStillRead(PdfDocumentSecurityLevel level)
        {
            // Documents already in the field carry the byte order mark in front of the ciphertext,
            // and they have to keep opening.
            var document = new StandardSecurity(SaveEncryptedDocument(level));
            byte[] asWrittenBefore = document.RewriteAsWrittenBeforeTheFix("/Title", Title);

            new StandardSecurity(asWrittenBefore).RawInfoString("/Title").Should()
                .StartWith("<FEFF", "the point of this test is a document in the old form");

            using var saved = new MemoryStream(asWrittenBefore);
            var reread = Pdf.IO.PdfReader.Open(saved, OwnerPassword, PdfDocumentOpenMode.Modify);

            reread.Info.Title.Should().Be(Title);
        }

        [Fact]
        public void AnUnencryptedDocumentSpellsOutTheByteOrderMarkAsBefore()
        {
            var document = new PdfDocument();
            document.AddPage();
            document.Info.Title = Title;

            using var output = new MemoryStream();
            document.Save(output, false);

            // Nothing is encrypted, so the bytes are what they always were.
            var expected = new StringBuilder("/Title <FEFF");
            foreach (byte b in Encoding.BigEndianUnicode.GetBytes(Title))
                expected.AppendFormat("{0:X2}", b);
            expected.Append('>');

            Encoding.Latin1.GetString(output.ToArray()).Should().Contain(expected.ToString());
        }

        private static byte[] SaveEncryptedDocument(PdfDocumentSecurityLevel level)
        {
            var document = new PdfDocument();
            document.AddPage();
            document.Info.Title = Title;
            document.Info.Author = Author;

            // The settings reported in the issue.
            var settings = document.SecuritySettings;
            settings.DocumentSecurityLevel = level;
            settings.OwnerPassword = OwnerPassword;
            settings.UserPassword = "";
            settings.PermitAnnotations = false;
            settings.PermitAssembleDocument = false;
            settings.PermitExtractContent = false;
            settings.PermitFormsFill = false;
            settings.PermitFullQualityPrint = true;
            settings.PermitModifyDocument = false;
            settings.PermitPrint = true;

            using var output = new MemoryStream();
            document.Save(output, false);
            return output.ToArray();
        }
    }
}
