# Spec — text feature parity with PDFKit

[PDFKit](https://github.com/foliojs/pdfkit) documents 25 options on its `doc.text()` method. This
records which of them PdfSharpCore already answers, which it answers only partly, and which are
missing, together with the work each gap needs. Reference is
[`lib/mixins/text.js`](https://github.com/foliojs/pdfkit/blob/f308aae92f1491b0e952545fc0fbbef561c40e9e/lib/mixins/text.js#L114)
and its companion `lib/line_wrapper.js` at the same revision.

This is the gap analysis and the plan. Sections A and B are built, and item D3, on
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
| 11 | `characterSpacing` | **done** — `XStringFormat.CharacterSpacing`, written as `Tc` | **missing**, marked unported at `Font.cs:276` |
| 12 | `wordSpacing` | **done** — `XStringFormat.WordSpacing`, written as `Tw` or as a `TJ` array | **missing** |
| 13 | `horizontalScaling` | **done** — `XStringFormat.HorizontalScaling`, written as `Tz` | **missing**, `Font.cs:277` |
| 14 | `fill` | **done** — pass no brush and the text is not filled | always on |
| 15 | `stroke` | **done** — `DrawString` takes an `XPen`, written as `Tr` 1 or 2 | **missing**, `Font.cs:266` |
| 16 | `oblique` | **done** — `XStringFormat.ObliqueAngle`, in degrees | **missing** |
| 17 | `baseline` | partial — `XLineAlignment` has 4 of the 6 canvas values | n/a |
| 18 | `underline` | partial — bound to `XFontStyle`, one style, font colour only | `Underline`, 7 styles (`enums/Underline.cs`) |
| 19 | `strike` | partial — same | `Strikethrough`, 7 styles |
| 20 | `rotation` | partial — `RotateTransform` reaches text via the CTM, but is not an option | `TextFrame.Orientation`, 90° steps only |
| 21 | `link` | partial — `PdfPage.AddWebLink` takes a page-space rect; no text-level shortcut | `Hyperlink`, `HyperlinkType.Web` |
| 22 | `goTo` | **missing** — no way to link to a name | `HyperlinkType.Local` + `BookmarkField` |
| 23 | `destination` | **missing** — `PdfNamedDestinations` is `internal` and read-only (`Pdf.Advanced/PdfNamedDestinations.cs:41`) | `BookmarkField` |
| 24 | `continued` | partial — `XTextSegmentFormatter` takes mixed runs in one call, but there is no resumable cursor | `FormattedText`, nestable |
| 25 | `features` | **missing** — `GlyphSubstitutionTable.Read()` is an empty `try` block (`Fonts.OpenType/OpenTypeFontTables.cs:1067`); no GPOS, no `kern` | **missing** |

Two of twenty-five were at parity when this was written; eight are now, and `Ts` is emitted for a
text rise PDFKit has no option for at all.

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
- [x] **A3. Track and emit `Tc` for `characterSpacing`.**
  The two branches that set `Tc` from the rendering mode alone are now one line —
  `format.CharacterSpacing`, plus the bold-simulation spacing when the mode is 2. The two compose
  rather than one overwriting the other, so asking for a spacing on a simulated-bold font no longer
  quietly un-boldens it.
- [x] **A4. Emit `Tw` for `wordSpacing`, and space the words by hand where `Tw` cannot.**
  Both, as it turned out — the choice this item left open is not really a choice. `Tw` applies to
  the single-byte code 32 and expressly not to the byte 32 inside a multiple-byte code
  (PDF 32000-1 section 9.3.3), so for a font embedded as `Identity-H` a `Tw` is accepted and
  silently ignored. `PdfGraphicsState.NeedsWordSpacingByHand` decides which case a font is in:
  WinAnsi gets `Tw`, Unicode gets a `TJ` array with `-wordSpacing * 1000 / size` between the runs,
  and `Tw` is held at zero for it rather than written and ignored.

  This is the first `TJ` the renderer has ever emitted. All four show-text sites now carry their
  operator in the operand string rather than in the format, which is what lets one of them be `TJ`.

  The sign is the part worth having a test for: a number in a `TJ` array is *subtracted* from the
  horizontal position, so the number that opens a gap is a negative one, and a test that only
  checked the number it wrote would agree with itself whichever way round it was.
  `TextStateRenderingTests` rasterizes instead and measures the ink —
  `TheTwoEncodingsSpaceTheirWordsOutTheSameAmount` is the one that would catch it.
- [x] **A5. Emit `Tz` for `horizontalScaling`.** New realized field, seeded to 100 rather than 0 so
  that a page whose text is unscaled does not open with a redundant `Tz`. The stray literal
  `100 Tz` in the image path (`XGraphicsPdfRenderer.cs:641`) is inside a `q`/`Q` pair and does not
  disturb the tracked value.
- [x] **A6. Emit `Ts` for text rise.** Not in PDFKit's list, but it is the mechanism super/subscript
  should use, and MigraDoc currently fakes both by shifting the baseline and shrinking the font
  (`ParagraphRenderer.cs:1199-1207`). A realized field and a compare, the same shape as A3's.

  One thing it is not: `Ts` lives in the text *rendering* matrix, not the text matrix, so it lifts
  the glyphs without moving where `Td` puts the next string —
  `TextRiseDoesNotDisturbWhereTheNextStringGoes`. The underline and strikeout rules do have to be
  moved by hand, because they are rectangles drawn in graphics mode and never see the text matrix
  at all.

### B — fill and stroke

- [x] **B1. Widen the text rendering mode.** `RealizeBrush` threw for anything but 0 and 2. It now
  fills for 0, strokes for 1 and does both for 2.

  **Mode 3 is not implemented, and `RenderingMode` was not added as a property.** Both of those
  were this item's original plan and both turn out to be wrong. PDFKit's own rule is
  `fill && stroke ? 2 : stroke ? 1 : 0` — it never produces mode 3 either, so invisible text is not
  a parity gap. And with the pen and brush deciding the mode between them there is nothing left for
  a `RenderingMode` property to say that they do not, while there would be plenty for the two of
  them to disagree about. Modes 4-7 stay out of scope, as planned.
- [x] **B2. Add `DrawString` overloads taking an `XPen`.** Six of them, mirroring the six that take
  a brush: a brush alone fills, a pen alone strokes, both do both, and neither is an
  `ArgumentNullException` carrying `PSSR.NeedPenOrBrush` — which is the answer `DrawRectangle`,
  `DrawEllipse` and the rest already give to the same question.

  `IXGraphicsRenderer.DrawString` gained the pen, and that interface is **public**, so this is a
  breaking change for anyone implementing it outside this repo. It was taken rather than avoided:
  the alternative was a second method on the interface saying almost the same thing, or hanging a
  pen off `XStringFormat`, which is not what that type is for. There is one implementation.
- [x] **B3. Compose stroke with bold simulation.** A caller's pen wins outright over the one bold
  simulation would have stroked with.

  The other half of this was not in the original item and matters more. Bold simulation also
  *widens* the glyphs, with a character spacing worked out from the em size, and that widening was
  keyed on the rendering mode being 2. Mode 2 used to mean bold simulation and nothing else; now a
  caller who strokes their own text reaches it too, and owes none of the widening. It is keyed on
  the simulation itself — `StrokingTextDoesNotWidenItTheWayBoldSimulationDoes` is the test that
  fails if that is ever undone.

  The underline and strikeout rules are filled rectangles, so text drawn with a pen and no brush
  has nothing to fill them with; they take the pen's colour.

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
- [x] **D3. `Oblique` at an arbitrary angle.** `XStringFormat.ObliqueAngle`, in degrees, leaning
  right for a positive angle.

  The skew already existed but was hardcoded to `sin(20°)` and reachable only through italic
  simulation, and the state that tracked it was a `bool` — `ItalicSimulationOn` — because a fixed
  lean is either on or off. It is now `RealizedTextSkew`, a `double` holding the tangent the last
  `Tm` set, which collapses the four-branch decision in `DrawString` to two: the matrix already
  leans right, or it does not and a `Tm` says so.

  **Italic simulation keeps `sin(20°)`, not `tan(20°)`.** The tangent is what the angle actually
  means, and what a caller's angle is converted with, but sin is what PDFsharp has always skewed
  simulated italics by and what every document built with it looks like. Changing it would move
  every italic glyph in the library for no defect. The two compose by adding, which is exact
  rather than a convenience: shearing by *a* and then by *b* is shearing by *a + b*.

  `AdjustTdOffset` took a `bool adjustSkew` and corrected x by the same hardcoded sinus. It takes
  the skew itself now, or the correction would be wrong for every angle but 20°.
  `ATdThroughALeaningMatrixIsCorrectedForTheLean` holds it down: two strings asked for at the same
  x are eleven points apart per line without it.
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

**Section A is done, and so is D3.** Every property on `XStringFormat` now means something: the
three that change a width are measured and drawn and the two agree, and the two that do not —
`TextRise` and `ObliqueAngle` — are drawn as well. Nothing on the type is inert, which is what the
branch was waiting for.

At the defaults the content stream is what it always was. `Tc`, `Tw`, `Tz` and `Ts` are written only
when they differ from what a content stream starts with; the show-text operator moved into the
operand string without changing a character; and the only difference D3 leaves behind is a space
where simulated-italic text used to have a newline between its `Td` and its `Tj`.

**B is done too**, and cost one deliberate breaking change: `IXGraphicsRenderer.DrawString` takes a
pen. Nothing in the repo implements that interface but `XGraphicsPdfRenderer`.

**C is the next worthwhile stretch.** C1-C4 are a few lines each in `XTextFormatter`; C6, the
columns, is most of the section on its own.

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
- `CSequence` is left as it is. It declares `IList<CObject>` and then throws
  `NotImplementedException` from its explicit `IEnumerable<CObject>.GetEnumerator`
  (`Pdf.Content.Objects/CObjects.cs:408-411`), along with `ICollection`'s `Count`, `IsReadOnly` and
  `Remove`. `foreach` binds to the public `GetEnumerator` and works, which is why nothing had
  noticed; LINQ asks for the interface one and does not. The tests reading `TJ` arrays back out
  copy each sequence into a list first (`TextOperators.ItemsOf`). Fixing the type is a change to
  the content object model, not to text, and belongs with whatever next has business there.
