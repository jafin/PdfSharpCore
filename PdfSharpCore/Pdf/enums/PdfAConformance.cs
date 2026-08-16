namespace PdfSharpCore.Pdf;

/// <summary>
/// The archival profile a document claims to conform to.
/// </summary>
/// <remarks>
/// Setting this does more than label the file. The writer refuses to save a document that breaks a
/// rule of the profile it claims, because a conformance claim discovered to be false by a validator
/// — or by a customer — is worse than no claim at all.
/// <para>
/// Only the <c>B</c> ("basic") levels are here. The <c>A</c> ("accessible") levels additionally
/// require a full tagged structure tree, so they belong with that work and not with this.
/// </para>
/// </remarks>
public enum PdfAConformance
{
    /// <summary>
    /// No claim. The document is an ordinary PDF and nothing is enforced.
    /// </summary>
    None,

    /// <summary>
    /// PDF/A-1b (ISO 19005-1). The strictest of the three: no transparency, no JPXDecode, no
    /// embedded files, and PDF 1.4 constructs only.
    /// </summary>
    PdfA1B,

    /// <summary>
    /// PDF/A-2b (ISO 19005-2). Allows transparency and JPXDecode. Still no embedded files unless
    /// they are themselves PDF/A.
    /// </summary>
    PdfA2B,

    /// <summary>
    /// PDF/A-3b (ISO 19005-3). As PDF/A-2b, and the only profile that may carry an attachment of
    /// any kind — which is what hybrid e-invoices such as ZUGFeRD and Factur-X are built on.
    /// </summary>
    PdfA3B,
}
