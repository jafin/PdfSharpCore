using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SampleApp.Infrastructure;

namespace SampleApp.Demos;

/// <summary>
///   PDF/A: a document that promises to still open in fifty years, and a library that refuses to
///   write the promise unless the document keeps it.
/// </summary>
internal sealed class ArchiveDemo : PdfDemo
{
    public ArchiveDemo() : base() { }

    public override string Name => "Archive";

    public override string Summary => "PDF/A conformance, the XMP packet, and the refusals that enforce the claim.";

    public override IReadOnlyList<string> Shows => new[]
    {
        "PdfDocumentOptions.Conformance - PdfA1B, PdfA2B and PdfA3B, and what separates them",
        "That the claim is enforced at save time rather than stamped on the file",
        "The XMP packet the document actually carries, printed from its own bytes",
        "OutputIntentIccProfile - why no profile ships, and what the one embedded here is",
        "CustomizeMetadata and XmpMetadata.AdditionalDescriptions, the seam the FacturX demo uses",
        "That a namespace PDF/A has not heard of has to be declared in an extension schema first",
        "Five refusal messages, caught from documents built to break one rule each",
    };

    public override int PageCount => 4;

    protected override PdfDocument Build(DemoContext context)
    {
        #region example
        XFont heading = new XFont(BundledFontResolver.SansFamily, 16, XFontStyle.Bold);
        XFont label = new XFont(BundledFontResolver.SansFamily, 9.5, XFontStyle.Bold);
        XFont body = new XFont(BundledFontResolver.SansFamily, 9);
        XFont mono = new XFont(BundledFontResolver.MonoFamily, 7);

        // The output intent every PDF/A document needs. Carried as an embedded asset rather
        // than built here: no profile ships with the library, so a demo making the claim has
        // to be given one, and a real profile that may be redistributed is a better answer
        // than a convincing fake. assets/icc/LICENSE.txt says where it came from.
        byte[] profile = Assets.Bytes(Assets.SrgbProfile);

        PdfDocument document = new PdfDocument();

        // A rule, not a nicety: a PDF/A document has to have a title, in the information dictionary
        // and in the XMP packet alike, and the writer refuses without one.
        document.Info.Title = "Archive";
        document.Info.Author = "PdfSharpCore sample app";
        document.Info.Subject = "A document claiming PDF/A-3b";
        document.Info.Creator = "SampleApp";

        // The claim. Everything else on this page follows from this one line.
        document.Options.Conformance = PdfAConformance.PdfA3B;
        document.Options.OutputIntentIccProfile = profile;
        document.Options.OutputIntentIdentifier = "sRGB IEC61966-2.1";

        // Written verbatim after the descriptions this library builds, which is the seam a hybrid
        // e-invoice goes through: ZUGFeRD and Factur-X are a PDF/A-3 file with an XML attachment
        // and an extension schema saying what the attachment is. The FacturX demo is that, built
        // through PdfSharpCore.EInvoice rather than by hand.
        document.CustomizeMetadata = metadata =>
        {
            metadata.Keywords = "archival, conformance, sample";

            // Declared before it is used, and that order is the whole lesson. Clause 6.6.2.3.1
            // holds every property in the packet to a schema the file either predefines or
            // describes, so a document writing sample:demo without the block below opens perfectly
            // in every reader and fails validation - for its metadata, not for anything a reader
            // would notice. This demo did exactly that until veraPDF was pointed at its own output.
            metadata.AdditionalDescriptions.Add(SampleExtensionSchema);
            metadata.AdditionalDescriptions.Add(
                "<rdf:Description rdf:about=\"\" xmlns:sample=\"http://example.invalid/sample/1.0/\">"
                + "<sample:demo>Archive</sample:demo>"
                + "</rdf:Description>");
        };

        // ----- page one: what the claim means ------------------------------------------------------

        PdfPage first = document.AddPage();
        using (XGraphics gfx = XGraphics.FromPdfPage(first))
        {
            XTextFormatter prose = new XTextFormatter(gfx);

            gfx.DrawString("A document that has to last", heading, XBrushes.Black, 50, 60);

            prose.DrawString(
                "PDF/A is the profile of PDF for keeping things. It removes everything whose meaning "
                + "depends on something outside the file - a font installed on the machine, a colour "
                + "profile named rather than embedded, a JavaScript action, an external stream - so "
                + "that the bytes are the whole document. This file claims PDF/A-3b, which is why it "
                + "carries an ICC profile and an XMP packet it would otherwise have no use for.",
                body, XBrushes.Black, new XRect(50, 80, 495, 70));

            gfx.DrawString("The three profiles", label, XBrushes.Black, 50, 165);

            (string Name, string Says)[] profiles =
            {
                ("PdfA1B (ISO 19005-1)",
                    "The strictest. PDF 1.4 constructs only, so no transparency, no JPXDecode, no "
                    + "cross-reference stream, and no embedded files at all."),
                ("PdfA2B (ISO 19005-2)",
                    "Defined against PDF 1.7. Transparency and JPXDecode are allowed. Still no "
                    + "embedded file unless that file is itself PDF/A."),
                ("PdfA3B (ISO 19005-3)",
                    "As PDF/A-2b, and the only profile that may carry an attachment of any kind - "
                    + "which is what hybrid e-invoices such as ZUGFeRD and Factur-X are built on."),
            };

            double y = 185;
            foreach ((string Name, string Says) each in profiles)
            {
                gfx.DrawString(each.Name, label, XBrushes.MidnightBlue, 50, y);
                prose.DrawString(each.Says, body, XBrushes.Black, new XRect(50, y + 6, 495, 34));
                y += 52;
            }

            gfx.DrawString("Only the B levels are here", label, XBrushes.Firebrick, 50, y + 6);

            prose.DrawString(
                "The A levels - PDF/A-1a and its successors - additionally require a full tagged "
                + "structure tree. That is a different piece of work with a different point to it, "
                + "and it is the Accessibility demo. A document may claim both: PDF/A-3 says it will "
                + "still open in fifty years, PDF/UA-1 says it can be read aloud, and neither implies "
                + "the other.",
                body, XBrushes.Black, new XRect(50, y + 20, 495, 60));

            gfx.DrawString("The claim is checked, not stamped", label, XBrushes.Black, 50, y + 95);

            prose.DrawString(
                "Setting Options.Conformance does more than label the file. Before a byte is written "
                + "the writer walks the document and throws on the first rule it can settle by "
                + "looking - naming the rule and what to do about it. A library that writes "
                + "pdfaid:part 3 onto a file and leaves the caller to hear from a validator, or from "
                + "their customer, that it does not conform has made things worse rather than better. "
                + "The third page of this demo is those refusals, in their own words.",
                body, XBrushes.Black, new XRect(50, y + 109, 495, 80));

            gfx.DrawString("And what a successful save is still not", label, XBrushes.Black, 50, y + 200);

            prose.DrawString(
                "A validator's verdict. Some real rules are not checked here and are said plainly "
                + "rather than implied by silence: that a PDF/A-1 document uses no transparency means "
                + "walking every page's resources, and that no image is JPXDecode means walking every "
                + "image. Neither is done. What is checked is checked properly; veraPDF has the last "
                + "word.",
                body, XBrushes.Black, new XRect(50, y + 214, 495, 60));
        }

        // ----- page two: the packet the file carries -----------------------------------------------

        PdfPage second = document.AddPage();
        using (XGraphics gfx = XGraphics.FromPdfPage(second))
        {
            XTextFormatter prose = new XTextFormatter(gfx);

            gfx.DrawString("The metadata packet", heading, XBrushes.Black, 50, 60);

            prose.DrawString(
                "Every fact in the packet also lives in the document information dictionary, and a "
                + "validator compares the two and complains when they disagree - so the packet is "
                + "built from the dictionary at save time rather than kept beside it and hoped about. "
                + "The conformance identifier is the one thing that has no dictionary entry at all, "
                + "which is why XMP is not optional for a document making a claim.",
                body, XBrushes.Black, new XRect(50, 80, 495, 60));

            gfx.DrawString("A namespace of your own needs declaring", label, XBrushes.Firebrick, 50, 148);

            prose.DrawString(
                "AdditionalDescriptions writes what it is given, verbatim, and PDF/A accepts no "
                + "property whose schema the file has not either predefined or described. So a "
                + "namespace nobody has heard of - this demo's own, or an invoice format's - is "
                + "declared in a pdfaExtension:schemas block naming every property before any of "
                + "them is written. The FacturX demo is that done for real, by "
                + "PdfSharpCore.EInvoice rather than by hand.",
                body, XBrushes.Black, new XRect(50, 162, 495, 62));

            gfx.DrawString("Written by a document just like this one", label, XBrushes.Black, 50, 236);

            prose.DrawString(
                "Read back out of a probe document built with the same options, saved to memory and "
                + "reopened. It is the bytes, not a description of them. Note that the packet is left "
                + "uncompressed: it carries those xpacket markers so a tool can find it by scanning "
                + "for them without parsing the PDF around it, and a compressed packet is invisible "
                + "to one. The probe claims conformance and adds nothing of its own, so what follows "
                + "is the packet a document gets for free.",
                body, XBrushes.Black, new XRect(50, 250, 495, 48));

            double y = 312;
            foreach (string line in PacketOfAProbe(profile))
            {
                if (y > 780)
                    break;

                gfx.DrawString(line, mono, XBrushes.Black, 50, y);
                y += 8.6;
            }
        }

        // ----- page three: the refusals ------------------------------------------------------------

        PdfPage third = document.AddPage();
        using (XGraphics gfx = XGraphics.FromPdfPage(third))
        {
            XTextFormatter prose = new XTextFormatter(gfx);

            gfx.DrawString("The refusals, in their own words", heading, XBrushes.Black, 50, 60);

            prose.DrawString(
                "Each block below is a real exception message, caught from a document built to break "
                + "one rule and then asked to save. Nothing here is quoted - the text is whatever the "
                + "library said when this demo was run, so a reworded message reaches this page by "
                + "itself.",
                body, XBrushes.Black, new XRect(50, 80, 495, 44));

            double y = 140;
            foreach ((string Broken, string Message) refusal in Refusals(profile))
            {
                gfx.DrawString(refusal.Broken, label, XBrushes.Firebrick, 50, y);

                XSize measured = gfx.MeasureString(refusal.Message, mono);
                double height = Math.Ceiling(measured.Width / 470.0) * 9.5 + 12;

                prose.DrawString(refusal.Message, mono, XBrushes.Black,
                    new XRect(62, y + 8, 470, height));

                y += height + 22;
            }
        }

        // ----- page four: the output intent --------------------------------------------------------

        PdfPage fourth = document.AddPage();
        using (XGraphics gfx = XGraphics.FromPdfPage(fourth))
        {
            XTextFormatter prose = new XTextFormatter(gfx);

            gfx.DrawString("The output intent", heading, XBrushes.Black, 50, 60);

            prose.DrawString(
                "A PDF/A document using a device colour space has to embed an ICC profile saying what "
                + "its colours mean, and RGB - the default - is a device colour space. The profile is "
                + "embedded rather than referenced, and that is the whole point of the rule: naming a "
                + "well-known profile is exactly what PDF/A exists to stop, since the name means "
                + "nothing once the machine that understood it is gone.",
                body, XBrushes.Black, new XRect(50, 80, 495, 62));

            gfx.DrawString("No profile ships with the library", label, XBrushes.Black, 50, 158);

            prose.DrawString(
                "Which one is right is a decision about the document rather than about the code - a "
                + "press wants the one its press was profiled with, and shipping a default would "
                + "encourage every caller to make a colour claim they had not thought about. So "
                + "Options.OutputIntentIccProfile has no default and the writer refuses without one.",
                body, XBrushes.Black, new XRect(50, 172, 495, 48));

            gfx.DrawString("The profile this demo embeds", label, XBrushes.Black, 50, 236);

            prose.DrawString(
                "A real sRGB profile, carried as an embedded asset of the sample app: 456 bytes from "
                + "the Compact ICC Profiles collection, released to the public domain under CC0, "
                + "which is what makes it shippable in a repository at all. It states the true sRGB "
                + "primaries and approximates the transfer curve with 42 sampled points rather than "
                + "the parametric form, which is where the size goes. ICC version 2 rather than 4, "
                + "because PDF/A-1 predates version 4 and will not take one - so a v2 profile is the "
                + "one that serves every part. A document that matters should still embed the profile "
                + "its colours were actually made in.",
                body, XBrushes.Black, new XRect(50, 250, 495, 88));

            (string Field, string Value)[] facts =
            {
                ("Profile size", profile.Length.ToString("N0") + " bytes"),
                ("Device class", "mntr (display), ICC version 2.1"),
                ("Colour space", "RGB, PCS XYZ"),
                ("Tags", "desc, cprt, wtpt, rXYZ, gXYZ, bXYZ, rTRC, gTRC, bTRC"),
                ("Licence", "CC0 1.0, public domain - the cprt tag says so itself"),
                ("/OutputConditionIdentifier", document.Options.OutputIntentIdentifier),
                ("/S", "/GTS_PDFA1, for every part of PDF/A and not only the first"),
            };

            double y = 356;
            foreach ((string Field, string Value) fact in facts)
            {
                gfx.DrawString(fact.Field, label, XBrushes.Black, 50, y);
                gfx.DrawString(fact.Value, body, XBrushes.Black, 230, y);
                y += 17;
            }

            gfx.DrawString("/GTS_PDFA1 for all three parts", label, XBrushes.Firebrick, 50, y + 16);

            prose.DrawString(
                "The output intent's subtype names the family rather than the part, so a PDF/A-3 file "
                + "carries /GTS_PDFA1 too. It reads like a mistake and is not; writing /GTS_PDFA3 is "
                + "the mistake.",
                body, XBrushes.Black, new XRect(50, y + 30, 495, 34));

            gfx.DrawString("Fonts were never optional", label, XBrushes.Black, 50, y + 78);

            prose.DrawString(
                "PDF/A requires every font to be embedded, and this library embeds every font with no "
                + "setting to disable it - so the rule that catches most producers out cannot be "
                + "broken here. TrueType outlines are subsetted; PostScript (CFF) outlines cannot be "
                + "and go in whole.",
                body, XBrushes.Black, new XRect(50, y + 92, 495, 48));
        }
        #endregion

        return document;
    }

