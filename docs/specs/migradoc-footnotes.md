# Spec — MigraDoc footnotes

`MigraDocCore.DocumentObjectModel` has carried a complete footnote model since the fork began.
`MigraDocCore.Rendering` has never contained a line that draws one. Until recently a footnote was
dropped in silence: `ParagraphRenderer.FormatElement` fell through its `default` and the note simply
did not appear, with nothing thrown and nothing logged. It now throws a descriptive
`NotSupportedException` (see `demonstration-app-coverage.md`, item 19), which makes the gap audible
but does not close it. This spec is the plan for closing it.

Nothing here is built yet. Status is tracked in the table below.

| item | what | status |
|---|---|---|
| 1 | `FormattedFootnote` — format a note's block content into a column | not started |
| 2 | Reference marks — measure and draw the superscript in the body text | not started |
| 3 | Area reservation — shrink the page's content area by the notes it collects | not started |
| 4 | Numbering — the four `Document` properties that decide the mark | not started |
| 5 | The separator rule above the note block | not started |
| 6 | Notes that do not fit — carrying a note onto the next page | not started |
| 7 | `FootnoteLocation.BeneathText` | not started |
| 8 | Notes inside a table cell, a text frame or a header | not started |
| 9 | Tests | not started |
| 10 | A `Footnotes` panel in the demonstration app | not started |

---

## What already exists

The DOM half is done and needs no change:

- `Footnote` — a `DocumentObject` with `Elements` (a `DocumentElements`, so block content:
  paragraphs, tables and images), a `Reference` string, a `Style` and a `ParagraphFormat`. It
  serializes to and parses from DDL already.
- `AddFootnote()` / `AddFootnote(string)` on `Paragraph`, `FormattedText`, `Hyperlink` and
  `ParagraphElements` — so a note can be attached anywhere an inline run can.
- `Document.FootnoteLocation` (`BottomOfPage`, `BeneathText`),
  `Document.FootnoteNumberingRule` (`RestartPage`, `RestartContinuous`, `RestartSection`),
  `Document.FootnoteNumberStyle` (`Arabic`, `LowercaseLetter`, `UppercaseLetter`,
  `LowercaseRoman`, `UppercaseRoman`) and `Document.FootnoteStartingNumber`.
- `StyleNames.Footnote`, a predefined paragraph style based on `Normal`.
- `DocumentObjectVisitor.VisitFootnote`.

So the work is entirely in `MigraDocCore.Rendering`, and every public API a caller needs is already
shipped. That is worth stating plainly: **implementing this adds no DOM surface**, which makes it a
much smaller decision than its size suggests.

## Why it is hard

The pipeline is a two-phase one. `FormattedDocument.Format` runs `TopDownFormatter.FormatOnAreas`
over the document's elements, asking an `IAreaProvider` for one area at a time; each element's
`Renderer.Format` decides what fits, and `Render` is a separate later pass driven by the stored
`RenderInfo`s. That works because **the area is known before the content is measured**.

A footnote inverts it. The height of the note block is not known until the body text of the page has
been formatted, but the body text can only be formatted once the height available to it is known —
and that height is the page less the note block. It is a fixed point, and the standard answer is to
iterate:

1. Format the page's body into the full content area.
2. Collect the footnotes whose reference marks landed on that page.
3. Format those notes and total their height.
4. If that height is not zero, shrink the content area by it and go back to step 1.
5. Repeat until the set of notes on the page stops changing.

Step 5 terminates because shrinking the area can only move marks off the page, never on, so the note
set is monotonically decreasing. **A bound is still needed** — three passes, say, after which the
last stable arrangement is used — because a note whose own height is what pushes its mark to the
next page can oscillate. That case is real and every typesetter has a rule for it; ours should be to
stop and accept a slightly under-filled page rather than to loop.

This is the single largest piece of work in the spec, and item 3 is where it lives.

## Item 1 — `FormattedFootnote`

