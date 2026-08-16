using System.Collections.Generic;
using PdfSharpCore.Pdf.Advanced;

namespace PdfSharpCore.Pdf.Structure;

/// <summary>
/// Builds a document's structure tree as it is drawn, and joins it up at save time.
/// </summary>
/// <remarks>
/// <para>
/// Three things have to line up for a tagged PDF to work, and this owns all three so that they
/// cannot disagree: the marked-content identifiers written into a page's content stream, the
/// structure elements that give them meaning, and the parent tree that takes a reader from one back
/// to the other.
/// </para>
/// <para>
/// Reached through <see cref="PdfDocument.Structure"/>. Asking for it is what makes a document
/// tagged; a document that never does is written exactly as it was before.
/// </para>
/// </remarks>
public sealed class PdfStructureBuilder
{
    readonly PdfDocument _document;
    readonly Dictionary<PdfPage, PageMarks> _pages = new();
    readonly List<KeyValuePair<int, PdfStructureElement>> _annotations = new();

    /// <summary>
    /// The next key to hand out in the parent tree.
    /// </summary>
    /// <remarks>
    /// One counter for pages and annotations both, because they share the tree and their keys have
    /// to be distinct: a page's key resolves to the array of elements its marks belong to, while an
    /// annotation's resolves straight to its own element. Handing an annotation the page's key —
    /// which is what deriving keys from the page count did — points a reader at an array where it
    /// expects an element, so the annotation's place in the structure cannot be found at all.
    /// </remarks>
    int _nextParentKey;

    internal PdfStructureBuilder(PdfDocument document)
    {
        _document = document;
        Root = new PdfStructureTreeRoot(document);
    }

    /// <summary>
    /// Gets the root of the structure tree.
    /// </summary>
    public PdfStructureTreeRoot Root { get; }

    /// <summary>
    /// Gets or sets the language of the document as a whole, as an RFC 3066 tag such as "en-GB".
    /// PDF/UA requires it, and a reader that does not know the language cannot choose a voice.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Creates an element of the given type under the given parent, or under the root when there is
    /// no parent.
    /// </summary>
    public PdfStructureElement CreateElement(PdfTag tag, PdfStructureElement parent = null)
    {
        var element = new PdfStructureElement(_document, tag);
        _document._irefTable.Add(element);

        if (parent == null)
            Root.Add(element);
        else
            parent.Add(element);

        return element;
    }

    /// <summary>
    /// Allocates the next marked-content identifier for a page and records which element it belongs
    /// to. The identifier is an index into that page's run of the parent tree, so it counts from
    /// zero on every page rather than across the document.
    /// </summary>
    internal int AddMarkedContent(PdfPage page, PdfStructureElement element)
    {
        var marks = MarksOf(page);
        var mcid = marks.Elements.Count;
        marks.Elements.Add(element);
        element.AddMarkedContent(page, mcid);
        return mcid;
    }

    /// <summary>
    /// Joins an annotation to an element, so that a link is reachable by a reader walking the
    /// structure rather than only by one hit-testing rectangles.
    /// </summary>
    public void AddAnnotation(PdfStructureElement element, PdfPage page, PdfDictionary annotation)
    {
        element.AddObjectReference(page, annotation);

        // An annotation is indexed by the parent tree through its own /StructParent — a key of its
        // own, resolving to a single element rather than to the array a page's key resolves to.
        var key = _nextParentKey++;
        annotation.Elements.SetInteger("/StructParent", key);
        _annotations.Add(new KeyValuePair<int, PdfStructureElement>(key, element));
    }

    PageMarks MarksOf(PdfPage page)
    {
        if (_pages.TryGetValue(page, out var marks))
            return marks;

        marks = new PageMarks(_nextParentKey++);
        _pages[page] = marks;
        page.Elements.SetInteger("/StructParents", marks.StructParents);
        return marks;
    }

    /// <summary>
    /// Hangs the tree off the catalog and fills in the parent tree. Called while the document is
    /// preparing to be saved, because elements may be added until then.
    /// </summary>
    internal void PrepareForSave()
    {
        foreach (var pair in _pages)
        {
            var elements = new PdfArray(_document);
            foreach (var element in pair.Value.Elements)
                elements.Elements.Add(element.Reference);

            _document._irefTable.Add(elements);
            Root.ParentTree.SetValue(pair.Value.StructParents, elements.Reference);
        }

        foreach (var pair in _annotations)
            Root.ParentTree.SetValue(pair.Key, pair.Value.Reference);

        // Greater than every key handed out, pages and annotations alike, because it is what a later
        // revision adding to the tree starts counting from. Derived from the page count it could
        // name a key that is already in use.
        Root.Elements.SetInteger(PdfStructureTreeRoot.Keys.ParentTreeNextKey, _nextParentKey);
        Root.PrepareForSave();

        var catalog = _document.Catalog;
        catalog.Elements[PdfCatalog.Keys.StructTreeRoot] = Root.Reference;

        // Saying the document is tagged is a claim about the whole of it, and it is what makes a
        // reader look for the tree at all.
        var markInfo = new PdfDictionary(_document);
        markInfo.Elements.SetBoolean("/Marked", true);
        catalog.Elements[PdfCatalog.Keys.MarkInfo] = markInfo;

        if (!string.IsNullOrEmpty(Language))
            catalog.Elements.SetString(PdfCatalog.Keys.Lang, Language);
    }

    /// <summary>
    /// What one page contributes to the parent tree: the key it is filed under, and the elements
    /// its marks belong to, in identifier order.
    /// </summary>
    sealed class PageMarks
    {
        public PageMarks(int structParents) => StructParents = structParents;

        public int StructParents { get; }

        public List<PdfStructureElement> Elements { get; } = new();
    }
}
