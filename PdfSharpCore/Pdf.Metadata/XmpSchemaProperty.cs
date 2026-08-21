using System;

namespace PdfSharpCore.Pdf.Metadata;

/// <summary>
/// One property of an <see cref="XmpExtensionSchema"/>: what it is called, what it means, where its
/// value came from, and what this document says for it.
/// </summary>
/// <remarks>
/// The name and the value travel together deliberately. A schema is declared and written from the
/// same list of these, so a property that is declared but never written, or written but never
/// declared, cannot happen — there is only the one list to get wrong.
/// </remarks>
public sealed class XmpSchemaProperty
{
    /// <param name="name">
    /// The property's name. Becomes part of an XML element name in the packet — <c>fx:DocumentType</c>
    /// for a property named <c>DocumentType</c> in a schema prefixed <c>fx</c> — so it has to be one a
    /// reader can use as one; this is not checked the way a prefix is, because every caller so far has
    /// passed a literal it wrote itself.
    /// </param>
    /// <param name="description">The human-readable description a validator shows for the property.</param>
    /// <param name="category">
    /// Whether the value is derived from the document's own content or came from outside it.
    /// </param>
    /// <param name="value">
    /// The value this document says for the property, written as XMP's <c>Text</c> value type — the
    /// only one this shape delivers today, though it does not preclude another arriving later.
    /// </param>
    public XmpSchemaProperty(string name, string description, XmpPropertyCategory category, string value)
    {
        Name = Require(name, nameof(name));
        Description = Require(description, nameof(description));
        Category = category;
        Value = Require(value, nameof(value));
    }

    /// <summary>The property's name.</summary>
    public string Name { get; }

    /// <summary>The human-readable description a validator shows for the property.</summary>
    public string Description { get; }

    /// <summary>Whether the value came from the document's own content or from outside it.</summary>
    public XmpPropertyCategory Category { get; }

    /// <summary>The value this document says for the property.</summary>
    public string Value { get; }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException(
                parameterName + " has to say something: it goes into the metadata, and a property "
                + "described by an empty string is described by nothing.");

        return value;
    }
}
