namespace PdfSharpCore.Pdf;

/// <summary>
/// The accessibility profile a document claims to conform to.
/// </summary>
/// <remarks>
/// As with <see cref="PdfAConformance"/>, setting this does more than label the file: the writer
/// refuses to save a document that breaks a rule it can check, naming the rule. An accessibility
/// claim is read by a procurement officer rather than by a reader, and one that turns out to be
/// false is worse than none.
/// <para>
/// What can be checked before the bytes are written is not the whole standard, and
/// <see cref="Structure.PdfUaValidator"/> says which rules it holds a document to. A successful save
/// is not a validator's verdict; veraPDF is.
/// </para>
/// </remarks>
public enum PdfUAConformance
{
    /// <summary>
    /// No claim. A document may still be tagged — tagging is what makes it useful, and the claim is
    /// a separate promise about the whole file.
    /// </summary>
    None,

    /// <summary>
    /// PDF/UA-1 (ISO 14289-1). What every tool validates against today.
    /// </summary>
    PdfUA1,
}
