Group 1 is the layout change and carries all the risk; it lands with no drop cap in sight and must
leave every existing document identical. Groups 2 and 3 are the feature.

## 1. A measure that varies by line

- [x] 1.1 Pin the current output first: lay out several blocks through `XTextFormatter` — justified,
      centred, right-aligned, multi-column, indented, truncated with an ellipsis — save the
      document, and keep the bytes. Without this, "unchanged when unused" is an intention. The
      gradient work in `fix-drawing-gaps` did the same and it is what caught the one thing that
      moved. **17 arrangements**, pinned in `Assets/Layout/formatter-baseline.txt` — a checked-in
      file rather than a string literal, which is what #85 had to fix in the gradient pin.
- [x] 1.2 In `CreateLayout`, replace the `columnWidth` and `lineStart` locals with a query answered
      from the line's top and height, returning the same pair every time. No behaviour changes yet;
      this is the shape of the change on its own, so that anything it breaks is visible before a
      drop cap is added to the picture. `MeasureOfLineAt` returns a `LineMeasure` of start and
      width; the width is carried to the drawing pass on `Block.LineWidth`.
- [x] 1.3 Re-run 1.1. The bytes must be identical. If they are not, the difference is in this step
      and nowhere else, which is the whole reason for doing it alone. **Identical**, all 17.
- [x] 1.4 Give the justified-line blank-width calculation the **line's** measure rather than the
      column's. This is the one place a careless change produces text that is subtly ragged rather
      than obviously broken, so it gets a test of its own that reads the drawn positions. Code done
      here; the test needs something that narrows a line, so it is written in 2.5 where one exists.
- [x] 1.5 Check what `ApplyEllipsis` measures against, and give it the line's measure too if a
      truncated line can fall inside a narrowed region. Answerable by reading the method. It
      measured to the right edge of the *column*; it now measures to the right edge of the line.

## 2. The drop cap

- [ ] 2.1 Add the type carrying the cap's font, its depth in lines and its gutter, and the
      `XTextFormatter.DropCap` property. Depth in **lines**, not a font size: lines are what the
      surrounding text is measured in, and a size infers a fractional depth that has to be rounded
      somewhere the caller cannot see.
- [ ] 2.2 Reserve the rectangle — the cap's width plus the gutter, by the depth in line heights —
      and narrow every line whose **box** overlaps it. By the box and not the baseline: a line whose
      baseline falls below the reserved depth still has ascenders that would collide.
- [ ] 2.3 Draw the cap with its foot on the last reserved line's baseline, sized so its cap height
      spans from the first line's ascent to that baseline.
- [ ] 2.4 Take the cap's left edge and reserved width from `XGraphicsPath.AddString` where
      `GlobalFontSettings.GlyphOutlineProvider` is registered, and from `MeasureString` where it is
      not. **Test the unregistered path as its own case**: a drop cap that needs a backend seam
      would be a worse failure than the one it replaces, and an untried fallback is not a fallback.
- [ ] 2.5 Test the requirements that catch the ways this goes wrong quietly: the first character
      appears once and not twice, every later word appears exactly once, and the fourth line starts
      at the block's left edge.
- [ ] 2.6 Test the edges — text shorter than the cap is deep, and an empty string — and record the
      choice made for each rather than leaving it to whatever the code happens to do.
- [ ] 2.7 Rasterize a page with a drop cap in a serif face and look at it. Flush is a thing the eye
      judges and no assertion does; that is how the four gaps in `fix-drawing-gaps` were found.

## 3. Make the demo tell the truth

- [ ] 3.1 Replace `MagazineDemo`'s measuring loop with the property. The loop is currently the most
      instructive thing on the page and it teaches a workaround as though it were a technique.
- [ ] 3.2 Update `Shows` on `MagazineDemo`, and the demo's remarks, which explain the arithmetic
      that is about to stop being necessary.
- [ ] 3.3 Update `docs/specs/demonstration-app.md`, which lists the drop cap among the things
      computed by hand.
- [ ] 3.4 Re-run the demo smoke tests. `Magazine` declares two pages; a drop cap that reserves the
      wrong depth repaginates it and the test will say so.

## 4. Close out

- [ ] 4.1 Add a line to the release notes. Additive public API, no signature changes.
- [ ] 4.2 `./ci-build.ps1` clean and `dotnet test` green on both target frameworks.
- [ ] 4.3 Note in the change what shape the per-line measure took, so that `shape-side-wrap` extends
      it rather than inventing a second one.
