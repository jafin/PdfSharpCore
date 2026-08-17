using System;
using System.Collections.Generic;
using MigraDocCore.DocumentObjectModel;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Structure;

namespace MigraDocCore.Rendering;

/// <summary>
/// Turns what MigraDoc already knows about a document into a PDF structure tree, so that a caller
/// gets an accessible file without tagging anything by hand.
/// </summary>
/// <remarks>
/// <para>
/// The renderer is rendering a <c>Paragraph</c> whose style is <c>Heading1</c>, and throws that away
/// on the way to the page: a heading and a caption both arrive as a <c>Tj</c>. This keeps it. Every
/// renderer that draws something asks here for the element it belongs to and wraps its drawing in
/// the scope that comes back; everything that is furniture rather than content — running heads,
/// folios, cell shading, borders, rules — goes inside <see cref="Artifact"/> instead.
/// </para>
/// <para>
/// <b>An element belongs to a document object, not to a page.</b> That is the whole reason this is a
/// class with a dictionary in it rather than a few calls scattered through the renderers. A paragraph
/// broken over a page boundary is one paragraph, and a table heading repeated at the top of every
/// page the table runs onto is one heading. Keyed by the DOM object and reused, they come out that
/// way; created per render pass they would come out as two paragraphs and five headings, and a reader
/// would announce them as such.
/// </para>
/// <para>
/// <b>Structural nesting is not drawing nesting.</b> A table is rows and cells, and it is drawn as
/// shading, then content, then borders, cell by cell through a flat list. So the tree is built by
/// naming a parent rather than by nesting <c>using</c> blocks, and the parent that a renderer drawing
/// something finds is whatever the renderer that called it made current.
/// </para>
/// </remarks>
internal sealed class StructureTagger
{
    /// <summary>
    /// A scope that closes nothing, handed back whenever tagging is off or the graphics being drawn
    /// into is not a page. Every call site is a <c>using</c> either way, so that the decision lives
    /// here and not at forty call sites.
    /// </summary>
    internal static readonly IDisposable Nothing = new NullScope();

    readonly Dictionary<ElementKey, PdfStructureElement> _elements =
        new Dictionary<ElementKey, PdfStructureElement>();

    readonly Stack<PdfStructureElement> _parents = new Stack<PdfStructureElement>();

    PdfDocument _document;
    PdfStructureElement _root;

    // The run of consecutive list paragraphs currently being gathered into one /L, and what makes a
    // paragraph part of it rather than the start of a new one.
    PdfStructureElement _listRun;
    PdfStructureElement _listRunParent;
    ListType _listRunType;

    /// <summary>
    /// Whether to tag at all. Set from <see cref="DocumentRenderer.TagContent"/>.
    /// </summary>
    internal bool Enabled { get; set; } = true;

    /// <summary>
    /// The language the document is written in, as an RFC 3066 tag. A reader that does not know it
    /// cannot choose a voice, so PDF/UA requires it.
    /// </summary>
    internal string Language { get; set; }

    /// <summary>
    /// The element that anything tagged now becomes a child of.
    /// </summary>
    internal PdfStructureElement Current => _parents.Count > 0 ? _parents.Peek() : _root;

    /// <summary>
    /// Whether the graphics being drawn into can carry marks — which it cannot when tagging is off,
    /// nor when it is a measuring context or a form rather than a page.
    /// </summary>
    bool CanTag(XGraphics gfx) => Enabled && gfx != null && gfx.PdfPage != null;

    /// <summary>
    /// Whether content may be tagged here, which it may not once anything has declared itself
    /// furniture.
    /// </summary>
    /// <remarks>
    /// <b>An artifact is not a container that its contents can opt out of.</b> A running head is
    /// drawn by the same paragraph renderer as body text is, and that renderer tags what it draws —
    /// so without this the header's paragraphs appear in the tree as paragraphs, inside an
    /// <c>/Artifact</c> sequence saying they are not content. A reader following the tree reads the
    /// running head aloud on every page, which is the exact failure marking it as an artifact was
    /// meant to prevent.
    /// </remarks>
    bool CanTagContent(XGraphics gfx) => CanTag(gfx) && _artifactDepth == 0;

    int _artifactDepth;

    /// <summary>
    /// Brings a page into the tree whether or not anything on it is tagged, and settles the
    /// document-level entries the first time a page is seen.
    /// </summary>
    /// <remarks>
    /// A page that draws nothing still has to be in the tree, or it cannot be told apart from one
    /// imported out of an untagged document — and a validator is right to object to both.
    /// </remarks>
    internal void BeginPage(XGraphics gfx)
    {
        if (!CanTag(gfx))
            return;

        var page = gfx.PdfPage;
        if (_document == null)
        {
            _document = page.Owner;

            if (!string.IsNullOrEmpty(Language))
                _document.Structure.Language = Language;

            _root = _document.Structure.CreateElement(PdfTag.Document);
        }
        else if (!ReferenceEquals(_document, page.Owner))
        {
            // One tagger belongs to one document. Two would share a cache of elements built against
            // the first document's object table, and hanging those off the second produces a tree
            // full of references to objects that are not in the file.
            throw new InvalidOperationException(
                "This renderer has already tagged pages of a different PdfDocument. A DocumentRenderer "
                + "renders one document; use a second one, or set TagContent to false.");
        }

        _document.Structure.RegisterPage(page);
    }

