## Context

This is the largest of the three typographic follow-ups and the only one that changes public DOM
surface. It is also the one with a seam already cut for it.

`Area` is abstract, has exactly one concrete implementation, and its central method is:

```csharp
internal abstract Rectangle GetFittingRect(XUnit yPosition, XUnit height);
```

That signature only earns its keep for an area that is not a rectangle — for `Rectangle` it returns
itself, narrowed to the requested band. `ParagraphRenderer` already calls it once per line, in three
places (formatting, re-formatting after a break, and when a word forces a new line). An `Area` that
returns a narrower rect where an obstacle overlaps makes text flow beside that obstacle with no
change to the line-breaking loop.

Two things in the surrounding code have to be dealt with before that can be trusted:

- `Rectangle.GetFittingRect` returns `null` past the bottom of the area, and the code carries a
  standing `// BUG: Code removed because null is not handled in caller`, plus a defensive
  `if (fittingRect == null) GetType();` in `ParagraphRenderer`. A non-rectangular area returns null
  in more situations — including a band entirely blocked by an obstacle — so the null path stops
  being rare.
- `Rectangle.Unite` is commented "This implementation is of course not correct, but it works for our
  purposes". It takes the bounding box of two areas. For rectangles that is the answer; for an area
  with a hole in it, it silently fills the hole.

`TopDownFormatter` is where floating is decided. Three lines ask `Floating != None`, and they are
what places an element after a shape rather than beside it.

## Goals / Non-Goals

**Goals:**

- Text flows beside a shape, on a side the document chooses.
- `WrapFormat`'s four distances hold text off on all four sides.
- The line-breaking loop in `ParagraphRenderer` is not rewritten.
- A document that asks for no side wrap lays out byte-identically.

**Non-Goals:**

- **Contour wrapping.** Following the outline of an image's subject, or the diagonal of a shape, is
  a different feature: it needs the shape sampled per line rather than tested as a box. The
  bounding box is what every word processor did for twenty years and it is what makes a sidebar
  possible.
- **A shape that spans a page break.** A floating shape that does not fit is moved whole to the next
  page today. Splitting the obstacle across two areas means the second page's area has to inherit
  part of it, and page-break handling in this renderer is the part with the most edge cases.
- **Wrapping beside a table.** `TableRenderer` does not use `GetFittingRect`; it has its own
  geometry. A table beside a shape is a separate problem.
- **Overlap resolution between two floating shapes.** Two shapes on the same lines are the caller's
  arrangement to get right; the area subtracts both and the text goes where it can.

## Decisions

### 1. An `Area` that subtracts obstacles, not a rewritten paragraph renderer

The whole feature hangs off one new class: an `Area` holding a base rectangle and a list of obstacle
rectangles, whose `GetFittingRect(y, height)` returns the widest clear span in that band.

This is the decision the rest follows from, and the reason it is affordable at all. The alternative
— teaching `ParagraphRenderer` about shapes — would put layout knowledge into the loop that breaks
lines, and that loop is already the most intricate code in the renderer.

**Widest clear span, not all clear spans.** A band interrupted by an obstacle in its middle has two
clear spans. Returning one rectangle means the text uses the wider and leaves the other empty. That
is a real limitation and the right first answer: `GetFittingRect` returns a `Rectangle`, and
changing it to return a set would touch every caller and every implementation. It is also what a
reader expects — text that hops across a pull quote and back mid-line is unreadable.

### 2. `WrapStyle` gains `Left`, `Right`, `Largest` and `Both`

`Left` and `Right` say which side the *text* runs down, which is the convention worth stating
explicitly because the opposite reading is equally natural. `Largest` picks whichever side has more
room, which is what a caller wants for a shape positioned by alignment rather than by measurement.
`Both` allows text on either side of a shape that stands in the middle of the measure — accepted
knowing that decision 1 means only one side is filled per line.

**Alternative considered — reuse the existing `Through`.** Rejected. `Through` currently means "the
text ignores this shape", which is a real behaviour some documents rely on for overlapping artwork.
Redefining it would change existing output silently.

The DOM's serialisation is generated. New enumeration values must round-trip through MDDDL, and the
generator gets checked rather than assumed — `docs/specs/generated-serialization.md` is the note to
read first.

### 3. The null path gets fixed before it gets exercised

`GetFittingRect` returning null is rare today and becomes ordinary here: a band fully blocked by an
obstacle has no clear span at all. The right behaviour is for the paragraph to advance past the
obstacle rather than to fail, and the current callers do not do that reliably — one of them contains
a `GetType()` call whose only purpose is to be a breakpoint.

So: the null contract is established and the callers made to honour it **before** the obstacle area
is introduced, as its own task group, with tests written against the plain `Rectangle`. That way a
failure in the new area is a failure in the new area.

### 4. `Unite` is left alone and kept away from obstacles

The bounding-box `Unite` is wrong for an area with holes. Rather than fix it — it is used for
render-info geometry where the bounding box is what is wanted — the obstacle area does not
participate: uniting anything with an obstacle area yields a plain rectangle covering both, which is
what its callers already assume.

Written down because the alternative is a future reader assuming `Unite` is exact and building on it.

## Risks / Trade-offs

- **This is layout, and layout regressions are silent.** A change here can move text on every
  document the library produces → The obstacle area is a *new* implementation; `Rectangle` is
  untouched. A document with no side wrap never constructs one, and that is pinned by saving and
  comparing bytes for a corpus of documents before and after.
- **The null path becomes ordinary and the callers were never ready for it** → Its own task group,
  landing first, tested against the existing `Rectangle`.
- **Justified text beside an obstacle stretches to the wrong measure** if the blank-width
  calculation reads the area's width rather than the line's → It reads the fitting rect today, which
  is the right thing; the risk is a helper that does not. It gets a test that reads drawn positions
  rather than trusting the reading.
- **New enumeration values break older readers of MDDDL** → Additive within this fork, noted in the
  release notes. The values are only written when a document asks for them.
- **A shape taller than the area it is placed in** would produce an obstacle extending past the
  bottom, and page-break handling is out of scope → Detect it and fall back to `TopBottom` for that
  shape rather than producing an area whose obstacles outlive it. A predictable degradation beats a
  wrong one.

## Open Questions

- **Does `drop-cap-layout` land first, and if so what shape did its per-line measure take?** The two
  are different engines and will be different code, but they should not be different ideas. If that
  change is done, read it before starting here.
- **Is a drop cap in MigraDoc a caller of this same machinery?** It should be — a drop cap is an
  obstacle at the top-left of a paragraph — and if so, the obstacle area wants to be reachable from
  `ParagraphRenderer` and not only from a floating shape. Worth settling before the area's
  constructor is fixed.
- **What does `Largest` do when the two sides are equal?** Any answer works provided it is stable
  across formatting passes; an unstable one produces text that moves between the measuring pass and
  the rendering pass.
## Settled during implementation

### `DistanceTop` and `DistanceBottom` become the gap above and below the obstacle

For a `TopBottom` shape they are the margins of the element, and they stay that. For a side-wrapped
one they grow the obstacle vertically, so that a line whose box would otherwise clear the shape by a
hair is pushed past it instead.

Chosen because it is the reading that makes all four distances mean something, which is the point of
the change: two of them have never meant anything at all. A side-wrapped shape has no "above" and
"below" in the `TopBottom` sense — there is no element placed after it to be held off — so keeping
the old meaning would leave them inert exactly where they are asked for.

The obstacle handed to the area is therefore the shape's rectangle grown by all four distances, and
the area needs to know nothing about wrapping at all.

## Open Questions
