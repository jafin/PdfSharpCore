Group 1 fixes a defect that is reachable today and must land before anything can produce the case it
mishandles. Group 2 is a pure lift, shared with MigraDoc. Group 3 is the abstraction, group 4 wires
it into the formatter, group 5 makes it visible and closes the change out.

`FormatterLayoutPinTests` — seventeen arrangements — is re-run at the end of **every** group. A
layout regression is silent, and the whole compatibility claim of this change is that a block with
nothing reserved lays out byte for byte as it did.

## 1. The "no room" path, before anything can produce one

Landed on its own as PR #96, ahead of the rest of the change.

- [x] 1.1 Probe the loop before touching it: does a band with no room already occur? **It does.** A
      drop cap is scaled to its own depth and nothing holds its width to the measure, so a 30pt-wide
      area with a five-line cap draws five lines sixteen points past its own right edge, one word to
      a line, nothing thrown. The proposal had this down as latent; it is shipped. Corrected there
      rather than left to mislead.
- [x] 1.2 Give the measure a way to say the band has no room, and how far down the thing standing in
      it reaches. A start at or past its own limit is a different answer from a narrow line and
      cannot be expressed as one — the loop places the first block of a line whether it fits or not,
      which is right for an over-wide word and is what put the text outside the column.
- [x] 1.3 Write the termination guard **before** the skip, not after. iText7's shift-under-floats
      rule relies on a bounded box; `AllowVerticalOverflow` leaves none here, so nothing outside the
      skip can stop it. The floor of one line is what ends it, and makes the worst case equal to the
      line-at-a-time advance it replaces.
- [x] 1.4 Skip to the foot of the obstruction rather than a line at a time, so the number of tries is
      a fact about the obstruction and not about the line height. Where the skip runs out of layout,
      drop the text exactly as text past the last column is dropped.
- [x] 1.5 Test against the drop cap: text kept inside the column, text beginning below the cap,
      nothing lost on the way past, the move across to the next column, and both termination cases.
      **Six tests.**
- [x] 1.6 Re-run the pins. **Byte-identical**, all seventeen.
- [x] 1.7 Check whether `XTextSegmentFormatter` inherits any of this. **It does not** — it carries
      its own `CreateLayout` with no per-line measure in it, so it has neither the defect nor the
      fix. The proposal and design said it shared the loop; both corrected.

## 2. The one piece of arithmetic the two engines share

- [x] 2.1 Extract the tolerance-aware widest-free-span scan into `PdfSharpCore/Drawing.Layout/` as a
      pure static helper taking and returning **plain doubles**. No `Rectangle`, `XRect` or `XUnit`
      in the signature — that is what sidesteps the type mismatch between the engines. Public,
      because this repository deliberately carries no `InternalsVisibleTo` and MigraDoc must call it.
      `LineSpans.TryWidestFree`.
- [x] 2.2 A **pure lift**: same comparison operators, same tie-breaking, tolerance threaded as a
      parameter rather than hardcoded. Any tidying belongs in a later commit — tidying here is what
      would break the pin and make the cause hard to find. The spans are still sorted **in place**
      rather than copied, for the same reason: it is what the code did, and a per-line call in a
      layout loop should not allocate a second list. Said plainly on the parameter and pinned.
- [x] 2.3 Point `ObstructedArea.GetFittingRect` at it, wrapping the result back into a `Rectangle`.
      `BlockedSpansIn` stays: obstacle collection and band filtering are per-engine, because
      MigraDoc's obstacles are page-absolute and the formatter's are block-relative.
      **The "nothing is standing here" early-out stays in the area too**, which the first pass had
      folded into the scan: the scan judges a run against the tolerance and answers null for an area
      no wider than that, where the area answers with itself. They differ only for an area of no
      width, which is exactly the sort of case a pinned layout is made of. Pinned by a test of its
      own so a later tidy cannot quietly undo it.
