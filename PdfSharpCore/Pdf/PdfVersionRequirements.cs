namespace PdfSharpCore.Pdf;

/// <summary>
/// Where a feature that needs a newer PDF version than the document's default raises the floor —
/// so that fixing what one feature requires is not three modules independently agreeing to raise
/// the same number.
/// </summary>
/// <remarks>
/// <see cref="Advanced.PdfAttachments"/> raises the floor for <c>/AF</c> and <c>/UF</c>,
/// <see cref="PdfDocument"/> raises it for a cross-reference stream, and
/// <see cref="Metadata.PdfConformanceWriter"/> raises it for the PDF/A profile claimed. All three
/// only ever raise: a document that has already asked for something newer keeps it.
/// </remarks>
internal static class PdfVersionRequirements
{
    public static void Require(PdfDocument document, int floor)
    {
        if (document._version < floor)
            document._version = floor;
    }
}
