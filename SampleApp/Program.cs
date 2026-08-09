using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Drawing.Layout.enums;
using PdfSharpCore.Pdf;
using PdfSharpCore.Skia;

namespace SampleApp;

public static class Program
{
    private static string GetOutFilePath(string name)
    {
        var outputDirName = ".";
        return System.IO.Path.Combine(outputDirName, name);
    }


    private static void SaveDocument(PdfDocument document, string name)
    {
        string outFilePath = GetOutFilePath(name);
        string? dir = System.IO.Path.GetDirectoryName(outFilePath);
        if (dir != null && !System.IO.Directory.Exists(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        document.Save(outFilePath);
    }


    public static void Main(string[] args)
    {
        System.Console.WriteLine("Starting...");

        // PdfSharpCore has no imaging or font backend of its own; register one before use.
        PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = new PdfSharpCore.Utils.SkiaFontResolver();
        MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes.ImageSource.ImageSourceImpl
            = new SkiaImageSource();

        const string outName = "test1.pdf";

        PdfDocument document = new PdfDocument();

        PdfPage? pageNewRenderer = document.AddPage();

        XGraphics? renderer = XGraphics.FromPdfPage(pageNewRenderer);

        renderer.DrawString(
            "Testy Test Test"
            , new XFont("Arial", 12)
            , XBrushes.Black
            , new XPoint(12, 12)
        );

        XTextFormatter formatter = new XTextFormatter(renderer);

        var font = new XFont("Arial", 12);
        var brush = XBrushes.Black;

        formatter.AllowVerticalOverflow = true;
        var originalLayout = new XRect(0, 30, 120, 120);
        var text = "More and more text boxes to show alignment capabilities"; // " with additional gline";
        var anotherText =
            "Text to determine the size of the box I would like to place the text I'm going to test";
        var rect = formatter.GetLayout(
            anotherText,
            font,
            brush,
            originalLayout);
        rect.Location = new XPoint(50, 50);
        formatter.AllowVerticalOverflow = false;

        // Prepare brush to draw the box that demonstrates the text fits and aligns correctly
        var translucentBrush = new XSolidBrush(XColor.FromArgb(20, 0, 0, 0));

        // Draw the string with default alignments
        formatter.DrawString(
            text,
            font,
            brush,
            rect
        );
        // For checking purposes
        renderer.DrawRectangle(translucentBrush, rect);

        rect.Location = new XPoint(300, 50);

        // Draw the string with custom alignments
        formatter.DrawString(text, font, brush, rect, new TextFormatAlignment()
        {
            Horizontal = XParagraphAlignment.Center,
            Vertical = XVerticalAlignment.Middle
        });

        // For checking purposes
        renderer.DrawRectangle(translucentBrush, rect);

        SaveDocument(document, outName);

        ResizeAnA4DocumentWithLinksToA5();

        System.Console.WriteLine("Done!");
    }

    /// <summary>
    /// Builds a two page A4 document whose first page links to a spot on the second, then shrinks
    /// the whole thing to A5.
    /// <para>
    /// The point of the example is what does <b>not</b> have to be done. The content is scaled
    /// rather than cropped, and the link goes on pointing at the same words - the destination is
    /// found and moved even though it is held on a different page from the one being resized.
    /// </para>
    /// </summary>
    private static void ResizeAnA4DocumentWithLinksToA5()
    {
        var document = new PdfDocument();
        var font = new XFont("Arial", 14);

        var first = document.AddPage();
        first.Size = PageSize.A4;
        var second = document.AddPage();
        second.Size = PageSize.A4;

        using (var gfx = XGraphics.FromPdfPage(first))
            gfx.DrawString("Go to chapter two", font, XBrushes.Blue, new XPoint(60, 100));

        using (var gfx = XGraphics.FromPdfPage(second))
            gfx.DrawString("Chapter two", font, XBrushes.Black, new XPoint(60, 100));

        // A link on page one, pointing a third of the way down page two.
        first.AddDocumentLink(
            new PdfRectangle(new XPoint(60, first.Height - 115), new XPoint(220, first.Height - 95)),
            destinationPage: 2,
            destinationTop: second.Height - 90);

        // One call. The drawing on both pages is scaled to 70.7%, the link rectangle shrinks with
        // the words underneath it, and the destination it points at moves to where those words
        // ended up. Setting page.Size instead would crop both pages and leave the link behind.
        document.ResizePages(PageSize.A5);

        SaveDocument(document, "resized-a5.pdf");
    }
}
