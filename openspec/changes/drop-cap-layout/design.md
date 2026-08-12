## Context

`XTextFormatter.CreateLayout` is one loop over blocks. Two locals decide where a line begins and
where it must break:

```csharp
double columnWidth = ColumnWidthWithin(rectWidth);   // once, for the whole layout
double lineStart   = IndentOf(true);                 // first line of a paragraph, or not
...
if (!LineBreak || x + width <= columnWidth || x == lineStart)   // the break test
```

Everything else — columns, justification, ellipsis, vertical overflow — is already expressed in
terms of those two. That is the good news: a drop cap does not need a new layout engine, it needs
those two values to depend on the line's vertical position.

The block loop tracks `y` as it goes, so the position is already in hand at every point where either
value is read. There are three such points: the start of the layout, an explicit line break, and an
implicit one.

MigraDoc has the same shape and a better seam — `Area.GetFittingRect(yPosition, height)` is abstract
with one concrete implementation — but also page breaks, floating shapes, `KeepWith` and a
`GetFittingRect` whose null return is documented as unhandled by its callers. That is the subject of
`shape-side-wrap`, not this change.

## Goals / Non-Goals

**Goals:**

- A drop cap is one property, not a measuring loop.
- The line measure can vary with vertical position, in a form the side-wrap change can reuse.
- Output is byte-identical for every caller who does not ask for a drop cap.
- The cap is placed the way a typesetter would place it, which means by its ink.

**Non-Goals:**

- **MigraDoc.** Same feature, different engine, much larger blast radius. Deliberately second.
- **Raised caps, sunk caps, contoured wrap.** A raised cap sits *on* the first line rather than into
  it and needs no reserved room at all; contouring to the glyph's diagonal (the classic 'A' problem)
  needs the outline sampled per line. Both are worth having and neither is needed to stop callers
  writing measuring loops.
- **Automatic depth from the cap's font size.** The caller says how many lines deep; the formatter
  sizes the cap to fit. The inverse — infer the depth from a font size — reads well and produces
  fractional depths that have to be rounded somewhere, and the rounding is the caller's business.
- **Kerning the body text against the cap.** Out of scope here as everywhere else; see
  `pdfkit-text-parity.md`.

## Decisions

### 1. The measure becomes a function of the line's top, not a constant

Replace the two locals with a pair of queries answered per line:

```text
LineMeasure(yTop, lineHeight) -> (start, width)
```

For a layout with no drop cap this returns the same `(lineStart, columnWidth)` every time and the
loop behaves exactly as it does now — which is what makes "byte-identical when unused" achievable
rather than hoped for.

**Alternative considered — a list of per-line indents supplied by the caller.** Rejected: the caller
does not know how many lines there will be until the text is laid out, which is the same circularity
`MagazineDemo`'s loop exists to break.

**Alternative considered — lay out twice, once to count lines and once to place them.** Rejected: it
doubles the work, and the second pass can produce a different line count from the first when the
narrowing changes where words break, which does not converge in general.

### 2. The reserved area is a rectangle, and the drop cap owns it

The cap reserves a box: as wide as the cap's ink plus a gutter, as deep as `Lines` line heights. Any
line whose vertical extent overlaps that box gets a `start` moved right and a `width` reduced by the
same amount.

Overlap is by the line's **box**, not its baseline: a line whose top sits inside the reserved depth
but whose baseline falls below it still has ascenders that would collide.

This keeps the per-line query trivially cheap — one rectangle test — and makes the extension point
for `shape-side-wrap` obvious: several rectangles instead of one, and a side to choose.

### 3. The cap is measured by ink where a provider is registered

`MeasureString` reports the advance. A drop cap set flush left by advance is visibly inset, because
the glyph's left side bearing is empty space that a body letter needs and a display letter does not.

Where `GlobalFontSettings.GlyphOutlineProvider` is set, build the cap with
`XGraphicsPath.AddString`, take its bounds, and use those for both the left edge and the reserved
width. Where it is not, fall back to `MeasureString` and accept the inset.

**This must not become a hard dependency.** The seam exists so that the core package carries no font
dependency; a drop cap that throws without a backend registered would be a worse failure than the
one it replaces. The fallback is tested as its own case, not left as an untried branch.

### 4. The cap's baseline sits on the last reserved line's baseline

