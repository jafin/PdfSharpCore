# Spec — closing the demonstration app's coverage gaps

This continues `demonstration-app.md`, which built `SampleApp` and stopped at sixteen demos. Like
that one it is tied to no upstream issue: it is the fork's own tooling. It was written before the
work, so the status column tracks progress.

The sixteen demos cover the drawing surface and the interactive layer. Measured against the
library's actual public surface they leave whole assemblies untouched — charting, bar codes,
encryption, document assembly, form XObjects — and leave most of MigraDoc's structural features and
most of `XGraphics`' vector methods with nothing a reader can look at.

| item | what | status |
|---|---|---|
| 1 | `Charts` — the charting assembly, both routes into it | not started |
| 2 | `Assemble` — merge, split, reorder, import, prune, consolidate | not started |
| 3 | `Imposition` — `XForm`, `XPdfForm`, watermark, 2-up, booklet | not started |
| 4 | `Vectors` — the twelve `Draw*` methods no demo calls | not started |
| 5 | `Barcodes` — the three linear codes and DataMatrix | not started |
| 6 | `Protect` — passwords, permissions, and reading one back | not started |
| 7 | `Navigation` — page labels, viewer preferences, custom values | not started |
| 8 | `Compress` — `PdfDocumentOptions`, measured in bytes | not started |
| 9 | `Inspect` — reading back the content stream that was written | not started |
| 10 | `Unicode` — `PdfFontEncoding`, CID fonts, CFF versus TrueType | not started |
| 11 | `Structure` — MigraDoc's TOC, bookmarks, sections, lists | not started |
| 12 | `Ddl` — MigraDoc's own serialisation format, round-tripped | not started |
| 13 | `Images` extended — alpha, interpolation, failure reporting | not started |
| 14 | L1 — MigraDoc drops a `Footnote` silently | done, 3 tests |
| 15 | L2 — MigraDoc drops a `Barcode` shape silently | done, 3 tests |
| 16 | L3 — `BarCode.FromType` reaches two of the four code types | done, 7 tests |
| 17 | Infrastructure — an optional password, so item 6 can be real | not started |

Items 14 to 16 are defects found while surveying for the rest. They are in this spec rather than one
of their own because finding them *is* what the demonstration app is for, and the same argument
`demonstration-app.md` makes about its four gaps applies again: no exception, no warning, a property
that reads back exactly as it was set, and a file that quietly does not contain it.

---

## What the survey measured against

Not the demos' own list. Each of the following is a public, implemented, shipped capability with no
demonstration of any kind, found by walking the assemblies rather than the documentation:

```text
  assembly / namespace                         demonstrated by
  ─────────────────────────────────────────    ───────────────
  PdfSharpCore.Charting, 12 chart types        nothing
  MigraDoc Chart + ChartMapper                 nothing
  Drawing.BarCodes, 3 linear + DataMatrix      nothing
  Pdf.Security, RC4 40/128, AES, 8 permissions nothing
  ImportPage/DuplicatePage/MovePage/PlacePage  nothing
  PruneUnusedResources, ConsolidateImages      nothing
  XForm, XPdfForm                              nothing
  DrawEllipse/Arc/Pie/Polygon/Curve/Bezier/…   nothing
  XRadialGradientBrush, pen caps, joins, dash  nothing
  XGraphicsContainer, Scale/Translate/Multiply nothing
  PageLabels, ViewerPreferences, CustomValues  nothing
  PdfDocumentOptions, all five                 nothing
  Pdf.Content.ContentReader, the CObject model nothing
  XPdfFontOptions.Unicode vs WinAnsi           nothing
  MigraDoc TOC, OutlineLevel, ListInfo, DDL    nothing
  DocumentRenderer.ImageFailed                 nothing
```

`docs/PdfSharpCore/samples/` describes six of these in prose with no code behind any of them —
`CombineDocuments`, `ConcatenateDocuments`, `SplitDocument`, `Booklet`, `TwoPagesOnOne`,
`Watermark`. `demonstration-app.md` left those alone on the grounds that they were a separate piece
of work. Items 2 and 3 are that piece of work.

## Two rules carried forward, and one relaxed

The two load-bearing rules from `demonstration-app.md` are unchanged. **No demo registers a
backend** — registration stays in `Backends.EnsureRegistered`, reached only from `Program.Main`,
because the smoke test runs these inside a host that has already installed `PinnedFontResolver`.
**Assets are embedded resources, not content files**, because a referenced project's content items
do not reach the referencing project's output directory.

