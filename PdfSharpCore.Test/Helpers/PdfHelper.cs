using System;
using System.Collections.Generic;
using System.IO;
using ImageMagick;
using PdfSharpCore.Pdf;

namespace PdfSharpCore.Test.Helpers
{
    public class PdfHelper
    {
        private static readonly string _rootPath = PathHelper.GetInstance().RootDir;

        /// <summary>
        ///   Rasterize all pages within a PDF to PNG images
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static RasterizeOutput Rasterize(PdfDocument document)
        {
            var readerSettings = new MagickReadSettings
            {
                Density = new Density(300, 300),
                BackgroundColor = MagickColors.White
            };
            var images = new MagickImageCollection();
            
            // Add all pages to the collection
            using var ms = new MemoryStream();
            document.Save(ms);

            try
            {
                images.Read(ms, readerSettings);
            }
            catch (MagickDelegateErrorException ex)
            {
                throw new Exception("Ghostscript is not installed or is an incompatible version, unable to rasterize PDF", ex);
            }
            
            // Remove transparency to guarantee a standard white background
            foreach (var img in images)
            {
                img.Alpha(AlphaOption.Deactivate);
                img.BackgroundColor = MagickColors.White;
            }

            return new RasterizeOutput
            {
                ImageCollection = images,
            };
        }
        
        public static List<string> WriteImageCollection(MagickImageCollection images, string outDir, string filePrefix)
        {
            var outPaths = new List<string>();
            for (var pageNum = 0; pageNum < images.Count; pageNum++)
            {
                var outPath = GetOutFilePath(outDir, $"{filePrefix}_{pageNum+1}.png");
                images[pageNum].Write(outPath);
                outPaths.Add(outPath);
            }

            return outPaths;
        }
        
        public static string WriteImage(IMagickImage image, string outDir, string fileNameWithoutExtension)
        {
            var outPath = GetOutFilePath(outDir, $"{fileNameWithoutExtension}.png");
            image.Write(outPath);
            return outPath;
        }

        // Note: For diff to function properly, it requires the underlying image to be in the proper format
        //   For instance, actual and expected must both be sourced from .png files
        /// <summary>
        /// How much the pages are shrunk before they are compared. Two rasterizers disagree about
        /// the pixels along the edge of a glyph, and that disagreement is as fine as the pixels
        /// themselves, so shrinking the pages averages it away. Text that sits in the wrong place
        /// is a difference the width of a line or the height of one, which survives.
        /// </summary>
        private const int ComparedAtPercent = 25;

        // Note: For diff to function properly, it requires the underlying image to be in the proper format
        //   For instance, actual and expected must both be sourced from .png files
        public static DiffOutput Diff(string actualImagePath, string expectedImagePath, string outputPath = null, string filePrefix = null)
        {
            var actual = new MagickImage(actualImagePath);
            var expected = new MagickImage(expectedImagePath);

            actual.Resize(new Percentage(ComparedAtPercent));
            expected.Resize(new Percentage(ComparedAtPercent));

            // Root mean squared rather than a count, so the answer is a share of how far a page
            // can differ at all, and does not grow with the size of the page.
            var diffImg = actual.Compare(expected, ErrorMetric.RootMeanSquared, out var diffVal);

            if (diffVal > 0 && outputPath != null && filePrefix != null)
            {
                WriteImage(diffImg, outputPath, $"{filePrefix}_diff");
            }

            return new DiffOutput
            {
                DiffValue = diffVal,
                DiffImage = diffImg
            };
        }
        
        private static string GetOutFilePath(string outDir, string name)
        {
            var dir = Path.Combine(_rootPath, outDir);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }
    }

    public class RasterizeOutput
    {
        public List<string> OutputPaths;
        public MagickImageCollection ImageCollection;
    }

    public class DiffOutput
    {
        public IMagickImage DiffImage;

        /// <summary>
        /// How far the two images stand apart, from 0 for a pair that matches to 1 for black
        /// against white. Text that moved shows up here in the percents, while the edges of glyphs
        /// drawn by one rasterizer rather than another stay far below.
        /// </summary>
        public double DiffValue;
    }
}