    /// <summary>
    /// Marks everything drawn in the scope as belonging to a new element of the given type, a child
    /// of whatever is current, and makes it current in turn.
    /// </summary>
    /// <param name="gfx">The surface being drawn on, which the marks are written into.</param>
    /// <param name="key">
    /// The DOM object the element stands for. Asked for twice — because the object was drawn on two
    /// pages, or in a table heading repeated on each — the same element comes back and gains a second
    /// run of marks, which is what makes it one thing in the tree rather than two.
    /// </param>
    /// <param name="tag">What kind of element it is.</param>
    internal IDisposable Block(XGraphics gfx, object key, PdfTag tag)
    {
        if (!CanTagContent(gfx))
            return Nothing;

        return Marks(gfx, Element(key, tag, ParentFor(key)));
    }

    /// <summary>
    /// Makes a new element of the given type current for the scope, without giving it any marks of
    /// its own.
    /// </summary>
    /// <remarks>
    /// For an element that holds other elements and draws nothing itself — a table, a row, a list.
    /// Giving one an identifier of its own would claim that something on the page belongs directly to
    /// the table rather than to one of its cells, which is a claim about reading order and a false
    /// one.
    /// </remarks>
    internal IDisposable Container(XGraphics gfx, object key, PdfTag tag)
    {
        if (!CanTagContent(gfx))
            return Nothing;

        return Enter(Element(key, tag, ParentFor(key)));
    }

    /// <summary>
    /// Marks everything drawn in the scope as belonging to an element built earlier, and makes it
    /// current.
    /// </summary>
    internal IDisposable Marks(XGraphics gfx, PdfStructureElement element)
    {
        if (!CanTagContent(gfx) || element == null)
            return Nothing;

        var scope = gfx.BeginMarkedContent(element);
        _parents.Push(element);
        return new Scope(this, scope, popsParent: true);
    }

    /// <summary>
    /// Makes an element current for the scope without opening a marked-content sequence.
    /// </summary>
    internal IDisposable Enter(PdfStructureElement element)
    {
        if (element == null)
            return Nothing;

        _parents.Push(element);
        return new Scope(this, null, popsParent: true);
    }

    /// <summary>
    /// Marks everything drawn in the scope as furniture: on the page, and not part of what the page
    /// says.
    /// </summary>
    /// <remarks>
    /// Half the rule rather than a tidiness measure. Everything on a page is either content or an
    /// artifact, and something that is neither is the first thing a validator objects to — so the
    /// running head, the folio, the shading behind a table and every border go through here.
    /// </remarks>
    internal IDisposable Artifact(XGraphics gfx)
    {
        if (!CanTag(gfx))
            return Nothing;

        // A second sequence inside the first would say nothing the first has not said already, and
        // the depth is what the content scopes read to know they are inside furniture. So the inner
        // one counts and writes nothing.
        var marks = _artifactDepth == 0 ? gfx.BeginArtifact() : null;
        _artifactDepth++;
        return new Scope(this, marks, popsParent: false, leavesArtifact: true);
    }

    /// <summary>
    /// The element for a document object, built the first time it is asked for and handed back
    /// unchanged afterwards.
    /// </summary>
    /// <param name="key">The DOM object the element stands for.</param>
    /// <param name="tag">What kind of element it is.</param>
    /// <param name="parent">The element it hangs beneath, the first time it is built.</param>
    /// <param name="slot">
    /// Which of the object's elements is wanted, for the few objects that need more than one. A list
    /// paragraph is one object holding a label and a body, and the two have to be told apart by
    /// something that outlives the renderer drawing them — a renderer is built afresh for each page a
    /// split paragraph appears on, so anything belonging to the renderer would file the second half
    /// of the paragraph under a new element and read it out as a second paragraph.
    /// </param>
    internal PdfStructureElement Element(object key, PdfTag tag, PdfStructureElement parent, int slot = 0)
    {
        // Nothing to build against until a page has been begun, which is also to say: whenever
        // tagging is off — and nothing to build at all inside furniture. Every caller treats a null
        // element as "do not tag this", so both checks live here rather than at each of them.
        if (_document == null || _artifactDepth > 0)
            return null;

        if (key == null)
            return _document.Structure.CreateElement(tag, parent ?? _root);

        var identity = new ElementKey(key, slot);
        if (_elements.TryGetValue(identity, out var existing))
            return existing;

        var element = _document.Structure.CreateElement(tag, parent ?? _root);
        _elements[identity] = element;
        return element;
    }

