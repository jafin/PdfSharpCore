# Spec — remaining page-import defects after issue #461

Status: draft for review. Branch `fix/imported-annotations-copy-linked-pages` already fixes the
link-destination cause of #461; everything below is what that investigation turned up and left.

Three items, independent, listed in the order I would do them. Item 2 is a crash and is cheap.
Item 3 falls out of item 2. Item 1 is the large one.

---

## Item 1 — Unused resources are copied with an imported page

### The defect

`PdfPages.ImportExternalPage` copies a page's `/Resources` wholesale. When pages share one resource
dictionary — either literally the same object, or one inherited from the `/Pages` node and pushed
down onto every page by `PdfPages.FlattenPageTree` — that dictionary names *every* font, image and
form in the document. Splitting therefore gives each page a copy of all of them.

Reproduced with a hand-crafted 3-page document, 3 × 20 KB images, one drawn per page, pages
inheriting one `/Resources` from the `/Pages` node: **every split file was 63,256 bytes**, against
~22,600 for the same document with per-page resources. Unchanged by the #461 fix.

This is very likely the residual cause in the reporter's file (`…\Slip\tesst12321.pdf` reads like a
generated payslip batch, and report generators habitually share a font resource dictionary).

Note this is *not* a violation of PDFsharp's stated import contract — "adding an external page
always makes a deep copy of their transitive closure" is exactly what it does. It is a missing
optimisation, not a broken invariant. That framing drives the API shape below.

### Proposed shape

A public, opt-in method on `PdfDocument`, following the `ConsolidateImages` precedent
(`PdfDocument.cs:815`) — an explicit post-processing pass the caller asks for:

```csharp
/// <summary>
/// Drops the entries of each page's resource dictionary that the page does not draw with.
/// </summary>
public void PruneUnusedResources()
```

Opt-in rather than automatic on import, because it has to decode and parse every content stream,
and because it can only ever be a best guess about what a content stream reaches. Callers splitting
a document call it before `Save`.

### Algorithm

Per page:

1. Resolve the page's effective `/Resources`.
2. Parse the content with `ContentReader.ReadContent(page)` (`Pdf.Content/ContentReader.cs`).
   `CParser(PdfPage)` already concatenates a `/Contents` array and unfilters it via
   `PdfContents.CreateSingleContent()`, and does not mutate the page.
3. Walk the `CSequence` collecting (category, name) pairs from the operators that name a resource:

   | operator | category | operand |
   |---|---|---|
   | `Do` | `/XObject` | 0 |
   | `Tf` | `/Font` | 0 |
   | `gs` | `/ExtGState` | 0 |
   | `sh` | `/Shading` | 0 |
   | `cs`, `CS` | `/ColorSpace` | 0, unless a device space (`/DeviceGray`, `/DeviceRGB`, `/DeviceCMYK`, `/Pattern`) |
   | `scn`, `SCN` | `/Pattern` | last, only when it is a `CName` |
   | `BDC`, `DP` | `/Properties` | 1, only when it is a `CName` |

   `ri` names a standard rendering intent, not a resource. Text-showing operators name nothing
   beyond the font `Tf` already selected.

4. Recurse into every used `/XObject` of `/Subtype /Form`, and into tiling patterns and Type 3
   font `/CharProcs`:
   - a form **with** its own `/Resources` is a separate scope, scanned against that dictionary;
   - a form **without** `/Resources` inherits the page's, so its names join the page's used set.
     This is the rule most likely to be got wrong, and getting it wrong drops a resource that is
     genuinely drawn.
   - Cycle guard on `PdfObjectID`, plus a depth cap.

5. Build a **fresh** `/Resources` dictionary for the page holding only the used names per category.

### Traps

- **Never mutate a resource dictionary in place.** It is shared — that is the whole defect. Pruning
  page 1 in place would strip what page 2 draws with. Always write a new dictionary onto the page.
- **Nothing needs deleting.** Once a page stops naming an image, it is unreachable from the trailer
  and `PdfDocument.PrepareForSave` → `_irefTable.Compact()` (`PdfDocument.cs:423`) drops it.
- Preserve entries that are not name-keyed resource categories (`/ProcSet`, and anything the
  scanner does not model) untouched.

### Failure mode: when in doubt, keep everything

A page that comes out too large is a nuisance. A page that comes out missing a font or an image is
corrupt. So any of the following leaves that page's `/Resources` **exactly as it was**:

- the content stream does not parse (`ContentReaderException`);
- **an inline image (`BI`) appears anywhere in the content.** `CLexer.ScanInlineImage`
  (`Pdf.Content/CLexer.cs:149`) is explicitly `NYI: Just scans over it`, and for non-ASCII85 data it
  finds the end by scanning for the literal bytes `E`,`I`, which can match inside binary image data.
  A false match desynchronises the parse, after which a real `/Name Do` can be missed entirely;
