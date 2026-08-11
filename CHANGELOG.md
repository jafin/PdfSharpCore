# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This file starts at the entry below. Changes before that point are recorded only in the git history.


## [Unreleased]

### Added

- `PdfPage.Resize` and `PdfDocument.ResizePages` — change the size, shape or orientation of a page
  that already has content on it, in the document that holds it. The content is scaled into the new
  size rather than cropped by it, and the annotations of the page and the link destinations that
  point at it move with it.

  ```csharp
  page.Resize(PageSize.A5);                                  // fit the whole page in, centred
  page.Resize(PageSize.A4, PageOrientation.Landscape);       // reshape and refit
  document.ResizePages(PageSize.A4, PageOrientation.Portrait,
      new PageResizeOptions { AutoRotate = true });          // normalise a mixed batch
  ```

  `PageResizeOptions` carries the fit mode (`Fit`, `Fill`, `Stretch`, `None`), a nine-way
  alignment, a margin, `AutoRotate`, and switches for the annotation and destination passes.
  `PageResizeOptions.Default` and `PageResizeOptions.Crop` are the two common intents.

  Refused on a document that is encrypted, signed or tagged, rather than producing one whose
  signature no longer verifies or whose structure tree no longer describes the page.

  `PdfPage.Rotate` is unchanged and is still the free, lossless way to turn a page over without
  touching its content. See `docs/specs/page-resize.md`.

