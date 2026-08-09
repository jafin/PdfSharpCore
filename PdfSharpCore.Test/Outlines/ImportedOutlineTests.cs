using System.IO;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace PdfSharpCore.Test.Outlines;

/// <summary>
///   Reading the outline of a document written elsewhere. A destination can be named rather than
///   written out, and an entry can perform an action that goes nowhere in this document at all;
///   neither is a reason for asking a document for its bookmarks to throw.
///   See https://github.com/empira/PDFsharp/issues/8.
/// </summary>
public class ImportedOutlineTests
{
    [Fact]
    public void AnEntryNamingItsDestinationThroughAGoToActionGoesToThePageTheNameStandsFor()
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.NamedInGoToAction());

        outline.DestinationPage.Should().BeSameAs(PageTwoOf(outline));
        outline.PageDestinationType.Should().Be(PdfPageDestinationType.Xyz);
        outline.Left.Should().Be(11);
        outline.Top.Should().Be(22);
    }

    [Fact]
    public void AnEntryNamingItsDestinationOutrightGoesToThePageTheNameStandsFor()
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.NamedInDestEntry());

        outline.DestinationPage.Should().BeSameAs(PageTwoOf(outline));
        outline.Top.Should().Be(22);
    }

    [Fact]
    public void AnEntryNamingADestinationHeldTheWayPdf11HeldItGoesToTheRightPage()
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.NamedInDestsDictionary());

        outline.DestinationPage.Should().BeSameAs(PageTwoOf(outline));
        outline.Top.Should().Be(22);
    }

    [Fact]
    public void AnEntryWhoseDestinationIsWrittenOutStillGoesToThePageItNames()
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.WithDestinationArray());

        outline.DestinationPage.Should().BeSameAs(PageTwoOf(outline));
        outline.Left.Should().Be(11);
        outline.Top.Should().Be(22);
    }

    [Fact]
    public void AnEntryWhoseDestinationIsHeldInAnObjectOfItsOwnStillGoesToThePageItNames()
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.WithIndirectDestinationArray());

        outline.DestinationPage.Should().BeSameAs(PageTwoOf(outline));
        outline.Top.Should().Be(22);
    }

    [Fact]
    public void AnEntryWhoseActionIsHeldInAnObjectOfItsOwnStillGoesToThePageItNames()
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.WithIndirectGoToAction());

        outline.DestinationPage.Should().BeSameAs(PageTwoOf(outline));
        outline.Top.Should().Be(22);
    }

    [Fact]
    public void AnEntryGoingToAPageByNumberGoesToThatPage()
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.WithPageNumberDestination(1));

        outline.DestinationPage.Should().BeSameAs(PageTwoOf(outline));
        outline.PageDestinationType.Should().Be(PdfPageDestinationType.Fit);
    }

    [Fact]
    public void AnEntryGoingToAPageTheDocumentDoesNotHaveIsReadWithoutADestination()
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.WithPageNumberDestination(7));

        outline.DestinationPage.Should().BeNull();
    }

    [Fact]
    public void AnEntryNamingADestinationTheDocumentDoesNotHoldIsReadWithoutOne()
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.NamedNowhere());

        outline.DestinationPage.Should().BeNull();
        // The action is what the entry does. Nothing here understands it, and dropping it would
        // lose the entry its behaviour, so it is left where it was found.
        outline.Elements.GetDictionary("/A").Should().NotBeNull();
    }

    [Theory]
    [InlineData("uri")]
    [InlineData("remote")]
    public void AnEntryPerformingAnActionThatLeavesTheDocumentIsReadAndLeftAlone(string kind)
    {
        var document = kind == "uri"
            ? ImportedOutlineFixtures.WithUriAction()
            : ImportedOutlineFixtures.WithRemoteGoToAction();

        var outline = FirstOutlineOf(document);

        outline.Title.Should().Be("Section 1");
        outline.DestinationPage.Should().BeNull();
        outline.Elements.GetDictionary("/A").Should().NotBeNull();
    }

    [Theory]
    // A destination that stops short of the parameters its type takes.
    [InlineData("[4 0 R/XYZ]")]
    [InlineData("[4 0 R/FitR 1 2]")]
    // A destination whose type is not one of those the specification gives.
    [InlineData("[4 0 R/FitNothing 1]")]
    public void AnEntryWhoseDestinationIsMalformedStillGoesToThePageItNames(string destination)
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.WithDestinationArray(destination));

        outline.DestinationPage.Should().BeSameAs(PageTwoOf(outline));
    }

    [Theory]
    // A destination naming no page at all.
    [InlineData("[]")]
    [InlineData("[/XYZ 11 22 0]")]
    public void AnEntryWhoseDestinationNamesNoPageIsReadWithoutOne(string destination)
    {
        var outline = FirstOutlineOf(ImportedOutlineFixtures.WithDestinationArray(destination));

        outline.DestinationPage.Should().BeNull();
    }

    [Fact]
    public void ADestinationTakenFromANameSurvivesBeingSavedAgain()
    {
        using var input = new MemoryStream(ImportedOutlineFixtures.NamedInGoToAction());
        var document = PdfSharpCore.Pdf.IO.PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        // Reading the outline is what resolves the name.
        document.Outlines.Count.Should().Be(1);

        using var output = new MemoryStream();
        document.Save(output, false);

        output.Position = 0;
        var reopened = PdfSharpCore.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.Modify);
        var outline = reopened.Outlines[0];
        outline.DestinationPage.Should().BeSameAs(reopened.Pages[1]);
        outline.Top.Should().Be(22);
    }

    [Fact]
    public void ADestinationOfATypeThisLibraryDoesNotKnowIsSavedAsItWasFound()
    {
        using var input = new MemoryStream(ImportedOutlineFixtures.WithDestinationArray("[4 0 R/FitNothing 1]"));
        var document = PdfSharpCore.Pdf.IO.PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        document.Outlines.Count.Should().Be(1);

        using var output = new MemoryStream();
        document.Save(output, false);

        // Writing the destination from what was read of it would make it an /XYZ with no position,
        // which is not where the entry went.
        output.Position = 0;
        var reopened = PdfSharpCore.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.Modify);
        var destination = reopened.Outlines[0].Elements.GetArray("/Dest");
        destination.Elements.GetName(1).Should().Be("/FitNothing");
        destination.Elements.GetInteger(2).Should().Be(1);
    }

    private static PdfOutline FirstOutlineOf(byte[] document)
    {
        using var input = new MemoryStream(document);
        var opened = PdfSharpCore.Pdf.IO.PdfReader.Open(input, PdfDocumentOpenMode.Import);

        opened.Outlines.Count.Should().Be(1);
        return opened.Outlines[0];
    }

    /// <summary>The page every fixture sends its outline entry to.</summary>
    private static PdfPage PageTwoOf(PdfOutline outline)
    {
        return outline.Owner.Pages[1];
    }
}
