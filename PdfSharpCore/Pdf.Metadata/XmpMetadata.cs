using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PdfSharpCore.Pdf.Metadata;

/// <summary>
/// The XMP metadata packet of a document: the same facts the document information dictionary holds,
/// said again in RDF/XML, plus the conformance identifier that is the only place a document can say
/// which standard it claims to meet.
/// </summary>
/// <remarks>
/// <para>
/// Every fact here also lives in <see cref="PdfDocumentInformation"/>, and a validator compares the
/// two and complains when they disagree. So the packet is built from the information dictionary at
/// save time rather than kept alongside it and hoped about — see <see cref="FromDocument"/>.
/// </para>
/// <para>
/// The packet is deliberately built to be extended. PDF/UA adds an identifier of its own, ZUGFeRD
/// adds a whole extension schema, and a caller may want a namespace nobody has thought of; all three
/// go in through <see cref="AdditionalDescriptions"/> rather than by editing this class.
/// </para>
/// </remarks>
public sealed class XmpMetadata
{
    /// <summary>
    /// The identifier every XMP packet carries. It is a fixed string rather than anything
    /// meaningful — a marker a byte scanner can find without parsing the PDF around it.
    /// </summary>
    private const string PacketId = "W5M0MpCehiHzreSzNTczkc9d";

    /// <summary>The document title. Required by every PDF/A profile, and by PDF/UA.</summary>
    public string Title { get; set; }

    /// <summary>The document author, going out as the single entry of a <c>dc:creator</c> sequence.</summary>
    public string Author { get; set; }

    /// <summary>The document subject, going out as <c>dc:description</c>.</summary>
    public string Subject { get; set; }

    /// <summary>The document keywords, going out as <c>pdf:Keywords</c>.</summary>
    public string Keywords { get; set; }

    /// <summary>The application that produced the content, going out as <c>xmp:CreatorTool</c>.</summary>
    public string CreatorTool { get; set; }

    /// <summary>The library that wrote the file, going out as <c>pdf:Producer</c>.</summary>
    public string Producer { get; set; }

    /// <summary>When the document was created.</summary>
    public DateTime? CreationDate { get; set; }

    /// <summary>When the document was last changed.</summary>
    public DateTime? ModificationDate { get; set; }

    /// <summary>
    /// The archival profile the document claims, which becomes the <c>pdfaid:part</c> and
    /// <c>pdfaid:conformance</c> pair. <see cref="PdfAConformance.None"/> writes neither.
    /// </summary>
    public PdfAConformance Conformance { get; set; }

    /// <summary>
    /// The accessibility profile the document claims, which becomes the <c>pdfuaid:part</c> entry.
    /// <see cref="PdfUAConformance.None"/> writes none.
    /// </summary>
    /// <remarks>
    /// XMP is the only place a PDF/UA claim can be made — unlike PDF/A there is no dictionary entry
    /// for it, so a document with a perfect structure tree and no identifier claims nothing at all.
    /// The two claims are independent and a document may carry both: PDF/A-3 says it will still open
    /// in fifty years, PDF/UA-1 says it can be read aloud, and neither implies the other.
    /// </remarks>
    public PdfUAConformance UAConformance { get; set; }

    /// <summary>
    /// Whole <c>rdf:Description</c> elements to place after the ones built here, for schemas this
    /// class knows nothing about. Each entry is written verbatim, so each is the caller's to get
    /// right — including its own namespace declarations.
    /// </summary>
    public IList<string> AdditionalDescriptions { get; } = new List<string>();

    /// <summary>
    /// Takes the facts from the document's information dictionary, so that the two cannot drift
    /// apart. Anything set on the result afterwards wins.
    /// </summary>
    public static XmpMetadata FromDocument(PdfDocument document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var info = document.Info;
        return new XmpMetadata
        {
            Title = NullIfEmpty(info.Title),
            Author = NullIfEmpty(info.Author),
            Subject = NullIfEmpty(info.Subject),
            Keywords = NullIfEmpty(info.Keywords),
            CreatorTool = NullIfEmpty(info.Creator),
            Producer = NullIfEmpty(info.Producer),
            CreationDate = NullIfDefault(info.CreationDate),
            ModificationDate = NullIfDefault(info.ModificationDate),
        };
    }