A cap three lines deep has its baseline on the third line's baseline. That is the convention, it is
what makes the cap look set *into* the text rather than floating above it, and it is computable
from the line height and the ascent the formatter already has.

The cap's size follows from the depth: it is scaled so its ink spans from the **cap height** of the
first line to the baseline of the last. A caller who wants a specific size instead can set the font
and let the depth follow — but the property takes lines, because lines are what the surrounding text
is measured in.

The head is the half that is easy to get wrong, and this is the second answer to it. A line is
placed by the top of its **box**, which stands an ascent above the baseline, and the letters in that
line reach only a **cap height** — the ascent keeps room above them for accents and for the tall
lowercase. Hang the cap from the box and it stands clear of the letter beside it by the difference,
which is a fifth of the body size in Liberation Sans and a third in Source Code Pro; at four times
the body size that is a gap nobody has to measure to see. So the two heights the cap spans are

```text
first line's baseline − first line's cap height        ← the head
last spanned line's baseline                           ← the foot
```

and neither is `lineHeight × lines`, which is the third wrong answer and matches neither end.

## Risks / Trade-offs

- **A per-line query in the inner loop is a per-line cost** → It is one rectangle test against at
  most a handful of obstacles, against a loop that already calls `MeasureString` per block. Not
  measurable beside what is there.
- **Justification interacts with a narrowed measure**: the blank-width calculation divides the room
  left over, and "room left over" now differs by line → The calculation already takes the line's
  width as an argument; it must be given the *line's* measure rather than the column's. This is the
  one place a careless change silently produces ragged justified text, so it gets its own test.
- **Byte-identity when unused is a claim, not an observation** → Pinned the way the gradient work
  pinned it: a document laid out through the formatter, saved, and compared before and after.
- **The cap's ink bounds depend on which backend is registered**, and the two subdivide curves
  differently → Bounds only, never coordinates, and a tolerance of a point. The same discipline
  `glyph-outlines` settled on.
- **A drop cap deeper than the text is a caller error with no obvious right answer** → Draw the cap
  and let the text be short; do not throw. A page with a big letter and two words on it is visibly
  wrong in the way the caller can act on, which is the standard `fix-drawing-gaps` set.

## What the per-line measure turned out to be

Written down for `shape-side-wrap`, which needs the same idea and must extend this rather than
invent a second one.

```csharp
readonly struct LineMeasure { double Start; double Width; }

LineMeasure MeasureOfLineAt(double yTop, bool firstLineOfParagraph, double columnWidth, int column)
```

Both numbers are measured **from the left edge of the column**. `Width` is therefore the position of
the line's right limit, not the room between the two — the room is `Width - Start`. That is not the
obvious reading, and it is the one the existing code already assumed: `HorizontalAlignLine` computes
`layoutWidth - LineIndent - totalWidth`, and the justification and ellipsis arithmetic subtract the
indent in the same way.

The consequence is worth stating plainly. **Reserving room on the left moves `Start` and leaves
`Width` alone.** A drop cap therefore never changes `Width` at all. Reserving on the right — a shape
with text down its left side — is the case that moves `Width`, and it has no exercise here.

So two of the corrections in task group 1 are unexercised by this change and are not dead: giving the
justified blank-width calculation and `ApplyEllipsis` the line's measure rather than the column's is
a no-op for a left-side reservation and is load-bearing for a right-side one. `shape-side-wrap`
is where they earn their keep, and where they want a test that reads drawn positions.

The measure is carried from the layout pass to the drawing pass on `Block.LineIndent` and
`Block.LineWidth`, because the drawing pass aligns and justifies each line a second time.

## Open Questions

- **What happens when the block has fewer lines than the cap is deep?** Draw and let it overhang is
  the proposed answer, but a reader may prefer the cap to shrink. Cheap to change once something
  draws it.
- **Should the gutter default to a fixed measure or to a fraction of the cap's width?** A fraction
  scales with the cap; a fixed measure matches the body text's word spacing. Decide against a
  rasterized page rather than in the abstract.
- **Does the ellipsis pass need to know about the reserved area?** `ApplyEllipsis` measures against
  `columnWidth`. If a truncated line falls inside the cap's depth, it must measure against that
  line's narrower measure instead. Answerable by reading the method; not worth blocking on.
