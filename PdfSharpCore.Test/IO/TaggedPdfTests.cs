using System;
using System.IO;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;  // PdfArray lives here for the tree assertions
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Structure;
using PdfSharpCore.Test.Helpers;
using Xunit;

// This namespace has a PdfReader of its own, so the one that opens documents needs saying in full.
using Reader = PdfSharpCore.Pdf.IO.PdfReader;

namespace PdfSharpCore.Test.IO;

/// <summary>
///   The library could not say what anything on a page was. A heading and a caption were both a
///   <c>Tj</c> in a content stream, and a reader had nothing to go on but position — so a screen
///   reader got a soup of glyphs in drawing order, and the document could not be made accessible at
///   any price.
///
///   The names were all in the source and nothing stood behind them: <c>/StructTreeRoot</c> and
///   <c>/MarkInfo</c> were key-name constants on <see cref="PdfCatalog"/>, and <c>BDC</c>, <c>BMC</c>
///   and <c>EMC</c> lived only in the content-stream <em>reader's</em> operator table, never emitted.
///
///   This is Stage A: the plumbing and a hand-driven API. Tagging MigraDoc's own output is Stage B
///   and is not here.
/// </summary>
public class TaggedPdfTests
{
    [Fact]
    public void ADocumentThatNeverAsksToBeTaggedIsWrittenExactlyAsBefore()
    {
        var bytes = Save((gfx, document) => gfx.DrawString("Plain", Font, XBrushes.Black, 40, 60));

        var catalog = Catalog(bytes);
        catalog.Elements.ContainsKey("/StructTreeRoot").Should().BeFalse();
        catalog.Elements.ContainsKey("/MarkInfo").Should().BeFalse();

        ContentOf(bytes).Should().NotContain("BDC",
            "not one extra byte for a document that wants none of this");
    }

    [Fact]
    public void TaggedContentIsWrappedInAMarkedContentSequence()
    {
        var bytes = Save((gfx, document) =>
        {
            using (gfx.BeginMarkedContent(PdfTag.H1))
                gfx.DrawString("Invoice", Font, XBrushes.Black, 40, 60);
        });

        var content = ContentOf(bytes);
        content.Should().Contain("/H1 <</MCID 0>> BDC");
        content.Should().Contain("EMC");
    }

    [Fact]
    public void TheCatalogSaysTheDocumentIsTagged()
    {
        var bytes = Save((gfx, document) =>
        {
            using (gfx.BeginMarkedContent(PdfTag.P))
                gfx.DrawString("Body", Font, XBrushes.Black, 40, 60);
        });

        using var saved = new MemoryStream(bytes);
        var catalog = Reader.Open(saved, PdfDocumentOpenMode.Modify).Internals.Catalog;

        catalog.Elements.GetDictionary("/MarkInfo")
            .Elements.GetBoolean("/Marked").Should().BeTrue();
        catalog.Elements["/StructTreeRoot"].Should().NotBeNull();
    }

    [Fact]
    public void TheStructureTreeIsReadBackWithTheTypesItWasGiven()
    {
        var bytes = Save((gfx, document) =>
        {
            using (gfx.BeginMarkedContent(PdfTag.H1))
                gfx.DrawString("Heading", Font, XBrushes.Black, 40, 60);
            using (gfx.BeginMarkedContent(PdfTag.P))
                gfx.DrawString("Body", Font, XBrushes.Black, 40, 100);
        });

        var kids = TreeRoot(bytes).Elements.GetArray("/K");

        kids.Elements.Count.Should().Be(2);
        TypeOf(kids, 0).Should().Be("/H1");
        TypeOf(kids, 1).Should().Be("/P");
    }

    [Fact]
    public void AScopeOpenedInsideAnotherBecomesItsChild()
    {
        var bytes = Save((gfx, document) =>
        {
            using (gfx.BeginMarkedContent(PdfTag.Section))
            {
                using (gfx.BeginMarkedContent(PdfTag.H2))
                    gfx.DrawString("Nested heading", Font, XBrushes.Black, 40, 60);
            }
        });

        var kids = TreeRoot(bytes).Elements.GetArray("/K");
        kids.Elements.Count.Should().Be(1, "only the section hangs off the root");

        var section = kids.Elements.GetDictionary(0);
        section.Elements.GetName("/S").Should().Be("/Sect");

        // The section's children are its own marked-content identifier and then the heading. Both
        // belong there: the identifier covers anything drawn directly in the section and outside
        // the heading, and a structure element is allowed to mix marks with child elements. So the
        // heading is found by looking for it rather than by assuming it comes first.
        var inner = ChildElementOf(section);
        inner.Elements.GetName("/S").Should().Be("/H2");
        inner.Elements[PdfStructureElement.Keys.P].Should().NotBeNull("the tree is linked both ways");
    }