    /// <summary>
    ///   The extension schema declaring this demo's own namespace, which is what makes writing
    ///   <c>sample:demo</c> into the packet legal rather than merely possible.
    /// </summary>
    /// <remarks>
    ///   One property, so it is short; the shape is the same however many there are. Each property
    ///   needs a name, a value type, a category - <c>internal</c> for something derived from the
    ///   document's own content, <c>external</c> for something that came from outside it - and a
    ///   description. Declare a property the packet never writes, or write one the schema never
    ///   declared, and a validator objects to either.
    /// </remarks>
    const string SampleExtensionSchema =
        "<rdf:Description rdf:about=\"\""
        + " xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\""
        + " xmlns:pdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\""
        + " xmlns:pdfaProperty=\"http://www.aiim.org/pdfa/ns/property#\">"
        + "<pdfaExtension:schemas><rdf:Bag><rdf:li rdf:parseType=\"Resource\">"
        + "<pdfaSchema:schema>PdfSharpCore sample app</pdfaSchema:schema>"
        + "<pdfaSchema:namespaceURI>http://example.invalid/sample/1.0/</pdfaSchema:namespaceURI>"
        + "<pdfaSchema:prefix>sample</pdfaSchema:prefix>"
        + "<pdfaSchema:property><rdf:Seq><rdf:li rdf:parseType=\"Resource\">"
        + "<pdfaProperty:name>demo</pdfaProperty:name>"
        + "<pdfaProperty:valueType>Text</pdfaProperty:valueType>"
        + "<pdfaProperty:category>internal</pdfaProperty:category>"
        + "<pdfaProperty:description>The demo that wrote this document</pdfaProperty:description>"
        + "</rdf:li></rdf:Seq></pdfaSchema:property>"
        + "</rdf:li></rdf:Bag></pdfaExtension:schemas>"
        + "</rdf:Description>";

