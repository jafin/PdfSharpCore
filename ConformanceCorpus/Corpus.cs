using System.Collections.Generic;
using System.Text;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Tables;
using MigraDocCore.Rendering;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;

namespace ConformanceCorpus;

/// <summary>
/// The documents a validator is asked about, and what each of them is for.
/// </summary>
/// <remarks>
/// <para>
/// Every document here <b>makes a claim</b> — a PDF/A part or PDF/UA-1 — and that is the whole
/// entry requirement. veraPDF is run with automatic flavour detection, so each file is held to the
/// profile it names in its own metadata; a file claiming nothing would be held to the fallback
/// flavour instead and fail for saying nothing rather than for being wrong.
/// </para>
/// <para>
/// They are small on purpose. A corpus is read by whoever is looking at a failure, and a hundred-page
/// document says no more about a rule than a paragraph does while making the report far harder to
/// place. Each one covers a feature that has a way of breaking conformance: an embedded subset, an
/// output intent, transparency, an associated file, a structure tree.
/// </para>
/// </remarks>
static class Corpus
{
    internal static IEnumerable<(string Name, byte[] Bytes)> Documents()
    {
        yield return ("pdfa-1b", Drawn(PdfAConformance.PdfA1B, transparency: false));
        yield return ("pdfa-2b", Drawn(PdfAConformance.PdfA2B, transparency: true));
        yield return ("pdfa-3b", Drawn(PdfAConformance.PdfA3B, transparency: true));
        yield return ("pdfa-3b-facturx", Invoice());
        yield return ("pdfua-1", Tagged());
    }

    /// <summary>
    /// A page drawn straight onto the graphics surface, claiming a PDF/A part.
    /// </summary>
    /// <remarks>
    /// Text in an embedded subset and a filled rectangle: between them they exercise the font
    /// embedding and the output intent, which is most of what a basic-level claim rests on.
    /// Transparency is what separates PDF/A-1b from the two after it — 1b forbids it outright — so
    /// the same page is drawn with and without, and a validator that disagrees with us about which
    /// part allows what says so on exactly one of them.
    /// </remarks>
    static byte[] Drawn(PdfAConformance conformance, bool transparency)
    {
        var document = new PdfDocument();
        document.Info.Title = "Conformance corpus: " + conformance;
        document.Info.Author = "PdfSharpCore conformance corpus";

        document.Options.Conformance = conformance;
        document.Options.OutputIntentIccProfile = SrgbProfile.Bytes();
        document.Options.OutputIntentIdentifier = "sRGB IEC61966-2.1";

        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 14);
            gfx.DrawString("PdfSharpCore conformance corpus", font, XBrushes.Black, 50, 70);
            gfx.DrawString(conformance.ToString(), new XFont("Arial", 24), XBrushes.Black, 50, 110);

            gfx.DrawRectangle(XBrushes.LightSteelBlue, 50, 140, 200, 60);

