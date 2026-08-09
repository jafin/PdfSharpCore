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

### Changed

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
