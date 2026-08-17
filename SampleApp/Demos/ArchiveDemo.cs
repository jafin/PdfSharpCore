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
        "OutputIntentIccProfile - why no profile ships, and what a minimal one contains",
        "CustomizeMetadata and XmpMetadata.AdditionalDescriptions, the ZUGFeRD seam",
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

        byte[] profile = MinimalSrgbProfile();

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
        // and an extension schema saying what the attachment is.
        document.CustomizeMetadata = metadata =>
        {
            metadata.Keywords = "archival, conformance, sample";
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

            gfx.DrawString("Written by a document just like this one", label, XBrushes.Black, 50, 155);

            prose.DrawString(
                "Read back out of a probe document built with the same options, saved to memory and "
                + "reopened. It is the bytes, not a description of them. Note that the packet is left "
                + "uncompressed: it carries those xpacket markers so a tool can find it by scanning "
                + "for them without parsing the PDF around it, and a compressed packet is invisible "
                + "to one.",
                body, XBrushes.Black, new XRect(50, 170, 495, 48));

            double y = 232;
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
                "Written by this file, in the method below the example, for the same reason: there "
                + "was none to hand and checking a binary into a sample app raises a licence question "
                + "the sample does not need. It is a minimal ICC v2 matrix/TRC display profile with "
                + "the sRGB primaries adapted to D50 and a gamma of 2.2 - enough to be a real profile "
                + "and honest about being the least one. A document that matters should embed the "
                + "profile its colours were actually made in.",
                body, XBrushes.Black, new XRect(50, 250, 495, 72));

            (string Field, string Value)[] facts =
            {
                ("Profile size", profile.Length.ToString("N0") + " bytes"),
                ("Device class", "mntr (display)"),
                ("Colour space", "RGB, PCS XYZ"),
                ("Tags", "desc, cprt, wtpt, rXYZ, gXYZ, bXYZ, rTRC, gTRC, bTRC"),
                ("/OutputConditionIdentifier", document.Options.OutputIntentIdentifier),
                ("/S", "/GTS_PDFA1, for every part of PDF/A and not only the first"),
            };

            double y = 340;
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

    /// <summary>
    ///   A minimal but genuine ICC v2 RGB matrix/TRC display profile, with the sRGB primaries
    ///   adapted to the D50 white point every ICC profile connection space uses, and a gamma of 2.2.
    /// </summary>
    /// <remarks>
    ///   Built here rather than carried as a file. No profile ships with the library, on purpose;
    ///   checking a binary into a sample app raises a redistribution question the sample does not
    ///   need to answer, and a profile written out in code can be read. It is deliberately the least
    ///   profile that is still one: nine required tags, a single gamma curve per channel, and no
    ///   measurement data. A real archival document should embed the profile its colours were
    ///   actually made in.
    /// </remarks>
    static byte[] MinimalSrgbProfile()
    {
        // Tag signature, and the bytes of the tag's own data.
        (string Signature, byte[] Data)[] tags =
        {
            ("desc", TextDescription("Minimal sRGB, written by the PdfSharpCore sample app")),
            ("cprt", Text("No rights reserved.")),
            ("wtpt", Xyz(0.96420, 1.00000, 0.82491)),
            ("rXYZ", Xyz(0.43607, 0.22249, 0.01392)),
            ("gXYZ", Xyz(0.38515, 0.71687, 0.09708)),
            ("bXYZ", Xyz(0.14307, 0.06061, 0.71410)),
            ("rTRC", Gamma(2.2)),
            ("gTRC", Gamma(2.2)),
            ("bTRC", Gamma(2.2)),
        };

        const int headerSize = 128;
        int tableSize = 4 + tags.Length * 12;

        // Every tag starts on a four-byte boundary, so the offsets have to be laid out before
        // anything is written and the padding counted into the total.
        int[] offsets = new int[tags.Length];
        int at = Aligned(headerSize + tableSize);
        for (int index = 0; index < tags.Length; index++)
        {
            offsets[index] = at;
            at = Aligned(at + tags[index].Data.Length);
        }

        byte[] icc = new byte[at];

        WriteUInt32(icc, 0, (uint)icc.Length);
        WriteUInt32(icc, 8, 0x02100000);                   // version 2.1.0
        WriteSignature(icc, 12, "mntr");                       // display device
        WriteSignature(icc, 16, "RGB ");
        WriteSignature(icc, 20, "XYZ ");                       // profile connection space
        WriteSignature(icc, 36, "acsp");                       // the file magic

        // The PCS illuminant is D50 and is not negotiable: it is the white point the connection
        // space is defined at, whatever the profile's own media white point says.
        WriteFixed(icc, 68, 0.96420);
        WriteFixed(icc, 72, 1.00000);
        WriteFixed(icc, 76, 0.82491);

        WriteUInt32(icc, headerSize, (uint)tags.Length);
        for (int index = 0; index < tags.Length; index++)
        {
            int entry = headerSize + 4 + index * 12;
            WriteSignature(icc, entry, tags[index].Signature);
            WriteUInt32(icc, entry + 4, (uint)offsets[index]);
            WriteUInt32(icc, entry + 8, (uint)tags[index].Data.Length);

            Array.Copy(tags[index].Data, 0, icc, offsets[index], tags[index].Data.Length);
        }

        return icc;
    }

    /// <summary>An <c>XYZType</c> tag: three s15Fixed16 numbers behind their type signature.</summary>
    static byte[] Xyz(double x, double y, double z)
    {
        byte[] data = new byte[20];
        WriteSignature(data, 0, "XYZ ");
        WriteFixed(data, 8, x);
        WriteFixed(data, 12, y);
        WriteFixed(data, 16, z);
        return data;
    }

    /// <summary>
    ///   A <c>curveType</c> tag holding a single number, which an ICC reader takes as a gamma
    ///   exponent in u8Fixed8 rather than as a one-entry sampled curve.
    /// </summary>
    static byte[] Gamma(double exponent)
    {
        byte[] data = new byte[14];
        WriteSignature(data, 0, "curv");
        WriteUInt32(data, 8, 1);
        WriteUInt16(data, 12, (ushort)Math.Round(exponent * 256));
        return data;
    }

    /// <summary>A <c>textType</c> tag: an ASCII string with a terminating NUL.</summary>
    static byte[] Text(string value)
    {
        byte[] ascii = Encoding.ASCII.GetBytes(value);
        byte[] data = new byte[8 + ascii.Length + 1];
        WriteSignature(data, 0, "text");
        Array.Copy(ascii, 0, data, 8, ascii.Length);
        return data;
    }

    /// <summary>
    ///   A <c>textDescriptionType</c> tag, which ICC v2 requires for <c>desc</c> and which carries
    ///   room for a Unicode and a Macintosh ScriptCode form neither of which is filled in here.
    /// </summary>
    static byte[] TextDescription(string value)
    {
        byte[] ascii = Encoding.ASCII.GetBytes(value);
        byte[] data = new byte[8 + 4 + ascii.Length + 1 + 4 + 4 + 2 + 1 + 67];
        WriteSignature(data, 0, "desc");
        WriteUInt32(data, 8, (uint)(ascii.Length + 1));
        Array.Copy(ascii, 0, data, 12, ascii.Length);
        return data;
    }

    static int Aligned(int value) => (value + 3) & ~3;

    static void WriteSignature(byte[] buffer, int at, string signature)
    {
        for (int index = 0; index < 4; index++)
            buffer[at + index] = (byte)signature[index];
    }

    static void WriteUInt32(byte[] buffer, int at, uint value)
    {
        buffer[at] = (byte)(value >> 24);
        buffer[at + 1] = (byte)(value >> 16);
        buffer[at + 2] = (byte)(value >> 8);
        buffer[at + 3] = (byte)value;
    }

    static void WriteUInt16(byte[] buffer, int at, ushort value)
    {
        buffer[at] = (byte)(value >> 8);
        buffer[at + 1] = (byte)value;
    }

    /// <summary>An s15Fixed16 number: the value multiplied by 65536, as a signed 32-bit integer.</summary>
    static void WriteFixed(byte[] buffer, int at, double value) =>
        WriteUInt32(buffer, at, unchecked((uint)(int)Math.Round(value * 65536)));
}
