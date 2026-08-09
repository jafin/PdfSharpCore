using System;
using System.Collections.Generic;
using System.IO;
using ImageMagick;
using PdfSharpCore.Pdf;

namespace PdfSharpCore.Test.Helpers;

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
            
        // Composite onto white, to guarantee a standard background. Remove rather than
        // Deactivate: Deactivate merely drops the alpha channel and leaves whatever colour was
        // underneath it, so every pixel of a transparency group that was never painted comes
        // out black. A page carrying an annotation drawn under a blend mode is such a page.
        foreach (var img in images)
        {
            img.BackgroundColor = MagickColors.White;
            img.Alpha(AlphaOption.Remove);
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
        // Every one of these three holds a bitmap in unmanaged memory that the garbage collector
        // cannot see the size of. Left to a finalizer they pile up until the test host is killed
        // outright - see the remarks on RasterizeOutput.
        using var actual = new MagickImage(actualImagePath);
        using var expected = new MagickImage(expectedImagePath);

        actual.Resize(new Percentage(ComparedAtPercent));
        expected.Resize(new Percentage(ComparedAtPercent));

        // Root mean squared rather than a count, so the answer is a share of how far a page
        // can differ at all, and does not grow with the size of the page.
        using var diffImg = actual.Compare(expected, ErrorMetric.RootMeanSquared, out var diffVal);

        if (diffVal > 0 && outputPath != null && filePrefix != null)
        {
            WriteImage(diffImg, outputPath, $"{filePrefix}_diff");
        }

        return new DiffOutput
        {
            DiffValue = diffVal
        };
    }
        
    private static string GetOutFilePath(string outDir, string name)
    {
        var dir = Path.Combine(_rootPath, outDir);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }
}

/// <summary>
/// The rasterized pages of a document. <b>Dispose it.</b>
/// </summary>
/// <remarks>
/// A page rasterized at 300 dpi is some tens of megabytes of bitmap, and that bitmap lives in
/// unmanaged memory. The garbage collector sees only the small managed wrapper, so nothing about
/// the pressure of holding a dozen of them makes a collection happen any sooner - the process
/// simply runs out of memory and dies. In a test run that shows up as
/// <c>Test host process crashed</c> and <c>Test Run Aborted</c> with <b>no failing test</b> and a
/// passing count quietly short of the total, which reads like flakiness and is not.
/// </remarks>
public class RasterizeOutput : IDisposable
{
    public List<string> OutputPaths;
    public MagickImageCollection ImageCollection;

    public void Dispose()
    {
        ImageCollection?.Dispose();
        ImageCollection = null;
    }
}

public class DiffOutput
{
    /// <summary>
    /// How far the two images stand apart, from 0 for a pair that matches to 1 for black
    /// against white. Text that moved shows up here in the percents, while the edges of glyphs
    /// drawn by one rasterizer rather than another stay far below.
    /// </summary>
    public double DiffValue;
}
