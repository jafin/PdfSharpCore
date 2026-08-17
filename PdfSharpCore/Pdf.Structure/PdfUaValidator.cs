using System;
using System.Collections.Generic;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.Annotations;

namespace PdfSharpCore.Pdf.Structure;

/// <summary>
/// Holds a document claiming PDF/UA-1 to the rules of PDF/UA-1 that can be settled by looking at it,
/// and throws before a byte is written when it breaks one.
/// </summary>
/// <remarks>
/// <para>
/// Running before the file is written rather than after is the whole point. A validator that runs
/// afterwards tells somebody who is no longer in a position to do anything about it — usually a
/// customer — and teaches the caller nothing. Every message here names the rule and what to do.
/// </para>
/// <para>
/// <b>What is checked.</b> The document is tagged; it has a title, in the information dictionary and
/// declared as the thing to show in the title bar; it declares a language; every page is in the
/// structure tree and orders its tabs by structure; every figure has alternate text; every note has an
/// identifier; every link annotation has a description and is reachable from the tree.
/// </para>
/// <para>
/// <b>What is not.</b> That no content sits outside the structure tree — the marks would have to be
/// walked and matched against the tree, which is a content-stream pass this does not make. That
/// reading order is sensible, which no machine can decide. That headings do not skip a level. That
/// every glyph maps back through <c>/ToUnicode</c>, which is true of everything this library embeds
/// but not of an imported page. A page imported from an untagged document passes every rule here and
/// conforms to nothing, which is why the last word belongs to veraPDF and not to this.
/// </para>
/// </remarks>
public static class PdfUaValidator
{
    /// <summary>
    /// Checks the document, throwing <see cref="InvalidOperationException"/> on the first rule it
    /// breaks. Called during save when <see cref="PdfDocumentOptions.UAConformance"/> claims a
    /// profile; public so a caller can ask the question at a moment of their own choosing.
    /// </summary>
    public static void Validate(PdfDocument document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        RequireStructureTree(document);
        RequireTitle(document);
        RequireLanguage(document);
        RequirePagesInTheTree(document);
        RequireAlternateTextOnFigures(document);
        RequireIdentifiedNotes(document);
        RequireDescribedLinks(document);
    }

    static void RequireStructureTree(PdfDocument document)
    {
        if (!document.IsTagged)
            throw new InvalidOperationException(
                "A PDF/UA document has to be tagged, and nothing in this one is. Build a structure "
                + "tree through PdfDocument.Structure and XGraphics.BeginMarkedContent, or let "
                + "MigraDoc do it — its renderer tags what it draws unless told not to.");
    }

    static void RequireTitle(PdfDocument document)
    {
        if (string.IsNullOrEmpty(document.Info.Title))
            throw new InvalidOperationException(
                "A PDF/UA document has to have a title. Set Info.Title — it is what a reader "
                + "announces the document as, so the file name standing in for it is the failure "
                + "this rule exists to stop.");

        // The title is only reached if the reader is told to prefer it. Left unset, every viewer
        // shows the file name instead and the title might as well not be there.
        if (!document.ViewerPreferences.DisplayDocTitle)
            throw new InvalidOperationException(
                "A PDF/UA document has to ask that its title be shown rather than its file name. "
                + "Set ViewerPreferences.DisplayDocTitle to true.");
    }

    static void RequireLanguage(PdfDocument document)
    {
        var language = document.Catalog.Elements.GetString(PdfCatalog.Keys.Lang);
        if (string.IsNullOrEmpty(language))
            throw new InvalidOperationException(
                "A PDF/UA document has to declare its natural language, or a reader cannot choose a "
                + "voice to read it in. Set PdfDocument.Language to an RFC 3066 tag such as \"en-GB\".");
    }

    static void RequirePagesInTheTree(PdfDocument document)
    {
        for (var index = 0; index < document.PageCount; index++)
        {
            var page = document.Pages[index];

            if (!page.Elements.ContainsKey(PdfPage.Keys.StructParents))
                throw new InvalidOperationException(
                    "Page " + (index + 1) + " has no /StructParents, so nothing on it belongs to the "
                    + "structure tree. Every page of a PDF/UA document has to be tagged; a page "
                    + "imported from an untagged document cannot be, and has to be tagged by hand or "
                    + "left out.");

            // /Tabs decides what the tab key walks. Left unset it is the order the annotations
            // happen to sit in the array, which is drawing order, which is exactly the order the
            // structure tree exists to correct.
            if (page.Elements.GetName(PdfPage.Keys.Tabs) != "/S")
                throw new InvalidOperationException(
                    "Page " + (index + 1) + " does not order its tab stops by structure. Set /Tabs "
                    + "to /S on every page.");
        }
    }

