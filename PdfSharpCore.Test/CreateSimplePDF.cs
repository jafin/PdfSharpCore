﻿using System.IO;
using System.Reflection;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace PdfSharpCore.UnitTests
{
    public class CreateSimplePdf
    {
        private static readonly string RootPath = Path.GetDirectoryName(typeof(CreateSimplePdf).GetTypeInfo().Assembly.Location);
        private const string OutputDirName = "Out";

        [Fact]
        public void CreateTestPdf()
        {
            var outName = "test1.pdf";

            ValidateTargetAvailable(outName);

            var document = new PdfDocument();

            var pageNewRenderer = document.AddPage();

            var renderer = XGraphics.FromPdfPage(pageNewRenderer);

            renderer.DrawString("Testy Test Test", new XFont("Arial", 12), XBrushes.Black, new XPoint(12, 12));

            SaveDocument(document, outName);
            ValidateFileIsPDF(outName);
        }

        [Fact]
        public void CreateTestPdfWithImage()
        {
            using var stream = new MemoryStream();
            var document = new PdfDocument();

            var pageNewRenderer = document.AddPage();

            var renderer = XGraphics.FromPdfPage(pageNewRenderer);

            renderer.DrawImage(XImage.FromFile(Path.Combine(RootPath, "Assets", "lenna.png")), new XPoint(0, 0));

            document.Save(stream);
            stream.Position = 0;
            Assert.True(stream.Length > 1);
            ReadStreamAndVerifyPDFMagicNumber(stream);
        }

        private void SaveDocument(PdfDocument document, string name)
        {
            var outFilePAth = Path.Combine(RootPath, OutputDirName, name);
            var dir = Path.GetDirectoryName(outFilePAth);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            document.Save(outFilePAth);
        }

        private void ValidateFileIsPDF(string v)
        {
            var path = Path.Combine(RootPath, OutputDirName, v);
            Assert.True(File.Exists(path));
            var fi = new FileInfo(path);
            Assert.True(fi.Length > 1);

            using var stream = File.OpenRead(path);
            ReadStreamAndVerifyPDFMagicNumber(stream);
        }

        private static void ReadStreamAndVerifyPDFMagicNumber(Stream stream)
        {
            var readBuffer = new byte[5];
            // PDF must start with %PDF-
            var pdfsignature = new byte[5] { 0x25, 0x50, 0x44, 0x46, 0x2d };

            stream.Read(readBuffer, 0, readBuffer.Length);
            Assert.Equal(pdfsignature, readBuffer);
        }

        private void ValidateTargetAvailable(string file)
        {
            var path = Path.Combine(RootPath, OutputDirName, file);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            Assert.False(File.Exists(path));
        }
    }
}