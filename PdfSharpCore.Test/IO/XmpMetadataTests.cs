using System;
using System.IO;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Metadata;
using PdfSharpCore.Pdf.Security;
using Xunit;

// This namespace has a PdfReader of its own, so the one that opens documents needs saying in full.
using Reader = PdfSharpCore.Pdf.IO.PdfReader;

namespace PdfSharpCore.Test.IO;

/// <summary>
///   A document could not say what standard it claimed to meet. <c>/Metadata</c> was a key-name
///   constant on the catalog, on the page and on the image, with no writer behind any of them, and
///   there was no output intent and no notion of conformance — so PDF/A was out of reach, and with
///   it the hybrid e-invoice formats that are built on PDF/A-3.
///
///   <see cref="XmpMetadata"/> is the packet; <see cref="PdfDocumentOptions.Conformance"/> is the
///   claim, and it is enforced rather than merely stamped on.
/// </summary>
public class XmpMetadataTests
{
    private const string Title = "Invoice 2026-0042";

    /// <summary>
    ///   Not a real ICC profile. Nothing here parses one — the writer embeds the bytes it is given —
    ///   so a recognisable stand-in makes the assertions legible and says plainly that this test is
    ///   not a colour-management test.
    /// </summary>
    private static readonly byte[] SomeProfile = Encoding.ASCII.GetBytes("NOT-AN-ICC-PROFILE");

    [Fact]
    public void ADocumentGetsNoMetadataPacketUnlessItAsksForOne()
    {
        var bytes = Save(document => { });

        Latin1(bytes).Should().NotContain("xpacket",
            "the packet is several hundred bytes and most documents have no use for it");
    }

    [Fact]
    public void AskingForMetadataWritesAPacketThatSaysWhatTheDocumentSays()
    {
        var bytes = Save(document =>
        {
            document.Options.WriteXmpMetadata = true;
            document.Info.Author = "Ada Lovelace";
        });

        var text = Latin1(bytes);
        text.Should().Contain("<?xpacket begin=");
        text.Should().Contain("<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">" + Title);
        text.Should().Contain("<rdf:li>Ada Lovelace</rdf:li>");
    }

    [Fact]
    public void ThePacketIsLeftUncompressedSoThatAScannerCanFindIt()
    {
        // The xpacket markers exist so a tool can find the metadata by reading the bytes without
        // parsing the PDF around them, and a compressed packet is invisible to one.
        var bytes = Save(document =>
        {
            document.Options.WriteXmpMetadata = true;
            document.Options.CompressContentStreams = true;
        });

        Latin1(bytes).Should().Contain("<x:xmpmeta");
    }

    [Fact]
    public void AConformanceClaimSaysWhichPartAndWhichLevel()
    {
        var bytes = Save(Conforming(PdfAConformance.PdfA3B));

        var text = Latin1(bytes);
        text.Should().Contain("<pdfaid:part>3</pdfaid:part>");
        text.Should().Contain("<pdfaid:conformance>B</pdfaid:conformance>");
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA1B, "1")]
    [InlineData(PdfAConformance.PdfA2B, "2")]
    [InlineData(PdfAConformance.PdfA3B, "3")]
    public void EachProfileNamesItsOwnPartOfTheStandard(PdfAConformance conformance, string part)
    {
        var bytes = Save(Conforming(conformance));

        Latin1(bytes).Should().Contain("<pdfaid:part>" + part + "</pdfaid:part>");
    }

    [Fact]
    public void AConformingDocumentEmbedsItsOutputIntentProfile()
    {
        var bytes = Save(Conforming(PdfAConformance.PdfA3B));

        var text = Latin1(bytes);
        text.Should().Contain("/OutputIntent");
        text.Should().Contain("/GTS_PDFA1", "the subtype names the family and not the part, for every part");
        text.Should().Contain("/DestOutputProfile");
        text.Should().Contain("NOT-AN-ICC-PROFILE", "the profile is embedded rather than referenced by name");
    }

    [Fact]
    public void AnEncryptedDocumentMayNotClaimConformance()
    {
        var saving = () => Save(document =>
        {
            Conforming(PdfAConformance.PdfA2B)(document);
            document.SecuritySettings.DocumentSecurityLevel = PdfDocumentSecurityLevel.Encrypted128Bit;
            document.SecuritySettings.OwnerPassword = "12343";
        });

        saving.Should().Throw<InvalidOperationException>().WithMessage("*may not be encrypted*");
    }

