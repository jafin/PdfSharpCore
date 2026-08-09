using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace PdfSharpCore.Test.IO;

/// <summary>
///   An object that does not say "endobj" leaves the object number of the next one where the
///   keyword should be, and the parser reported that as a token it did not expect. The object
///   before it is whole, and objects are found through the cross-reference table rather than by
///   reading on from where the last one ended, so there is nothing to be gained by refusing the
///   file. An object that did not parse is still reported.
///   See https://github.com/empira/PDFsharp/issues/211.
/// </summary>
public class MissingEndObjectTests
{
    [Fact]
    public void ADocumentWhoseObjectDoesNotSayEndobjIsRead()
    {
        // The reported file: "Token '60' was not expected", where 60 is the object that follows.
        var document = Read(Document(endobj: false));

        document.PageCount.Should().Be(1);
    }

    [Fact]
    public void TheObjectAfterTheOneMissingEndobjIsReadAsWell()
    {
        var document = Read(Document(endobj: false));

        // Object 4 is written after the page and is what the page's /Contents refers to.
        document.Pages[0].Contents.Elements.Count.Should().Be(1);
    }

    [Fact]
    public void ADocumentThatSaysEndobjIsStillRead()
    {
        var document = Read(Document(endobj: true));

        document.PageCount.Should().Be(1);
    }

    [Theory]
    [InlineData("42")]              // an integer
    [InlineData("true")]            // a boolean
    [InlineData("/Name")]           // a name
    [InlineData("(a string)")]      // a string
    [InlineData("[1 2 3]")]         // an array
    [InlineData("null")]            // the null object
    public void AnObjectOfAnyKindMayLeaveEndobjOut(string body)
    {
        var document = Read(Document(endobj: false, spare: body));

        document.PageCount.Should().Be(1);
    }

    [Fact]
    public void TheLastObjectOfTheBodyMayLeaveEndobjOut()
    {
        // Nothing follows the last object but the cross-reference table.
        var document = Read(Document(endobj: true, endobjOnTheLastObject: false));

        document.PageCount.Should().Be(1);
    }

    [Fact]
    public void AnObjectThatDoesNotParseIsStillReported()
    {
        // "endobj" missing is one thing; a body that is not an object at all is another, and
        // tolerating the first must not quietly accept the second.
        var broken = Document(endobj: true, spare: "<</Good true>> } (rubbish)");

        var read = () => Read(broken);

        read.Should().Throw<PdfReaderException>();
    }

    static PdfDocument Read(byte[] document)
    {
        return Pdf.IO.PdfReader.Open(new MemoryStream(document), PdfDocumentOpenMode.Modify);
    }

    /// <summary>
    ///   A one page document whose page dictionary is object 3, optionally written without the
    ///   "endobj" that should close it, and carrying a spare object 5 written the same way.
    /// </summary>
    static byte[] Document(bool endobj, string spare = "<</Spare true>>", bool endobjOnTheLastObject = true)
    {
        var pdf = new MemoryStream();
        var offsets = new Dictionary<int, long>();
        const string content = "BT ET";

        void Write(string text) => pdf.Write(Encoding.Latin1.GetBytes(text));

        void WriteObject(int number, string body, bool close)
        {
            offsets[number] = pdf.Position;
            Write(number + " 0 obj\n" + body + "\n" + (close ? "endobj\n" : ""));
        }

        Write("%PDF-1.4\n");
        WriteObject(1, "<</Type/Catalog/Pages 2 0 R>>", true);
        WriteObject(2, "<</Type/Pages/Kids[3 0 R]/Count 1>>", true);
        WriteObject(3, "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 100]/Contents 4 0 R>>", endobj);
        WriteObject(4, "<</Length " + content.Length + ">>stream\n" + content + "\nendstream", endobj);
        WriteObject(5, spare, endobjOnTheLastObject);

        var startOfCrossReferenceTable = pdf.Position;
        Write("xref\n0 6\n0000000000 65535 f \n");
        for (var number = 1; number <= 5; number++)
            Write(offsets[number].ToString("0000000000") + " 00000 n \n");
        Write("trailer\n<</Size 6/Root 1 0 R>>\n");
        Write("startxref\n" + startOfCrossReferenceTable + "\n%%EOF\n");

        return pdf.ToArray();
    }
}
