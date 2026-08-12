using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace PdfSharpCore.Test.Helpers;

/// <summary>
///   What <see cref="PdfHelper.Rasterize" /> decides to draw a document at.
/// </summary>
/// <remarks>
///   <para>
///   The reference images every golden-image test compares against were made at 300 dpi, so a
///   change here that quietly lowered the resolution of an ordinary page would not fail with a
///   wrong number - it would fail as dozens of unrelated image comparisons drifting at once. These
///   pin the boundary instead: an ordinary page is still drawn at exactly 300 dpi, and only a page
///   over the limit is reduced.
///   </para>
///   <para>
///   No page is rasterized here. The decision is arithmetic over the page sizes, so it can be
///   asked for directly, and these run in milliseconds rather than the seconds a drawing costs.
///   </para>
/// </remarks>
public class RasterizeResolutionTests
{
    /// <summary>
    ///   A square page of exactly <see cref="PdfHelper.MaxPixelsPerPage" /> pixels at full
    ///   resolution. 300 dpi over 72 points to the inch is 25/6 pixels per point, so a page of
    ///   960 points square comes to 4000 pixels square, which is 16 million of them.
    /// </summary>
    private const double PointsAtExactlyTheLimit = 960;

    private static PdfDocument DocumentOf(params (double Width, double Height)[] pages)
    {
        var document = new PdfDocument();

        foreach (var size in pages)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(size.Width);
            page.Height = XUnit.FromPoint(size.Height);
        }

        return document;
    }

    [Fact]
    public void AnOrdinaryPageIsDrawnAtFullResolution()
    {
        // A4, which is what almost every page in these tests is.
        PdfHelper.ResolutionFor(DocumentOf((595.276, 841.89))).Should().Be(300);
    }

    /// <summary>
    ///   The limit is a page that may be drawn, not one that may not.
    /// </summary>
    [Fact]
    public void APageExactlyOnTheLimitIsDrawnAtFullResolution()
    {
        var document = DocumentOf((PointsAtExactlyTheLimit, PointsAtExactlyTheLimit));

        PdfHelper.PixelsIn(document, PdfHelper.ResolutionFor(document))
            .Should().Be(PdfHelper.MaxPixelsPerPage, "the page was chosen to land exactly on it");
        PdfHelper.ResolutionFor(document).Should().Be(300);
    }

    /// <summary>
    ///   Twice the length of a side is four times the pixels, so it takes half the resolution to
    ///   come back to the limit.
    /// </summary>
    [Fact]
    public void APageOverTheLimitIsDrawnAtALowerResolution()
    {
        var document = DocumentOf((PointsAtExactlyTheLimit * 2, PointsAtExactlyTheLimit * 2));

        PdfHelper.ResolutionFor(document).Should().Be(150);
        PdfHelper.PixelsIn(document, 150).Should().BeLessThanOrEqualTo(PdfHelper.MaxPixelsPerPage);
    }

    /// <summary>
    ///   One resolution is chosen for the whole document, so it has to be the one that suits the
    ///   page that needs it most. A document is drawn in one call and its pages are compared with
    ///   each other, so they cannot be drawn at sizes that do not correspond.
    /// </summary>
    [Fact]
    public void TheLargestPageOfADocumentDecidesTheResolution()
    {
        var oversized = (PointsAtExactlyTheLimit * 2, PointsAtExactlyTheLimit * 2);

        PdfHelper.ResolutionFor(DocumentOf((595.276, 841.89), oversized, (595.276, 841.89)))
            .Should().Be(150, "the one big page decides it however few of them there are");
    }

    /// <summary>
    ///   The page this limit exists for. FamilyTree.pdf is 58 inches wide by 23, which at 300 dpi
    ///   is 122 million pixels and about 940MB to draw - enough to end the test host, because
    ///   Ghostscript is loaded into it rather than run as a command. See
    ///   docs/specs/test-host-crash-investigation.md.
    /// </summary>
    [Fact]
    public void TheOversizedAssetIsBroughtUnderTheLimit()
    {
        // Qualified: the test assembly has a PdfReader of its own that would be found first.
        var document = global::PdfSharpCore.Pdf.IO.PdfReader.Open(
            PathHelper.GetInstance().GetAssetPath("FamilyTree.pdf"), PdfDocumentOpenMode.Import);

        PdfHelper.PixelsIn(document, 300)
            .Should().BeGreaterThan(100e6, "this is the page the limit is here for");

        var resolution = PdfHelper.ResolutionFor(document);

        resolution.Should().Be(108);
        PdfHelper.PixelsIn(document, resolution)
            .Should().BeLessThanOrEqualTo(PdfHelper.MaxPixelsPerPage);
    }
}