    [Fact]
    public void ADocumentWithNoTitleMayNotClaimConformance()
    {
        var saving = () => Save(document =>
        {
            Conforming(PdfAConformance.PdfA2B)(document);
            document.Info.Title = "";
        });

        saving.Should().Throw<InvalidOperationException>().WithMessage("*has to have a title*");
    }

    [Fact]
    public void ADocumentWithNoOutputIntentProfileMayNotClaimConformance()
    {
        // The message has to say what to do, because there is nothing the library can do by itself:
        // no profile ships with it, and which one is right is a decision about the document.
        var saving = () => Save(document => document.Options.Conformance = PdfAConformance.PdfA2B);

        saving.Should().Throw<InvalidOperationException>().WithMessage("*OutputIntentIccProfile*");
    }

    [Fact]
    public void TheVersionIsRaisedToWhatTheClaimedProfileIsDefinedAgainst()
    {
        Latin1(Save(Conforming(PdfAConformance.PdfA1B))).Should().StartWith("%PDF-1.4");
        Latin1(Save(Conforming(PdfAConformance.PdfA2B))).Should().StartWith("%PDF-1.7");
    }

    [Fact]
    public void TheHookCanAddASchemaTheLibraryKnowsNothingAbout()
    {
        // What a PDF/UA identifier or a ZUGFeRD extension schema would go in through.
        var bytes = Save(document =>
        {
            document.Options.WriteXmpMetadata = true;
            document.CustomizeMetadata = metadata => metadata.AdditionalDescriptions.Add(
                "<rdf:Description rdf:about=\"\" xmlns:zf=\"urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#\">"
                + "<zf:DocumentType>INVOICE</zf:DocumentType></rdf:Description>");
        });

        Latin1(bytes).Should().Contain("<zf:DocumentType>INVOICE</zf:DocumentType>");
    }

    [Fact]
    public void ThePacketAndTheInformationDictionaryAgreeAboutTheTitle()
    {
        // A validator compares the two and complains when they differ, which is why the packet is
        // built from the dictionary at save time rather than kept beside it.
        var bytes = Save(document => document.Options.WriteXmpMetadata = true);

        using var saved = new MemoryStream(bytes);
        var reread = Reader.Open(saved, PdfDocumentOpenMode.Modify);

        reread.Info.Title.Should().Be(Title);
        Latin1(bytes).Should().Contain(">" + Title + "</rdf:li>");
    }

    [Fact]
    public void MarkupInAValueIsEscapedRatherThanWritten()
    {
        var bytes = Save(document =>
        {
            document.Options.WriteXmpMetadata = true;
            document.Info.Title = "Bolts & Nuts <Ltd>";
        });

        var text = Latin1(bytes);
        text.Should().Contain("Bolts &amp; Nuts &lt;Ltd&gt;");
        text.Should().NotContain("Bolts & Nuts <Ltd>", "unescaped markup would make the packet unparseable");
    }

    [Fact]
    public void ADocumentThatClaimsNothingIsUnchanged()
    {
        var bytes = Save(document => { });

        Latin1(bytes).Should().NotContain("/OutputIntent").And.NotContain("pdfaid");
    }

    [Fact]
    public void APacketCanBeBuiltWithoutADocumentToBuildItFrom()
    {
        var metadata = new XmpMetadata
        {
            Title = "Standalone",
            Conformance = PdfAConformance.PdfA1B,
        };

        var text = Encoding.UTF8.GetString(metadata.Build());

        text.Should().Contain("<pdfaid:part>1</pdfaid:part>");
        text.Should().Contain("Standalone");
        text.Should().EndWith("<?xpacket end=\"w\"?>\n");
    }

    /// <summary>Everything a document needs before it may claim a profile.</summary>
    private static Action<PdfDocument> Conforming(PdfAConformance conformance) => document =>
    {
        document.Options.Conformance = conformance;
        document.Options.OutputIntentIccProfile = SomeProfile;
        document.Options.OutputIntentIdentifier = "sRGB IEC61966-2.1";
    };

    private static byte[] Save(Action<PdfDocument> arrange)
    {
        var document = new PdfDocument();
        document.AddPage();
        document.Info.Title = Title;

        arrange(document);

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}
