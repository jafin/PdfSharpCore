using System;
using System.IO;
using System.Text;
using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace MigraDocCore.Rendering.Tests;

/// <summary>
///   Tagging a document and conforming to PDF/UA are not the same thing, and the gap is mostly rules
///   that are cheap to check and easy to break. These are the ones the writer holds a document to.
/// </summary>
/// <remarks>
///   The refusal is the point. Stamping <c>pdfuaid:part 1</c> on a file and leaving the caller to
///   hear from a validator — or from their customer — that it does not conform makes things worse
///   rather than better, and an accessibility claim is read by a procurement officer.
///   <para>
///   A successful save is still not a validator's verdict. What is checked is listed on
///   <c>PdfUaValidator</c>, and it is not the whole standard: veraPDF has the last word, and it is
///   not in CI yet.
///   </para>
/// </remarks>
public class PdfUaConformanceTests
{
    [Fact]
    public void AClaimingDocumentSaysSoInItsMetadata()
    {
        var saved = Save(Claiming());

        // XMP is the only place a PDF/UA claim can be made — unlike PDF/A there is no dictionary
        // entry for it, so a document with a perfect tree and no identifier claims nothing at all.
        var metadata = MetadataOf(saved);
        metadata.Should().Contain("http://www.aiim.org/pdfua/ns/id/");
        metadata.Should().Contain("<pdfuaid:part>1</pdfuaid:part>");

        // PDF/UA-1 has parts and no conformance levels, where PDF/A has both. Writing one to match
        // the pdfaid pair is a common mistake and a validator objects to it.
        metadata.Should().NotContain("pdfuaid:conformance");
    }

    [Fact]
    public void AClaimingDocumentAsksThatItsTitleBeShownRatherThanItsFileName()
    {
        var saved = Save(Claiming());

        // Set rather than demanded: nobody claims PDF/UA and wants a reader announcing the file
        // name. Without it the title is in the file and nothing ever reaches it.
        saved.ViewerPreferences.DisplayDocTitle.Should().BeTrue();
    }

    [Fact]
    public void ADocumentWithNoTitleIsRefused()
    {
        var renderer = Claiming();
        renderer.PdfDocument.Info.Title = "";

        Saving(renderer).Should().Throw<InvalidOperationException>()
            .WithMessage("*title*", "the caller is the only one who knows what it is");
    }

    [Fact]
    public void ADocumentWithNoLanguageIsRefused()
    {
        var renderer = Claiming(language: null);

        Saving(renderer).Should().Throw<InvalidOperationException>()
            .WithMessage("*language*", "a reader that does not know it cannot choose a voice");
    }

    [Fact]
    public void ADocumentThatWasNotTaggedIsRefused()
    {
        var renderer = Claiming(tagged: false);

        Saving(renderer).Should().Throw<InvalidOperationException>()
            .WithMessage("*tagged*", "a claim over an untagged file is the claim worth refusing");
    }

    [Fact]
    public void AnUndescribedFigureIsRefused()
    {
        var renderer = Claiming();

        // Reaching past the renderer, because MigraDoc will not produce one: an image with no
        // alternative text is drawn as an artifact rather than as a figure with nothing to say. This
        // is the check standing behind a document tagged by hand, or by a later version of this.
        var structure = renderer.PdfDocument.Structure;
        var figure = structure.CreateElement(PdfSharpCore.Pdf.Structure.PdfTag.Figure);
        figure.Should().NotBeNull();

        Saving(renderer).Should().Throw<InvalidOperationException>()
            .WithMessage("*alternate text*");
    }

    [Fact]
    public void TwoElementsUnderOneIdentifierAreRefused()
    {
        // An identifier is what something else points at, so it has to name one element - and the
        // loser of a collision is reported by nothing, it is simply unreachable. A caller naming
        // elements of their own alongside the generated note1, note2 collides easily, which is why
        // this is worth a rule rather than being left to the save.
        //
        // Asked of the validator rather than of a save, and deliberately: saving refuses it too, but
        // from the structure builder, which runs first. What this pins is that a caller can find out
        // by asking - which is the whole point of the validator being public.
        var renderer = Claiming();
        var structure = renderer.PdfDocument.Structure;

        var first = structure.CreateElement(PdfSharpCore.Pdf.Structure.PdfTag.Note);
        var second = structure.CreateElement(PdfSharpCore.Pdf.Structure.PdfTag.Note);
        first.Id = "note1";
        second.Id = "note1";

        // Set by the conformance writer during a save, so it has to be set by hand to reach the rule
        // this test is about rather than the one before it.
        renderer.PdfDocument.ViewerPreferences.DisplayDocTitle = true;

        var validating = () => PdfSharpCore.Pdf.Structure.PdfUaValidator.Validate(renderer.PdfDocument);

        validating.Should().Throw<InvalidOperationException>()
            .WithMessage("*share the identifier*note1*");
    }

