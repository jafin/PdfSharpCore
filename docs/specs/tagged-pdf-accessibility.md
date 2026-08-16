# Proposal — tagged PDF and PDF/UA accessible output

What accessible output covers, and what it deliberately leaves out.
Gap **G2** of the competitive gap analysis. **Stage A is built. Stages B and C are not.**

| item | what | stage | status |
|---|---|---|---|
| 1 | `PdfSharpCore.Pdf.Structure` — structure tree, role map, parent tree | A | done |
| 2 | `XGraphics.BeginMarkedContent` / `BeginArtifact`, and `BDC`/`EMC` emission | A | done |
| 3 | Catalog `/MarkInfo`, `/Lang` | A | done |
| 3b | `/ViewerPreferences /DisplayDocTitle`, and requiring a title | A | not started |
| 4 | MigraDoc tags its own output — headings, tables, lists, figures, links | B | not started |
| 5 | `Image.AlternativeText`, `Table.Summary` on the DOM | B | not started |
| 6 | PDF/UA-1 identifier in XMP, `/ActualText`, `/Tabs /S`, a pre-save validator | C | not started |

Covered by `PdfSharpCore.Test/IO/TaggedPdfTests.cs`.

```csharp
using (gfx.BeginMarkedContent(PdfTag.H1))
    gfx.DrawString("Invoice", headingFont, XBrushes.Black, 40, 60);

using (gfx.BeginArtifact())                       // running heads, folios, rules
    gfx.DrawString("Page 3 of 9", smallFont, XBrushes.Gray, 500, 800);
```

Asking for `PdfDocument.Structure` is what makes a document tagged. One that never does is written
exactly as it was before — no tree, no `/MarkInfo`, and not one extra byte, which the first test
pins.

## What Stage A settled that the proposal had not

**A container element holds both its own marks and its child elements.** Opening a scope inside
another gives the outer element a `/K` of `[mcid, childRef]` rather than `[childRef]`. That is
correct and it is what the standard intends: the identifier covers anything drawn directly in the
section and outside the heading. It is worth knowing before reading a `/K` array, because the first
entry is often an integer and code that assumes a dictionary there finds null.

**The marked-content scope is a `using`, and that is load-bearing rather than stylistic.** An
unbalanced `BDC` corrupts every mark after it on the page, so an early return or a throw mid-paragraph
must not be able to leave one open. There is a test that throws inside a scope and then asserts the
`BDC` and `EMC` counts still match.

**`BDC` is always emitted in graphic mode.** A marked-content sequence opened between `BT` and `ET`
nests inside the text object instead of containing it. `BeginGraphicMode` is called first, always.

## Still true, and still the reason to finish this

Stage A is a hand-driven API. **The value is in Stage B** — a caller should get an accessible
document out of MigraDoc without tagging anything by hand, because almost nobody will do it by hand.
Until then this is the plumbing and not the feature, and no document produced through
`MigraDocCore.Rendering` is tagged at all.

Stage C, and therefore any actual PDF/UA claim, additionally needs the XMP identifier — which
`docs/specs/pdf-a-conformance.md` now has the writer for — and veraPDF in CI, without which a
conformance claim is self-certified.

---

## The defect

The library cannot say what anything on a page *is*. A heading and a caption are both a `Tj` in a
content stream, and a screen reader has nothing to go on but their position.

The names are in the source and nothing stands behind them:

- `Pdf.Advanced/PdfCatalog.cs` declares `StructTreeRoot`, `MarkInfo` and `OutputIntent` as key-name
  constants. Nothing constructs any of them.
- `Pdf.Content.Objects/Operators.cs` has `BDC`, `BMC` and `EMC` in the operator table — because the
  content-stream **reader** must cope with files that contain them. `Drawing.Pdf/XGraphicsPdfRenderer.cs`
  never emits one.
- No MCID, no `/StructParents`, no `/Alt`, no `/Lang`, no role map.

Meanwhile `docs/specs/page-resize.md` already lists "tagged documents are refused" as a guard — the
codebase knows tagged PDFs exist and treats them as something other people make.

## Why this is worth eight weeks

Two reasons, and only one of them is competitive.