    /// <summary>
    /// Builds the packet, ready to become the value of a metadata stream.
    /// </summary>
    /// <remarks>
    /// UTF-8 without a byte order mark on the stream itself: the mark belongs inside the
    /// <c>begin</c> attribute of the processing instruction, where the specification puts it, and a
    /// second one in front of that is a parse error in some readers.
    /// </remarks>
    public byte[] Build()
    {
        var xmp = new StringBuilder();

        // U+FEFF inside the attribute is how a scanner works out the encoding of what follows.
        xmp.Append("<?xpacket begin=\"﻿\" id=\"").Append(PacketId).Append("\"?>\n");
        xmp.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n");
        xmp.Append(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");

        AppendDublinCore(xmp);
        AppendPdf(xmp);
        AppendBasic(xmp);
        AppendConformance(xmp);
        AppendAccessibility(xmp);

        foreach (var description in AdditionalDescriptions)
            xmp.Append("  ").Append(description).Append('\n');

        xmp.Append(" </rdf:RDF>\n");
        xmp.Append("</x:xmpmeta>\n");

        // The run of spaces is padding a writer may overwrite in place to change the metadata
        // without moving anything after it. Nothing here does that yet, and leaving the room costs
        // a few hundred bytes against the day something does.
        for (var line = 0; line < 4; line++)
            xmp.Append(new string(' ', 99)).Append('\n');

        // "w" says the packet may be written over in place; "r" would say it may not.
        xmp.Append("<?xpacket end=\"w\"?>\n");

        return new UTF8Encoding(false).GetBytes(xmp.ToString());
    }

    private void AppendDublinCore(StringBuilder xmp)
    {
        if (Title == null && Author == null && Subject == null)
            return;

        xmp.Append("  <rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n");
        AppendLanguageAlternative(xmp, "dc:title", Title);
        AppendSequence(xmp, "dc:creator", Author);
        AppendLanguageAlternative(xmp, "dc:description", Subject);
        xmp.Append("  </rdf:Description>\n");
    }

    private void AppendPdf(StringBuilder xmp)
    {
        if (Keywords == null && Producer == null)
            return;

        xmp.Append("  <rdf:Description rdf:about=\"\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">\n");
        AppendSimple(xmp, "pdf:Keywords", Keywords);
        AppendSimple(xmp, "pdf:Producer", Producer);
        xmp.Append("  </rdf:Description>\n");
    }

    private void AppendBasic(StringBuilder xmp)
    {
        if (CreatorTool == null && CreationDate == null && ModificationDate == null)
            return;

        xmp.Append("  <rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">\n");
        AppendSimple(xmp, "xmp:CreatorTool", CreatorTool);
        AppendSimple(xmp, "xmp:CreateDate", Iso8601(CreationDate));
        AppendSimple(xmp, "xmp:ModifyDate", Iso8601(ModificationDate));
        xmp.Append("  </rdf:Description>\n");
    }

    private void AppendConformance(StringBuilder xmp)
    {
        if (Conformance == PdfAConformance.None)
            return;

        xmp.Append("  <rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">\n");
        AppendSimple(xmp, "pdfaid:part", PartOf(Conformance));
        AppendSimple(xmp, "pdfaid:conformance", "B");
        xmp.Append("  </rdf:Description>\n");
    }

    private void AppendAccessibility(StringBuilder xmp)
    {
        if (UAConformance == PdfUAConformance.None)
            return;

        xmp.Append("  <rdf:Description rdf:about=\"\" xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\">\n");

        // No conformance letter to go with it. PDF/UA-1 has parts and no levels, where PDF/A has
        // both — writing a pdfuaid:conformance to match the pdfaid one above is a common mistake and
        // a validator objects to it.
        AppendSimple(xmp, "pdfuaid:part", "1");
        xmp.Append("  </rdf:Description>\n");
    }

    /// <summary>
    /// The part number of ISO 19005 a profile belongs to, which is what <c>pdfaid:part</c> holds.
    /// </summary>
    internal static string PartOf(PdfAConformance conformance) => conformance switch
    {
        PdfAConformance.PdfA1B => "1",
        PdfAConformance.PdfA2B => "2",
        PdfAConformance.PdfA3B => "3",
        _ => null,
    };

    private static void AppendSimple(StringBuilder xmp, string element, string value)
    {
        if (value == null)
            return;

        xmp.Append("   <").Append(element).Append('>')
           .Append(Escape(value))
           .Append("</").Append(element).Append(">\n");
    }

    /// <summary>
    /// A title or a description is a set of alternatives in different languages, even when there is
    /// only one of them, and the default one is marked <c>x-default</c> rather than by position.
    /// </summary>
    private static void AppendLanguageAlternative(StringBuilder xmp, string element, string value)
    {
        if (value == null)
            return;

        xmp.Append("   <").Append(element).Append("><rdf:Alt><rdf:li xml:lang=\"x-default\">")
           .Append(Escape(value))
           .Append("</rdf:li></rdf:Alt></").Append(element).Append(">\n");
    }

    /// <summary>
    /// An author is an ordered sequence, because a document may have several and the order is part
    /// of what it says.
    /// </summary>
    private static void AppendSequence(StringBuilder xmp, string element, string value)
    {
        if (value == null)
            return;

        xmp.Append("   <").Append(element).Append("><rdf:Seq><rdf:li>")
           .Append(Escape(value))
           .Append("</rdf:li></rdf:Seq></").Append(element).Append(">\n");
    }

    /// <summary>
    /// The date form XMP wants, which is ISO 8601 with the offset spelled out. A local time written
    /// without one is read as UTC by some tools and as local by others.
    /// </summary>
    private static string Iso8601(DateTime? value) =>
        value?.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    private static string Escape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");

    private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static DateTime? NullIfDefault(DateTime value) => value == default ? null : value;
}
