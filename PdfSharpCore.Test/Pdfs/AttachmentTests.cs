using System;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.Annotations;
using PdfSharpCore.Pdf.IO;
using Xunit;

// This namespace has a PdfReader of its own, so the one that opens documents needs saying in full.
using Reader = PdfSharpCore.Pdf.IO.PdfReader;

namespace PdfSharpCore.Test.Pdfs;

/// <summary>
///   A document could hold an embedded file and a file specification — both classes were already
///   here — but nothing said what the file had to do with the document, and nothing associated it
///   with the document at all. Without <c>/AFRelationship</c> and the catalog's <c>/AF</c> array an
///   attachment is bytes lying inside a PDF rather than part of it, which is precisely what PDF/A-3
///   exists to rule out, and PDF/A-3 is the container the hybrid e-invoice formats are built on.
///
///   <see cref="PdfAttachments"/> is the way in, and the conformance writer now holds a PDF/A-3
///   claim to both rules that permitting attachments brings with it.
/// </summary>
public class AttachmentTests
{
    private const string Title = "Invoice 2026-0042";

    private const string InvoiceXml = "<Invoice><Total>42.00</Total></Invoice>";

    /// <summary>
    ///   Not a real ICC profile — nothing here parses one, and saying so plainly keeps this from
    ///   reading as a colour-management test.
    /// </summary>
    private static readonly byte[] SomeProfile = Encoding.ASCII.GetBytes("NOT-AN-ICC-PROFILE");

    // ── What a document says when nothing is attached ───────────────────────────────────────────

    [Fact]
    public void ADocumentCarriesNothingUntilSomethingIsAttached()
    {
        var document = new PdfDocument();
        document.AddPage();

        document.Attachments.Count.Should().Be(0);
        document.Attachments.Should().BeEmpty();
    }

    [Fact]
    public void AskingWhatADocumentCarriesDoesNotMakeItCarryAnything()
    {
        // Reading the collection looks at the catalog rather than building anything, so a document
        // that only ever asks is written exactly as it was before.
        var withoutAsking = Save(document => { });
        var afterAsking = Save(document => document.Attachments.Count.Should().Be(0));

        Latin1(afterAsking).Should().NotContain("/AF").And.NotContain("/EmbeddedFiles");
        afterAsking.Length.Should().Be(withoutAsking.Length);
    }

    // ── Where an attachment goes ────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAttachedFileIsBothAssociatedWithTheDocumentAndListedForAReader()
    {
        // Both, because either alone is half a job: a file only in /AF is invisible to a reader's
        // attachments pane, and a file only in the name tree is invisible to a validator.
        var reread = SaveAndReopen(document => Attach(document, PdfAFRelationship.Data));

        var associated = reread.Internals.Catalog.Elements.GetArray("/AF");
        associated.Should().NotBeNull();
        associated.Elements.Count.Should().Be(1);

        var names = reread.Internals.Catalog.Elements.GetDictionary("/Names");
        names.Should().NotBeNull();
        names.Elements.GetDictionary("/EmbeddedFiles").Should().NotBeNull();
    }

    [Fact]
    public void TheSpecificationIsOneObjectRatherThanOneCopyPerPlaceItIsMentioned()
    {
        // Both mentions hold a reference to the same object. Two copies would drift apart the first
        // time anybody changed one, and would embed the bytes twice.
        var reread = SaveAndReopen(document => Attach(document, PdfAFRelationship.Data));

        reread.Attachments.Count.Should().Be(1, "the one file is listed in two places, not two files");
    }

    [Fact]
    public void AnAttachmentSurvivesBeingSavedAndReopened()
    {
        var reread = SaveAndReopen(document => Attach(document, PdfAFRelationship.Data));

        var attachment = reread.Attachments.Single();
        attachment.FileName.Should().Be("factur-x.xml");
        attachment.Relationship.Should().Be(PdfAFRelationship.Data);
        attachment.Description.Should().Be("Factur-X invoice");
        attachment.EmbeddedFile.Should().NotBeNull();
        attachment.EmbeddedFile.MimeType.Should().Be("/text/xml");
    }

