using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfSharpCore.Test.IO
{
    /// <summary>
    ///   Documents written by hand, for the tests that are about what importing a page copies along
    ///   with it. The pages have to be told apart by weight, which no document PdfSharpCore writes
    ///   itself does, because it gives every page resources of its own.
    /// </summary>
    internal static class ImportedPageFixtures
    {
        /// <summary>The length of the stream of each of the three images, in bytes.</summary>
        internal const int ImageLength = 20000;

        /// <summary>The number of the first object that holds an annotation.</summary>
        private const int FirstAnnotation = 12;

        /// <summary>
        ///   Three pages, each drawing an image of its own, the first one carrying the annotations
        ///   given. The pages are objects 3, 4 and 5, which is how a destination names one.
        /// </summary>
        internal static byte[] LinkedPagesDocument(params string[] annotations)
        {
            var references = Enumerable.Range(FirstAnnotation, annotations.Length)
                .Select(number => number + " 0 R");

            var objects = new List<string>
            {
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Kids[3 0 R 4 0 R 5 0 R]/Count 3>>",
                Page("/Resources<</XObject<</Im0 6 0 R>>>>/Contents 9 0 R" +
                     "/Annots[" + string.Join(" ", references) + "]"),
                Page("/Resources<</XObject<</Im1 7 0 R>>>>/Contents 10 0 R"),
                Page("/Resources<</XObject<</Im2 8 0 R>>>>/Contents 11 0 R"),
                Image(),
                Image(),
                Image(),
                Content("Im0"),
                Content("Im1"),
                Content("Im2"),
            };
            objects.AddRange(annotations);

            return Build(objects);
        }

        /// <summary>A link annotation carrying the destination entries given.</summary>
        internal static string Link(string destination)
        {
            return "<</Type/Annot/Subtype/Link/Rect[0 0 10 10]/Border[0 0 0]" + destination + ">>";
        }

        /// <summary>An annotation that is not a link and names no page at all.</summary>
        internal static string Note()
        {
            return "<</Type/Annot/Subtype/Text/Rect[20 20 30 30]/Contents(a note)>>";
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

        /// <summary>
        ///   Wraps the objects given in a header, a cross reference table and a trailer.
        /// </summary>
        private static byte[] Build(IReadOnlyList<string> objects)
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