    /// <summary>
    /// Which element of which document object. Compared by identity rather than by value, because
    /// several DOM types override <c>Equals</c> to compare by value and two empty paragraphs of the
    /// same style are equal without being the same paragraph.
    /// </summary>
    readonly struct ElementKey : IEquatable<ElementKey>
    {
        readonly object _owner;
        readonly int _slot;

        internal ElementKey(object owner, int slot)
        {
            _owner = owner;
            _slot = slot;
        }

        public bool Equals(ElementKey other) =>
            ReferenceEquals(_owner, other._owner) && _slot == other._slot;

        public override bool Equals(object obj) => obj is ElementKey other && Equals(other);

        public override int GetHashCode() =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_owner) * 397 ^ _slot;
    }

    /// <summary>
    /// The list item a list paragraph belongs to, gathering consecutive paragraphs of the same kind
    /// into one list.
    /// </summary>
    /// <remarks>
    /// MigraDoc has no list object — a list is however many paragraphs in a row happen to carry a
    /// <c>ListInfo</c>, and the run is only visible from the order they are rendered in. So the run
    /// is tracked here and broken by <see cref="EndList"/> the moment anything else is tagged at the
    /// same level. That reads a change of list type as a new list, which matches what the page looks
    /// like; it cannot see a nested list as nested, because nothing in the DOM says so.
    /// </remarks>
    internal PdfStructureElement ListItem(XGraphics gfx, Paragraph paragraph, ListType type)
    {
        if (!CanTagContent(gfx))
            return null;

        var parent = ParentFor(paragraph);

        if (_listRun == null || _listRunType != type || !ReferenceEquals(_listRunParent, parent))
        {
            _listRun = _document.Structure.CreateElement(PdfTag.L, parent);
            _listRunParent = parent;
            _listRunType = type;
        }

        return Element(paragraph, PdfTag.LI, _listRun);
    }

    /// <summary>
    /// Ends the run of list paragraphs, so that the next one starts a new list. Called by every
    /// renderer that tags something which is not a list item.
    /// </summary>
    internal void EndList()
    {
        _listRun = null;
        _listRunParent = null;
    }

    /// <summary>
    /// Joins a link annotation to its element, so that the link is reachable by a reader walking the
    /// structure and not only by one hit-testing rectangles.
    /// </summary>
    internal void AddAnnotation(XGraphics gfx, PdfStructureElement element, PdfDictionary annotation)
    {
        if (!CanTagContent(gfx) || element == null || annotation == null)
            return;

        _document.Structure.AddAnnotation(element, gfx.PdfPage, annotation);
    }

    /// <summary>
    /// What a new element for this object hangs off: whatever renderer is drawing it, or — for
    /// anything at the top of the flow — the section it came from.
    /// </summary>
    PdfStructureElement ParentFor(object key)
    {
        if (_parents.Count > 0)
            return _parents.Peek();

        var section = SectionOf(key as DocumentObject);
        return section != null ? Element(section, PdfTag.Section, _root) : _root;
    }

    /// <summary>
    /// The section a document object came out of, found by walking up the DOM.
    /// </summary>
    /// <remarks>
    /// Only meaningful for something at the top of the flow. A paragraph inside a table cell would
    /// answer with the section the table is in, which is true and useless — but that paragraph never
    /// reaches here, because the cell it is in is already current.
    /// </remarks>
    static Section SectionOf(DocumentObject documentObject)
    {
        var current = documentObject;
        while (current != null)
        {
            if (current is Section section)
                return section;

            current = DocumentRelations.GetParent(current);
        }

        return null;
    }

    /// <summary>
    /// Closes a marked-content sequence and gives back the parent it displaced.
    /// </summary>
    sealed class Scope : IDisposable
    {
        readonly StructureTagger _tagger;
        readonly IDisposable _marks;
        readonly bool _popsParent;
        readonly bool _leavesArtifact;
        bool _closed;

        internal Scope(StructureTagger tagger, IDisposable marks, bool popsParent,
            bool leavesArtifact = false)
        {
            _tagger = tagger;
            _marks = marks;
            _popsParent = popsParent;
            _leavesArtifact = leavesArtifact;
        }

        public void Dispose()
        {
            if (_closed)
                return;

            _closed = true;

            // The marks first and the rest second, mirroring the order they were taken in. An
            // unbalanced BDC corrupts every mark after it on the page, so this runs from a using
            // rather than from a matched pair of calls that an early return could step over.
            _marks?.Dispose();

            if (_popsParent)
                _tagger._parents.Pop();

            if (_leavesArtifact)
                _tagger._artifactDepth--;
        }
    }

    sealed class NullScope : IDisposable
    {
        public void Dispose() { }
    }
}