Model it on `FormattedTextArea`, which is the existing example of "format a `DocumentElements` into
a sub-area and hand back `RenderInfo`s". It is an `IAreaProvider` of about 130 lines that runs its
own `TopDownFormatter`, and a footnote needs the same shape:

```csharp
internal class FormattedFootnote : IAreaProvider
{
    internal void Format(XGraphics gfx);      // runs TopDownFormatter over footnote.Elements
    internal XUnit ContentHeight { get; }     // RenderInfo.GetTotalHeight(GetRenderInfos())
    internal RenderInfo[] GetRenderInfos();
}
```

Two differences from `FormattedTextArea`:

- The width is the column width of the page, not an inherent width — a note is as wide as the text
  it belongs to.
- The first paragraph must be indented by the width of the reference mark, and the mark drawn into
  that indent. `FormattedTextArea` has no equivalent.

The `Style` on the `Footnote` and `StyleNames.Footnote` supply the formatting; the resolution order
is the one `Paragraph` already uses (explicit `Format` over `Style` over `Normal`).

## Item 2 — reference marks

`ParagraphRenderer` measures inline elements through `FormatElement` and draws them through
`RenderElement`, and both are switches on `docObj.GetType().Name`. A footnote's mark is an inline
run of text like any other, so:

- `FormatElement`'s `case "Footnote"` replaces the current throw with `FormatFootnote`, which
  measures the mark string in the current font at superscript size and consumes that width — the
  same shape as `FormatPageRefField`, which measures a string whose value is not yet known.
- `RenderElement` gains `case "Footnote": RenderFootnote(...)`, drawing the mark with
  `ParagraphFormat.TextRise` set, or an explicit `XFont` at a reduced size. Which of the two is a
  detail worth settling against how `FormattedText` already does superscripts.

The mark's *text* is either `Footnote.Reference` when the caller set one — a custom mark such as `*`
or `†` — or the generated number from item 4 when they did not.

**The iterator does not descend into a footnote.** `ParagraphIterator` unwraps `FormattedText` and
`Hyperlink` and nothing else, so `Footnote.Elements` is never walked as inline content. That is
already correct and must stay that way: the note's body is block content formatted separately, and a
descent would spill it into the running text.

## Item 3 — area reservation