- [x] 2.4 Verify against `ObstructedAreaTests` (now 21, which exercise the tolerance boundaries)
      **and** `MigraDocLayoutPinTests` (10 documents, 14 pages). **Byte-identical.** Plus 20 tests on
      the helper directly, since it is public API and an area cannot produce some of the inputs it
      has to accept: an obstacle hanging outside the line, obstacles in any order, one swallowed by
      another, and a caller passing nothing at all.

## 3. The abstraction

- [ ] 3.1 A band type: a top and a bottom, the **line box** rather than the baseline. A line whose
      baseline clears an obstacle can still have ascenders inside it.
- [ ] 3.2 An interval set with subtraction, and the free-span query answering with it. More general
      than either reference implementation — iText7 coalesces to one span, QuestPDF has no per-line
      seam at all — and chosen anyway so that taking the widest span is a policy in the loop rather
      than a property of the type.
- [ ] 3.3 An obstacle interface returning excluded intervals for a band, with a rectangle
      implementation. This is where an ellipse, a polygon and an `XGraphicsPath` become new
      implementations rather than a redesign.
- [ ] 3.4 Padding **on the obstacle** — `Inflate` for a rectangle. A margin is a fact about the thing
      being avoided, and two obstacles in one block can want different distances.
- [ ] 3.5 Unit-test the geometry with no formatter in sight: obstacle left, right, in the middle
      (two spans), spanning the full width, above the band, below the band, two at once, and
      touching an edge exactly. Correctness is cheap to establish here and expensive to debug
      through a rendered page.
- [ ] 3.6 Re-run the pins.

## 4. Wire it into the formatter

- [ ] 4.1 Give `XTextFormatter` a flow region: bounds plus obstacles, supplied by the caller.
- [ ] 4.2 Refuse an obstacle where `Rotation != 0`, with an error naming the frame obstacles are
      given in. Layout runs unrotated and rotation is a draw-time transform, so a layout-local
      obstacle costs nothing while a page-space one would inverse-rotate a rectangle into a quad —
      dragging the polygon implementation onto the critical path to support rectangles.
- [ ] 4.3 Make the drop cap an obstacle the formatter creates. Only the formatter can size it, since
      it scales the glyph to the line depth. `MeasureOfLineAt` loses its `_dropCap` branch **and**
      its `column == 0` test: with the reservation in layout coordinates, "first column only" falls
      out geometrically. `Reserved.X == 0` currently hides that choice, because column-local and
      layout-global coincide there.
- [ ] 4.4 Clip each obstacle to the column of the line being measured, so an obstacle straddling the
      gutter becomes two ordinary reductions with nothing to do about the gutter itself.
- [ ] 4.5 Measure `ApplyEllipsis` against the line it lands on rather than the column it sits in. It
      cannot be wrong while every line shares a limit and becomes wrong on the first narrowed last
      line — the same shape as the MigraDoc defect fixed in #92, where a later phase re-derived what
      an earlier phase already knew.
- [ ] 4.6 Test: both sides narrowed, an obstacle spanning two columns, ellipsis on a narrowed last
      line, a cap and an obstacle narrowing one line together, and the drop cap unchanged in a
      two-column layout.
- [ ] 4.7 Re-run the pins, and `DropCapTests` unchanged and green — the cap's behaviour must survive
      the change of mechanism untouched.

## 5. Make it visible, and close out

- [ ] 5.1 `MagazineDemo` drops its hand-split pull quote and becomes a caller.
- [ ] 5.2 `docs/specs/demonstration-app.md` says the formatter does not flow text beside a shape and
      is not going to, and that the hand-split quote is therefore not a gap. Both statements go.
- [ ] 5.3 Release notes.
- [ ] 5.4 `./ci-build.ps1` clean; `dotnet test -f net8.0` and `-f net10.0` **separately** — run
      together they report green and the host then crashes, which is the documented memory-pressure
      abort rather than a failure.
- [ ] 5.5 Rasterize a page per arrangement and **look at it**. On `shape-side-wrap` this caught a
      demo that demonstrated nothing — equal room either side of the shape — which no assertion
      would have flagged.