    static void RequireAlternateTextOnFigures(PdfDocument document)
    {
        foreach (var element in Elements(document))
        {
            if (element.Elements.GetName(PdfStructureElement.Keys.S) != PdfTag.Figure.Name)
                continue;

            var alternate = element.Elements.GetString(PdfStructureElement.Keys.Alt);
            var actual = element.Elements.GetString(PdfStructureElement.Keys.ActualText);

            if (string.IsNullOrEmpty(alternate) && string.IsNullOrEmpty(actual))
                throw new InvalidOperationException(
                    "A figure in the structure tree has no alternate text, which leaves it saying "
                    + "nothing at all to the reader it was tagged for. Give it one, or make it an "
                    + "artifact — a decorative image honestly marked as decoration conforms, and an "
                    + "undescribed figure does not. From MigraDoc, set Image.AlternativeText.");
        }
    }

    /// <summary>
    /// Every note has to be nameable, because something else has to be able to name it.
    /// </summary>
    /// <remarks>
    /// ISO 14289-1 7.9. A footnote is the one structure type whose whole purpose is to be pointed at
    /// from somewhere else — the raised mark in the sentence that cited it — and an element with no
    /// identifier cannot be pointed at, so a reader has no way to offer the jump from the mark to the
    /// note or back again. The identifier is also what the structure tree root's <c>/IDTree</c> is
    /// keyed by, so an unnamed note is absent from that index as well.
    /// </remarks>
    static void RequireIdentifiedNotes(PdfDocument document)
    {
        foreach (var element in Elements(document))
        {
            if (element.Elements.GetName(PdfStructureElement.Keys.S) != PdfTag.Note.Name)
                continue;

            if (string.IsNullOrEmpty(element.Elements.GetString(PdfStructureElement.Keys.ID)))
                throw new InvalidOperationException(
                    "A note in the structure tree has no /ID, which PDF/UA requires of every one: a "
                    + "note exists to be pointed at from the mark that cited it, and an element with "
                    + "no identifier cannot be pointed at. Set PdfStructureElement.Id. A footnote "
                    + "rendered by MigraDoc is given one automatically.");
        }
    }

    static void RequireDescribedLinks(PdfDocument document)
    {
        for (var index = 0; index < document.PageCount; index++)
        {
            // The raw array rather than the Annotations collection, which creates an empty one on a
            // page that has none — an inspection that adds bytes to the file it is inspecting.
            if (!(Resolve(document.Pages[index].Elements[PdfPage.Keys.Annots]) is PdfArray annotations))
                continue;

            foreach (var item in annotations.Elements)
            {
                if (!(Resolve(item) is PdfDictionary dictionary))
                    continue;

                if (dictionary.Elements.GetName(PdfAnnotation.Keys.Subtype) != "/Link")
                    continue;

                if (string.IsNullOrEmpty(dictionary.Elements.GetString(PdfAnnotation.Keys.Contents)))
                    throw new InvalidOperationException(
                        "A link on page " + (index + 1) + " has no /Contents, so a reader announcing "
                        + "it can only say \"link\". Give the annotation a description of where it "
                        + "goes. From MigraDoc, this is written from the hyperlink's own text.");

                if (!dictionary.Elements.ContainsKey("/StructParent"))
                    throw new InvalidOperationException(
                        "A link on page " + (index + 1) + " is not reachable from the structure tree. "
                        + "A link is content as much as anything drawn is, and one that only exists "
                        + "as a rectangle is found by a reader hit-testing the page and by nothing "
                        + "else. Join it with PdfStructureBuilder.AddAnnotation.");
            }
        }
    }

    /// <summary>
    /// Every element of the tree, depth first. Kids hold three different things — child elements,
    /// bare marked-content identifiers and reference dictionaries — and only the first are elements,
    /// which is what the type check below is filtering for.
    /// </summary>
    static IEnumerable<PdfDictionary> Elements(PdfDocument document)
    {
        var pending = new Stack<PdfDictionary>();
        Push(pending, document.Structure.Root.Elements[PdfStructureTreeRoot.Keys.K]);

        while (pending.Count > 0)
        {
            var element = pending.Pop();
            yield return element;
            Push(pending, element.Elements[PdfStructureElement.Keys.K]);
        }
    }

    static void Push(Stack<PdfDictionary> pending, PdfItem kids)
    {
        switch (Resolve(kids))
        {
            case PdfArray array:
                foreach (var item in array.Elements)
                {
                    if (Resolve(item) is PdfDictionary child && IsElement(child))
                        pending.Push(child);
                }
                break;

            case PdfDictionary single when IsElement(single):
                pending.Push(single);
                break;
        }
    }

    static bool IsElement(PdfDictionary dictionary) =>
        dictionary.Elements.GetName(PdfStructureElement.Keys.Type) == "/StructElem";

    static PdfItem Resolve(PdfItem item) => item is PdfReference reference ? reference.Value : item;
}
