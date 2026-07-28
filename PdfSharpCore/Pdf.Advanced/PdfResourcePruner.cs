using System;
using System.Collections.Generic;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;

namespace PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// Works out which entries of a page's resource dictionary the page draws with, by reading the
    /// content of the page and of everything the content draws.
    /// <para>
    /// A page carries the resources it was given, which is not the same as the resources it uses:
    /// pages of a document commonly share one dictionary naming every font and image in it. Copying
    /// such a page copies them all, which is what made a document split into one file per page give
    /// every file the weight of the whole document.
    /// </para>
    /// <para>
    /// A page that comes out heavier than it needs to be is a nuisance; a page that comes out
    /// missing a font is broken. So anything about the content that is not understood in full
    /// leaves the page exactly as it was.
    /// </para>
    /// </summary>
    internal sealed class PdfResourcePruner
    {
        /// <summary>The categories of a resource dictionary whose entries content names.</summary>
        static readonly string[] Categories =
        {
            "/XObject", "/Font", "/ExtGState", "/Shading", "/Pattern", "/ColorSpace", "/Properties"
        };

        /// <summary>
        /// The colour spaces that are always available and never named by a resource dictionary.
        /// </summary>
        static readonly string[] DeviceColorSpaces =
        {
            "/DeviceGray", "/DeviceRGB", "/DeviceCMYK", "/Pattern"
        };

        /// <summary>
        /// How deep forms may be drawn within one another before the page is left alone. Well past
        /// anything a real document does, and there to stop a malformed one running away.
        /// </summary>
        const int MaximumDepth = 32;

        /// <summary>
        /// Drops the entries of the page's resource dictionary that the page does not draw with.
        /// </summary>
        internal static void Prune(PdfPage page)
        {
            if (page == null)
                throw new ArgumentNullException("page");

            PdfDictionary resources = page.Elements.GetDictionary(PdfPage.Keys.Resources);
            if (resources == null)
                return;

            PdfResourcePruner pruner = new PdfResourcePruner(resources);
            pruner.ReadPage(page);
            if (!pruner._understood)
                return;

            pruner.Rewrite(page, resources);
        }

        PdfResourcePruner(PdfDictionary pageResources)
        {
            _pageResources = pageResources;
        }

        /// <summary>The dictionary being pruned. Names are only collected while it is in scope.</summary>
        readonly PdfDictionary _pageResources;

        /// <summary>The names the page draws with, by category.</summary>
        readonly Dictionary<string, Dictionary<string, object>> _used =
            new Dictionary<string, Dictionary<string, object>>();

        /// <summary>The streams already read, so that a form drawing itself does not go round forever.</summary>
        readonly Dictionary<string, object> _read = new Dictionary<string, object>();

        /// <summary>Whether everything the page draws was understood. Nothing is dropped once it is not.</summary>
        bool _understood = true;

        #region Reading

        void ReadPage(PdfPage page)
        {
            byte[] content;
            if (!TryGetPageContent(page, out content))
            {
                _understood = false;
                return;
            }

            Read(content, _pageResources, 0);

            // The appearance of an annotation is a form, and one without resources of its own falls
            // back on those of the page.
            ReadAppearances(page);
        }

        void Read(byte[] content, PdfDictionary scope, int depth)
        {
            if (!_understood)
                return;

            if (depth > MaximumDepth)
            {
                _understood = false;
                return;
            }

            CSequence sequence;
            try
            {
                sequence = ContentReader.ReadContent(content);
            }
            catch (Exception)
            {
                // Content that cannot be read cannot be told what it draws with.
                _understood = false;
                return;
            }

            ReadSequence(sequence, scope, depth);
        }

        void ReadSequence(CSequence sequence, PdfDictionary scope, int depth)
        {
            foreach (CObject item in sequence)
            {
                COperator op = item as COperator;
                if (op == null)
                    continue;

                switch (op.OpCode.OpCodeName)
                {
                    case OpCodeName.BI:
                        // The lexer finds the end of an inline image by looking for the bytes of EI,
                        // which the image data itself may hold. A wrong guess carries the reading off
                        // and the operators after it are lost, so the page is left alone.
                        _understood = false;
                        return;

                    case OpCodeName.Do:
                        Use("/XObject", NameAt(op, 0), scope, depth);
                        break;

                    case OpCodeName.Tf:
                        Use("/Font", NameAt(op, 0), scope, depth);
                        break;

                    case OpCodeName.gs:
                        Use("/ExtGState", NameAt(op, 0), scope, depth);
                        break;

                    case OpCodeName.sh:
                        Use("/Shading", NameAt(op, 0), scope, depth);
                        break;

                    case OpCodeName.cs:
                    case OpCodeName.CS:
                        string colorSpace = NameAt(op, 0);
                        if (colorSpace != null && Array.IndexOf(DeviceColorSpaces, colorSpace) < 0)
                            Use("/ColorSpace", colorSpace, scope, depth);
                        break;

                    case OpCodeName.scn:
                    case OpCodeName.SCN:
                        // A pattern is named last, after the components of the underlying colour.
                        Use("/Pattern", NameAt(op, op.Operands.Count - 1), scope, depth);
                        break;

                    case OpCodeName.BDC:
                    case OpCodeName.DP:
                        // The property list is named second, after the tag, unless it is written out.
                        Use("/Properties", NameAt(op, 1), scope, depth);
                        break;
                }

                if (!_understood)
                    return;
            }
        }

        static string NameAt(COperator op, int index)
        {
            if (index < 0 || index >= op.Operands.Count)
                return null;

            CName name = op.Operands[index] as CName;
            return name == null ? null : name.Name;
        }

        #endregion

        #region Following what is drawn

        /// <summary>
        /// Records that the content in scope draws with the named resource, and reads whatever that
        /// resource draws in its turn.
        /// </summary>
        void Use(string category, string name, PdfDictionary scope, int depth)
        {
            if (name == null)
                return;

            if (ReferenceEquals(scope, _pageResources))
                Record(category, name);

            PdfDictionary resource = Resolve(category, name, scope);
            if (resource == null)
            {
                // The content names something the resources do not hold. There is nothing to keep
                // and nothing to follow, and dropping the rest of the dictionary is still safe.
                return;
            }

            switch (category)
            {
                case "/XObject":
                    if (resource.Elements.GetName("/Subtype") == "/Form")
                        ReadNested(resource, resource, scope, depth);
                    break;

                case "/Pattern":
                    // A tiling pattern is drawn from a content stream; a shading pattern is not.
                    if (resource.Elements.GetInteger("/PatternType") == 1)
                        ReadNested(resource, resource, scope, depth);
                    break;

                case "/Font":
                    if (resource.Elements.GetName("/Subtype") == "/Type3")
                        ReadCharProcs(resource, scope, depth);
                    break;

                case "/ExtGState":
                    UseSoftMask(resource, scope, depth);
                    break;
            }
        }

        /// <summary>
        /// Follows the soft mask of a graphics state. The mask is painted by a form, and a form
        /// without resources of its own paints with those of whatever set the state, so what the
        /// mask names has to be kept as well.
        /// </summary>
        void UseSoftMask(PdfDictionary extGState, PdfDictionary scope, int depth)
        {
            PdfItem item = extGState.Elements["/SMask"];
            if (item is PdfReference)
                item = ((PdfReference)item).Value;

            if (item == null)
            {
                // The state says nothing about the mask and leaves it as it was.
                return;
            }

            PdfName none = item as PdfName;
            if (none != null)
            {
                // A mask set to /None paints nothing. Any other name is not a mask this knows.
                if (none.Value != "/None")
                    _understood = false;
                return;
            }

            PdfDictionary mask = item as PdfDictionary;
            PdfDictionary group = mask == null ? null : mask.Elements.GetDictionary("/G");
            if (group == null)
            {
                // A mask whose form cannot be reached may paint with anything.
                _understood = false;
                return;
            }

            ReadNested(group, group, scope, depth);
        }

        /// <summary>
        /// Reads a stream drawn by the content, in the scope of its own resources where it has them
        /// and in the scope it was drawn from where it has not.
        /// </summary>
        void ReadNested(PdfDictionary stream, PdfDictionary owningResources, PdfDictionary scope, int depth)
        {
            if (!MarkAsRead(stream))
                return;

            byte[] content;
            if (!TryGetContent(stream, out content))
            {
                // A stream that cannot be read may draw with anything.
                _understood = false;
                return;
            }

            Read(content, ScopeOf(owningResources, scope), depth + 1);
        }

        void ReadCharProcs(PdfDictionary font, PdfDictionary scope, int depth)
        {
            PdfDictionary charProcs = font.Elements.GetDictionary("/CharProcs");
            if (charProcs == null)
                return;

            PdfDictionary fontScope = ScopeOf(font, scope);
            foreach (PdfName glyph in charProcs.Elements.KeyNames)
            {
                PdfDictionary procedure = charProcs.Elements.GetDictionary(glyph.Value);
                if (procedure == null)
                    continue;

                if (!MarkAsRead(procedure))
                    continue;

                byte[] content;
                if (!TryGetContent(procedure, out content))
                {
                    _understood = false;
                    return;
                }

                Read(content, fontScope, depth + 1);
                if (!_understood)
                    return;
            }
        }

        void ReadAppearances(PdfPage page)
        {
            PdfArray annotations = page.Elements.GetArray(PdfPage.Keys.Annots);
            if (annotations == null)
                return;

            for (int idx = 0; idx < annotations.Elements.Count && _understood; idx++)
            {
                PdfDictionary annotation = annotations.Elements.GetDictionary(idx);
                PdfDictionary appearance = annotation == null
                    ? null
                    : annotation.Elements.GetDictionary("/AP");
                if (appearance == null)
                    continue;

                foreach (PdfName kind in appearance.Elements.KeyNames)
                {
                    PdfDictionary stream = appearance.Elements.GetDictionary(kind.Value);
                    if (stream == null)
                        continue;

                    if (stream.Stream != null)
                    {
                        ReadNested(stream, stream, _pageResources, 0);
                    }
                    else
                    {
                        // An appearance that changes with the state of the annotation is a
                        // dictionary of one stream per state.
                        foreach (PdfName state in stream.Elements.KeyNames)
                        {
                            PdfDictionary perState = stream.Elements.GetDictionary(state.Value);
                            if (perState != null && perState.Stream != null)
                                ReadNested(perState, perState, _pageResources, 0);
                        }
                    }

                    if (!_understood)
                        return;
                }
            }
        }

        /// <summary>
        /// The scope names in a stream resolve against: its own resources where it has them, and
        /// those it was drawn from where it has not.
        /// </summary>
        PdfDictionary ScopeOf(PdfDictionary owner, PdfDictionary scope)
        {
            return owner.Elements.GetDictionary(PdfPage.Keys.Resources) ?? scope;
        }

        PdfDictionary Resolve(string category, string name, PdfDictionary scope)
        {
            PdfDictionary entries = scope.Elements.GetDictionary(category);
            return entries == null ? null : entries.Elements.GetDictionary(name);
        }

        /// <summary>
        /// Notes that the stream has been read, and says whether it had not been read already.
        /// </summary>
        bool MarkAsRead(PdfDictionary stream)
        {
            // A direct stream cannot be shared and so cannot be drawn within itself.
            if (!stream.IsIndirect)
                return true;

            string id = stream.ObjectID.ToString();
            if (_read.ContainsKey(id))
                return false;

            _read[id] = null;
            return true;
        }

        void Record(string category, string name)
        {
            Dictionary<string, object> names;
            if (!_used.TryGetValue(category, out names))
                _used[category] = names = new Dictionary<string, object>();

            names[name] = null;
        }

        bool IsUsed(string category, string name)
        {
            Dictionary<string, object> names;
            return _used.TryGetValue(category, out names) && names.ContainsKey(name);
        }

        #endregion

        #region Rewriting

        /// <summary>
        /// Puts a resource dictionary on the page holding only what it draws with. The dictionary it
        /// had is left untouched, being in all likelihood the one the other pages carry as well.
        /// </summary>
        void Rewrite(PdfPage page, PdfDictionary resources)
        {
            PdfResources pruned = new PdfResources(page.Owner);
            bool anythingDropped = false;

            foreach (PdfName key in resources.Elements.KeyNames)
            {
                PdfDictionary entries = Array.IndexOf(Categories, key.Value) < 0
                    ? null
                    : resources.Elements.GetDictionary(key.Value);

                if (entries == null)
                {
                    // Not a category of named resources, or not written as one. Kept as it stands.
                    pruned.Elements[key.Value] = resources.Elements[key.Value];
                    continue;
                }

                PdfDictionary kept = new PdfDictionary(page.Owner);
                foreach (PdfName name in entries.Elements.KeyNames)
                {
                    if (IsUsed(key.Value, name.Value))
                        kept.Elements[name.Value] = entries.Elements[name.Value];
                    else
                        anythingDropped = true;
                }

                if (kept.Elements.Count > 0)
                    pruned.Elements[key.Value] = kept;
            }

            // Leaving the page alone where there was nothing to drop keeps a document that gains
            // nothing from this unchanged by it.
            if (!anythingDropped)
                return;

            // An object of its own, as the dictionary it stands in for is in all likelihood one the
            // other pages carry as well, and must be left as it is for them.
            page.Owner._irefTable.Add(pruned);
            page.ReplaceResources(pruned);
        }

        #endregion

        #region Content of a stream

        static bool TryGetPageContent(PdfPage page, out byte[] content)
        {
            return PdfContentStreams.TryGetPageContent(page, out content);
        }

        static bool TryGetContent(PdfDictionary stream, out byte[] content)
        {
            return PdfContentStreams.TryGetContent(stream, out content);
        }

        #endregion
    }
}
