# Spec — text feature parity with PDFKit

[PDFKit](https://github.com/foliojs/pdfkit) documents 25 options on its `doc.text()` method. This
records which of them PdfSharpCore already answers, which it answers only partly, and which are
missing, together with the work each gap needs. Reference is
[`lib/mixins/text.js`](https://github.com/foliojs/pdfkit/blob/f308aae92f1491b0e952545fc0fbbef561c40e9e/lib/mixins/text.js#L114)
and its companion `lib/line_wrapper.js` at the same revision.

This is the gap analysis and the plan. Items A1 and A2 are built, on
`feat/text-state-and-measurement`; everything else is still to do.

## Where parity has to land

PDFKit has one text call. `doc.text(str, x, y, options)` wraps the string, styles it, draws it, and
attaches the annotation, all from one options bag. PdfSharpCore has three layers and no single one
of them is the counterpart:

| layer | what it does | what it cannot do |
|---|---|---|
| `XGraphics.DrawString` (`Drawing/XGraphics.cs:1180-1242`) | emits one `Tj` for the whole string | no wrapping — a `\n` is drawn as a literal, not a break |
| `XTextFormatter` (`Drawing.Layout/XTextFormatter.cs:43`) | greedy word wrap into a rectangle | four options total: alignment, vertical alignment, overflow, line height |
| MigraDoc | a document model with paragraphs, styles and flow | not reachable from someone holding an `XGraphics` |

**The parity surface is `XTextFormatter`.** It is the closest analogue — a string, a rectangle, and
a bag of layout options — and it already lives in the PdfSharpCore assembly, so nothing here forces
a MigraDoc dependency on a caller who only wants to draw text on a page.

Two consequences run through the whole checklist:

- Options that are *text state* (`characterSpacing`, `wordSpacing`, `horizontalScaling`, `fill`,
  `stroke`, `oblique`, `baseline`) have to be settable on `XGraphics.DrawString` too, because
  `XTextFormatter` draws through it. They cannot be formatter-only.
- Every one of those also changes how wide a string is, so `MeasureString` has to see them. It
  currently cannot: `XGraphics.MeasureString(text, font, stringFormat)` validates its format
  argument and then discards it (`XGraphics.cs:1258` passes `XStringFormats.Default` on to
  `FontHelper`), and `FontHelper.MeasureString` ignores the parameter as well
  (`Drawing/FontHelper.cs:45`). Wrapping computed against an unspaced measurement of spaced text is
  wrong at every line end.

That is why item 1 below comes before everything else.

---

## Parity table

Status is against `XTextFormatter` / `XGraphics`. The MigraDoc column is what a MigraDoc user can
already reach, and is informational — a MigraDoc equivalent does not close a core gap.

| # | PDFKit option | core status | MigraDoc equivalent |
|---|---|---|---|
| 1 | `width` | **done** — `LayoutRectangle.Width` | `PageSetup` margins, indents |
| 2 | `height` | **done** — `LayoutRectangle.Height` + `AllowVerticalOverflow` (`XTextFormatter.cs:108`) | none — a `TextFrame` formats to `double.MaxValue` (`FormattedTextFrame.cs:132`) |
| 3 | `lineBreak` | partial — wrapping cannot be switched off in the formatter | n/a |
| 4 | `lineGap` | partial — `lineHeight` sets absolute height, not extra gap (`XTextFormatter.cs:143`) | `LineSpacing` + `LineSpacingRule`, 6 rules |
| 5 | `indent` | **missing** — a TODO since upstream (`XTextFormatter.cs:446`) | `ParagraphFormat.FirstLineIndent` |
| 6 | `indentAllLines` | **missing** (`XTextFormatter.cs:445`) | `ParagraphFormat.LeftIndent` |
| 7 | `paragraphGap` | **missing** | `SpaceBefore` / `SpaceAfter` |
| 8 | `ellipsis` | **missing** — overflow is a hard cut, no glyph | **missing** |
| 9 | `columns` | **missing** | **missing** — `PageSetup` has no column properties |
| 10 | `columnGap` | **missing** | **missing** |
| 11 | `characterSpacing` | **missing** as API — `Tc` is emitted only to fake bold (`PdfGraphicsState.cs:363,372`) | **missing**, marked unported at `Font.cs:276` |
| 12 | `wordSpacing` | **missing** — `Tw` appears nowhere in the repo | **missing** |
| 13 | `horizontalScaling` | **missing** — the only `Tz` written is a literal `100 Tz` in the *image* path (`XGraphicsPdfRenderer.cs:641`) | **missing**, `Font.cs:277` |
| 14 | `fill` | partial — always on, cannot be disabled | always on |
| 15 | `stroke` | **missing** — no `DrawString` takes an `XPen`; `Tr` 1 throws (`PdfGraphicsState.cs:258`) | **missing**, `Font.cs:266` |
| 16 | `oblique` | partial — a fixed 20° skew, reachable only as italic simulation (`Configuration.cs:57`) | **missing** |
| 17 | `baseline` | partial — `XLineAlignment` has 4 of the 6 canvas values | n/a |
| 18 | `underline` | partial — bound to `XFontStyle`, one style, font colour only | `Underline`, 7 styles (`enums/Underline.cs`) |
| 19 | `strike` | partial — same | `Strikethrough`, 7 styles |
| 20 | `rotation` | partial — `RotateTransform` reaches text via the CTM, but is not an option | `TextFrame.Orientation`, 90° steps only |
| 21 | `link` | partial — `PdfPage.AddWebLink` takes a page-space rect; no text-level shortcut | `Hyperlink`, `HyperlinkType.Web` |
| 22 | `goTo` | **missing** — no way to link to a name | `HyperlinkType.Local` + `BookmarkField` |
| 23 | `destination` | **missing** — `PdfNamedDestinations` is `internal` and read-only (`Pdf.Advanced/PdfNamedDestinations.cs:41`) | `BookmarkField` |
| 24 | `continued` | partial — `XTextSegmentFormatter` takes mixed runs in one call, but there is no resumable cursor | `FormattedText`, nestable |
| 25 | `features` | **missing** — `GlyphSubstitutionTable.Read()` is an empty `try` block (`Fonts.OpenType/OpenTypeFontTables.cs:1067`); no GPOS, no `kern` | **missing** |

Two of twenty-five are at parity.

---

## Checklist

Ordered so that each item's prerequisites come before it. Items marked **(blocker)** are depended on
by later ones.

### A — text state, and making measurement see it

- [x] **A1 (blocker). Add the text state, carried by `DrawString` and honoured by `MeasureString`.**
  Built as five properties on `XStringFormat` — `CharacterSpacing`, `WordSpacing`,
  `HorizontalScaling`, `TextRise`, `ObliqueAngle` — rather than a separate `XTextState`.

  Two corrections to what this item originally said. **No interface change was needed:**
  `IXGraphicsRenderer.DrawString` (`Drawing/IXGraphicsRenderer.cs:114`) already takes an
  `XStringFormat` and always has, so hanging the state there carries it to the renderer for free.
  And `XStringFormats`' presets each build a fresh instance (`"Create new format to allow
  changes"`), so mutable state on the type is not shared between callers — the hazard that would
  have argued for a separate type is not there. `APresetCarriesItsOwnTextStateRatherThanSharingOne`
  pins that down.

  **`RenderingMode` was deliberately left out** and moves to B1. `PdfGraphicsState.RealizeBrush`
  throws for every mode but 0 and 2 (`PdfGraphicsState.cs:258`), so a public property here would
  have thrown at draw time for five of its eight values. It belongs with the work that widens the
  mode and adds the pen overloads, where it can be validated against what the caller passed.
- [x] **A2 (blocker). Make `FontHelper.MeasureString` apply the text state.**
  `Drawing/FontHelper.cs` now adds `CharacterSpacing` per glyph shown — the last one included, as
  PDF does — `WordSpacing` per space, and scales the total by `HorizontalScaling / 100`.

  Three callers threw the format away, not the two this item named. The third is the renderer's own
  `_gfx.MeasureString(s, font)` at `XGraphicsPdfRenderer.cs:390`, whose width places the alignment
  and the underline and strikeout rules. It now measures through the format it draws with. That is
  ahead of itself by one item: until A3-A5 emit the operators, a caller who sets a spacing gets a
  measurement that accounts for it and glyphs that do not. Inert at the defaults, and closed by A3.

  Two things turned up on the way and are fixed here because the restructuring forced the counts to
  be per-line anyway:
  - Bold simulation counted the whole string's characters and charged them to the *widest line*, so
    a simulated-bold multi-line string measured too wide. `SpacingIsCountedPerLineAndTheWidestLine`
    `DecidesTheWidth` covers the shape of it.
  - A line feed decremented that same count, so it was paid a character spacing it never drew.
- [ ] **A3. Track and emit `Tc` for `characterSpacing`.**
  `PdfGraphicsState` already has `_realizedCharSpace` (`PdfGraphicsState.cs:342`) and writes `Tc` at
  `:363`/`:372`. The value is currently derived solely from bold simulation; it must become
  `boldSimulationSpacing + state.CharacterSpacing` so the two compose rather than one overwriting
  the other.
- [ ] **A4. Emit `Tw` for `wordSpacing`.** New realized field alongside `_realizedCharSpace`.
  Note the PDF restriction PDFKit works around — `Tw` applies only to single-byte code 32, so it is
  inert on the Unicode/`Identity-H` path, which is the default for embedded TrueType here. Either
  restrict `Tw` to the WinAnsi path and position words individually on the Unicode path (PDFKit's
  approach), or always position individually. Decide before A5, because justification depends on it.
- [ ] **A5. Emit `Tz` for `horizontalScaling`.** New realized field. Beware the stray literal
  `100 Tz` inside the image format string at `XGraphicsPdfRenderer.cs:641` — it is inside a
  `q`/`Q` pair so it does not leak today, but a `Tz` that is now graphics-state-tracked must not be
  confused by it.
- [ ] **A6. Emit `Ts` for text rise.** Not in PDFKit's list, but it is the mechanism super/subscript
  should use, and MigraDoc currently fakes both by shifting the baseline and shrinking the font
  (`ParagraphRenderer.cs:1199-1207`). Cheap once A1 exists.

### B — fill and stroke

- [ ] **B1. Widen the text rendering mode, and expose it.** `PdfGraphicsState.RealizeBrush` throws
  for anything but 0 and 2 (`PdfGraphicsState.cs:258`). Support 0-3 at minimum (fill, stroke,
  fill+stroke, invisible) so `fill: false` and `stroke: true` are both expressible. Modes 4-7 (clip)
  are out of scope — PDFKit does not expose them either. This is where the `RenderingMode` property
  held back from A1 lands.
- [ ] **B2. Add `DrawString` overloads taking an `XPen`.** Every current overload takes an `XBrush`
  only (`XGraphics.cs:1180-1242`), so stroked text is unreachable regardless of B1. Signature should
  allow pen-only, brush-only, and both, mapping to `Tr` 1, 0 and 2.
- [ ] **B3. Compose stroke with bold simulation.** Synthetic bold already claims `Tr 2` and sets a
  stroke pen of width `fontEmSize * 0.02` (`PdfGraphicsState.cs:255`). A caller-supplied stroke has
  to win, or bold-simulated stroked text silently gets the wrong pen.

### C — the formatter's layout options

All of these are in `Drawing.Layout/XTextFormatter.cs`, and most are named in its own upstream TODO
list at lines 443-456.

- [ ] **C1. `LineBreak`.** A `bool` that, when false, skips wrapping entirely and draws one line,
  clipped to the rectangle width. Cheap: `CreateLayout` (`:325`) short-circuits.
- [ ] **C2. `Indent` (first line) and `IndentAllLines`.** PDFKit subtracts the indent from the line
  width for line 1, then adds it back unless `indentAllLines` (`line_wrapper.js`). The same shape
  works in `CreateLayout`; `Block.LineIndent` (`Drawing.Layout/Block.cs:76`) already exists and is
  the field to drive.
- [ ] **C3. `ParagraphGap`.** Extra vertical space after a block whose `EndsParagraph` is set
  (`Block.cs:66`) — the flag is already there and already tracked.
- [ ] **C4. `LineGap`.** Additive leading, distinct from the existing `lineHeight` parameter
  (`XTextFormatter.cs:143`) which sets an absolute height. Both should coexist: PDFKit's `lineGap`
  adds to the computed line height.
- [ ] **C5. `Ellipsis`.** On vertical overflow, instead of dropping the block (`block.Stop = true` at
  `:345-349` and `:366-370`), trim the last line character by character until the text plus the
  ellipsis string fits the line width, then append it. Needs a `string` property, not a `bool`, so a
  caller can pass `"..."` instead of `…` when the font has no ellipsis glyph. Depends on A2 for the
  trimming loop to measure correctly.
- [ ] **C6. `Columns` and `ColumnGap`.** Column width is
  `(width - columnGap * (columns - 1)) / columns`; on filling a column, reset y to the top and
  advance x by `columnWidth + columnGap`. Default gap 18pt (1/4 inch) to match PDFKit. This is the
  largest single item in section C, because `CreateLayout` currently assumes one flow region.
- [ ] **C7. `Rotation`.** A degrees property that wraps the draw in
  `Save()` / `RotateAtTransform(angle, origin)` / `Restore()`. The transform machinery already
  applies to text through the CTM (`XGraphicsPdfRenderer.cs:1588-1593`), so this is plumbing, not
  new capability. Fix the layout rectangle semantics deliberately — PDFKit rotates about the text
  origin, not the rectangle centre.
- [ ] **C8. `Continued`.** A resumable cursor: the formatter must expose where it stopped (x, y, and
  the pending block) and accept it back on the next call. `XTextSegmentFormatter` covers the
  common case — changing style mid-paragraph — in a single call, so decide whether `continued`
  is worth a second mechanism or whether the segment formatter is the answer and this row is
  closed by documentation.

### D — decoration as a per-draw option

- [ ] **D1. Decouple `underline` and `strike` from `XFontStyle`.** Today they are font style flags
  (`Drawing/XFont.cs:193,198`) drawn as filled rectangles from font descriptor metrics
  (`XGraphicsPdfRenderer.cs:541-561`). PDFKit takes them per call. Add them to the text state from
  A1, keeping the `XFontStyle` flags working so nothing breaks.
- [ ] **D2. Decoration style and colour.** MigraDoc already offers seven underline and seven
  strikethrough styles (`enums/Underline.cs`, `enums/Strikethrough.cs`) and the core offers one.
  Bringing the core up to that set is optional for PDFKit parity — PDFKit's `underline` is a plain
  boolean — but it is what closes the gap between the two layers of this library.
- [ ] **D3. `Oblique` at an arbitrary angle.** The skew already exists but is hardcoded to
  `sin(20°)` (`!internal/Configuration.cs:57`) and reachable only through italic simulation
  (`XGraphicsPdfRenderer.cs:517`). Take the angle from the text state, defaulting to the current
  constant when italic is simulated. Note `AdjustTdOffset` (`:1603`) corrects x by that same sinus
  and must use the same value.
- [ ] **D4. Fill out `baseline`.** `XLineAlignment` (`Drawing/enums/XLineAlignment.cs`) has
  `Near, Center, Far, BaseLine` — roughly canvas `top`, `middle`, `bottom`, `alphabetic`. Missing
  are `hanging`, `ideographic`, and PDFKit's `svg-middle`. Adding them needs `CapHeight` and
  `XHeight`, both already on `XFontMetrics`; there is a standing `// TODO: Use CapHeight` at
  `XGraphicsPdfRenderer.cs:424`.

### E — annotations

- [ ] **E1. `Link` as a text option.** `PdfPage.AddWebLink` (`Pdf/PdfPage.cs:677`) takes a
  `PdfRectangle` in page space; nothing converts a drawing rectangle into one. Needs a helper that
  measures the drawn text, maps through `WorldToView`, and adds the annotation — and it must handle
  a link that wraps across lines by emitting one annotation per line, as MigraDoc's
  `RealizeHyperlink` already does (`ParagraphRenderer.cs:1123-1188`).
- [ ] **E2. `Destination` — create named destinations.** `PdfNamedDestinations`
  (`Pdf.Advanced/PdfNamedDestinations.cs:41`) resolves `/Names/Dests` name trees on import and is
  `internal` with no writer. Needs a public API to register a name against a page and position, and
  a name tree written into the catalog on save.
- [ ] **E3. `GoTo` — link to a named destination.** `PdfLinkAnnotation` writes
  `/Dest [n 0 R /XYZ ...]` for document links (`Pdf.Annotations/PdfLinkAnnotation.cs:163-166`) —
  a direct page reference, never a name. Add a `CreateNamedLink` alongside the existing four
  factories. Depends on E2.

### F — OpenType features

- [ ] **F1. Parse GSUB.** `GlyphSubstitutionTable.Read()` is an empty `try` block with a `catch`
  that rethrows (`Fonts.OpenType/OpenTypeFontTables.cs:1067-1076`). The table is detected,
  instantiated and never read. This is the whole of item 25's foundation.
- [ ] **F2. Parse GPOS.** Only a tag constant exists (`enums/TableTagNames.cs:156`). No class, no
  reader.
- [ ] **F3. Parse `kern`.** Same — a tag constant at `TableTagNames.cs:188` and nothing else. No
  kerning is applied in measurement or rendering today, which is why `XTextFormatter`'s TODO list
  names it at line 452.
- [ ] **F4. A feature-tag API and a shaping pass.** Script and language system selection, a
  feature-tag set with PDFKit's defaults, and application of substitutions and positioning between
  `cmap` lookup and `Tj`. Requires `TJ` (kerned/positioned arrays), which the renderer has never
  emitted — it writes only `Tj`.
- [ ] **F5. Move off `char`.** Every text loop iterates UTF-16 code units
  (`XGraphicsPdfRenderer.cs:472`, `FontHelper.cs:64`), so non-BMP codepoints are two independent
  lookups and surrogate pairs are broken. Shaping cannot be correct until this is a codepoint path.

---

## Sequencing

**A1 and A2 gate almost everything.** Until the text state exists and measurement honours it, A3-A6,
B1-B3, C5 and D3 have nowhere to read their input from, and any wrapping computed on top of them
disagrees with what is drawn. Both are done.

**A3-A6 should land before this branch merges.** A1 and A2 give a caller properties to set and a
measurement that answers through them, while the renderer still writes no `Tc`, `Tw`, `Tz` or `Ts`.
At the defaults nothing changes and the whole suite is green either way, but a caller who sets a
spacing today gets a width that counts it and glyphs that do not.

After that the sections are independent and can be taken in any order. Rough weights:

| section | weight | note |
|---|---|---|
| A | medium | mostly plumbing, but touches `IXGraphicsRenderer` — a public interface change |
| B | small | the machinery is present and artificially restricted |
| C | medium-large | C6 (columns) is most of it; C1-C4 are each small |
| D | small | D1 and D3 are refactors of working code |
| E | medium | E2 needs a name tree writer, which does not exist |
| F | **large** | a text shaping engine. Bigger than everything above put together |

Section F is not comparable to the rest. PDFKit gets shaping from
[fontkit](https://github.com/foliojs/fontkit), a dedicated font library; PdfSharpCore parses ten
tables for embedding and subsetting and has no shaping layer to extend. Treat F as its own project,
or as a case for taking a dependency rather than writing one.

## Deliberately not listed

- `XGraphics.DrawString` is not given wrapping. PDFKit's single call does everything; splitting
  layout from drawing is this library's existing shape and the parity surface is the formatter.
- Text clipping modes (`Tr` 4-7) are absent from PDFKit's options too, so B1 stops at mode 3.
- MigraDoc gaps that PDFKit has no counterpart for — multi-column *sections*, linked text frames,
  automatic hyphenation, RTL and bidi — are out of scope here even though several sit close to
  items above.
