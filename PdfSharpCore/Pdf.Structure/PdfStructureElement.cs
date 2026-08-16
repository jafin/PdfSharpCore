using PdfSharpCore.Pdf.Advanced;

namespace PdfSharpCore.Pdf.Structure;

/// <summary>
/// One node of the structure tree: a heading, a paragraph, a table cell. It says what a piece of
/// content is, and points at the marks on the page that draw it.
/// </summary>
public sealed class PdfStructureElement : PdfDictionary
{
    internal PdfStructureElement(PdfDocument document, PdfTag tag)
        : base(document)
    {
        Elements.SetName(Keys.Type, "/StructElem");
        Elements.SetName(Keys.S, tag.Name);
        Tag = tag;
    }

    /// <summary>
    /// Gets the structure type of this element.
    /// </summary>
    public PdfTag Tag { get; }

    /// <summary>
    /// Gets or sets the text that stands in for this element for a reader who cannot see it. A
    /// figure without it says nothing at all, which is why tagging an image without alternate text
    /// is worse than leaving it an artifact.
    /// </summary>
    public string AlternateText
    {
        get => Elements.GetString(Keys.Alt);
        set => Elements.SetString(Keys.Alt, value ?? "");
    }

    /// <summary>
    /// Gets or sets the text this element's marks really spell, for when the glyphs and the text
    /// disagree — a ligature, or a word broken across a hyphenation point.
    /// </summary>
    public string ActualText
    {
        get => Elements.GetString(Keys.ActualText);
        set => Elements.SetString(Keys.ActualText, value ?? "");
    }

    /// <summary>
    /// Gets or sets the language of this element, when it differs from the document's.
    /// </summary>
    public string Language
    {
        get => Elements.GetString(Keys.Lang);
        set => Elements.SetString(Keys.Lang, value ?? "");
    }

    /// <summary>
    /// Adds a child element and records this element as its parent. The tree is doubly linked
    /// because a reader walks it in both directions — down to read, up to work out context.
    /// </summary>
    internal void Add(PdfStructureElement child)
    {
        Kids().Elements.Add(child.Reference);
        child.Elements[Keys.P] = Reference;
    }

    /// <summary>
    /// Points this element at a run of marks on a page. The integer is the marked-content
    /// identifier that the <c>BDC</c> in the content stream carries.
    /// </summary>
    internal void AddMarkedContent(PdfPage page, int mcid)
    {
        // /Pg says which page the marks are on. It could be left off when the parent already says
        // so, and is written always because an element whose children span pages is legal and the
        // saving is a few bytes.
        Elements[Keys.Pg] = page.Reference;
        Kids().Elements.Add(new PdfInteger(mcid));
    }

    /// <summary>
    /// Points this element at an annotation, which is content as much as anything drawn is — a link
    /// is unreachable to a screen reader that only walks the marks.
    /// </summary>
    internal void AddObjectReference(PdfPage page, PdfDictionary annotation)
    {
        var objr = new PdfDictionary(Owner);
        objr.Elements.SetName(Keys.Type, "/OBJR");
        objr.Elements[ObjectReferenceKeys.Obj] = annotation.Reference;
        objr.Elements[ObjectReferenceKeys.Pg] = page.Reference;
        Owner._irefTable.Add(objr);

        Kids().Elements.Add(objr.Reference);
    }

    PdfArray Kids()
    {
        if (Elements[Keys.K] is PdfArray kids)
            return kids;

        var array = new PdfArray(Owner);
        Elements[Keys.K] = array;
        return array;
    }

    /// <summary>
    /// The entries of a structure element dictionary.
    /// </summary>
    public sealed class Keys : KeysBase
    {
        /// <summary>(Required) Must be StructElem.</summary>
        [KeyInfo(KeyType.Name | KeyType.Required, FixedValue = "StructElem")]
        public const string Type = "/Type";

        /// <summary>(Required) The structure type — what this element is.</summary>
        [KeyInfo(KeyType.Name | KeyType.Required)]
        public const string S = "/S";

        /// <summary>(Required) The parent of this element in the structure tree.</summary>
        [KeyInfo(KeyType.Dictionary | KeyType.Required)]
        public const string P = "/P";

        /// <summary>(Optional) The children: elements, marked-content identifiers, object references.</summary>
        [KeyInfo(KeyType.Various | KeyType.Optional)]
        public const string K = "/K";

        /// <summary>(Optional) The page the marked content of this element is on.</summary>
        [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
        public const string Pg = "/Pg";

        /// <summary>(Optional) Alternate text, for a reader who cannot see the content.</summary>
        [KeyInfo(KeyType.TextString | KeyType.Optional)]
        public const string Alt = "/Alt";

        /// <summary>(Optional) What the marks really spell, when the glyphs disagree with the text.</summary>
        [KeyInfo(KeyType.TextString | KeyType.Optional)]
        public const string ActualText = "/ActualText";

        /// <summary>(Optional) The language of this element.</summary>
        [KeyInfo(KeyType.TextString | KeyType.Optional)]
        public const string Lang = "/Lang";

        /// <summary>Gets the KeysMeta for these keys.</summary>
        internal static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

        static DictionaryMeta _meta;
    }

    /// <summary>
    /// The entries of an object reference, which is how an annotation joins the structure tree.
    /// </summary>
    static class ObjectReferenceKeys
    {
        public const string Obj = "/Obj";
        public const string Pg = "/Pg";
    }

    internal override DictionaryMeta Meta => Keys.Meta;
}