    [Fact]
    public void ADocumentThatMeetsTheRulesIsWritten()
    {
        var saved = Save(Claiming());

        saved.Internals.Catalog.Elements.GetDictionary("/MarkInfo")
            .Elements.GetBoolean("/Marked").Should().BeTrue();
        saved.Internals.Catalog.Elements.GetString("/Lang").Should().Be("en-GB");
        saved.Pages[0].Elements.GetName("/Tabs").Should().Be("/S");
    }

    [Fact]
    public void ClaimingPdfUa2WritesPartTwoRatherThanPartOne()
    {
        var renderer = Tagged();
        renderer.PdfDocument.Options.UAConformance = PdfUAConformance.PdfUA2;

        var metadata = MetadataOf(Save(renderer));

        metadata.Should().Contain("<pdfuaid:part>2</pdfuaid:part>");
        metadata.Should().NotContain("<pdfuaid:part>1</pdfuaid:part>");
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA1A, "1")]
    [InlineData(PdfAConformance.PdfA2A, "2")]
    [InlineData(PdfAConformance.PdfA3A, "3")]
    public void EachArchivalALevelCanBeClaimedSavedAndReopened(PdfAConformance conformance, string part)
    {
        var renderer = Tagged();
        renderer.PdfDocument.Options.Conformance = conformance;

        var saved = Save(renderer);
        var metadata = MetadataOf(saved);

        // Both halves of the conjunction: the archival identifier at the A level, and the tagging
        // rules PdfUaValidator already holds a PDF/UA claim to — checked here by the fact that
        // Save did not refuse, rather than restated as a second assertion of the same thing.
        metadata.Should().Contain("<pdfaid:part>" + part + "</pdfaid:part>");
        metadata.Should().Contain("<pdfaid:conformance>A</pdfaid:conformance>");
        saved.ViewerPreferences.DisplayDocTitle.Should().BeTrue();

        // An A-level claim is not itself a PDF/UA claim — the two are separate standards, and this
        // document asked only for the first of them.
        metadata.Should().NotContain("pdfuaid");
    }

    [Fact]
    public void AnALevelClaimOnAnUntaggedDocumentIsRefusedAtTheClaim()
    {
        var renderer = Tagged(tagged: false);

        var claiming = () => renderer.PdfDocument.ClaimConformance(PdfAConformance.PdfA2A);

        claiming.Should().Throw<InvalidOperationException>()
            .WithMessage("*tagged*", "a document with no structure tree cannot become tagged by saving");
        renderer.PdfDocument.Options.Conformance.Should().Be(PdfAConformance.None,
            "a refused claim must not half-set what it refused to make");
    }

    /// <summary>
    ///   A rendered document claiming PDF/UA-1, ready to be saved.
    /// </summary>
    static PdfDocumentRenderer Claiming(bool tagged = true, string language = "en-GB")
    {
        var renderer = Tagged(tagged, language);
        renderer.PdfDocument.Options.UAConformance = PdfUAConformance.PdfUA1;
        return renderer;
    }

    /// <summary>
    ///   A rendered document meeting every rule <see cref="PdfSharpCore.Pdf.Structure.PdfUaValidator"/>
    ///   checks, claiming nothing yet — what an accessibility claim and an A-level archival claim
    ///   alike are held to.
    /// </summary>
    static PdfDocumentRenderer Tagged(bool tagged = true, string language = "en-GB")
    {
        var document = new Document();
        var section = document.AddSection();
        section.AddParagraph("Statement of account", "Heading1");
        section.AddParagraph("Amounts are in pounds sterling.");

        var renderer = new PdfDocumentRenderer(true)
        {
            Document = document,
            TagContent = tagged,
            Language = language,
        };

        renderer.RenderDocument();
        renderer.PdfDocument.Info.Title = "Statement of account";
        return renderer;
    }

    static PdfDocument Save(PdfDocumentRenderer renderer)
    {
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        stream.Position = 0;
        return PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
    }

    static Action Saving(PdfDocumentRenderer renderer) => () =>
    {
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
    };

    static string MetadataOf(PdfDocument saved)
    {
        var metadata = saved.Internals.Catalog.Elements.GetDictionary("/Metadata");
        return Encoding.UTF8.GetString(metadata.Stream.UnfilteredValue);
    }
}
