using System;
using System.Collections.Generic;
using PdfSharpCore.Pdf.AcroForms;
using PdfSharpCore.Pdf.Internal;

namespace PdfSharpCore.Pdf.Signatures;

/// <summary>
/// Finds the signatures a document already carries.
/// </summary>
/// <remarks>
/// Reading only. What is here is enough to say what a document claims about who signed it and which
/// bytes each signature covers, which is the part that needs the PDF object model. Deciding whether
/// to believe any of it is cryptography, and lives in <c>PdfSharpCore.Signing</c>.
/// </remarks>
public static class PdfSignatures
{
    /// <summary>
    /// Every signature in the document, in the order the interactive form lists its fields.
    /// </summary>
    public static IReadOnlyList<PdfSignatureInfo> InDocument(PdfDocument document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var found = new List<PdfSignatureInfo>();

        var form = document.Catalog.Elements.GetDictionary(PdfCatalogAcroFormKey);
        var fields = form?.Elements.GetArray(PdfAcroForm.Keys.Fields);
        if (fields == null)
            return found;

        Collect(fields, found, 0);
        return found;
    }

    const string PdfCatalogAcroFormKey = "/AcroForm";

    /// <summary>
    /// Walks the field tree. Fields nest — a field may stand for a group and hold its members in
    /// <c>/Kids</c> — so a signature is not always a direct child of <c>/Fields</c>.
    /// </summary>
    static void Collect(PdfArray fields, List<PdfSignatureInfo> found, int depth)
    {
        // A malformed document can point a field's kids back at an ancestor. Bounding the descent
        // is cheaper than tracking what has been seen and is just as effective: no real form is
        // anywhere near this deep.
        if (depth > 32)
            return;

        for (var index = 0; index < fields.Elements.Count; index++)
        {
            var field = fields.Elements.GetDictionary(index);
            if (field == null)
                continue;

            var kids = field.Elements.GetArray("/Kids");
            if (kids != null)
                Collect(kids, found, depth + 1);

            if (field.Elements.GetName("/FT") != "/Sig")
                continue;

            var value = field.Elements.GetDictionary("/V");
            var info = Read(field, value);
            if (info != null)
                found.Add(info);
        }
    }

    static PdfSignatureInfo Read(PdfDictionary field, PdfDictionary value)
    {
        if (value == null)
            return null;

        var range = value.Elements.GetArray("/ByteRange");
        if (range == null)
            return null;

        var byteRange = new int[range.Elements.Count];
        for (var index = 0; index < byteRange.Length; index++)
            byteRange[index] = range.Elements.GetInteger(index);

        return new PdfSignatureInfo(
            field.Elements.GetString("/T"),
            value.Elements.GetName("/SubFilter"),
            ContentsOf(value),
            byteRange,
            NullIfEmpty(value.Elements.GetString("/Name")),
            NullIfEmpty(value.Elements.GetString("/Reason")),
            NullIfEmpty(value.Elements.GetString("/Location")),
            NullIfEmpty(value.Elements.GetString("/ContactInfo")),
            SigningTimeOf(value),
            CertificationLevelOf(value));
    }

    /// <summary>
    /// The bytes of <c>/Contents</c>, one per character.
    /// </summary>
    /// <remarks>
    /// A PDF string is a byte string throughout this library — the lexer reads one character per
    /// byte and the writer asserts it on the way out — so turning one back into bytes is the raw
    /// encoding and never a text decoding.
    /// </remarks>
    static byte[] ContentsOf(PdfDictionary value)
    {
        var contents = value.Elements["/Contents"] as PdfString;
        if (contents == null)
            return Array.Empty<byte>();

        return PdfEncoders.RawEncoding.GetBytes(contents.Value);
    }

    static DateTime? SigningTimeOf(PdfDictionary value)
    {
        if (!value.Elements.ContainsKey("/M"))
            return null;

        var time = value.Elements.GetDateTime("/M", DateTime.MinValue);
        return time == DateTime.MinValue ? (DateTime?)null : time;
    }

    /// <summary>
    /// The <c>/P</c> of a <c>/DocMDP</c> transform, which is what makes a signature a certifying one.
    /// </summary>
    static int CertificationLevelOf(PdfDictionary value)
    {
        var references = value.Elements.GetArray("/Reference");
        if (references == null)
            return 0;

        for (var index = 0; index < references.Elements.Count; index++)
        {
            var reference = references.Elements.GetDictionary(index);
            if (reference?.Elements.GetName("/TransformMethod") != "/DocMDP")
                continue;

            var parameters = reference.Elements.GetDictionary("/TransformParams");
            if (parameters != null)
                return parameters.Elements.GetInteger("/P");
        }

        return 0;
    }

    static string NullIfEmpty(string value) => String.IsNullOrEmpty(value) ? null : value;
}
