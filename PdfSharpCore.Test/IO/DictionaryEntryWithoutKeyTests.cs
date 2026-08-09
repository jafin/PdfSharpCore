using System.IO;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace PdfSharpCore.Test.IO;

/// <summary>
///   pdfTeX has written its PTEX.FullBanner into the document information dictionary as two bare
///   strings with no key in front of them for decades. A dictionary entry is a name followed by a
///   value, so the strings are not conforming, but every other reader steps over them and reads
///   the rest of the document. Refusing the whole file over them makes a large body of TeX output
///   unreadable.
///   See https://github.com/empira/PDFsharp/issues/376.
/// </summary>
public class DictionaryEntryWithoutKeyTests
{
    private const string PdfTeXInfoDictionary =
        "<</Creator (LaTeX with hyperref)/CreationDate (D:20260101120000Z) (PTEX.FullBanner)" +
        "(This is pdfTeX, Version 3.141592653-2.6-1.40.29 \\(TeX Live 2026\\))>>";

    [Theory]
    [InlineData(PdfDocumentOpenMode.Import)]
    [InlineData(PdfDocumentOpenMode.Modify)]
    [InlineData(PdfDocumentOpenMode.ReadOnly)]
    public void ADocumentWhoseInformationDictionaryHoldsBareStringsCanBeRead(PdfDocumentOpenMode openMode)
    {
        using var input = new MemoryStream(BuildDocumentWithInformationDictionary(PdfTeXInfoDictionary));

        var document = PdfSharpCore.Pdf.IO.PdfReader.Open(input, openMode);

        document.PageCount.Should().Be(1);
    }

    [Fact]
    public void TheEntriesThatDoHaveAKeyAreStillRead()
    {
        using var input = new MemoryStream(BuildDocumentWithInformationDictionary(PdfTeXInfoDictionary));

        var document = PdfSharpCore.Pdf.IO.PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        document.Info.Creator.Should().Be("LaTeX with hyperref");
    }

    [Fact]
    public void TheValuesWithNoKeyAreDropped()
    {
        using var input = new MemoryStream(BuildDocumentWithInformationDictionary(PdfTeXInfoDictionary));

        var document = PdfSharpCore.Pdf.IO.PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        // There is nothing to key the two banner strings under, so the two entries that were
        // written properly are the only ones the dictionary can hold.
        document.Info.Elements.Count.Should().Be(2);
    }

    [Fact]
    public void AValueWithNoKeyDoesNotPairTheEntriesAfterItUpWrongly()
    {
        // A single stray value leaves an odd number of items, so a reader that walks the
        // dictionary two at a time reads /Author as the value of the stray string and (Ada) as
        // the key of the next entry.
        using var input = new MemoryStream(BuildDocumentWithInformationDictionary(
            "<</Creator (LaTeX)(PTEX.FullBanner)/Author (Ada)/Subject (Poetry)>>"));

        var document = PdfSharpCore.Pdf.IO.PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        document.Info.Creator.Should().Be("LaTeX");
        document.Info.Author.Should().Be("Ada");
        document.Info.Subject.Should().Be("Poetry");
        document.Info.Elements.Count.Should().Be(3);
    }

    [Fact]
    public void ADictionaryThatIsNothingButValuesIsReadAsAnEmptyOne()
    {
        using var input = new MemoryStream(BuildDocumentWithInformationDictionary("<<(one)(two)(three)>>"));

        var document = PdfSharpCore.Pdf.IO.PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        document.Info.Elements.Count.Should().Be(0);
    }

    [Fact]
    public void ADocumentWhoseInformationDictionaryHoldsBareStringsCanBeSaved()
    {
        using var input = new MemoryStream(BuildDocumentWithInformationDictionary(PdfTeXInfoDictionary));
        var document = PdfSharpCore.Pdf.IO.PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        using var output = new MemoryStream();
        document.Save(output, false);

        output.Position = 0;
        var reopened = PdfSharpCore.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.Import);
        reopened.Info.Creator.Should().Be("LaTeX with hyperref");
    }

    /// <summary>
    ///   A single page document whose fifth object is the information dictionary written as given,
    ///   referenced from the trailer the way pdfTeX writes it.
    /// </summary>
    private static byte[] BuildDocumentWithInformationDictionary(string informationDictionary)
    {
        const string content = "0 0 1 RG 10 10 100 100 re S";
        return RawPdf.Build(new[]
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Contents 4 0 R>>",
            RawPdf.Stream("", content),
            informationDictionary,
        }, "/Info 5 0 R");
    }
}
