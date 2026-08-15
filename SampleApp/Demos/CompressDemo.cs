using System;
using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Pdf;
using SampleApp.Infrastructure;

namespace SampleApp.Demos;

/// <summary>
///   <see cref="PdfDocumentOptions"/>, whose every setting is invisible on the page and visible
///   only in the byte count.
/// </summary>
internal sealed class CompressDemo : PdfDemo
{
    public CompressDemo() : base() { }

    public override string Name => "Compress";

    public override string Summary => "Every PdfDocumentOptions setting, measured in bytes.";

    public override IReadOnlyList<string> Shows => new[]
    {
        "CompressContentStreams and NoCompression, against the same page of content",
        "That CompressContentStreams defaults differently in a debug build than in a release one",
        "FlateEncodeMode - the trade between how long a save takes and how small it comes out",
        "UseFlateDecoderForJpegImages, Always against Automatic, measured on a real photograph",
        "ColorMode RGB against CMYK, and what changes in the file when it is switched",
        "A page whose whole subject is the byte counts, because nothing else here is visible",
    };

    public override int PageCount => 2;

    protected override PdfDocument Build(DemoContext context)
    {
        #region example
        XFont heading = new XFont("Liberation Sans", 16, XFontStyle.Bold);
        XFont label = new XFont("Liberation Sans", 9, XFontStyle.Bold);
        XFont body = new XFont("Liberation Sans", 9);
        XFont mono = new XFont("Source Code Pro", 8.5);

        // One page of representative content - text, a long path, a photograph - built the same
        // way every time so that the only thing that varies between the measurements below is the
        // options the document was saved under.
        void Representative(PdfDocument target)
        {
            PdfPage page = target.AddPage();
            using XGraphics gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString("Representative content", heading, XBrushes.Black, new XPoint(50, 60));

            XTextFormatter prose = new XTextFormatter(gfx);
            for (int block = 0; block < 4; block++)
            {
                prose.DrawString(
                    "Compression acts on the content stream, which is the list of drawing "
                    + "operators a page is made of. Text is cheap, paths are dear, and an image is "
                    + "usually already compressed by the time it arrives - which is why the "
                    + "settings below move the total by wildly different amounts.",
                    body, XBrushes.Black, new XRect(50, 90 + block * 60, 495, 55));
            }

            // A path with a great many segments. This is what compression has something to work
            // on: a few hundred coordinates written out as text.
            XGraphicsPath path = new XGraphicsPath();
            for (int step = 0; step < 400; step++)
            {
                double t = step / 400.0 * Math.PI * 8;
                XPoint point = new XPoint(
                    50 + step * 495.0 / 400,
                    500 + Math.Sin(t) * 60 * (1 - step / 400.0));

                if (step == 0)
                    path.AddLine(point, point);
                else
                    path.AddLine(new XPoint(50 + (step - 1) * 495.0 / 400,
                        500 + Math.Sin((step - 1) / 400.0 * Math.PI * 8) * 60 * (1 - (step - 1) / 400.0)), point);
            }

            gfx.DrawPath(new XPen(XColors.MidnightBlue, 0.8), path);

            using XImage photograph = XImage.FromStream(
                () => Assets.Open(Assets.ImagePrefix + "frog-and-toad.jpg"));
            gfx.DrawImage(photograph, 50, 580, 240, 180);
        }

        // Saved under one arrangement of the options and measured. Nothing is written to disk.
        long Measure(Action<PdfDocumentOptions> configure)
        {
            using PdfDocument probe = new PdfDocument();
            configure(probe.Options);
            Representative(probe);

            using MemoryStream buffer = new MemoryStream();
            probe.Save(buffer, false);
            return buffer.Length;
        }

        // Every row sets CompressContentStreams explicitly rather than leaving it alone, because
        // its default is not a constant: PdfDocumentOptions declares it false under #if DEBUG and
        // true otherwise, so the same code produces a materially larger file from a debug build
        // than from a release one. Measuring "the defaults" would measure the build.
        bool defaultCompression = new PdfDocument().Options.CompressContentStreams;

        long compressed = Measure(options => options.CompressContentStreams = true);
        long uncompressed = Measure(options => options.CompressContentStreams = false);
        long noCompression = Measure(options =>
        {
            options.CompressContentStreams = true;
            options.NoCompression = true;
        });
        long flateBest = Measure(options =>
        {
            options.CompressContentStreams = true;
            options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        });
        long flateFast = Measure(options =>
        {
            options.CompressContentStreams = true;
            options.FlateEncodeMode = PdfFlateEncodeMode.BestSpeed;
        });
        long jpegFlate = Measure(options =>
        {
            options.CompressContentStreams = true;
            options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Always;
        });
        long jpegAuto = Measure(options =>
        {
            options.CompressContentStreams = true;
            options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Automatic;
        });
        long cmyk = Measure(options =>
        {
            options.CompressContentStreams = true;
            options.ColorMode = PdfColorMode.Cmyk;
        });

        // ----- the document the demo hands back -----

        PdfDocument document = new PdfDocument();
        document.Info.Title = "Compress";

        // Page one is the content itself, so the reader can see that every measurement above was
        // taken over this and that none of the settings changed how it looks.
        Representative(document);

        PdfPage report = document.AddPage();
        using (XGraphics gfx = XGraphics.FromPdfPage(report))
        {
            XTextFormatter prose = new XTextFormatter(gfx);

            gfx.DrawString("What each setting costs", heading, XBrushes.Black, new XPoint(50, 60));

            prose.DrawString(
                "The page before this one, saved eight times under different options. Every one of "
                + "those files renders identically; the only difference between them is how many "
                + "bytes it takes to say the same thing. That is why this demo is a table - there "
                + "is nothing else to look at.",
                body, XBrushes.Black, new XRect(50, 80, 495, 50));

            (string Setting, long Bytes, string Note)[] rows =
            {
                ("CompressContentStreams = true", compressed, "The baseline every other row is measured against"),
                ("CompressContentStreams = false", uncompressed, "Operators written as readable text"),
                ("NoCompression = true", noCompression, "Nothing in the file is compressed at all"),
                ("FlateEncodeMode.BestCompression", flateBest, "Slower to save, smaller to keep"),
                ("FlateEncodeMode.BestSpeed", flateFast, "Faster to save, larger to keep"),
                ("UseFlateDecoderForJpegImages.Always", jpegFlate, "Flate over the already-compressed JPEG, whatever it costs"),
                ("UseFlateDecoderForJpegImages.Automatic", jpegAuto, "The same, kept only if it turned out smaller"),
                ("ColorMode.Cmyk", cmyk, "Four components per colour instead of three"),
            };

            double y = 145;
            gfx.DrawString("setting", label, XBrushes.Black, new XPoint(50, y));
            gfx.DrawString("bytes", label, XBrushes.Black, new XPoint(280, y));
            gfx.DrawString("against the first row", label, XBrushes.Black, new XPoint(350, y));
            y += 18;

            foreach ((string Setting, long Bytes, string Note) row in rows)
            {
                long delta = row.Bytes - compressed;
                gfx.DrawString(row.Setting, mono, XBrushes.Black, new XPoint(50, y));
                gfx.DrawString($"{row.Bytes:N0}", body, XBrushes.Black, new XPoint(280, y));
                gfx.DrawString(
                    delta == 0 ? "-" : $"{(delta > 0 ? "+" : "")}{delta:N0}",
                    body, delta > 0 ? XBrushes.Firebrick : XBrushes.SeaGreen, new XPoint(350, y));
                gfx.DrawString(row.Note, body, XBrushes.DimGray, new XPoint(50, y + 11));
                y += 28;
            }

            gfx.DrawString("The default is not a constant", label, XBrushes.Firebrick,
                new XPoint(50, y + 15));

            prose.DrawString(
                $"CompressContentStreams defaults to {defaultCompression.ToString().ToLowerInvariant()} "
                + "in the build this page was produced by, and that is a property of the build "
                + "rather than of the library: PdfDocumentOptions declares the field false under "
                + "#if DEBUG and true otherwise. The same code therefore writes a materially "
                + "larger PDF from a debug build than from a release one. Every row above sets it "
                + "explicitly for that reason - a table of \"the defaults\" would have been a "
                + "table of how this app happened to be compiled.",
                body, XBrushes.Black, new XRect(50, y + 28, 495, 70));

            gfx.DrawString("What to make of the rest", label, XBrushes.Black, new XPoint(50, y + 105));

            prose.DrawString(
                "Turning compression off has a large and predictable cost, and it is worth doing "
                + "exactly once: an uncompressed PDF can be opened in a text editor and read, "
                + "which is the fastest way to find out what the library actually wrote. Ship the "
                + "compressed one.",
                body, XBrushes.Black, new XRect(50, y + 118, 495, 45));

            prose.DrawString(
                "UseFlateDecoderForJpegImages runs flate over a stream that is already compressed, "
                + "and whether that wins depends entirely on the picture - the number above is "
                + "this photograph's answer and not a general one. Always takes the flated form "
                + "whatever it costs, because some readers will not decode a bare DCT stream; "
                + "Automatic keeps it only when it came out smaller, which is the setting to reach "
                + "for if size is the reason you are here.",
                body, XBrushes.Black, new XRect(50, y + 168, 495, 60));

            prose.DrawString(
                "ColorMode decides what is written for every colour in the file - three components "
                + "or four. Switching to CMYK is what a press wants and what a screen does not; "
                + "the colours are converted on the way out, so a document built in RGB and saved "
                + "as CMYK is not the same colours, only the nearest ones.",
                body, XBrushes.Black, new XRect(50, y + 235, 495, 45));
        }
        #endregion

        return document;
    }
}