    /// <summary>
    ///   Builds a probe claiming the same profile, saves it, reopens it and hands back the metadata
    ///   packet it carries, split into lines that fit the page.
    /// </summary>
    static IEnumerable<string> PacketOfAProbe(byte[] profile)
    {
        using PdfDocument probe = new PdfDocument();
        probe.AddPage();
        probe.Info.Title = "A probe";
        probe.Info.Author = "PdfSharpCore sample app";
        probe.Options.Conformance = PdfAConformance.PdfA3B;
        probe.Options.OutputIntentIccProfile = profile;
        probe.Options.OutputIntentIdentifier = "sRGB IEC61966-2.1";

        using MemoryStream buffer = new MemoryStream();
        probe.Save(buffer, false);
        buffer.Position = 0;

        using PdfDocument reopened = PdfReader.Open(buffer, PdfDocumentOpenMode.Import);
        PdfDictionary metadata = reopened.Internals.Catalog.Elements.GetDictionary("/Metadata");
        string packet = Encoding.UTF8.GetString(metadata.Stream.UnfilteredValue);

        foreach (string line in packet.Replace("\r\n", "\n").Split('\n'))
        {
            // Long lines are folded rather than clipped, because the interesting part of a packet is
            // frequently the attribute at the end of one.
            string rest = line.TrimEnd();
            if (rest.Length == 0)
            {
                yield return "";
                continue;
            }

            while (rest.Length > 96)
            {
                yield return rest.Substring(0, 96);
                rest = "    " + rest.Substring(96);
            }

            yield return rest;
        }
    }