**Regulation is pulling in one direction, and it is worth being exact about how far.** The European
Accessibility Act (Directive (EU) 2019/882) took effect on 28 June 2025, with the transition window
for existing products closing 28 June 2030. It is harmonised through EN 301 549, which incorporates
WCAG 2.1 AA; for documents that means tagged PDFs.

It does **not** follow that every PDF anyone generates is now covered. The EAA applies to the
products and services it lists — among them e-commerce, e-books, banking and transport services — and
to the documentation that goes with them, rather than to invoices, contracts and reports at large.
The broader driver for those is public-sector procurement: EN 301 549 is what EU public bodies buy
against under Directive (EU) 2016/2102, and national implementations are what decide any given case.

The conclusion for this library is the same either way and needs no exaggeration to stand up: a .NET
PDF library that cannot emit a tagged PDF is unusable for a customer whose own obligation is any of
the above, and cannot be adopted by anyone who might later acquire one.

**Everyone else already ships it.** Not just the paid libraries — *PDFKit*, a free JavaScript library,
advertises "marked content, logical structure, Tagged PDF, PDF/UA". QuestPDF ships it. This is table
stakes, not a differentiator, and the differentiator is item 4: making it **automatic**.

---

## Stage A — the plumbing and a manual API

### The structure tree

A parallel tree beside the page tree, describing the document as a document rather than as marks:

```text
Catalog
 ├─ /MarkInfo <</Marked true>>
 ├─ /Lang (en-GB)
 └─ /StructTreeRoot ──► /Type /StructTreeRoot
                         ├─ /RoleMap  <</Caption /P>>        ← custom → standard
                         ├─ /ParentTree ──► number tree      ← MCID → StructElem
                         └─ /K [ ──► /StructElem /S /Document
                                       ├─ /StructElem /S /H1 ──► /K [ 0 ]        ← MCID 0
                                       ├─ /StructElem /S /P  ──► /K [ 1, 2 ]
                                       └─ /StructElem /S /Figure /Alt (…) ──► …
                                    ]
```

Each leaf points at a **marked-content identifier** — an integer — and the page's content stream wraps
the corresponding marks in `BDC`/`EMC` carrying the same number. The `/ParentTree` is the reverse
index, letting a reader go from a mark back to its meaning. It is a number tree, and
`Pdf.Advanced/PdfNumberTreeNode.cs` **already exists** — a real head start on the fiddliest part.

New namespace `PdfSharpCore.Pdf.Structure`: `PdfStructureTreeRoot`, `PdfStructElement`,
`PdfMarkedContentReference`, `PdfObjectReference` (for annotations, which are structure content but not
marks), and a `PdfTag` enumeration of the standard structure types.

### The drawing API

```csharp
using (gfx.BeginMarkedContent(PdfTag.H1))
    gfx.DrawString("Invoice", headingFont, XBrushes.Black, 40, 60);

using (gfx.BeginArtifact(ArtifactType.Pagination))   // running heads, folios, rules
    gfx.DrawString("Page 3 of 9", smallFont, XBrushes.Grey, 500, 800);
```

Two operations, and the second matters as much as the first. **Everything on the page is either
content or an artifact.** A page number that is neither is a PDF/UA failure, so the artifact scope is
not a convenience — it is half the rule.

`XGraphicsPdfRenderer` emits `/H1 <</MCID 0>> BDC … EMC`, allocates MCIDs per page, and writes
`/StructParents` on the page. The scopes nest, so the renderer needs a stack, and it must interact
correctly with the graphics-state stack that `Drawing/GraphicsStateStack.cs` already keeps — a `q`/`Q`
pair may not straddle a `BDC`/`EMC` pair.

### The document-level keys

`/MarkInfo <</Marked true>>`, `/Lang` on the catalog and overridable per structure element,
`/ViewerPreferences <</DisplayDocTitle true>>`, and a document title in the info dictionary. PDF/UA
requires all four, and the last two are the ones everybody forgets.

---

## Stage B — MigraDoc tags its own output

This is where the value is. Hand-tagging is a feature; **not having to** is the product.

`MigraDocCore.Rendering` knows the semantics already — it is rendering a `Paragraph` with a `Heading1`
style, it just throws that away on the way to the page. The mapping:

| DOM | structure type | notes |
|---|---|---|
| `Paragraph` | `/P` | `/H1`…`/H6` when the style is a heading style |
| `Table` | `/Table` → `/TR` → `/TH` \| `/TD` | header rows become `/TH` with `/Scope /Column`; needs `Row.HeadingFormat`, which exists |
| `ListInfo` | `/L` → `/LI` → `/Lbl` + `/LBody` | the bullet or number is the `/Lbl` |
| `Image` | `/Figure` with `/Alt` | requires item 5 |
| `Hyperlink` | `/Link` with an `/OBJR` | the link annotation is structure content too, not just a rectangle |
| `Footnote` | `/Note` | |
| `TextFrame`, `Shape` | `/Figure` or artifact | depends on whether it carries meaning |
| `HeaderFooter` | **artifact** | never content |
| Cell borders, rules, shading | **artifact** | decoration |

`docs/specs/repeating-table-headings` and the existing `Row.HeadingFormat` mean the header/body
distinction is already modelled, which is lucky — table tagging is otherwise the hardest part, because
`/TH` scope and the header/data association is what actually makes a table navigable.

Item 5 adds two DOM properties, `Image.AlternativeText` and `Table.Summary`. These are generated
properties, so the source generator under `MigraDocCore.DocumentObjectModel.Generators` carries the
cost, and `MigraDocCore.DocumentObjectModel.Generators.Tests` covers the generation.

**The break worth taking:** make tagging the *default* for MigraDoc rendering rather than an opt-in.
An untagged document should be the thing you ask for.

---

## Stage C — PDF/UA conformance

Tagging a document and *conforming* are not the same, and the gap is mostly rules that are cheap to
check and easy to violate:

- The PDF/UA-1 identifier in XMP — **depends on `docs/specs/pdf-a-conformance.md`**, which builds the
  XMP writer.
- No content outside the structure tree, and no structure element with no content.
- `/ActualText` wherever the marks and the text disagree: ligatures, and hyphenation breaks if
  pattern-based hyphenation ever lands.
- `/Tabs /S` on every page, so tab order follows structure rather than the order annotations happen to
  sit in the array.
- Every annotation reachable from the structure tree; every link with `/Contents` alternate text.
- Fonts embedded (already true here) and every glyph reachable through `/ToUnicode` (already true).

A `PdfUaValidator` should run before the bytes are written and throw naming the specific rule, because
a validator that runs afterwards teaches nobody anything.

---

## What this deliberately does not cover

- **PDF/UA-2 (ISO 14289-2:2024).** Worth tracking; UA-1 is what tools validate against today.
- **Automatic alt text.** If the caller does not supply `AlternativeText`, the image is an artifact or
  the build fails — it is not the library's business to invent a description.
- **Retro-tagging an imported page.** Untagged content coming in through `XPdfForm` or page import
  stays untagged, and a document mixing the two is honestly reported as non-conforming rather than
  quietly labelled.
- **Reading order different from drawing order.** The structure tree defines reading order, so this
  falls out for MigraDoc. For hand-drawn pages the caller controls it by scope order and is on their
  own.
- **Interaction with `PdfPage.Resize`.** `docs/specs/page-resize.md` currently refuses tagged
  documents. Once this library *produces* them, that refusal stops being an edge case and becomes the
  common path — the structure tree survives a resize untouched (marks move with the content into the
  form XObject), but that needs proving, not assuming.

## Tests

`MigraDocCore.Rendering.Tests` is the right home: it covers MigraDoc's own layout, links the
content-stream readers out of `PdfSharpCore.Test/Helpers`, and **deliberately rasterizes nothing**, so
it needs neither Ghostscript nor ImageMagick. Structure assertions are exactly that shape — save,
reopen, walk `/StructTreeRoot`, assert the tree.

Stage C needs an outside opinion, and that means **veraPDF in CI** as a container step. CI is Linux-only
already, which makes it cheap, but it does add a Java dependency to a build that is currently pure .NET
plus Ghostscript. That is a real cost and a deliberate decision.

## Related

- `docs/specs/cross-reference-streams.md` — land it first; tagging multiplies the object count.
- `docs/specs/pdf-a-conformance.md` — builds the XMP writer Stage C needs.
- Optional content groups become near-free once the marked-content machinery here exists.