- a used form XObject cannot be resolved, or the cycle guard or depth cap trips;
- a resource category is present that the scanner does not model.

Prune per category, not all-or-nothing: uncertainty about `/ColorSpace` should still leave
`/XObject` and `/Font` prunable, which is where nearly all the bytes are.

### Verification

- Unit tests over hand-crafted documents — reuse the `BuildDocument` helper in
  `PdfSharpCore.Test/IO/SplitTests.cs`:
  - shared inherited `/Resources` naming three images, one drawn per page → each split file holds one;
  - form XObject without `/Resources` using a page font → font kept;
  - image reached only through a nested form → kept;
  - cyclic form → resources kept, terminates;
  - inline image present → resources kept unchanged;
  - unparsable content → resources kept unchanged;
  - one dictionary shared by two pages → pruning page 1 leaves page 2 whole.
- Rendered-output equivalence on the real assets (`FamilyTree.pdf`, `test.pdf`, `Pdf20.pdf`) using
  the existing golden-image harness — `[GoldenImageFact]`, `PdfHelper.Rasterize`, `PdfHelper.Diff`
  in `PdfSharpCore.Test/Helpers`. Pruning must not change a single rendered page.

### Cost

The largest of the three. The scanner and the form-recursion are the bulk; the conservative bail-outs
are what keep it safe. Worth doing behind an explicit method; not worth doing implicitly on import.

---

## Item 2 — `PdfPages.InsertRange` throws on a page that carries a resolvable link

### The defect

Confirmed by direct probe:

```
System.ArgumentException : An item with the same key has already been added. Key: /Annots
```

`InsertRange` calls `ImportExternalPage(importPage, annotationCopying)` in its first loop
(`PdfPages.cs:234`), which with the default `AnnotationCopyingType.ShallowCopy` already puts
`/Annots` on the new page. Its second loop then builds its own annotation array and does
`page.Elements.Add(PdfPage.Keys.Annots, annotations)` (`PdfPages.cs:350`).
`DictionaryElements.Add` is backed by `Dictionary<string, PdfItem>.Add`
(`PdfDictionary.cs:1328`), which throws on a duplicate key.

It only escapes notice because the `Add` sits behind `if (annotations.Count > 0)`, and the second
loop only ever adds an annotation that is a `/Link` **and** whose `/Dest` is a 5-element array
**and** whose target page is in the inserted range. Miss any of those and the array stays empty and
the crash does not happen. Hit all three — an ordinary `[page /XYZ l t z]` link between two pages
being inserted together — and it throws.

### Proposed fix

Delete the second loop entirely and let the import path handle annotations, adding the call the
first loop is missing:

```csharp
PdfAnnotations.FixImportedAnnotation(page);
DetachImportedDestinations(page, importPage, importedObjectTable);
```

The deferred-destination mechanism added for #461 does what the second loop was reaching for, and
does it properly: destinations are resolved against the pages that actually made it into the
document, at save time, whichever direction the link points. The bespoke loop then has no job left.

This also stops `InsertRange` silently discarding every annotation that is not a `/Link` — its
key handling is a whitelist (`/BS`, `/F`, `/Rect`, `/StructParent`, `/Subtype`, `/Dest`) that drops
everything else on the floor.

### Verification

- A page with a 5-element `/Dest` link inserted via `InsertRange` no longer throws.
- Links between two pages inserted together still point at the inserted pages.
- Non-link annotations survive `InsertRange` (new behaviour, worth an explicit test).
- The existing `Merge` tests stay green.

---

## Item 3 — `InsertRange` drops every destination that is not `[page /XYZ l t z]`

`PdfPages.cs:315` gates on `destArray.Elements.Count == 5`, so `[page /Fit]`, `[page /FitH t]`,
`[page /FitB]`, `[page /FitR l b r t]` and friends are all discarded, along with the whole
annotation. `/A << /S /GoTo /D … >>` actions are not looked at at all, and neither are named
destinations.

Resolved for free by item 2: the shared path keys off "is the first element of the destination array
a reference to a page", which is true of every explicit destination form, and it handles `/A`
go-to actions as well. No separate work.

---

## Not in scope

- Named destinations (`/Dest (name)` or `/Dest /Name`) resolved through the catalog `/Names` tree
  or `/Dests`. Neither the old code nor the #461 fix follows them; they dangle after an import
  because the name tree is not imported. Real, but a different feature.
- Importing `/Outlines`, `/StructTreeRoot` or `/AcroForm` alongside a page.