    [Fact]
    public void AnArtifactJoinsNoStructureElement()
    {
        // A page number read out between every paragraph is worse than no page number.
        var bytes = Save((gfx, document) =>
        {
            using (gfx.BeginMarkedContent(PdfTag.P))
                gfx.DrawString("Body", Font, XBrushes.Black, 40, 60);
            using (gfx.BeginArtifact())
                gfx.DrawString("Page 1 of 1", Font, XBrushes.Gray, 500, 800);
        });

        ContentOf(bytes).Should().Contain("/Artifact BMC");
        TreeRoot(bytes).Elements.GetArray("/K").Elements.Count
            .Should().Be(1, "the folio is on the page and is not part of what the page says");
    }

    [Fact]
    public void EveryPageThatCarriesMarksIsIndexedByTheParentTree()
    {
        // The parent tree is what takes a reader from a mark back to its meaning. Without it the
        // marks are numbers with nothing behind them.
        var bytes = SaveTwoPages();

        var root = TreeRoot(bytes);
        var parentTree = root.Elements.GetDictionary(PdfStructureTreeRoot.Keys.ParentTree);

        parentTree.Should().NotBeNull();
        root.Elements.GetInteger(PdfStructureTreeRoot.Keys.ParentTreeNextKey)
            .Should().Be(2, "two pages carried marks");
    }

    [Fact]
    public void EachPageCountsItsOwnMarkedContentFromZero()
    {
        // The identifier is an index into that page's run of the parent tree, so it restarts on
        // every page rather than counting across the document.
        var bytes = SaveTwoPages();

        Occurrences(ContentOf(bytes, 0), "<</MCID 0>>").Should().Be(1);
        Occurrences(ContentOf(bytes, 1), "<</MCID 0>>").Should().Be(1);
    }

    [Fact]
    public void APageThatCarriesMarksSaysWhereItsRunOfTheParentTreeIs()
    {
        var bytes = SaveTwoPages();

        using var saved = new MemoryStream(bytes);
        var document = Reader.Open(saved, PdfDocumentOpenMode.Modify);

        document.Pages[0].Elements.GetInteger("/StructParents").Should().Be(0);
        document.Pages[1].Elements.GetInteger("/StructParents").Should().Be(1);
    }

    [Fact]
    public void AFigureCarriesTheTextThatStandsInForIt()
    {
        var bytes = Save((gfx, document) =>
        {
            using (gfx.BeginMarkedContent(PdfTag.Figure, "A bar chart of monthly revenue"))
                gfx.DrawRectangle(XBrushes.LightGray, 40, 40, 120, 80);
        });

        var figure = TreeRoot(bytes).Elements.GetArray("/K").Elements.GetDictionary(0);

        figure.Elements.GetString(PdfStructureElement.Keys.Alt)
            .Should().Be("A bar chart of monthly revenue");
    }

    [Fact]
    public void TheDocumentCanSayWhatLanguageItIsIn()
    {
        var bytes = Save((gfx, document) =>
        {
            document.Structure.Language = "en-GB";
            using (gfx.BeginMarkedContent(PdfTag.P))
                gfx.DrawString("Body", Font, XBrushes.Black, 40, 60);
        });

        using var saved = new MemoryStream(bytes);
        Reader.Open(saved, PdfDocumentOpenMode.Modify).Internals.Catalog
            .Elements.GetString("/Lang").Should().Be("en-GB");
    }

    [Fact]
    public void ATypeOfTheDocumentsOwnInventionIsExplainedByTheRoleMap()
    {
        // A structure type that is neither standard nor in the role map means nothing to anybody.
        var bytes = Save((gfx, document) =>
        {
            document.Structure.Root.RoleMap["Invoice"] = "Sect";
            using (gfx.BeginMarkedContent(new PdfTag("Invoice")))
                gfx.DrawString("Body", Font, XBrushes.Black, 40, 60);
        });

        var roleMap = TreeRoot(bytes).Elements.GetDictionary(PdfStructureTreeRoot.Keys.RoleMap);

        roleMap.Elements.GetName("/Invoice").Should().Be("/Sect");
    }

