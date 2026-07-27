using System.Collections.Generic;
using System.IO;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace PdfSharpCore.Test.IO
{
    /// <summary>
    ///   A link annotation names the page it goes to by an indirect reference, so importing one used
    ///   to copy that page, and with it every page reachable through the page tree. Splitting a
    ///   document therefore produced files as large as the document they came from.
    ///   See https://github.com/ststeiger/PdfSharpCore/issues/461.
    /// </summary>
    public class SplitTests
    {
        private const int ImageLength = 20000;

        /// <summary>
        ///   The destination the link on the first page carries. The page is named the same way in
        ///   all of them, the difference being where in the annotation the destination sits.
        /// </summary>
        public static IEnumerable<object[]> Destinations => new[]
        {
            new object[] { "/Dest[4 0 R/Fit]" },                 // A destination on the annotation.
            new object[] { "/A<</S/GoTo/D[4 0 R/Fit]>>" },       // A go-to action.
            new object[] { "/P 3 0 R/Dest[4 0 R/Fit]" },         // Both, and a page back reference.
        };

        [Theory]
        [MemberData(nameof(Destinations))]
        public void SplittingAPageThatLinksToAnotherOneLeavesThatPageBehind(string destination)
        {
            var pages = Split(BuildDocumentWhosePagesLinkToEachOther(destination));

            // Every page draws one image, so a page that took another one with it is twice the size.
            pages.Should().OnlyContain(page => page.Length < 2 * ImageLength);
        }

        [Theory]
        [MemberData(nameof(Destinations))]
        public void SplittingAPageDropsTheLinkThatHasNowhereToGo(string destination)
        {
            var page = Split(BuildDocumentWhosePagesLinkToEachOther(destination))[0];

            var annotation = OnlyAnnotationOf(page, 0);
            annotation.Elements.ContainsKey("/Dest").Should().BeFalse();
            annotation.Elements.ContainsKey("/A").Should().BeFalse();
        }

        [Theory]
        [MemberData(nameof(Destinations))]
        public void MergingKeepsALinkPointingAtThePageItGoesTo(string destination)
        {
            using var input = new MemoryStream(BuildDocumentWhosePagesLinkToEachOther(destination));
            var inputDocument = Pdf.IO.PdfReader.Open(input, PdfDocumentOpenMode.Import);

            var merged = new PdfDocument();
            foreach (PdfPage page in inputDocument.Pages)
                merged.AddPage(page);

            using var output = new MemoryStream();
            merged.Save(output, false);

            // The link goes forward, to a page that was not imported when the link itself was.
            var reread = Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.Modify);
            reread.PageCount.Should().Be(3);
            DestinationOf(OnlyAnnotationOf(output, 0)).ObjectID
                .Should().Be(PdfInternals.GetObjectID(reread.Pages[1]));

            // And the page it goes to is the imported page, not a second copy of it.
            output.Length.Should().BeLessThan(4 * ImageLength);
        }

        private static PdfDictionary OnlyAnnotationOf(MemoryStream document, int pageIndex)
        {
            document.Position = 0;
            var page = Pdf.IO.PdfReader.Open(document, PdfDocumentOpenMode.Modify).Pages[pageIndex];
            var annotations = page.Elements.GetArray("/Annots");
            annotations.Elements.Count.Should().Be(1);
            return annotations.Elements.GetDictionary(0);
        }

        /// <summary>
        ///   The page a link goes to, wherever in the annotation the destination is held.
        /// </summary>
        private static PdfReference DestinationOf(PdfDictionary annotation)
        {
            var array = annotation.Elements.GetArray("/Dest")
                        ?? annotation.Elements.GetDictionary("/A").Elements.GetArray("/D");
            return (PdfReference)array.Elements[0];
        }

        /// <summary>
        ///   Writes every page of the document to a file of its own, the way the issue does it.
        /// </summary>
        private static List<MemoryStream> Split(byte[] document)
        {
            using var input = new MemoryStream(document);
            var inputDocument = Pdf.IO.PdfReader.Open(input, PdfDocumentOpenMode.Import);

            var pages = new List<MemoryStream>();
            for (var i = 0; i < inputDocument.PageCount; i++)
            {
                var outputDocument = new PdfDocument();
                outputDocument.AddPage(inputDocument.Pages[i]);

                var output = new MemoryStream();
                outputDocument.Save(output, false);
                pages.Add(output);
            }

            return pages;
        }

        /// <summary>
        ///   Three pages, each drawing an image of its own, where the first one carries a link
        ///   annotation pointing at the second one.
        /// </summary>
        private static byte[] BuildDocumentWhosePagesLinkToEachOther(string destination)
        {
            return BuildDocument(new[]
            {
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Kids[3 0 R 4 0 R 5 0 R]/Count 3>>",
                Page("/Resources<</XObject<</Im0 6 0 R>>>>/Contents 9 0 R/Annots[12 0 R]"),
                Page("/Resources<</XObject<</Im1 7 0 R>>>>/Contents 10 0 R"),
                Page("/Resources<</XObject<</Im2 8 0 R>>>>/Contents 11 0 R"),
                Image(),
                Image(),
                Image(),
                Content("Im0"),
                Content("Im1"),
                Content("Im2"),
                "<</Type/Annot/Subtype/Link/Rect[0 0 10 10]/Border[0 0 0]" + destination + ">>",
            });
        }

        private static string Page(string entries)
        {
            return "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]" + entries + ">>";
        }

        private static string Image()
        {
            var data = new string('A', ImageLength);
            return "<</Type/XObject/Subtype/Image/Width 100/Height 100/ColorSpace/DeviceGray" +
                   "/BitsPerComponent 8/Length " + ImageLength + ">>stream\n" + data + "\nendstream";
        }

        private static string Content(string name)
        {
            var content = "q 100 0 0 100 10 10 cm /" + name + " Do Q";
            return "<</Length " + content.Length + ">>stream\n" + content + "\nendstream";
        }

        private static byte[] BuildDocument(IReadOnlyList<string> objects)
        {
            var pdf = new StringBuilder("%PDF-1.7\n");
            var offsets = new List<int>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(pdf.Length);
                pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
            }

            var startOfCrossReferenceTable = pdf.Length;
            pdf.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
            pdf.Append("0000000000 65535 f \n");
            foreach (var offset in offsets)
                pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
            pdf.Append("trailer\n<</Size ").Append(objects.Count + 1).Append("/Root 1 0 R>>\n");
            pdf.Append("startxref\n").Append(startOfCrossReferenceTable).Append("\n%%EOF\n");

            // The document is plain ASCII, so a byte is a character and the offsets above hold.
            return Encoding.Latin1.GetBytes(pdf.ToString());
        }
    }
}
