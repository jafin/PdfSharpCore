## Context

Three notions of page geometry already overlap in `PdfPage`, and this fork has been through all of
them once (issue #464, `RotatedPageTests.cs`):

```
PdfPage.Orientation ──┐  authoring concept; the media box is always
                      │  stored portrait and turned on the way out
                      ├─► MediaBoxIsTurnedWhenWritten ─┐
PdfPage.Rotate ───────┴─► IsTurnedByAQuarter ──────────┴─► VisibleSizeIsTurned
  the PDF /Rotate entry;                                        │
  the viewer turns it                                           ▼
                                                    Width / Height / StoredSize
```

`PdfPage.cs:342-363`. So "the size of this page" already has three defensible readings, and a
fourth once `/CropBox` is considered — a viewer shows the crop box, not the media box.

The library also already knows how to turn a page into a form XObject: `PdfFormXObject.cs:60-150`
does resources, `/BBox`, the `/Rotate` matrix, filter preservation and the stream move. It is
hardwired to an *external* document through `PdfImportedObjectTable`.

And the destination machinery built for issue #461 —
`Pdf.Advanced/PdfNamedDestinations.cs`, `DetachImportedDestinations`, the deferred-resolution pass —
already walks every place a destination can hide. Resizing needs the same walk with a different verb.

## Goals / Non-Goals

**Goals:**

- Resize a page **in the document that holds it**, with no second document.
- Carry annotations across, geometry and all.
- Carry link destinations across, wherever in the document they point from.
- Make the existing wrong-answer path (`page.Size = A5` on a page with content) loud instead of silent.
- Keep `page.Rotate = 90` as the free, lossless way to turn a page. Resizing must not become the
  only way to change orientation.
- Be idempotent in effect: resizing twice must not nest wrappers.

**Non-Goals:**

- **Tagged PDF.** Moving page content into a form XObject breaks the `/StructParents` mapping into
  the structure tree. Refused rather than attempted — see decision 10.
- **Reflowing text.** A resize scales what is drawn. It does not re-wrap paragraphs. That is
  MigraDoc's job, at authoring time, through `PageSetup`.
- **Resizing a signed or encrypted document.** Refused, not attempted.
- **`/DA` font sizes in form fields.** A widget's fixed `/Helv 12 Tf` will not scale, so field text
  comes out proportionally large. Auto-size (`0 Tf`) is fine. Documented limitation.
- Importing `/Outlines`, `/StructTreeRoot` or `/AcroForm` semantics beyond coordinate fixes.

## Decisions

### 1. Wrap the content in a form XObject, do not prepend a `cm`

```
BEFORE                                    AFTER
┌─ page ───────────────┐                 ┌─ page ───────────────────────────────┐
│ /MediaBox [0 0 595 842]                │ /MediaBox [0 0 420 595]              │
│ /Contents ──► [bytes] │                │ /Contents ──► "q .7071 0 0 .7071 0 0 │
│ /Resources ──► {R} ◄──┼── shared with  │                cm /Fm0 Do Q"         │
│ /Annots   ──► [...]   │   other pages  │ /Resources ──► NEW {/XObject</Fm0 ►┐>}│
│ /Group    ──► (maybe) │                │ /Annots   ──► [... /Rect scaled]    ││
└───────────────────────┘                └─────────────────────────────────────┼┘
                                          ┌─ Form XObject ─────────────────────┘
                                          │ /BBox      [0 0 595 842]   ← source rect
                                          │ /Resources ──► {R}         ← same ref, untouched
                                          │ /Group     ──► copied from the page
                                          │ /Filter    ──► preserved
                                          │ stream     ──► [bytes] verbatim
                                          └────────────────────────────
```

The alternative — `Contents.PrependContent()` a `q … cm` and `AppendContent()` a `Q` — is about
twenty lines and looks cheaper. It is wrong on real files:

- **Real content streams have unbalanced `q`/`Q`.** An extra open `q` in the original swallows the
  trailing `Q` and the scale never unwinds; an extra `Q` tears the scale down mid-page and the rest
  draws at full size. `Do` on a form XObject saves and restores the graphics state implicitly
  (PDF 32000 §8.10.1), so an unbalanced `q` *inside* the form cannot escape it.
- `/BBox` preserves the existing crop. Content that fell outside the box stays outside once the
  page shrinks and slack appears around it. A `cm` prepend has no clip.
- Two resizes leave nested `cm`s that cannot be inspected or undone. A wrapper can be.

Sharing `/Resources` with other pages is safe here, unlike in `PdfResourcePruner`: the reference is
*moved* to the form and the page gets a fresh dictionary. Nothing shared is mutated. Set it through
`PdfPage.ReplaceResources` (`PdfPage.cs:562`) — `_resources` is cached, and writing
`Elements["/Resources"]` alone leaves a page that was asked for its resources earlier still
answering with the old ones.

Two entries must travel into the form or behaviour changes: **`/Group`** (a transparency group left
on the page no longer wraps the content that needed it) and, where present, a `/StructParent` — see
Non-Goals.

### 2. `/UserUnit` was considered and rejected as the primary mechanism

`/UserUnit` scales the interpretation of default user space. One dictionary entry — content,
annotation rectangles, destination coordinates and appearance streams all scale together because
nothing moves. It answers the annotation-and-link requirement perfectly and for free. The key is
already declared and unused (`PdfPage.cs:1018`).

It cannot be the primary path:

| | |
|---|---|
| PDF 1.6+ | `PdfDocument._version` defaults to `14` |
| viewer support | Acrobat and Ghostscript honour it; elsewhere it is patchy |
| aspect ratio | uniform scale only — cannot do A4 → Letter |
| orientation | cannot swap width and height |
| downstream tools | anything reading `/MediaBox` to choose paper still sees A4 |

A mechanism that is perfect sometimes and silently degrades otherwise is worse than one that always
works. Left out of this change; a `ResizeStrategy.UserUnit` opt-in fast path for the pure uniform
case remains available later.

### 3. Source rect is `CropBox ?? MediaBox`; arithmetic is done in unrotated space

A viewer shows the crop box. A page with an A4 media box and a trimmed crop box *is* the crop box to
anyone looking at it, and resizing relative to the media box gives a visibly wrong result.

`/Rotate` is **kept as it is** and every calculation is done in unrotated media-box coordinates.
Annotation `/Rect` values and destination coordinates already live in that space, so one matrix
covers content and metadata alike. Baking the rotation into the form matrix and zeroing `/Rotate`
is tidier in the abstract, changes what other tools see, and buys nothing.

### 4. The size setters throw rather than resize

```
page.Size = PageSize.A5
        │
        ├── /Contents absent or empty  ──► rebox, exactly as today
        │
        └── /Contents has bytes        ──► InvalidOperationException
```

Not "silently do the resize", for two reasons. A resize has irreducible parameters — fit mode,
alignment, margin, auto-rotate, whether to chase destinations — and a property setter cannot take
them, so "just do it" buries a policy choice in an assignment. And it turns an O(1) assignment into
a document-wide mutation touching every other page's links, which is not property-setter behaviour.

`PageFitMode.None` is what the old behaviour becomes when asked for explicitly, so the throw is
really *"say which one you meant"* and there is always a one-line answer to it.

**Trap:** do not probe `page.Contents` to decide. The getter builds a `PdfContents` and rewrites
`/Contents` into array form as a side effect — the same hazard `PdfResourcePruner` had to route
around. Read `Elements[Keys.Contents]` raw.

### 5. Orientation stays two verbs

```
"make this landscape"
        │
        ├── turn the paper       page.Rotate = 90 — exists, lossless, free,
        │                        annotations and links untouched
        │
        └── reshape the box      Resize(A4, Landscape) — letterboxes: content
                                 shrinks to 70.7% with wide side margins
```

For a page that already exists, the first is nearly always the intent. Scaling A4 portrait into an
A4 landscape box is technically correct and almost never wanted. `AutoRotate` bridges them — when
source and target are of opposite aspect, turn the content a quarter rather than letterbox it. That
is Ghostscript's `-dAutoRotatePages`, and it is the right default for normalising a batch of mixed
scans.

### 6. Annotations: `/Rect` carries the appearance; geometry arrays do not

`/AP` appearance streams need no work. The viewer maps the appearance `/BBox` through its `/Matrix`
into `/Rect` (PDF 32000 Algorithm 8.1), so scaling `/Rect` scales the appearance. Only annotations
carrying their own geometry need per-subtype handling:

| subtype | array |
|---|---|
| Line | `/L`, `/CL` |
| Polygon, PolyLine | `/Vertices` |
| Ink | `/InkList` (array of arrays) |
| Highlight, Underline, StrikeOut, Squiggly, Link | `/QuadPoints` |
| Square, Circle | `/RD` |

An unknown subtype gets its `/Rect` scaled and nothing else — which is right for anything whose
appearance is an `/AP`, and the failure mode for anything else is a misplaced decoration, not a
corrupt file.

### 7. Destinations are swept document-wide

A destination targeting the resized page can live in four places:

```
every page's /Annots  ──► /Dest, or /A << /S /GoTo /D >>
/Outlines tree        ──► /Dest, or /A
catalog /Names /Dests name tree, and the legacy /Dests dictionary
catalog /OpenAction
```

Gate on `/S` being `/GoTo`, as `DetachImportedDestinations` was taught to for #461 — a `/GoToR`
names a page in *another* file and must not be rewritten.

Five of the eight destination forms carry coordinates:

| form | scales |
|---|---|
| `[p /XYZ l t z]` | `l` and `t` by the matrix. **`z` is left exactly as it is** |
| `[p /FitR l b r t]` | all four |
| `[p /FitH t]`, `[p /FitBH t]` | `t` |
| `[p /FitV l]`, `[p /FitBV l]` | `l` |
| `[p /Fit]`, `[p /FitB]` | nothing |

**The `/XYZ` zoom is not scaled.** An earlier draft of this design had it scaled inversely, on the
reasoning that a destination should show text at the size it showed before. That is wrong, and the
enlargement case shows why:

| | shrink A4 → A5 | enlarge A5 → A4 |
|---|---|---|
| leave `z` | the destination shows at 100%, like the rest of the shrunk document | the destination shows bigger — which is the reason the document was enlarged |
| invert `z` | the destination jumps to 141%, a magnification nothing else in the document uses | the destination shrinks back to the original apparent size, undoing the enlargement at the moment the reader jumps to what they wanted to read |

A destination is a view into the document, not a promise about physical text size. After a resize
the whole document reads at a new scale and a destination should read at that scale too. Inverting
does not preserve the author's intent, it fights it.

It is also the simpler implementation, and it makes `z` of `0` or null — "keep the current zoom",
which is what Word, hyperref and Acrobat's own bookmark writer emit, and therefore the great
majority of real destinations — need no special case at all.

`ScaleDestinations = false` exists so that `ResizePages`, which resizes everything uniformly, can
do one sweep at the end rather than N.

### 8. The wrapper is marked, so a second resize adjusts rather than nests

The form carries a private key naming it as a resize wrapper and recording the source rect. A
resize of a page whose `/Contents` is exactly the wrapper invocation and whose form carries the
marker **rewrites the `cm` matrix** against the recorded source rect instead of wrapping again.
A4 → A5 → A4 therefore returns the original matrix, not three levels of nesting.

Anything at all unexpected in the content — extra operators, a second content stream, a missing
marker — falls back to wrapping. Nesting is correct, merely wasteful; a wrong in-place rewrite is not.

### 9. Encrypted or signed documents are refused

Resizing invalidates any digital signature over the document, and rewriting content streams in a
document that is encrypted needs the security handler in the loop. Both are detected up front —
a `/Sig` field in `/AcroForm`, or `_securityHandler` present — and `Resize` throws rather than
producing a document whose signature silently no longer verifies.

### 10. A tagged document is refused

The wrap moves page content into a form XObject. The page's `/StructParents` indexes that content's
marked-content sequences into the structure tree, and once the content lives in a form the mapping
needs a `/StructParent` on the form and the parent tree rewritten to match. Getting that right is a
feature of its own.

The reason to **refuse** rather than proceed and document is that this failure is invisible. A page
that comes out looking perfect while its accessibility tree silently no longer describes it is worse
than one that will not resize: nothing in the rendered output, the byte size or a golden image shows
the damage, and the caller finds out from a screen-reader user.

So a document whose catalog holds `/StructTreeRoot` throws, alongside the signed and encrypted
cases. Supporting it properly stays available as a later change; the refusal is what keeps that
option open rather than shipping a quiet corruption first.

### 11. Two presets, because the migration string is otherwise long

`PageResizeOptions.Default` — `Fit`, centred — and `PageResizeOptions.Crop` — `None`, top-left.

The second exists because the behaviour being taken away needs a one-line replacement, and
`new PageResizeOptions { Fit = PageFitMode.None, Alignment = … }` is not one.

Note that `Crop` is deliberately **not** a faithful reproduction of the old setter. The old one
wrote `MediaBox = (0, 0, w, h)` and PDF's origin is bottom-left, so it kept the *bottom* left region
and cropped the heading off the top:

```
      A4 page              what page.Size = A5 kept
      ┌─────────────┐      ┌─────────────┐
      │ Heading     │      │░░░░░░░░░░░░░│  ← cropped away
      │ body text   │      │░░░░░░░░░░░░░│
      │ body text   │      ┌───────┐░░░░░│
      │ body text   │      │body   │░░░░░│  ← kept
      │ footer      │      │footer │░░░░░│
      └─────────────┘      └───────┴─────┘
```

Nobody wants that; it is an artefact of the origin, not a choice anyone made. `Crop` anchors
top-left. The migration note says so rather than offering bug-compatibility.

## Risks / Trade-offs

- **The destination sweep makes a page-scope call document-scope.** `page.Resize(...)` reaching
  across every page and the outline tree is an altitude violation. The alternative — resizing a page
  and leaving every link to it pointing at the wrong place — is worse. `ScaleDestinations = false`
  is the escape hatch.
- **Cost on large documents.** The sweep is O(pages + outline nodes + name-tree entries) per resized
  page. `ResizePages` amortises it to one pass; a loop of `page.Resize` does not. Worth saying so in
  the XML docs.
- **Tagged PDF degrades silently.** A structure tree that no longer maps to the content is not
  detectable from the output by eye. Consider refusing, or at least warning, when
  `/StructTreeRoot` is present — flagged as an open question for implementation.
- **Inline images.** Unlike `PdfResourcePruner`, this change never parses content, so a `BI` in the
  stream is harmless here. Worth stating, because the neighbouring code bails on it.
- **`/BBox` clipping is a behaviour change on malformed input.** A page whose content deliberately
  drew outside its own media box was already being clipped by the viewer, so the visible result is
  the same — but a tool that read the raw content and ignored the box would now see a clip. Judged
  correct rather than risky.
