using System;
using System.Collections.Generic;
using System.Linq;

namespace PdfSharpCore.Pdf.Advanced;

/// <summary>
/// The named destinations of a document - places in it that can be linked to by name rather than
/// by page number.
/// </summary>
/// <remarks>
/// A name outlives the page it stands for. A link to page 7 means something different once a page
/// is inserted in front of it; a link to "chapter-3" does not, which is why a table of contents,
/// an outline built elsewhere, or a URL ending <c>#nameddest=chapter-3</c> all want one of these.
/// <para>
/// Written into the catalog as the <c>/Names /Dests</c> name tree of PDF 32000-1 section 7.7.4,
/// as one leaf node holding every name. A tree of one node is a tree, and nothing here is large
/// enough for the balancing to earn its keep - a reader finds a name in it either way.
/// </para>
/// <para>
/// <see cref="PdfNamedDestinations"/> is the other half of this and reads rather than writes: it
/// resolves a name when a page carrying one is imported from another document.
/// </para>
/// </remarks>
public sealed class PdfNamedDestinationTable
{
    internal PdfNamedDestinationTable(PdfDocument document)
    {
        _document = document;
    }
    readonly PdfDocument _document;

    readonly Dictionary<string, Destination> _destinations = new Dictionary<string, Destination>(StringComparer.Ordinal);

    readonly struct Destination
    {
        internal Destination(PdfPage page, double top)
        {
            Page = page;
            Top = top;
        }

        internal PdfPage Page { get; }

        /// <summary>How far up the page to put the top of the window; NaN leaves it alone.</summary>
        internal double Top { get; }
    }

    /// <summary>
    /// Gets how many destinations have been named.
    /// </summary>
    public int Count => _destinations.Count;

    /// <summary>
    /// Gets the names, in the order a reader will find them written - by their bytes, which is the
    /// order a name tree has to be in.
    /// </summary>
    public IEnumerable<string> Names => _destinations.Keys.OrderBy(name => name, StringComparer.Ordinal);

    /// <summary>
    /// Names the top of a page.
    /// </summary>
    public void Add(string name, PdfPage page)
    {
        Add(name, page, double.NaN);
    }

    /// <summary>
    /// Names a place on a page.
    /// </summary>
    /// <param name="name">The name to link to. Naming the same place twice replaces the first.</param>
    /// <param name="page">The page the name stands for.</param>
    /// <param name="top">
    /// How far up the page to put the top of the window, in default page coordinates - measured
    /// from the bottom of the page, as PDF measures. NaN lands the reader wherever the page is
    /// already scrolled to.
    /// </param>
    public void Add(string name, PdfPage page, double top)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("A destination must be named something.", nameof(name));
        if (page == null)
            throw new ArgumentNullException(nameof(page));

        _destinations[name] = new Destination(page, top);
    }

    /// <summary>
    /// Returns true if this table has been asked to name this destination.
    /// </summary>
    /// <remarks>
    /// About what is going to be written, not about what the document holds - a document opened
    /// from a file comes with names this table was never told about. <see cref="Resolve"/> is the
    /// one that answers for those.
    /// </remarks>
    public bool Contains(string name)
    {
        return name != null && _destinations.ContainsKey(name);
    }

    /// <summary>
    /// The destination array the document holds under this name, or null if it holds none.
    /// </summary>
    /// <remarks>
    /// Reads what is written in the document rather than what this table is about to write, so it
    /// finds the names a document opened from a file came with. The array belongs to the document
    /// it was found in, so a caller putting it into another one has to copy it.
    /// </remarks>
    public PdfArray Resolve(string name)
    {
        return name == null ? null : PdfNamedDestinations.Lookup(_document, new PdfString(name));
    }

    /// <summary>
    /// Removes a name, and returns true if there was one to remove.
    /// </summary>
    public bool Remove(string name)
    {
        return name != null && _destinations.Remove(name);
    }

    /// <summary>
    /// Writes the names into the catalog. Called while the document is being saved, when every
    /// page has an object number for a destination to point at.
    /// </summary>
    internal void PrepareForSave()
    {
        if (_destinations.Count == 0)
            return;

        PdfCatalog catalog = _document.Catalog;

        // Whatever else the document already names - an imported /Names holds file attachments and
        // JavaScript in the same dictionary - is left where it is.
        PdfDictionary names = catalog.Elements.GetDictionary(PdfCatalog.Keys.Names);
        if (names == null)
        {
            names = new PdfDictionary(_document);
            catalog.Elements[PdfCatalog.Keys.Names] = names;
        }

        PdfArray leaves = new PdfArray(_document);
        foreach (string name in Names)
        {
            Destination destination = _destinations[name];
            leaves.Elements.Add(new PdfString(name));
            leaves.Elements.Add(DestinationArrayFor(destination));
        }

        PdfDictionary dests = new PdfDictionary(_document);
        dests.Elements[PdfCatalog.Keys.Names] = leaves;
        names.Elements[PdfCatalog.Keys.Dests] = dests;
    }

    /// <summary>
    /// The destination array a name stands for: a page, and where on it to look.
    /// </summary>
    PdfArray DestinationArrayFor(Destination destination)
    {
        PdfArray array = new PdfArray(_document);
        array.Elements.Add(destination.Page.Reference);
        array.Elements.Add(new PdfName("/XYZ"));

        // Left, top, zoom. Null leaves the reader's own choice alone, and a zoom of zero means
        // the same - the three together are what /XYZ takes.
        array.Elements.Add(PdfNull.Value);
        array.Elements.Add(double.IsNaN(destination.Top) ? (PdfItem)PdfNull.Value : new PdfReal(destination.Top));
        array.Elements.Add(new PdfInteger(0));
        return array;
    }
}
