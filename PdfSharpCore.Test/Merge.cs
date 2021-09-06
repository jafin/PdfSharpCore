using System;
using System.IO;
using System.Reflection;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace PdfSharpCore.UnitTests
{
    public class Merge
    {
        [Fact]
        public void CanMerge2Documents()
        {
            var root = Path.GetDirectoryName(GetType().GetTypeInfo().Assembly.Location);
            if (root == null)
                throw new Exception("root cannot be null");

            var pdf1Path = Path.Combine(root, "Assets", "FamilyTree.pdf");
            var pdf2Path = Path.Combine(root, "Assets", "test.pdf");

            var outputDocument = new PdfDocument();

            foreach (var pdfPath in new[] { pdf1Path, pdf2Path })
            {
                using var fs = File.OpenRead(pdfPath);
                var inputDocument = Pdf.IO.PdfReader.Open(fs, PdfDocumentOpenMode.Import);
                var count = inputDocument.PageCount;
                for (var idx = 0; idx < count; idx++)
                {
                    var page = inputDocument.Pages[idx];
                    outputDocument.AddPage(page);
                }
            }

            var outFilePath = Path.Combine(root, "Out", "merge.pdf");
            var dir = Path.GetDirectoryName(outFilePath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            outputDocument.Save(outFilePath);
        }
    }
}