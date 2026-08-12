using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Drawing;

/// <summary>
///   What <see cref="PdfPage.TrimMargins"/> does: where it puts the drawing origin, what it does
///   to the size of the page, and which of the five page boxes it writes.
/// </summary>
/// <remarks>
///   Written against the behaviour as it stands rather than against the behaviour as it ought to
///   be. The whole feature arrived with the upstream library and had no test of any kind, and the
///   box arithmetic in <c>PrepareForSave</c> is documented as the values InDesign wrote for one
///   particular page rather than as anything derived from the PDF specification. So the first
///   question is not "does the new code work" but "is the old code doing what we think", and the
///   three tests at the foot of this file record answers of "no". They are marked as such.
/// </remarks>
public class PageBleedTests
{
    /// <summary>A bleed of 3mm, which is what a printer asks for and what InDesign defaults to.</summary>
    static readonly XUnit Bleed = XUnit.FromMillimeter(3);

    /// <summary>A5 in points, which is the trimmed size every page here is cut down to.</summary>
    const double A5Width = 420;
    const double A5Height = 595;

    // ----- the origin ---------------------------------------------------------------------------

    [Fact]
    public void TheOriginIsTheCornerOfTheTrimmedPageRatherThanOfTheSheet()
    {
        var page = Trimmed();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(0, 0, 20, 20)));

        var mark = Save(page).RectangleOnSheet;

        // The sheet is the trimmed page plus a bleed on each edge, so its top-left corner is one
        // bleed out from the origin on both axes.
        mark.Left.Should().BeApproximately(Bleed.Point, 0.001);
        mark.Top.Should().BeApproximately(Bleed.Point, 0.001);
    }

    [Fact]
    public void ANegativeCoordinateReachesTheEdgeOfTheSheet()
    {
        var page = Trimmed();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black,
            new XRect(-Bleed.Point, -Bleed.Point, 100, 100)));

        var mark = Save(page).RectangleOnSheet;

        // Flush with the corner of the sheet, which is what bleeding means: ink that survives a
        // cut landing a fraction off the mark.
        mark.Left.Should().BeApproximately(0, 0.001);
        mark.Top.Should().BeApproximately(0, 0.001);
    }

    [Fact]
    public void TheSameDrawingSitsInTheSamePlaceOnTheTrimmedPageEitherWay()
    {
        var trimmed = Trimmed();
        Draw(trimmed, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(30, 40, 20, 20)));

        var plain = Plain();
        Draw(plain, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(30, 40, 20, 20)));

        var onTrimmed = Save(trimmed).RectangleOnSheet;
        var onPlain = Save(plain).RectangleOnSheet;

        // Measured from the trim corner, which on the untrimmed page is the corner of the sheet.
        (onTrimmed.Left - Bleed.Point).Should().BeApproximately(onPlain.Left, 0.001);
        (onTrimmed.Top - Bleed.Point).Should().BeApproximately(onPlain.Top, 0.001);
    }

    // ----- the size of the page -----------------------------------------------------------------

    [Fact]
    public void SettingATrimMarginDoesNotChangeTheSizeOfThePageTheCallerDrawsOn()
    {
        var page = Trimmed();

        page.Width.Point.Should().Be(A5Width);
        page.Height.Point.Should().Be(A5Height);
    }

    [Fact]
    public void TheSheetIsLargerThanTheTrimmedPageByTheBleedOnEachEdge()
    {
        var page = Trimmed();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(0, 0, 20, 20)));

        var boxes = Save(page);

        boxes.MediaBox.Width.Should().BeApproximately(A5Width + 2 * Bleed.Point, 0.01);
        boxes.MediaBox.Height.Should().BeApproximately(A5Height + 2 * Bleed.Point, 0.01);
    }

    // ----- the boxes ----------------------------------------------------------------------------

    [Fact]
    public void ATrimmedPageIsSavedWithAllFiveBoxes()
    {
        var page = Trimmed();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(0, 0, 20, 20)));

        var boxes = Save(page);

        boxes.Names.Should().BeEquivalentTo(new[] { "/MediaBox", "/CropBox", "/BleedBox", "/TrimBox", "/ArtBox" });
    }

    [Fact]
    public void TheTrimBoxIsTheSheetInsetByTheBleedAndTheArtBoxMatchesIt()
    {
        var page = Trimmed();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(0, 0, 20, 20)));

        var boxes = Save(page);

        boxes.TrimBox.Width.Should().BeApproximately(A5Width, 0.01);
        boxes.TrimBox.Height.Should().BeApproximately(A5Height, 0.01);
        boxes.TrimBox.X1.Should().BeApproximately(Bleed.Point, 0.01);
        boxes.TrimBox.Y1.Should().BeApproximately(Bleed.Point, 0.01);

        boxes.ArtBox.ToString().Should().Be(boxes.TrimBox.ToString());
    }

    [Fact]
    public void TheBoxesNest()
    {
        var page = Trimmed();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(0, 0, 20, 20)));

        var boxes = Save(page);

        Encloses(boxes.MediaBox, boxes.CropBox).Should().BeTrue("the crop box lies within the media box");
        Encloses(boxes.MediaBox, boxes.BleedBox).Should().BeTrue("the bleed box lies within the media box");
        Encloses(boxes.BleedBox, boxes.TrimBox).Should().BeTrue("the trim box lies within the bleed box");
        Encloses(boxes.TrimBox, boxes.ArtBox).Should().BeTrue("the art box lies within the trim box");
    }

    [Fact]
    public void TheBleedBoxIsTheWholeSheet()
    {
        var page = Trimmed();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(0, 0, 20, 20)));

        var boxes = Save(page);

        // Nesting is satisfied, but there is no room left between the bleed and the sheet edge,
        // so there is nowhere to put crop marks. Recorded here so that a reader can see the
        // library makes no room for them rather than having to infer it from a comment.
        boxes.BleedBox.ToString().Should().Be(boxes.MediaBox.ToString());
    }

    [Fact]
    public void APageWithNoTrimMarginCarriesNoneOfTheExtraBoxes()
    {
        var page = Plain();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(0, 0, 20, 20)));

        var boxes = Save(page);

        // The whole feature stays invisible to every document that does not ask for it.
        boxes.Names.Should().BeEquivalentTo(new[] { "/MediaBox" });
    }

    // ----- what is written into the content stream ----------------------------------------------

    [Fact]
    public void ContentDrawnIntoTheBleedIsWrittenRatherThanClippedAway()
    {
        var page = Trimmed();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black,
            new XRect(-Bleed.Point, -Bleed.Point, A5Width + 2 * Bleed.Point, 60)));

        var saved = Save(page);
        var band = saved.RectangleOnSheet;

        // Reaching both edges of the sheet, with no clipping operator to cut it back to the trim.
        band.Left.Should().BeApproximately(0, 0.001);
        band.Right.Should().BeApproximately(saved.MediaBox.Width, 0.01);
        saved.Content.Should().NotContain(" W ", "nothing clips the page back to the trimmed area");
    }

    // ----- three answers of "no" ----------------------------------------------------------------

    [Fact]
    public void DEFECT_WidthAndHeightReportTheSheetOnceThePageHasBeenSaved()
    {
        var page = Trimmed();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(0, 0, 20, 20)));

        using var stream = new MemoryStream();
        page.Owner.Save(stream, false);

        // `Width` reads the media box, and saving rewrites the media box to the size of the
        // sheet. So the page a caller has in hand stops reporting the size it was asked for the
        // moment it is written out. Recorded, not endorsed: see the note in the spec.
        page.Width.Point.Should().BeApproximately(A5Width + 2 * Bleed.Point, 0.01);
        page.Height.Point.Should().BeApproximately(A5Height + 2 * Bleed.Point, 0.01);
    }

    [Fact]
    public void DEFECT_SavingATrimmedPageASecondTimeGrowsTheSheetAgain()
    {
        var page = Trimmed();
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(0, 0, 20, 20)));

        using (var first = new MemoryStream())
            page.Owner.Save(first, false);

        using var second = new MemoryStream();
        page.Owner.Save(second, false);
        var mediaBox = Reread(second).MediaBox;

        // `PrepareForSave` adds the margins to `Width`, and `Width` is the media box it just
        // wrote, so every save adds another bleed. Saving to a stream and then to a file - which
        // is an ordinary thing to do - produces two files of different sizes.
        mediaBox.Width.Should().BeApproximately(A5Width + 4 * Bleed.Point, 0.01);
        mediaBox.Height.Should().BeApproximately(A5Height + 4 * Bleed.Point, 0.01);
    }

    [Fact]
    public void DEFECT_AnUnevenTrimMarginPutsTheTrimBoxOnTheWrongEdgesVertically()
    {
        var top = XUnit.FromMillimeter(10);
        var bottom = XUnit.FromMillimeter(1);

        var page = Plain();
        page.TrimMargins.Left = Bleed;
        page.TrimMargins.Right = Bleed;
        page.TrimMargins.Top = top;
        page.TrimMargins.Bottom = bottom;
        Draw(page, gfx => gfx.DrawRectangle(XBrushes.Black, new XRect(0, 0, 20, 20)));

        var saved = Save(page);

        // The drawing origin is one top margin down from the sheet, which is right...
        saved.RectangleOnSheet.Top.Should().BeApproximately(top.Point, 0.001);

        // ...and the trim box says the trimmed page starts one *bottom* margin down from it,
        // which is not. `PrepareForSave` insets the box by `Top` at Y1 and by `Bottom` at Y2,
        // and in PDF space Y1 is the bottom edge, so the two are swapped. Invisible whenever
        // the margins are even, which is the usual case and the reason it survived.
        var distanceFromSheetTop = saved.MediaBox.Y2 - saved.TrimBox.Y2;
        distanceFromSheetTop.Should().BeApproximately(bottom.Point, 0.01);
        distanceFromSheetTop.Should().NotBeApproximately(top.Point, 0.01);
    }

    // ----- making pages, and reading back what was saved -----------------------------------------

    static PdfPage Plain()
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Size = PageSize.A5;
        return page;
    }

    static PdfPage Trimmed()
    {
        var page = Plain();
        page.TrimMargins.All = Bleed;
        return page;
    }

    static void Draw(PdfPage page, Action<XGraphics> draw)
    {
        using var gfx = XGraphics.FromPdfPage(page);
        draw(gfx);
    }

    static SavedPage Save(PdfPage page)
    {
        using var stream = new MemoryStream();
        page.Owner.Save(stream, false);
        return Reread(stream);
    }

    static SavedPage Reread(MemoryStream stream)
    {
        stream.Position = 0;
        var reopened = PdfSharpCore.Pdf.IO.PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        return new SavedPage(reopened.Pages[0]);
    }

    static bool Encloses(PdfRectangle outer, PdfRectangle inner)
    {
        const double slack = 0.001;
        return inner.X1 >= outer.X1 - slack && inner.Y1 >= outer.Y1 - slack &&
               inner.X2 <= outer.X2 + slack && inner.Y2 <= outer.Y2 + slack;
    }

    /// <summary>
    ///   A page as a reader finds it: its boxes, its content, and where on the sheet the first
    ///   rectangle of that content actually landed.
    /// </summary>
    sealed class SavedPage
    {
        readonly PdfPage _page;

        internal SavedPage(PdfPage page)
        {
            _page = page;
            Content = Encoding.ASCII.GetString(PageContent.Of(page));
        }

        internal string Content { get; }

        internal string[] Names
        {
            get
            {
                var present = new System.Collections.Generic.List<string>();
                foreach (var key in new[] { "/MediaBox", "/CropBox", "/BleedBox", "/TrimBox", "/ArtBox" })
                    if (_page.Elements[key] != null)
                        present.Add(key);
                return present.ToArray();
            }
        }

        internal PdfRectangle MediaBox => Box("/MediaBox");
        internal PdfRectangle CropBox => Box("/CropBox");
        internal PdfRectangle BleedBox => Box("/BleedBox");
        internal PdfRectangle TrimBox => Box("/TrimBox");
        internal PdfRectangle ArtBox => Box("/ArtBox");

        PdfRectangle Box(string key) => _page.Elements.GetRectangle(key);

        /// <summary>
        ///   Where the first rectangle in the content stream sits on the sheet, in points from
        ///   the sheet's top-left corner, with the page's own transformation applied.
        /// </summary>
        /// <remarks>
        ///   The renderer writes one <c>cm</c> for the whole page and then draws in the space it
        ///   sets up, so reading the rectangle alone says nothing about where it landed. Only
        ///   translation and the vertical flip are handled, which is all this page's matrix has.
        /// </remarks>
        internal Landed RectangleOnSheet
        {
            get
            {
                var offsetX = 0.0;
                var offsetY = 0.0;
                var cm = Regex.Match(Content, @"1 0 0 1 (-?[\d.]+) (-?[\d.]+) cm");
                if (cm.Success)
                {
                    offsetX = Number(cm.Groups[1].Value);
                    offsetY = Number(cm.Groups[2].Value);
                }

                var re = Regex.Match(Content, @"(-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) re");
                re.Success.Should().BeTrue("the drawing put a rectangle on the page");

                var x = Number(re.Groups[1].Value) + offsetX;
                var y = Number(re.Groups[2].Value) + offsetY;
                var width = Number(re.Groups[3].Value);
                var height = Number(re.Groups[4].Value);

                // Back to the reader's way round: y down from the top of the sheet.
                var sheetTop = MediaBox.Y2;
                return new Landed(x, sheetTop - (y + height), x + width, sheetTop - y);
            }
        }

        static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);
    }

    /// <summary>A rectangle on the sheet, measured in points down and across from its top-left corner.</summary>
    readonly struct Landed
    {
        internal Landed(double left, double top, double right, double bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        internal double Left { get; }
        internal double Top { get; }
        internal double Right { get; }
        internal double Bottom { get; }
    }
}