            if (transparency)
            {
                // Forbidden by PDF/A-1b and allowed by the two after it, which is the one thing
                // these three pages do not have in common.
                var translucent = new XSolidBrush(XColor.FromArgb(128, 0, 0, 160));
                gfx.DrawRectangle(translucent, 150, 170, 200, 60);
            }
        }

        return Bytes(document);
    }

    /// <summary>
    /// The hybrid e-invoice shape: a readable page for a person and the same invoice as XML for a
    /// machine, associated with the document rather than merely carried inside it.
    /// </summary>
    /// <remarks>
    /// This is what PDF/A-3 exists for, and the part a validator is worth most on — an associated
    /// file has to carry an <c>/AFRelationship</c>, be reachable from the catalog's <c>/AF</c>, and
    /// appear in the embedded-files name tree, and getting two of the three right produces a file
    /// that opens perfectly and conforms to nothing. The XML is a plausible skeleton rather than
    /// real Factur-X: nothing here validates against EN 16931, and pretending otherwise in a fixture
    /// would be a claim of its own.
    /// </remarks>
    static byte[] Invoice()
    {
        var document = new PdfDocument();
        document.Info.Title = "Conformance corpus: invoice with an associated file";
        document.Info.Author = "PdfSharpCore conformance corpus";

        document.Options.Conformance = PdfAConformance.PdfA3B;
        document.Options.OutputIntentIccProfile = SrgbProfile.Bytes();
        document.Options.OutputIntentIdentifier = "sRGB IEC61966-2.1";

        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawString("Invoice 2026-0042", new XFont("Arial", 20), XBrushes.Black, 50, 70);
            gfx.DrawString("Total due: 240.00 EUR", new XFont("Arial", 14), XBrushes.Black, 50, 110);
        }

        const string invoice =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<Invoice><ID>2026-0042</ID><Total currency=\"EUR\">240.00</Total></Invoice>\n";

        document.Attachments.Add("invoice.xml", Encoding.UTF8.GetBytes(invoice),
            PdfAFRelationship.Data, "The invoice as data", "text/xml");

        return Bytes(document);
    }

    /// <summary>
    /// A tagged document claiming PDF/UA-1, rendered through MigraDoc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one that most needs an outside opinion. <c>PdfUaValidator</c> holds a document to the
    /// rules that can be settled by looking at it, and says in its own remarks what it cannot reach —
    /// the largest being that no content sits outside the structure tree, which needs a content-stream
    /// pass it does not make. That is exactly what veraPDF does make.
    /// </para>
    /// <para>
    /// It carries a heading, body text, a list, a table with a heading row, a hyperlink and a
    /// footnote, because each of those is a different corner of the tagger. The footnote is the
    /// reason this document exists: its <c>/Note</c> is built where the mark is drawn and filled in
    /// from the foot of the page, and the nesting that produces — a <c>/Lbl</c> and body paragraphs
    /// inside an inline-level <c>/Note</c> — is a reading of the standard that no test here can
    /// settle.
    /// </para>
    /// </remarks>
    static byte[] Tagged()
    {
        var document = new Document();
        var section = document.AddSection();

        section.AddParagraph("Statement of account", "Heading1");
        section.AddParagraph(
            "Amounts are shown in pounds sterling and include value added tax where it applies.");

        var cited = section.AddParagraph("The rate applied to this account changed in April.");
        cited.AddFootnote("Set by the schedule in force on the date of issue.");

        var bulleted = section.AddParagraph("Payable by bank transfer", "List");
        bulleted.Format.ListInfo = new ListInfo { ListType = ListType.BulletList1 };
        var alsoBulleted = section.AddParagraph("Payable by direct debit", "List");
        alsoBulleted.Format.ListInfo = new ListInfo { ListType = ListType.BulletList1 };

        var table = section.AddTable();
        table.AddColumn(Unit.FromCentimeter(6));
        table.AddColumn(Unit.FromCentimeter(4));

        var heading = table.AddRow();
        heading.HeadingFormat = true;
        heading.Cells[0].AddParagraph("Description");
        heading.Cells[1].AddParagraph("Amount");

        var row = table.AddRow();
        row.Cells[0].AddParagraph("Standing charge");
        row.Cells[1].AddParagraph("40.00");

        var linking = section.AddParagraph("Terms are published at ");
        var link = linking.AddHyperlink("https://example.org/terms", HyperlinkType.Web);
        link.AddText("example.org/terms");

        var renderer = new PdfDocumentRenderer(true)
        {
            Document = document,
            TagContent = true,
            Language = "en-GB",
        };

        renderer.RenderDocument();

        renderer.PdfDocument.Info.Title = "Statement of account";
        renderer.PdfDocument.Info.Author = "PdfSharpCore conformance corpus";
        renderer.PdfDocument.Options.UAConformance = PdfUAConformance.PdfUA1;

        return Bytes(renderer.PdfDocument);
    }

    static byte[] Bytes(PdfDocument document)
    {
        using var stream = new System.IO.MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }
}