The rule that bends is *one file per demo*, and it bends only far enough for item 6. Every demo here
still writes exactly one PDF. Where a demo needs a second document — two files to merge, a source to
impose, a document to split — it builds that document **in memory** and never writes it. That is a
better demonstration than a folder of intermediates anyway: the reader sees the whole transaction in
one file, and the smoke test's contract is untouched.

Item 6 is the exception, and it is not really about file count. `Protect` must produce a genuinely
encrypted PDF or it demonstrates nothing, and the smoke test opens every declared output with
`PdfReader.Open(path, Import)` and no password. Item 17 gives `PdfDemo` an optional password for the
test to use.

---

## Item 1 — `Charts`

`PdfSharpCore.Charting` is a complete charting engine: twelve `ChartType` values (line, clustered and
stacked columns and bars, area, pie, exploded pie, combination), with `Axis`, `Gridlines`, `Legend`,
`DataLabel`, `TickLabels`, `MarkerStyle` and `FillFormat` around them, and a renderer for each in
`PdfSharp.Charting.Renderers`. `MigraDocCore.Rendering` renders a MigraDoc `Chart` too:
`Renderer.Create` dispatches it, and `MigraDoc.Rendering.ChartMapper` maps the DOM's chart onto the
charting engine's.

`demonstration-app.md` declined a charting demo on the grounds that it "would mean a fourth project
reference for one page". That was wrong when it was written and is checkable: `MigraDocCore.Rendering
.csproj` already carries `<ProjectReference Include="..\PdfSharpCore.Charting\…" />` with no
`PrivateAssets`, and `SampleApp` already references `MigraDocCore.Rendering`. The charting assembly
is on the app's reference graph today and in its output directory today. An explicit reference is
worth adding for legibility, but it adds no dependency that is not already there.

The demo shows both routes, because they are different tools:

- **The MigraDoc route** — `section.AddChart(ChartType.Column2D)`, series added to the DOM, the
  document renderer laying the chart out in the flow beside the text. This is what a report wants.
- **The PdfSharp route** — a `PdfSharp.Charting.Chart` drawn straight onto an `XGraphics` at a
  rectangle the caller chose. This is what a dashboard wants, and it is the only route that lets a
  chart sit inside a page the caller is otherwise drawing by hand.

Four pages: a page per chart family (column and bar, line and area, pie), and a last page setting
the two routes side by side with the same data so the difference is the API rather than the picture.

## Item 2 — `Assemble`

`PdfDocument` exposes a complete page-assembly API that nothing demonstrates:

```text
  AddPage(PdfPage)        take a page from another document
  InsertPage(int, PdfPage)  the same, at a position
  ImportPage              the explicit import, with AnnotationCopyingType
  PlacePage               replace rather than insert
  DuplicatePage           copy a page within a document
  MovePage                reorder
  PruneUnusedResources    drop what an imported page brought and does not use
  ConsolidateImages       one XObject where several were identical
