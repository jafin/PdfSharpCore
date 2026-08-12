## Context

`TrimMargins` is a whole feature that arrived with the upstream library and has never been exercised
here. Three pieces cooperate:

| piece | where | what it does |
|---|---|---|
| the offset | `XGraphicsPdfRenderer.BeginPage` | `DefaultViewMatrix.TranslatePrepend(left, -top)`, so the caller's origin is the trim corner |
| the height | `XGraphicsPdfRenderer.BeginPage` | `PageHeightPt += top + bottom`, so `WorldToView` still lands on the right row |
| the boxes | `PdfPage.PrepareForSave` | writes `MediaBox`, `CropBox`, `BleedBox`, `TrimBox`, `ArtBox` |

The comment above the box arithmetic records where the numbers came from — "the values InDesign set
for an A4 page with 3mm crop margin at each edge" — which is worth keeping in mind: they were copied
from a file, not derived from the specification.

One constraint is already enforced: `BeginPage` asserts `PageUnit == Point` when trim margins are
set, with the comment "Ohter cases nyi". Nothing here lifts that.

The starting position for this change is therefore unusual. There is almost nothing to build. The
work is to find out exactly what the existing behaviour is, write it down, and guard it — and to
settle two questions the original code answered by accident.

## Goals / Non-Goals

**Goals:**

- Every observable effect of `TrimMargins` is specified and tested, including the five boxes.
- A caller reading the demos can see how to bleed an image and can copy it.
- The crop-mark question is answered rather than left as an unexplained `BleedBox == MediaBox`.
- Whether MigraDoc can bleed is answered, and the answer is written down.

**Non-Goals:**

- **Other printer's marks.** Registration targets, colour bars and slug areas are a different
  feature with a different audience. Crop marks are in scope only because `BleedBox == MediaBox`
  makes them impossible, which is a question this change cannot avoid.
- **Non-point page units with trim margins.** The assert stays. Lifting it means auditing every
  `WorldToView` caller and is unrelated to bleeding.
- **Changing how the origin is placed.** The trim corner is the right origin: it means a caller who
  ignores bleed entirely writes exactly the same code as on an untrimmed page.

## Decisions

### 1. Specify what is there before touching any of it

The first task group writes tests against the *current* behaviour and the spec describes what those
tests find. Only then does anything change. This is the reverse of the usual order and is right here
because the risk is not "will the new code work" but "is the old code doing what we think".

**Alternative considered — write the spec from the PDF specification and fix the code to match.**
Rejected as the first step. `BleedBox == MediaBox` is arguably wrong, but a change that alters the
boxes *and* has no baseline to compare against is a change nobody can review. Pin first, then argue.

### 2. Crop marks: the question the design must settle, with a recommendation

Crop marks are drawn in the area between the bleed and the sheet edge, and tell the trimmer where
to cut. Today there is no such area: `BleedBox` is the whole sheet.

The correct nesting is `MediaBox ⊇ BleedBox ⊇ TrimBox`, with the bleed extending past the trim by
the bleed amount and the media extending past the bleed by enough for marks — commonly a further
3–5 mm.

Three options:

1. **Leave `BleedBox == MediaBox` and draw no marks.** Honest, costs nothing, and the file is still
   valid: a plate-making tool derives the bleed from `TrimBox` and `BleedBox` regardless. A caller
   who wants marks draws them.
2. **Give `TrimMargins` a separate mark allowance**, so `MediaBox` grows beyond `BleedBox`, and add
   `PdfPage.DrawCropMarks()` or similar. Correct, and a breaking change to the boxes of every page
   that sets trim margins today.
3. **Split the two**: keep `TrimMargins` writing exactly the boxes it writes now, and add a
   *separate* opt-in — a `MarkMargins`, or a `PageMarks` option — that grows the media box and draws
   the marks. Nothing that works today changes; a caller who wants marks asks for them.

**Recommend option 3.** It is the only one that answers the question without a breaking change, and
it separates two things that are genuinely separate: how far the artwork bleeds past the trim, and
how much sheet the press needs around it.

Whichever is chosen, the spec records the nesting rule so the next reader does not have to work out
from a comment whether `BleedBox == MediaBox` was deliberate.

### 3. MigraDoc bleeds through the renderer, not through `PageSetup`

`PdfDocumentRenderer.RenderPages` makes each page itself and sets only width, height and
orientation. But `DocumentRenderer.RenderPage(gfx, pageNumber)` is public, so a caller can make a
page, set `TrimMargins`, open an `XGraphics` on it and render into that. The layout is unaffected —
MigraDoc lays out to the trimmed size, and the origin shift is invisible to it.

That composition should be **documented and tested**, not replaced. Adding a bleed to `PageSetup`
means a new DOM property, MDDDL serialisation, the DOM source generators, and a decision about what
a bleed means for headers and footers — a large change for a feature whose whole point is content
that ignores the text area.

**Alternative considered — teach `RenderPages` to copy a bleed from `PageSetup`.** Deferred, not
rejected. If the documented route proves to be what everyone writes, promoting it is a small follow-
up with the tests already in place.

### 4. The demo shows the trim edge

A bled page rasterizes to an image where nothing marks where the paper will be cut, so the
demonstration is invisible. The demo draws a thin rule on the trim boundary and labels it, so the
reader can see the image running past it. That rule is part of the demonstration, not part of the
artwork — the demo says so on the page, in the way the other demos do.

## Risks / Trade-offs

- **The pinned behaviour may turn out to be wrong** in some detail — the box arithmetic was copied
  from an InDesign file → That is the point of pinning it first. A test that records wrong behaviour
  is a test that makes the wrongness visible and reviewable, which is better than the current
  position of no tests at all. Anything found is reported in the spec rather than quietly fixed.
- **A crop-mark option that grows `MediaBox` changes page dimensions**, which changes what
  `page.Width` reports and could surprise anything measuring pages → Option 3 keeps it opt-in, and
  the spec states the nesting so the effect is predictable.
- **The demo needs a trimmed page and the smoke test pins page counts** → One page, no pagination;
  the risk is nil, but the demo is registered like any other so the smoke test covers it anyway.
- **`XGraphics` asserts point units with trim margins**, and a demo written in millimetres would
  trip it in Debug only → The demo uses points, and the spec states the constraint so the next
  caller meets it as documentation rather than as an assert.

## Open Questions

- **Which crop-mark option?** Recommended above (option 3), to be confirmed before task group 3
  starts. Task groups 1 and 2 do not depend on the answer.
- **Should `ArtBox` equal `TrimBox`?** It does today. `ArtBox` is meant to bound the *meaningful*
  content, which for a designed page is usually the trim. Probably right, worth stating deliberately
  rather than inheriting.
- **Does anything in the wild set `TrimMargins` and rely on `BleedBox == MediaBox`?** Unknowable, but
  it decides how loudly option 2 would have to be announced if option 3 is rejected.
