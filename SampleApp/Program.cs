using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Drawing.Layout.enums;
using PdfSharpCore.Pdf;

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
            = new PdfSharpCore.Utils.SkiaImageSource();

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

        System.Console.WriteLine("Done!");
    } // End Sub Main 
} // End Class Program 
// End Namespace SampleApp 