    /// <summary>
    ///   One document per rule, each built to break exactly that rule, and what the writer said.
    /// </summary>
    static IEnumerable<(string Broken, string Message)> Refusals(byte[] profile)
    {
        yield return Refusal("No title", profile, document => document.Info.Title = "");

        yield return Refusal("No output intent", profile,
            document => document.Options.OutputIntentIccProfile = null);

        // Setting a password is what raises the security level - assigning the level on its own is
        // refused earlier and by something else, because there would be nothing to encrypt with.
        yield return Refusal("Encrypted", profile,
            document => document.SecuritySettings.UserPassword = "archive");

        yield return Refusal("PDF/A-1 with a cross-reference stream", profile, document =>
        {
            document.Options.Conformance = PdfAConformance.PdfA1B;
            document.Options.CrossReferenceFormat = PdfCrossReferenceFormat.Stream;
        });

        yield return Refusal("PDF/A-1 asked for a PDF 1.7 feature", profile, document =>
        {
            document.Options.Conformance = PdfAConformance.PdfA1B;
            document.Version = 17;
        });
    }

    static (string Broken, string Message) Refusal(string broken, byte[] profile,
        Action<PdfDocument> breakARule)
    {
        using PdfDocument probe = new PdfDocument();
        probe.AddPage();
        probe.Info.Title = "A probe";
        probe.Options.Conformance = PdfAConformance.PdfA2B;
        probe.Options.OutputIntentIccProfile = profile;

        breakARule(probe);

        try
        {
            using MemoryStream buffer = new MemoryStream();
            probe.Save(buffer, false);
        }
        catch (InvalidOperationException refused)
        {
            return (broken, refused.Message);
        }

        // Said rather than asserted: if the writer stops refusing one of these, this page reports it
        // instead of printing a quotation that is no longer true.
        return (broken, "This document saved. The rule is no longer enforced.");
    }
}
