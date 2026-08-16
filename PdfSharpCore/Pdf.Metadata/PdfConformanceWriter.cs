using System;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.Security;

namespace PdfSharpCore.Pdf.Metadata;

/// <summary>
/// Writes what a document needs in order to claim an archival profile — the XMP packet, the output
/// intent — and refuses to write it at all when it breaks a rule of the profile it claims.
/// </summary>
/// <remarks>
/// The refusal is the point. A library that stamps <c>pdfaid:part 3</c> on a file and leaves the
/// caller to find out from a validator, or from their customer, that it does not conform has made
/// things worse rather than better. So every rule checked here throws at save time, naming the rule
/// and what to do about it.
/// </remarks>
internal static class PdfConformanceWriter
{
    /// <summary>
    /// Called while the document is preparing to be saved, after the information dictionary has
    /// been settled — the XMP packet is built from it and the two must agree.
    /// </summary>
    public static void PrepareForSave(PdfDocument document)
    {
        var options = document.Options;
        var claimsConformance = options.Conformance != PdfAConformance.None;

        if (!claimsConformance && !options.WriteXmpMetadata)
            return;

        if (claimsConformance)
        {
            Enforce(document);
            AttachOutputIntent(document);
        }

        AttachMetadata(document);
    }

    /// <summary>
    /// The rules of the claimed profile that can be settled by looking at the document.
    /// </summary>
    /// <remarks>
    /// Deliberately not all of them. Checking that a PDF/A-1 document uses no transparency means
    /// walking every page's resources — <see cref="PdfTransparencyDetector"/> can answer it for one
    /// XObject but not yet for a page — and checking for JPXDecode means walking every image. Both
    /// are real rules and neither is checked here. What is checked is checked properly; what is not
    /// is said plainly rather than implied by silence, so nobody reads a successful save as a
    /// validator's verdict.
    /// </remarks>
    static void Enforce(PdfDocument document)
    {
        var options = document.Options;

        if (document.SecuritySettings.DocumentSecurityLevel != PdfDocumentSecurityLevel.None)
            throw new InvalidOperationException(
                "A PDF/A document may not be encrypted. Either drop the conformance claim or clear "
                + "SecuritySettings.DocumentSecurityLevel.");

        if (string.IsNullOrEmpty(document.Info.Title))
            throw new InvalidOperationException(
                "A PDF/A document has to have a title, in the document information dictionary and "
                + "in its XMP metadata alike. Set Info.Title.");

        if (options.OutputIntentIccProfile == null || options.OutputIntentIccProfile.Length == 0)
            throw new InvalidOperationException(
                "A PDF/A document using a device colour space has to embed an ICC profile as its "
                + "output intent, and " + options.ColorMode + " is one. Set "
                + "Options.OutputIntentIccProfile to the bytes of a profile — no profile ships with "
                + "this library, because which one is right is a decision about the document.");

        if (options.Conformance != PdfAConformance.PdfA3B && HasEmbeddedFiles(document))
            throw new InvalidOperationException(
                "Only PDF/A-3 may carry embedded files; " + options.Conformance + " may not. This is "
                + "the rule that makes PDF/A-3 the profile hybrid e-invoices such as ZUGFeRD and "
                + "Factur-X are built on.");

        // PDF/A-1 is defined against PDF 1.4 and the later parts against PDF 1.7. Raise rather than
        // set, so a document that has already asked for more keeps it.
        var floor = options.Conformance == PdfAConformance.PdfA1B ? 14 : 17;
        if (document._version < floor)
            document._version = floor;
    }

    static bool HasEmbeddedFiles(PdfDocument document)
    {
        var names = document.Catalog.Elements.GetDictionary(PdfCatalog.Keys.Names);
        return names?.Elements["/EmbeddedFiles"] != null;
    }

    /// <summary>
    /// Builds the XMP packet from the information dictionary and hangs it off the catalog.
    /// </summary>
    static void AttachMetadata(PdfDocument document)
    {
        var metadata = XmpMetadata.FromDocument(document);
        metadata.Conformance = document.Options.Conformance;

        document.CustomizeMetadata?.Invoke(metadata);

        var stream = new PdfDictionary(document);
        stream.Elements.SetName("/Type", "/Metadata");
        stream.Elements.SetName("/Subtype", "/XML");

        // Left uncompressed on purpose. The packet is meant to be findable by a tool that scans the
        // bytes without parsing the PDF around it, which is the whole reason it carries those
        // xpacket markers, and a compressed packet is invisible to one.
        stream.CreateStream(metadata.Build());

        document._irefTable.Add(stream);
        document.Catalog.Elements[PdfCatalog.Keys.Metadata] = stream.Reference;
    }

    /// <summary>
    /// Embeds the ICC profile and points the catalog's output intent at it.
    /// </summary>
    static void AttachOutputIntent(PdfDocument document)
    {
        var options = document.Options;

        var profile = new PdfDictionary(document);
        profile.Elements.SetInteger("/N", options.ColorMode == PdfColorMode.Cmyk ? 4 : 3);
        profile.CreateStream(options.OutputIntentIccProfile);
        document._irefTable.Add(profile);

        var intent = new PdfDictionary(document);
        intent.Elements.SetName(Keys.Type, "/OutputIntent");

        // /GTS_PDFA1 for every part of PDF/A, not only the first. The subtype names the family
        // rather than the part — a quirk of the standard that reads like a mistake and is not.
        intent.Elements.SetName(Keys.S, "/GTS_PDFA1");
        intent.Elements.SetString(Keys.OutputConditionIdentifier, options.OutputIntentIdentifier ?? "Custom");
        intent.Elements[Keys.DestOutputProfile] = profile.Reference;
        document._irefTable.Add(intent);

        var intents = new PdfArray(document);
        intents.Elements.Add(intent.Reference);
        document.Catalog.Elements[PdfCatalog.Keys.OutputIntents] = intents;
    }

    /// <summary>
    /// The entries of an output intent dictionary.
    /// </summary>
    static class Keys
    {
        public const string Type = "/Type";
        public const string S = "/S";
        public const string OutputConditionIdentifier = "/OutputConditionIdentifier";
        public const string DestOutputProfile = "/DestOutputProfile";
    }
}