`FormattedDocument` is the `IAreaProvider` for the body, and `IAreaProvider.GetNextArea` is where
the page's content rectangle is decided. The loop described above belongs in `FormattedDocument
.Format`, around the existing `formatter.FormatOnAreas(gfx, true)` call.

Collecting "the notes whose marks landed on this page" needs the renderer to record, during
formatting, which page each mark fell on. `FieldInfos` already carries per-page state of exactly
this kind for `PageRefField` resolution, and is the natural place to hang it.

`GetFooterArea` already shows how a band is carved off the bottom of a page. The footnote block
sits above the footer and below the body, so `GetNextArea` subtracts the note height from the
content rectangle's height in the same way, and the note block is drawn between the two.

## Item 4 — numbering

A small formatter, taking the ordinal and `Document.FootnoteNumberStyle` and returning the mark
text. `NumberFormatter` already exists in the assembly for list numbering — check whether roman and
alphabetic conversion can be shared rather than written twice.

`FootnoteNumberingRule` decides what the ordinal counts from:

- `RestartContinuous` — one counter for the document, starting at `FootnoteStartingNumber`.
- `RestartSection` — reset when the section changes.
- `RestartPage` — reset per page, which interacts with item 3: a note moved to the next page by
  reformatting changes its own number, so numbering must be assigned *after* the fixed point in
  item 3 settles, not during formatting.

That ordering constraint is the one genuinely subtle thing in this item and is worth a test of its
own.

## Item 5 — the separator

A short horizontal rule above the note block, conventionally about a third of the column width. Word
and LaTeX both draw one and readers expect it. Draw it in the note-block render pass, not as part of
any note.

There is no DOM property for it, and none should be added in this work — a fixed rule of one third
the column width at hairline weight matches what MigraDoc's own documentation shows. If it later
needs to be configurable, that is a separate change to the DOM with its own DDL serialization.

## Item 6 — notes that do not fit

A note longer than the space available must split, with the remainder carried to the next page,
which is standard typesetting behaviour. `TopDownFormatter` already knows how to break an element
across areas — that is `NeedsEndingOnNextArea` and the `previousFormatInfo` argument to
`Renderer.Format` — so the machinery exists.

The interaction with item 3 is where the care goes: a note that splits contributes only its first
part's height to this page's reservation.

**This item may reasonably be deferred.** A first implementation that refuses to split, and instead
moves the whole note to the next page when it does not fit, is correct for the overwhelming majority
of footnotes (which are one or two lines) and much simpler. If it is deferred, it should be deferred
loudly — a note too tall for a whole page is otherwise an infinite loop.

## Item 7 — `FootnoteLocation.BeneathText`

The enum has two values and item 3 implements one of them. `BeneathText` puts the note immediately
after the text block rather than at the foot of the page, which is a different and simpler layout: no
fixed point is needed, because the note goes wherever the text ended.

Worth doing second, once `BottomOfPage` works, so that the shared parts are already factored.

## Item 8 — notes in nested contexts

A footnote can be added to any paragraph, including one inside a table cell, a text frame or a
header. Each of those formats through its own `IAreaProvider` (`FormattedCell`,
`FormattedTextFrame`, `FormattedHeaderFooter`), none of which owns a page.

The note must still surface at the foot of the *page*, so the collection mechanism from item 3 has
to reach up out of the nested provider. The alternative — refusing a footnote in those contexts with
a clear message — is defensible for a first implementation and is what should ship if item 8 is cut,
because silently dropping the note is exactly the behaviour this whole spec exists to remove.

A footnote in a header or footer should be refused outright in any case. It has no sensible meaning:
the header is formatted once per position and reused across pages.

## Item 9 — tests

`MigraDocCore.Rendering.Tests` is the home — it covers MigraDoc's own layout and deliberately
rasterizes nothing, which is right for this. The content-stream readers it links out of
`PdfSharpCore.Test/Helpers` are how the assertions get at what was drawn.

What is worth pinning:

- A one-line note appears at the foot of the page, below the body text and above the footer.
- The body text is shorter on a page carrying notes than on one that is not — the reservation
  actually happened.
- All five `FootnoteNumberStyle` values produce the expected mark.
- All three `FootnoteNumberingRule` values, including that `RestartPage` numbers by final page and
  not by the page a note was first formatted onto.
- A `Reference` set by the caller wins over the generated number.
- Two notes on one page come out in reading order.
- A note whose mark is pushed to the next page by the reservation ends up on that page too, and the
  formatter terminates.
- Whatever item 8 settles on: either the note surfaces from a table cell, or the refusal is thrown.

## Item 10 — the demonstration app

A `Footnotes` demo, in the curriculum after `Structure`. `demonstration-app.md` argues that a demo
that is never looked at is a demo that proves nothing, and every defect in
`demonstration-app-coverage.md` was found by drawing a page and looking at it — which is exactly the
check this feature most needs. The panel should show a numbered note, a custom-reference note, the
five number styles, and a page where the reservation visibly shortens the body text.

## Deliberately not done

- **Endnotes.** No DOM support and not asked for.
- **`FootnoteLocation` per section.** The property is on `Document` and stays there.
- **A configurable separator.** See item 5.
- **Footnotes referenced more than once.** A DOM `Footnote` is one object in one place; sharing one
  mark between two call sites is not expressible and should not become expressible here.

## Relationship to the barcode gap

`Renderer.cs` throws for `Barcode` in the same way and for the same reason, and that gap is *not*
covered by this spec. The two are unrelated in everything but symptom: a MigraDoc barcode needs a
shape renderer mapping the DOM `Barcode` onto `PdfSharpCore.Drawing.BarCodes`, which is a day's work
with no layout problem in it at all, where footnotes are a layout problem and almost nothing else.
