using System.Collections.Generic;
using PdfSharpCore.Pdf.Advanced;

namespace PdfSharpCore.Pdf.Structure;

/// <summary>
/// The root of the structure tree, hanging off the catalog and holding the two indexes a reader
/// needs: the role map, which explains any structure type it does not already know, and the parent
/// tree, which takes it from a mark on a page back to what that mark means.
/// </summary>
public sealed class PdfStructureTreeRoot : PdfDictionary
{
    internal PdfStructureTreeRoot(PdfDocument document)
        : base(document)
    {
        Elements.SetName(Keys.Type, "/StructTreeRoot");
        document._irefTable.Add(this);

        ParentTree = new PdfNumberTreeNode(document);
        document._irefTable.Add(ParentTree);
        Elements[Keys.ParentTree] = ParentTree.Reference;
    }

    /// <summary>
    /// Maps a structure type of the document's own invention onto one a reader understands. A type
    /// that is not standard and not in here means nothing to anybody.
    /// </summary>
    public IDictionary<string, string> RoleMap { get; } = new Dictionary<string, string>();

    /// <summary>
    /// The index from a mark on a page back to the element that gives it meaning. Keyed by the
    /// page's <c>/StructParents</c>, valued by an array holding one element per marked-content
    /// identifier on that page.
    /// </summary>
    internal PdfNumberTreeNode ParentTree { get; }

    internal void Add(PdfStructureElement child)
    {
        Kids().Elements.Add(child.Reference);
        child.Elements[PdfStructureElement.Keys.P] = Reference;
    }

    /// <summary>
    /// Writes out the role map, if there is one. Left to save time because entries may be added at
    /// any point before then.
    /// </summary>
    internal void PrepareForSave()
    {
        if (RoleMap.Count == 0)
            return;

        var map = new PdfDictionary(Owner);
        foreach (var pair in RoleMap)
            map.Elements.SetName(Name(pair.Key), Name(pair.Value));

        Elements[Keys.RoleMap] = map;
    }

    static string Name(string value) => value.Length > 0 && value[0] == '/' ? value : "/" + value;

    PdfArray Kids()
    {
        if (Elements[Keys.K] is PdfArray kids)
            return kids;

        var array = new PdfArray(Owner);
        Elements[Keys.K] = array;
        return array;
    }

    /// <summary>
    /// The entries of a structure tree root dictionary.
    /// </summary>
    public sealed class Keys : KeysBase
    {
        /// <summary>(Required) Must be StructTreeRoot.</summary>
        [KeyInfo(KeyType.Name | KeyType.Required, FixedValue = "StructTreeRoot")]
        public const string Type = "/Type";

        /// <summary>(Optional) The top-level structure elements.</summary>
        [KeyInfo(KeyType.Various | KeyType.Optional)]
        public const string K = "/K";

        /// <summary>(Required if any content is marked) Mark back to structure element.</summary>
        [KeyInfo(KeyType.NumberTree | KeyType.Optional)]
        public const string ParentTree = "/ParentTree";

        /// <summary>(Optional) The next free key of the parent tree.</summary>
        [KeyInfo(KeyType.Integer | KeyType.Optional)]
        public const string ParentTreeNextKey = "/ParentTreeNextKey";

        /// <summary>(Optional) Non-standard structure types explained in terms of standard ones.</summary>
        [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
        public const string RoleMap = "/RoleMap";

        /// <summary>Gets the KeysMeta for these keys.</summary>
        internal static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

        static DictionaryMeta _meta;
    }

    internal override DictionaryMeta Meta => Keys.Meta;
}