```

`import-size-and-annotations.md` records four defects fixed in exactly this area — unused resources
travelling with an imported page, `InsertRange` throwing on a page carrying a resolvable link,
destinations arriving standing for nothing. All four are fixed, none is visible to a reader, and
`AnnotationCopyingType` is a parameter on four methods with no example of why it exists.

The demo builds two source documents in memory — one with links, one with a shared image drawn on
three pages — then merges, reorders, duplicates and prunes, and reports on a final page what each
step did to the page count and to the byte count. `PruneUnusedResources` and `ConsolidateImages`
are the two that pay for themselves in bytes, so the demo measures them rather than asserting them.

Splitting is the same operation read backwards: pages are extracted into in-memory documents whose
sizes are reported. Nothing is written to disk, per the rule above.

## Item 3 — `Imposition`

`XForm` and `XPdfForm` are the two `XImage` subclasses no demo touches, and they are the mechanism
behind three of the six orphaned prose samples.

- `XForm` is a **form XObject the caller draws into**: a piece of content drawn once and stamped
  many times, at different scales and rotations, costing one copy in the file however often it is
  placed. That is the thing to demonstrate — draw a complicated device once, place it twenty times,
  and show the file is not twenty times bigger.
- `XPdfForm` is **a page of an existing PDF as something drawable**. Watermarking, 2-up and booklet
  imposition are all the same call with different transforms.

Four pages: the stamp, a watermark under and over existing content (the order matters and the demo
says why), two source pages imposed 2-up, and a four-page booklet sheet.

## Item 4 — `Vectors`

`XGraphics` has eighteen `Draw*` methods. The sixteen demos call five of them. Untouched:
`DrawEllipse`, `DrawArc`, `DrawPie`, `DrawPolygon`, `DrawCurve`, `DrawClosedCurve`, `DrawBezier`,
`DrawBeziers`, `DrawRoundedRectangle`, `DrawLines`, `DrawRectangles`. Also untouched:
`XRadialGradientBrush`, `XPen`'s caps, joins, miter limit and custom dash patterns, `XFillMode`,
`XGraphicsContainer`, and every transform except `RotateAtTransform`.

Three pages: the primitives with their parameters labelled (an arc's angles are measured in a
direction people get wrong, so the demo draws the angles); pens and brushes, including the two fill
modes on the same self-intersecting path; and transforms, including `BeginContainer`/`EndContainer`
against `Save`/`Restore` — they are not the same thing and the difference is worth a page.

One thing the demo must not do is set a clip inside a nested graphics state. `XGraphicsPdfRenderer`
throws `NotImplementedException("Cannot set new clip region in an inner graphic state level.")`, and
a demo should teach that boundary rather than trip over it.

## Item 5 — `Barcodes`

`Drawing.BarCodes` implements Code 2 of 5 Interleaved, Code 3 of 9, OMR and a complete ECC200
DataMatrix — encoder, Reed-Solomon, symbol sizing, the lot — reachable through
`XGraphics.DrawBarCode` and `XGraphics.DrawMatrixCode`. Only the matrix code has tests
(`Drawing/MatrixCodeTests.cs`); the three linear codes have none and no demo.

One page: each code type at a usable size, with its text, direction and anchor varied, plus the
`CodeDirection` and `TextLocation` options that a caller would otherwise have to read the enum to
find. See item 16 for the API defect this turned up.

## Item 6 — `Protect`

`PdfSecuritySettings` offers user and owner passwords, `PdfDocumentSecurityLevel` selecting RC4 40,
RC4 128 or AES, and eight independent permission flags — print, modify, extract, annotate, fill
forms, accessibility extract, assemble, full-quality print. `PdfReader.Open` takes a password and
`XPdfForm.FromStream` takes one too. There is no demonstration of any of it, and `Security/` in the
test project is the only place a caller can see the API used.

The demo's own output **is** encrypted, with both passwords set and the passwords printed on the
page and in `Shows`. It sets a deliberately mixed permission set — printing allowed, extraction
refused — so that a reader opening it in a viewer can see the permissions dialog reflect what the
code asked for. A second page reports the settings back after a round trip through
`PdfReader.Open(…, password)`, which is the half of the API a caller reaches for when *reading* a
protected file.

This is the only item that needs item 17.

## Item 7 — `Navigation`

Everything a document says about how it should be *presented*, which is entirely undemonstrated
apart from one `PageMode` in `Outline`:

- `PdfPageLabels` — front matter in roman numerals and the body in arabic, which is why a reader's
  page box shows "iv" on the fourth page and "1" on the tenth. Fully implemented here, including
  `GetLabel`, and completely invisible.
- `PdfViewerPreferences` — hide the toolbar, centre the window, fit the window to the first page.
- `PageLayout` — single page, one column, two columns left or right.
- `Language`, for a screen reader.
- `CustomValues` — private data in the catalog, which survives a round trip.
- `NamedDestinations` — the table, as against the individual named destination `Text` already
  creates.

Three pages, laid out as front matter, body and a report page, so the page labels have something to
label.

## Item 8 — `Compress`

`PdfDocumentOptions` has five properties and no demonstration: `ColorMode` (RGB against CMYK),
`CompressContentStreams`, `NoCompression`, `FlateEncodeMode` and `UseFlateDecoderForJpegImages`.
Every one of them is invisible in the rendered page and visible only in the byte count, which is
exactly the kind of feature a demo can make real.

The demo builds one page of representative content — text, a path, a photograph — saves it to a
`MemoryStream` under each combination of options, and prints the resulting byte counts as a table on
the page it then writes. The reader sees the settings, the sizes, and the trade.

## Item 9 — `Inspect`

`Pdf.Content.ContentReader` and the `CObject` model are public: a caller can read back the operators
a page was drawn with. The test project has four content-stream readers under `Helpers` — linked
into `MigraDocCore.Rendering.Tests` rather than copied — so the technique is well understood inside
the repository and has no example outside it.

The demo draws a page, saves it to a `MemoryStream`, reopens it, and prints its own content stream —
the operators, their operands, and a count by operator name. It is the only demo whose subject is
the file rather than the page, and it is the fastest way for someone debugging output to learn that
they can look.

## Item 10 — `Unicode`

`XPdfFontOptions` selects `PdfFontEncoding.WinAnsi` or `PdfFontEncoding.Unicode`, which decides
whether a simple font or a CID font is written, and therefore which characters survive.
`PdfDocumentRenderer(true)` — the `unicode:` argument three MigraDoc demos pass without comment — is
the same switch. `font-embedding-gaps.md` records six gaps closed around this, including CFF
embedding whole where TrueType is subsetted, and none of it is visible.

The demo shows the same string in both encodings, the CID font path, and the difference in what
lands in the file. Liberation Sans carries Latin, Latin Extended, Greek and Cyrillic, so the demo
covers those without a new asset; **CJK is deliberately out of scope** and the page says so, because
a CJK face is a multi-megabyte asset for one page and this app already carries 3MB.

The CFF-against-TrueType difference gets a panel of its own: Source Code Pro is already embedded and
is already the app's only CFF face, so the demo can report that one of the two embedded faces was
subsetted and the other could not be, and name the reason.

## Item 11 — `Structure`

The four MigraDoc demos between them call `AddParagraph`, `AddTable`, `AddImage`, `AddTextFrame`,
`AddStyle`, `AddTabStop`, `AddFormattedText` and two page fields. What they leave out is most of
what makes MigraDoc worth using for a long document:

- **A table of contents** — `PageRefField` against a `BookmarkField`, with `TabLeader.Dots`. The
  classic MigraDoc TOC, and the reason `TabStops` has a leader property at all.
- **Bookmarks for free** — `ParagraphFormat.OutlineLevel` drives `DocumentRenderer.AddOutline`, so
  headings become a PDF outline with no manual `Outlines.Add` anywhere. `Outline` builds its tree by
  hand, which is right for PdfSharp and misleading as the only example.
- **Sections** with different page setups, and the full `HeadersFooters` set — primary, first page,
  even page — which is how a report gets a title page without a header.
- **Lists** — `ListInfo`, the only list support in the library. `demonstration-app.md` notes that
  `Layout` draws its bullets by hand; the real one still has no demo.
- **`Hyperlink`** in its web, local, bookmark and file forms.
- The predefined styles in `StyleNames` and how style inheritance resolves.

Four pages: a title page with its own header treatment, a TOC, and two pages of headed, numbered,
cross-referenced body — which is also the only demo in the app whose PDF has a working outline it
never asked for.

## Item 12 — `Ddl`

`DdlWriter` and `DdlReader` serialise a MigraDoc document to MigraDoc's own text format and read it
back. It is a real feature with a hand-written scanner and parser (`DdlScanner`, `DdlParser`,
`DdlReaderErrors`) and it has no example anywhere outside the test project.

The demo builds a small document in the object model, writes it to DDL with
`DdlWriter.WriteToString`, reads that string back with `DdlReader`, renders the *re-read* document,
and prints the DDL beside the result. If the round trip loses anything the page shows it, which
makes this the one demo that is also a regression test a human can read.

## Item 13 — `Images` extended

`Images` draws one JPEG seven ways, and the computation of those rectangles is the demonstration.
What it cannot show with one opaque JPEG:

- **An alpha channel.** A PNG with transparency over a coloured ground, which is the case people
  actually ask about, and the one where a backend difference would show.
- **`XImage.Interpolate`**, which decides whether an upscaled image is smoothed or blocky.
- **`DocumentRenderer.ImageFailed`**, the event `image-failure-reporting.md` was built for. Without
  a handler a failed image is a grey box and the exception is dropped; the demo subscribes and
  prints the reason. This is a MigraDoc-side event, so the panel showing it renders a tiny MigraDoc
  document rather than drawing directly.

This means one new asset: a PNG with an alpha channel, generated rather than found so its licence is
not a question, embedded like the rest.

## Item 14 — L1, MigraDoc drops a `Footnote` silently

`ParagraphElements.AddFootnote` exists in two overloads, `Footnote.cs` is in the DOM,
`StyleNames.Footnote` is a predefined style — and `MigraDocCore.Rendering` contains the string
"Footnote" nowhere at all. `ParagraphRenderer.FormatElement` switches on the element's type name and
ends `default: return FormatResult.Continue;`, so a footnote is skipped without a word.

A caller writes `paragraph.AddFootnote("…")`, reads the property back, and gets a PDF with no
footnote and no error.

Implementing footnote layout — reserving the area, numbering, splitting the note across a page break
— is a feature, not a fix, and is out of scope here. What is in scope is that it stops being silent.
`FormatElement` gets an explicit `case "Footnote":` that throws a `NotSupportedException` naming the
gap. Tests go in `MigraDocCore.Rendering.Tests`, which is where MigraDoc layout is covered and which
rasterizes nothing.

This is a **breaking** change in the same sense as `page-resize.md` item 3: code that today produces
a silently wrong document will throw. That is the point.

## Item 15 — L2, MigraDoc drops a `Barcode` shape silently

The same shape of defect one level up. `Shapes/Barcode.cs` is in the DOM with `Type`, `Text`,
`BearerBars` and the rest; `Renderer.Create` dispatches `Paragraph`, `Table`, `PageBreak`,
`TextFrame`, `Chart` and `Image` and nothing else. A `Barcode` added to a section returns a null
renderer and is dropped.

`Renderer.Create` returning null is *legitimate* for two element kinds and the code says so — a
legend is rendered as part of its chart, and a bookmark draws nothing. So the fix cannot be a blanket
throw at the call site; it is an explicit branch for `Barcode` that throws and names
`XGraphics.DrawBarCode` as the working route, which item 5 demonstrates.

While in that method: its `RenderInfo` overload tests `is Chart` twice, at `Renderer.cs:195` and
`:197`, so the second branch is unreachable. Harmless, inherited from upstream, and worth deleting
while the file is open.

## Item 16 — L3, `BarCode.FromType` reaches two of the four code types

`CodeType` has four values. `BarCode.FromType` handles `Code2of5Interleaved` and `Code3of9Standard`
and falls to `default: throw new InvalidEnumArgumentException(...)` — so `CodeType.Omr` and
`CodeType.DataMatrix`, both of them implemented and both of them reachable by constructing the class
directly, are reported as though they were not values of the enum.

`CodeOmr : BarCode`, so it belongs in the factory and is a two-line addition.
`CodeDataMatrix : MatrixCode`, which is not a `BarCode` and cannot be returned from a method typed
that way — so that case gets a message saying what to construct instead and which draw method takes
it, rather than a message implying the caller passed a bad enum value.

## Item 17 — infrastructure, an optional password

`PdfDemo` gains `public virtual string? OpenPassword => null;`, and `DemoSmokeTests` passes it to
`PdfReader.Open` when it is set. Fifteen demos are unaffected. `Protect` overrides it, which is what
lets the demo's declared output be genuinely encrypted rather than an unencrypted page describing
encryption.

The runner prints it beside the output path when it is set, so somebody running `SampleApp run -e
protect` is told the password rather than having to read the source to find it.

---

## The order the demos are read in

`DemoRegistry` is a curriculum, not an alphabetical list. The twelve new demos slot in by subject:

```text
   1 HelloWorld     10 Tables          19 Forms
   2 Fonts          11 PageResize      20 Annotations
   3 Unicode   ★    12 Bleed           21 Outline
   4 Orientation    13 Assemble   ★    22 Invoice
   5 Images         14 Imposition ★    23 Structure   ★
   6 Text           15 Navigation ★    24 Charts      ★
   7 Vectors   ★    16 Protect    ★    25 Ddl         ★
   8 Barcodes  ★    17 Compress   ★    26 Newspaper
   9 Layout         18 Inspect    ★    27 Magazine
                                       28 SideWrap
```

Fonts then Unicode; the drawing surface then the vector methods then the codes drawn with them; the
page-level demos then the document-level ones; the interactive layer then MigraDoc, ending as it
does today with the three combined layouts.

## Deliberately not done

- **No footnote rendering and no MigraDoc barcode renderer.** Items 14 and 15 make both audible.
  Building either is a feature with its own spec.
- **No CJK font.** Item 10 says so on the page. A face that covers CJK is megabytes for one panel.
- **No `.ttc` demonstration.** `FontResolverBase` supports the `file.ttc#1` form and the app carries
  no collection file; manufacturing one to demonstrate it would be demonstrating the asset.
- **No golden images**, unchanged from `demonstration-app.md`, and for the same reason: demos are
  meant to be edited.
- **No second output file from any demo**, item 6's encryption aside. In-memory documents are a
  better demonstration and leave the smoke test's contract alone.
- **The prose samples under `docs/` are still not rewritten.** Items 2 and 3 cover six of them with
  code; pointing the prose at the demos is a documentation change and belongs with the rest of that
  work.