    [Fact]
    public void TheAttachedBytesComeBackOut()
    {
        // The point of the whole exercise for an e-invoice: a system reads the XML back out of the
        // document a person read.
        var reread = SaveAndReopen(document => Attach(document, PdfAFRelationship.Data));

        var bytes = reread.Attachments.Single().EmbeddedFile.Stream.UnfilteredValue;

        Encoding.UTF8.GetString(bytes).Should().Be(InvoiceXml);
    }

    [Fact]
    public void AnAttachmentRecordsWhenItWasLastModified()
    {
        // PDF/A-3 asks for it, and an archive that cannot say how old what it keeps is has lost half
        // the point of keeping it.
        var before = DateTime.Now.AddSeconds(-1);
        DateTime recorded = default;

        var reread = SaveAndReopen(document =>
            recorded = Attach(document, PdfAFRelationship.Data).EmbeddedFile.ModificationDate);

        recorded.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.Now.AddSeconds(1));

        // Loose on the way back, and deliberately. A PDF date carries a UTC offset and the date
        // parser normalises one away, so a document written at noon in Sydney reads back as two in
        // the morning — which is the parser's business and not this feature's. A day covers every
        // offset there is, and what is being asserted here is that the entry survived at all.
        reread.Attachments.Single().EmbeddedFile.ModificationDate
            .Should().BeCloseTo(DateTime.Now, TimeSpan.FromDays(1));
    }

    // ── The relationship ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PdfAFRelationship.Source, "/Source")]
    [InlineData(PdfAFRelationship.Data, "/Data")]
    [InlineData(PdfAFRelationship.Alternative, "/Alternative")]
    [InlineData(PdfAFRelationship.Supplement, "/Supplement")]
    [InlineData(PdfAFRelationship.Unspecified, "/Unspecified")]
    public void EachRelationshipIsWrittenAsItsOwnName(PdfAFRelationship relationship, string name)
    {
        var bytes = Save(document => Attach(document, relationship));

        Latin1(bytes).Should().Contain("/AFRelationship " + name);
    }

    [Fact]
    public void AnAttachmentWithNothingToSayStillSaysThat()
    {
        // /Unspecified rather than a missing entry. PDF/A-3 wants the entry there, and "I do not
        // know" is a legal answer where silence is a broken file.
        var reread = SaveAndReopen(document =>
            document.Attachments.Add("notes.txt", Encoding.UTF8.GetBytes("hello")));

        reread.Attachments.Single().Relationship.Should().Be(PdfAFRelationship.Unspecified);
    }

    [Fact]
    public void ARelationshipFromALaterStandardReadsAsUnspecifiedRatherThanThrowing()
    {
        // ISO 32000-2 has three this enumeration does not, and a document is entitled to use one.
        // Refusing to read the rest of the attachment over a name we do not know would help nobody.
        var document = new PdfDocument();
        document.AddPage();
        var attachment = document.Attachments.Add("payload.bin", new byte[] { 1, 2, 3 });

        attachment.Elements.SetName(PdfFileSpecification.Keys.AFRelationship, "/EncryptedPayload");

        attachment.Relationship.Should().Be(PdfAFRelationship.Unspecified);
        attachment.FileName.Should().Be("payload.bin", "the rest of the attachment still reads");
    }

    // ── The name is what identifies an attachment ───────────────────────────────────────────────

    [Fact]
    public void TwoAttachmentsMayNotShareAName()
    {
        var document = new PdfDocument();
        document.AddPage();
        document.Attachments.Add("factur-x.xml", Encoding.UTF8.GetBytes("first"));

        var attaching = () => document.Attachments.Add("factur-x.xml", Encoding.UTF8.GetBytes("second"));

        attaching.Should().Throw<InvalidOperationException>().WithMessage("*already carries*factur-x.xml*");
    }

    [Fact]
    public void TheListedNamesAreSortedWhateverOrderTheyWereAttachedIn()
    {
        // A reader is entitled to binary-search a name tree, even one that is a single node.
        var document = new PdfDocument();
        document.AddPage();
        document.Attachments.Add("zeta.txt", new byte[] { 1 });
        document.Attachments.Add("alpha.txt", new byte[] { 2 });
        document.Attachments.Add("middle.txt", new byte[] { 3 });

        Keys(document).Should().ContainInOrder("alpha.txt", "middle.txt", "zeta.txt");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnAttachmentHasToBeNamed(string fileName)
    {
        var document = new PdfDocument();
        document.AddPage();

        var attaching = () => document.Attachments.Add(fileName, new byte[] { 1 });

        attaching.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AnAttachmentHasToHaveContent()
    {
        var document = new PdfDocument();
        document.AddPage();

        var attaching = () => document.Attachments.Add("empty.txt", null);

        attaching.Should().Throw<ArgumentNullException>();
    }

    // ── Taking one back off ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAttachmentCanBeTakenBackOffAgain()
    {
        var document = new PdfDocument();
        document.AddPage();
        var attachment = document.Attachments.Add("scratch.txt", new byte[] { 1 });

        document.Attachments.Remove(attachment).Should().BeTrue();

        document.Attachments.Count.Should().Be(0);
        Keys(document).Should().BeEmpty();
        document.Internals.Catalog.Elements.GetArray("/AF")
            .Should().BeNull("an empty association array says less than no array at all");
    }

    [Fact]
    public void RemovingAFileTheDocumentDoesNotCarrySaysSo()
    {
        var document = new PdfDocument();
        document.AddPage();
        var elsewhere = new PdfFileSpecification(document, "other.txt",
            new PdfEmbeddedFile(document, new byte[] { 1 }));

        document.Attachments.Remove(elsewhere).Should().BeFalse();
    }

    // ── Reading what another producer wrote ─────────────────────────────────────────────────────

    [Fact]
    public void AnAttachmentListedOnlyInTheNameTreeIsStillFound()
    {
        // How every attachment written before /AF existed looks, and how a plain Acrobat attachment
        // still looks: in the name tree and associated with nothing. Reading only /AF would show a
        // caller an empty document that plainly has a file in it.
        var reread = SaveAndReopen(document =>
        {
            document.Attachments.Add("legacy.txt", Encoding.UTF8.GetBytes("older"));
            document.Internals.Catalog.Elements.Remove("/AF");
        });

        reread.Attachments.Single().FileName.Should().Be("legacy.txt");
    }

    // ── What the archival profiles make of it ───────────────────────────────────────────────────

    [Theory]
    [InlineData(PdfAConformance.PdfA1B)]
    [InlineData(PdfAConformance.PdfA2B)]
    public void OnlyPdfA3MayCarryAnAttachment(PdfAConformance conformance)
    {
        var saving = () => Save(document =>
        {
            Conforming(conformance)(document);
            Attach(document, PdfAFRelationship.Data);
        });

        saving.Should().Throw<InvalidOperationException>()
            .WithMessage("*may not carry an embedded file*factur-x.xml*");
    }

    [Fact]
    public void PdfA3CarriesAnAttachmentAndSaysWhatItIs()
    {
        var bytes = Save(document =>
        {
            Conforming(PdfAConformance.PdfA3B)(document);
            Attach(document, PdfAFRelationship.Data);
        });

        var text = Latin1(bytes);
        text.Should().Contain("<pdfaid:part>3</pdfaid:part>");
        text.Should().Contain("/AFRelationship /Data");
        text.Should().Contain("/AF");
    }

    [Fact]
    public void AnAttachmentHangingOffAnAnnotationIsSeenByTheCheckToo()
    {
        // The defect the check had while it looked only at the name tree: a file attachment
        // annotation was the one way a caller could attach anything, and the one way the check
        // could not see. A PDF/A-1 claim over such a document was accepted in silence.
        var saving = () => Save(document =>
        {
            Conforming(PdfAConformance.PdfA1B)(document);
            AttachToAnAnnotation(document);
        });

        saving.Should().Throw<InvalidOperationException>()
            .WithMessage("*may not carry an embedded file*attached.txt*");
    }

    [Fact]
    public void PdfA3RefusesAFileThatIsInTheDocumentButNotOfIt()
    {
        var saving = () => Save(document =>
        {
            Conforming(PdfAConformance.PdfA3B)(document);
            AttachToAnAnnotation(document);
        });

        saving.Should().Throw<InvalidOperationException>()
            .WithMessage("*attached.txt*associated with nothing*Attachments.Associate*");
    }

    [Fact]
    public void AssociatingAnAnnotationsFileIsWhatPdfA3WantedAllAlong()
    {
        // Nothing is attached twice: the same specification gains one more mention, which is the
        // mention that makes it part of the document.
        var bytes = Save(document =>
        {
            Conforming(PdfAConformance.PdfA3B)(document);
            var specification = AttachToAnAnnotation(document);
            specification.Relationship = PdfAFRelationship.Supplement;
            document.Attachments.Associate(specification).Should().BeTrue();
            document.Attachments.Associate(specification).Should().BeFalse("it is listed already");
        });

        Latin1(bytes).Should().Contain("/AFRelationship /Supplement");
    }

    [Fact]
    public void PdfA3RefusesAnAttachmentThatWillNotSayWhatItIs()
    {
        // Refused rather than written as /Unspecified. Deciding what an attachment means is the one
        // thing this code cannot do, and guessing is how a claim nothing stands behind gets made.
        var saving = () => Save(document =>
        {
            Conforming(PdfAConformance.PdfA3B)(document);
            var attachment = Attach(document, PdfAFRelationship.Data);
            attachment.Elements.Remove(PdfFileSpecification.Keys.AFRelationship);
        });

        saving.Should().Throw<InvalidOperationException>()
            .WithMessage("*factur-x.xml*says nothing*Relationship*");
    }

    [Fact]
    public void ADocumentThatAttachesNothingIsUnaffectedByAnyOfThis()
    {
        var bytes = Save(Conforming(PdfAConformance.PdfA3B));

        Latin1(bytes).Should().NotContain("/AF").And.NotContain("/EmbeddedFiles");
    }

    // ── Arranging ───────────────────────────────────────────────────────────────────────────────

    private static PdfFileSpecification Attach(PdfDocument document, PdfAFRelationship relationship)
    {
        return document.Attachments.Add("factur-x.xml", Encoding.UTF8.GetBytes(InvoiceXml),
            relationship, "Factur-X invoice", "text/xml");
    }

    /// <summary>
    ///   The older way of putting a file in a document, and the only way there was: hang it off an
    ///   annotation, which lists it nowhere the catalog can see.
    /// </summary>
    private static PdfFileSpecification AttachToAnAnnotation(PdfDocument document)
    {
        var embedded = new PdfEmbeddedFile(document, Encoding.UTF8.GetBytes("attached"));
        var specification = new PdfFileSpecification(document, "attached.txt", embedded);

        var annotation = new PdfFileAttachmentAnnotation(document) { File = specification };
        document.Pages[0].Annotations.Add(annotation);

        return specification;
    }

    /// <summary>Everything a document needs before it may claim a profile.</summary>
    private static Action<PdfDocument> Conforming(PdfAConformance conformance) => document =>
    {
        document.Options.Conformance = conformance;
        document.Options.OutputIntentIccProfile = SomeProfile;
        document.Options.OutputIntentIdentifier = "sRGB IEC61966-2.1";
    };

    /// <summary>The names the <c>/EmbeddedFiles</c> tree lists, in the order it lists them.</summary>
    private static string[] Keys(PdfDocument document)
    {
        var names = document.Internals.Catalog.Elements.GetDictionary("/Names");
        var leaves = names?.Elements.GetDictionary("/EmbeddedFiles")?.Elements.GetArray("/Names");
        if (leaves == null)
            return Array.Empty<string>();

        return Enumerable.Range(0, leaves.Elements.Count / 2)
            .Select(pair => ((PdfString)leaves.Elements[pair * 2]).Value)
            .ToArray();
    }

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

    private static PdfDocument SaveAndReopen(Action<PdfDocument> arrange)
    {
        var saved = new MemoryStream(Save(arrange));
        return Reader.Open(saved, PdfDocumentOpenMode.Modify);
    }

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}