    [Fact]
    public void AMarkedContentSequenceIsClosedEvenWhenTheDrawingThrows()
    {
        // The scope is a using, so an early return or an exception cannot leave a BDC unbalanced —
        // and an unbalanced one corrupts every mark after it on the page.
        var document = new PdfDocument();
        var gfx = XGraphics.FromPdfPage(document.AddPage());

        try
        {
            using (gfx.BeginMarkedContent(PdfTag.P))
                throw new InvalidOperationException("something went wrong mid-paragraph");
        }
        catch (InvalidOperationException)
        {
            // Expected — the point is what the content stream looks like afterwards.
        }

        gfx.DrawString("After", Font, XBrushes.Black, 40, 100);
        using var output = new MemoryStream();
        document.Save(output, false);

        // Counted against one rather than against each other: a scope that was never opened would
        // satisfy "as many EMC as BDC" with none of either, and that is exactly the failure a
        // release build produces when this reads the file instead of the stream.
        var content = ContentOf(output.ToArray());
        Occurrences(content, "BDC").Should().Be(1);
        Occurrences(content, "EMC").Should().Be(1);
    }

    [Fact]
    public void MarkedContentCannotBeWrittenToSomethingThatIsNotAPdfPage()
    {
        var form = new XForm(new PdfDocument(), XUnit.FromPoint(100), XUnit.FromPoint(100));
        var gfx = XGraphics.FromForm(form);

        var tagging = () => gfx.BeginMarkedContent(PdfTag.P);

        tagging.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ATaggedDocumentSurvivesBeingIndexedByACrossReferenceStream()
    {
        // Tagging multiplies the object count, which is the reason the compressed cross-reference
        // format landed first. The two have to work together or neither is much use.
        var bytes = Save((gfx, document) =>
        {
            document.Options.CrossReferenceFormat = PdfCrossReferenceFormat.Stream;
            using (gfx.BeginMarkedContent(PdfTag.H1))
                gfx.DrawString("Heading", Font, XBrushes.Black, 40, 60);
        });

        TreeRoot(bytes).Elements.GetArray("/K").Elements.Count.Should().Be(1);
    }

    private static XFont Font => new("Arial", 12);

    /// <summary>
    ///   The first child of an element that is an element rather than a marked-content identifier.
    /// </summary>
    private static PdfDictionary ChildElementOf(PdfDictionary element)
    {
        var kids = element.Elements.GetArray("/K");
        for (var index = 0; index < kids.Elements.Count; index++)
        {
            var child = kids.Elements.GetDictionary(index);
            if (child != null)
                return child;
        }

        throw new InvalidOperationException("The element has no child element, only marks.");
    }

    private static string TypeOf(PdfArray kids, int index) =>
        kids.Elements.GetDictionary(index).Elements.GetName("/S");

    private static PdfDictionary TreeRoot(byte[] bytes) =>
        Catalog(bytes).Elements.GetDictionary("/StructTreeRoot");

    private static PdfCatalog Catalog(byte[] bytes)
    {
        using var saved = new MemoryStream(bytes);
        return Reader.Open(saved, PdfDocumentOpenMode.Modify).Internals.Catalog;
    }

    /// <summary>
    ///   The operators of a page's content stream, as text.
    /// </summary>
    /// <remarks>
    ///   Read back through the page rather than out of the file's bytes, because a content stream is
    ///   compressed or not depending on <c>CompressContentStreams</c> — which defaults to false in a
    ///   debug build and true in a release one. Searching the raw file for <c>BDC</c> therefore
    ///   passes locally and fails in CI, and the two tests here that assert something is *absent*
    ///   fail the other way round: they pass against a release build for the wrong reason, because
    ///   nothing at all is legible in a deflated stream.
    /// </remarks>
    private static string ContentOf(byte[] bytes, int pageIndex = 0)
    {
        using var saved = new MemoryStream(bytes);
        var document = Reader.Open(saved, PdfDocumentOpenMode.Modify);
        return Encoding.Latin1.GetString(PageContent.Of(document.Pages[pageIndex]));
    }

    private static byte[] SaveTwoPages()
    {
        var document = new PdfDocument();
        for (var index = 0; index < 2; index++)
        {
            var gfx = XGraphics.FromPdfPage(document.AddPage());
            using (gfx.BeginMarkedContent(PdfTag.P))
                gfx.DrawString("Page " + index, Font, XBrushes.Black, 40, 60);
            gfx.Dispose();
        }

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static byte[] Save(Action<XGraphics, PdfDocument> draw)
    {
        var document = new PdfDocument();
        document.Info.Title = "Tagged";
        var gfx = XGraphics.FromPdfPage(document.AddPage());

        draw(gfx, document);
        gfx.Dispose();

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
            count++;
        return count;
    }
}