- 27 predefined page sizes that `PageSize` did not name: `A7`–`A10`, `TwoA0` and `FourA0` (the
  DIN 476 oversizes 2A0 and 4A0, spelled out because a C# identifier cannot begin with a digit),
  `B6`–`B10`, the whole ISO 269 `C0`–`C10` envelope series, and the untrimmed `SRA0`–`SRA4` stock.

  ```csharp
  page.Size = PageSize.C5;    // the envelope an A5 sheet goes into unfolded
  page.Size = PageSize.A7;
  ```

  With these, `PageSize` covers every size in the
  [pdfkit paper-size table](https://pdfkit.org/docs/paper_sizes.html), which it previously met only
  in part. Each is rounded to whole points as the existing entries are.

- 57 page formats that MigraDoc's `PageFormat` did not name. It knew twelve — A0–A6, B5, Letter,
  Legal, Ledger and P11x17 — and everything else had to be set as a `PageWidth` and a `PageHeight`.
  It now names every size `PdfSharpCore.PageSize` does: `A7`–`A10`, `TwoA0` and `FourA0`, the rest
  of the B series, the `C0`–`C10` envelopes, `RA0`–`RA5`, `SRA0`–`SRA4`, `JISB5`, and the North
  American and traditional sheets from `Tabloid` and `Executive` through to `QuadDemy`.

  ```csharp
  section.PageSetup.PageFormat = PageFormat.C5;
  ```

  The two enumerations stay separate types — MigraDoc records the format by name in MDDDL, and its
  sizes are held in the unit that defines them, whole millimetres for the ISO and DIN sheets, rather
  than rounded to whole points. The names now agree, which is what made them confusable.

  `P11x17` is kept alongside the `Tabloid` that names the same sheet, because MDDDL files hold it.

- Text state on `XStringFormat`, honoured by both drawing and measurement: `CharacterSpacing`,
  `WordSpacing`, `HorizontalScaling`, `TextRise` and `ObliqueAngle`, written as the PDF `Tc`, `Tw`,
  `Tz` and `Ts` operators and as a skewed text matrix.

  ```csharp
  var format = XStringFormats.Default;
  format.CharacterSpacing = 2;      // points after every glyph
  format.WordSpacing = 4;           // points after every space, on top of that
  format.HorizontalScaling = 80;    // percent
  format.ObliqueAngle = 12;         // degrees, leaning right
  gfx.DrawString("spaced out", font, XBrushes.Black, 20, 40, format);
  ```

  `Tw` counts the single-byte code 32 and is inert for a font embedded as Identity-H, which is what
  `GlobalFontSettings.DefaultFontEncoding` gives every `XFont` built without options of its own.
  Those have their words spaced out with a `TJ` array instead, so the same setting produces the same
  page whichever encoding the font uses.

- Stroked text. `DrawString` takes an `XPen` beside its `XBrush`, in the six shapes the brush-only
  overloads already came in. A brush alone fills the glyphs, a pen alone outlines them, both does
  both, and neither throws — as `DrawRectangle` has always answered the same question.

  ```csharp
  gfx.DrawString("outlined", font, new XPen(XColors.Black, 0.6), null, 20, 40);
  ```

- `XStringFormat.Underline` and `.Strikeout`, in the seven shapes MigraDoc has always had —
  `Single`, `Words`, `Dotted`, `Dash`, `DotDash`, `DotDotDash` — plus `DecorationColor`, which draws
  the rule in a colour of its own. Setting them on the font through `XFontStyle` still works and
  still means one solid rule.

- `XLineAlignment.Hanging`, `.Ideographic` and `.SvgMiddle`, the three baselines the HTML canvas has
  and this did not. They are measured against the text rather than against the layout rectangle,
  which is what distinguishes them from `Near` and `Far`.

- `XTextFormatter` grew the paragraph options its own TODO list had named for years: `LineBreak`,
  `Indent`, `IndentAllLines`, `ParagraphGap`, `LineGap`, `Ellipsis`, `Rotation`, and `Columns` with
  `ColumnGap` for text that flows down one column and on into the next.

  ```csharp
  var formatter = new XTextFormatter(gfx)
  {
      Columns = 2, ColumnGap = 18,
      Indent = 12, ParagraphGap = 6,
      Ellipsis = XTextFormatter.DefaultEllipsis,
  };
  formatter.DrawString(text, font, XBrushes.Black, new XRect(40, 40, 500, 300));
  ```

- Named destinations. `PdfDocument.NamedDestinations` names pages and places on them, written into
  the catalog as a `/Names /Dests` name tree; `Resolve` reads one back out of a document, which
  until now only the import machinery could do. `PdfPage.AddNamedLink` and
  `PdfLinkAnnotation.CreateNamedLink` follow a name.

  ```csharp
  document.NamedDestinations.Add("chapter-3", document.Pages[7], top: 500);
  page.AddNamedLink(rect, "chapter-3");
  ```

  A name outlives the page it stands for. Insert a page in front of page 7 and every link to page 7
  is wrong; every link to `chapter-3` is still right.

- `XGraphics.AddWebLink`, `AddDocumentLink`, `AddNamedLink` and `AddNamedDestination`, which take
  the coordinates the drawing methods take. An annotation is placed in default page space, measured
  up from the bottom left, and everything drawn is placed in world space, measured down from the top
  left — the conversion was the whole of what stood between drawing a piece of text and linking it.

### Changed

- **BREAKING:** `IXGraphicsRenderer.DrawString` takes an `XPen` before its `XBrush`, so that text
  can be outlined as well as filled. The interface is public; anything implementing it outside this
  repository has to add the parameter. `XGraphicsPdfRenderer` is the only implementation here.

  Migration is `DrawString(s, font, brush, rect, format)` →
  `DrawString(s, font, null, brush, rect, format)`.

- `XGraphics.MeasureString(text, font, stringFormat)` now answers through the format it is given.
  It took one and passed `XStringFormats.Default` on instead, which nothing noticed while a format
  held only alignment — where a string sits does not change how wide it is. Every text state
  property added above does change how wide it is, and a width measured without them is what decides
  where a line wraps.


- **BREAKING:** MigraDoc's `PageFormat.B5` measured 182 mm × 257 mm, which is the **JIS** B5 sheet,
  not the ISO one. It was the only B format the enumeration had, so nothing sat beside it to
  contradict it; now that `B0`–`B4` and `B6`–`B10` are named, a JIS sheet in the middle of an ISO
  series would be a sheet that is not half of the one above it. `B5` is now ISO B5, 176 mm × 250 mm.

  A section set to `PageFormat.B5` therefore reflows: it loses 6 mm of width and 7 mm of height, and
  text that fitted a line may no longer. To keep the sheet you had, use the new `PageFormat.JISB5`,
  which measures exactly what `B5` used to.

- **BREAKING:** the `PdfPage.Size`, `PdfPage.Width` and `PdfPage.Height` setters now throw
  `InvalidOperationException` when the page already has content on it. Before this change they
  wrote a new media box and nothing else, which cropped the page rather than resizing it —
  silently, with no exception and no warning. Setting them on a page with no content, which is the
  usual `document.AddPage(); page.Size = PageSize.A4;`, is unchanged.

  Migration is `page.Size = X` → `page.Resize(X)`.

  If you were relying on the crop, note what it actually did: it wrote the new box at the origin,
  and the origin of a PDF page is its **bottom-left** corner, so it kept the foot of the page and
  cropped the heading away. `page.Resize(X, PageOrientation.Portrait, PageResizeOptions.Crop)`
  crops from the **top left** instead, which is almost certainly what was wanted. To reproduce the
  old anchoring exactly, ask for `PageAlignment.BottomLeft`.

### Fixed

- Bold simulation measured multi-line text too wide. The widening it adds was counted over the whole
  string and charged to the widest line, so a simulated-bold string of three lines measured as
  though every character of all three sat on one of them.

- A line feed was charged a character spacing for a glyph it never drew.

- `PageSize.Executive` measured 540 × 720 points (7.5 × 10 inch), which is not the Executive sheet.
  It is 7.25 × 10.5 inch and now converts to 522 × 756 points — the size its own documentation
  always claimed, and the one ISO, `System.Drawing.Printing.PaperKind.Executive` and every other
  library give. A page asking for `PageSize.Executive` changes size as a result; a page whose width
  and height were set in points does not.

- A page-level transparency group (`/Group << /S /Transparency /CS /DeviceRGB >>`) was written onto
  every page of every saved document, whether or not anything on the page painted with transparency
  and whether or not the page arrived with one. Opening a document and saving it again was enough to
  add one to all of its pages.

  A transparency group is not inert: it tells a reader to composite the page as a unit against the
  backdrop, which can change how overprint and non-RGB content render, and `/CS /DeviceRGB` was
  imposed on pages whose content is not RGB.

  A page is now given a group only where it needs one: where something drawn on it uses an alpha
  below 1, or where an image or form placed on it paints with transparency of its own — a soft mask,
  a blend mode that reads what is underneath, or a transparency group of its own. A page whose
  content is opaque throughout, and an imported page that came in without a group, are written
  without one. A page that came in **with** a group keeps the one it had, as before.

  Documents that PdfSharpCore produced before this change are unaffected on the way in; they keep
  the group they were written with. The one visible difference is on the way out: opaque pages get
  smaller and no longer claim a colour space they do not use.

- Drawing a page of another document with `XPdfForm` dropped that page's transparency group. A group
  describes the content it wraps, and the content was being moved into a form XObject while the
  group was left behind in the document it came from, so the imported page arrived composited
  against the wrong backdrop. It is now imported with the rest of the page. The equivalent path for
  a page of the *same* document, which a page resize uses, already moved the group across.

- A PDF null was read as though it were the thing it stands in for. `/SMask null` in a graphics state
  or an image counted as a soft mask, which put a transparency group back onto pages whose content is
  opaque; `/Group null` on an imported page was cast to a dictionary, which threw rather than drew;
  and an indirect null anywhere in an imported page — `/SMask 6 0 R` with `null` in object six — hit
  a debug assertion while the page was being imported. A null now reads as the absent entry it is.

### Removed

- **BREAKING:** `PdfDocumentOptions.EnableCcittCompressionForBilevelImages`. The CCITT encoder this
  option gated was unreachable, so the option had no effect on any document — setting it changed
  nothing. Code that sets it will no longer compile; delete the assignment. No PDF that this library
  produces changes as a result.
- The CCITT Group 3/4 fax encoder (`PdfImage.FaxEncode.cs`) — `DoFaxEncoding`,
  `DoFaxEncodingGroup4`, and their `BitReader`/`BitWriter` helpers. Its only two call sites were the
  unreachable code removed below. Reading `/CCITTFaxDecode` streams from existing PDFs is unaffected;
  that is a separate path in `Pdf.Filters/Filtering.cs`.
- `PdfImage.ReadIndexedMemoryBitmap`, which had no callers. It could not have worked if called: it
  never filled its `MemoryStream`, so its `streamLength > 0` guard skipped the whole method body.
- The unused image importer subsystem in `PdfSharpCore/Drawing.Internal/` — `ImageImporter`,
  `ImageImporterBmp`, `ImageImporterJpeg`, `ImageImporterRoot`, `IImageImporter`, and the
  `StreamReaderHelper`, `ImportedImage`, `ImageInformation`, `ImagePrivateData` and `ImageData`
  types it defined. Nothing constructed it; `ImageImporter.GetImageImporter` had no callers and
  `ImageImporterBmp.PrepareImage` was an unimplemented stub. Every type was `internal` and the
  assembly has no `InternalsVisibleTo`, so nothing outside could reach them either.

Image handling continues to go through `PdfImage.ReadTrueColorMemoryBitmap`, which was already the
only live path. All 2,617 removed lines were unreachable before removal.
