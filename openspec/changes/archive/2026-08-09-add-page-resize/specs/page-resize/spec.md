## ADDED Requirements

### Requirement: Resizing a page in place

A page that already carries content SHALL be resizable within the document that holds it, without
building a second document. The page's drawing SHALL be transformed into the new box rather than
cropped by it.

The document SHALL be open in `PdfDocumentOpenMode.Modify`; resizing a page of a read-only or
import-mode document SHALL throw `InvalidOperationException`.

#### Scenario: A4 shrunk to A5 keeps its whole drawing

- **WHEN** a page whose media box is A4 and which draws a mark at each of its four corners is
  resized to `PageSize.A5`
- **THEN** the page's media box is A5
- **AND** all four marks are still drawn, at the corners of the A5 page
- **AND** the document has the same page count it had before

#### Scenario: A5 grown to A4 keeps its whole drawing

- **WHEN** an A5 page is resized to `PageSize.A4`
- **THEN** the media box is A4 and the drawing fills it, having been scaled up rather than
  positioned in a corner

#### Scenario: The resized page belongs to the same document

- **WHEN** a page of a document opened for modification is resized
- **THEN** the same `PdfDocument` instance holds the resized page at the same index
- **AND** no other page of the document is altered

#### Scenario: Resizing a page of a read-only document is refused

- **WHEN** `Resize` is called on a page of a document opened with `PdfDocumentOpenMode.ReadOnly`
  or `PdfDocumentOpenMode.Import`
- **THEN** an `InvalidOperationException` is thrown
- **AND** the page is unchanged

### Requirement: The size setters refuse a page that has content

The `Size`, `Width` and `Height` setters of `PdfPage` SHALL throw `InvalidOperationException` when
the page's `/Contents` entry is present and non-empty. The message SHALL name `Resize` as the
operation to use instead.

On a page with no content the setters SHALL behave exactly as before, writing a new media box.

Determining whether the page has content SHALL NOT alter the page. In particular the `Contents`
property SHALL NOT be read, because its getter rewrites `/Contents` into array form as a side
effect.

#### Scenario: Setting the size of a blank page still works

- **WHEN** a page is added to a document and `page.Size = PageSize.A4` is assigned before anything
  is drawn on it
- **THEN** the media box is A4 and no exception is thrown

#### Scenario: Setting the size after drawing throws

- **WHEN** a page has been drawn on and `page.Size = PageSize.A5` is assigned
- **THEN** an `InvalidOperationException` is thrown whose message names `Resize`
- **AND** the page's media box is unchanged

#### Scenario: Setting the size of an imported page throws

- **WHEN** a page imported from another document is assigned a new `Size`
- **THEN** an `InvalidOperationException` is thrown and the page is unchanged

#### Scenario: The width and height setters behave the same way

- **WHEN** `page.Width` or `page.Height` is assigned on a page that has content
- **THEN** an `InvalidOperationException` is thrown naming `Resize`

#### Scenario: Testing for content does not disturb the page

- **WHEN** the size setter of a page whose `/Contents` is a single stream rather than an array
  throws
- **THEN** the page's `/Contents` entry is still that single stream, not an array

### Requirement: Fit modes and placement

Resizing SHALL accept a fit mode governing how a source box maps into a target box of a different
aspect ratio:

- `Fit` — uniform scale, the whole source visible, slack distributed by the alignment.
- `Fill` — uniform scale, the target covered, the overflowing part of the source clipped.
- `Stretch` — non-uniform scale, the source distorted to match the target exactly.
- `None` — no scale, the source placed in the target by the alignment and clipped by it.

Alignment SHALL select where slack or overflow falls, defaulting to centred. An optional uniform
margin SHALL be inset from the target box before fitting.

Two presets SHALL be offered, so that both common intents are a single expression:
`PageResizeOptions.Default` (`Fit`, centred) and `PageResizeOptions.Crop` (`None`, top-left).

`Crop` SHALL anchor **top-left**, not bottom-left. The behaviour being replaced anchored at the
origin, and PDF's origin is bottom-left, so it kept the foot of the page and cropped the heading
away. That was an artefact of the coordinate system rather than a choice, and is not reproduced.

#### Scenario: Fit letterboxes rather than distorting

- **WHEN** an A4 page (aspect 1:√2) is resized to `PageSize.Letter` with `PageFitMode.Fit`
- **THEN** the content is scaled uniformly, both dimensions fit inside the Letter box, and the
  drawing is centred with equal slack on the two sides that have any

#### Scenario: Fill covers the target and clips the overflow

- **WHEN** an A4 page is resized to Letter with `PageFitMode.Fill`
- **THEN** the content is scaled uniformly, the Letter box is fully covered, and the part of the
  source falling outside it is not visible

#### Scenario: Stretch matches the target exactly

- **WHEN** an A4 page is resized to Letter with `PageFitMode.Stretch`
- **THEN** the horizontal and vertical scale factors differ, and the source corners land exactly on
  the target corners

#### Scenario: None reboxes without scaling

- **WHEN** an A4 page is resized to A5 with `PageFitMode.None` and top-left alignment
- **THEN** nothing is scaled, and the page shows the top-left A5 region of what the A4 page showed

#### Scenario: A margin insets the target box

- **WHEN** a page is resized with a margin of 10 points and `PageFitMode.Fit`
- **THEN** the drawing is fitted into the target box inset by 10 points on every side

#### Scenario: The Crop preset keeps the head of the page

- **WHEN** an A4 page with a heading at its top and a footer at its bottom is resized to A5 with
  `PageResizeOptions.Crop`
- **THEN** nothing is scaled
- **AND** the heading is on the resized page and the footer is not, which is the opposite of what
  the replaced `Size` setter did

### Requirement: The source box follows what the viewer shows

The rectangle a resize maps **from** SHALL be the page's crop box when it has one, and its media box
otherwise. Arithmetic SHALL be performed in unrotated media-box coordinates, and the page's
`/Rotate` entry SHALL be left as it was.

All of the page's boxes — `/CropBox`, `/BleedBox`, `/TrimBox`, `/ArtBox` — SHALL be transformed by
the same matrix as the content, and the media box set to the target.

#### Scenario: A cropped page resizes by its crop box

- **WHEN** a page whose media box is A4 but whose crop box is a smaller region is resized to A5
- **THEN** what filled the crop box before fills the A5 page afterwards

#### Scenario: A turned page keeps its /Rotate entry

- **WHEN** a page carrying `/Rotate 90` is resized
- **THEN** its `/Rotate` entry is still `90`
- **AND** the size the page reports through `Width` and `Height` is the target size as the viewer
  sees it

#### Scenario: The other boxes travel with the content

- **WHEN** a page carrying a bleed box, trim box and art box is resized to half scale
- **THEN** each of those boxes is at the position and size the same transform gives it

### Requirement: Annotations are transformed with the page

Every annotation of a resized page SHALL have its `/Rect` transformed by the same matrix as the
content. Annotations carrying their own geometry SHALL have that geometry transformed too:
`/QuadPoints`, `/InkList`, `/Vertices`, `/L`, `/CL`, `/RD`.

Appearance streams SHALL NOT be modified — the viewer maps an appearance into `/Rect`, so
transforming `/Rect` transforms the appearance.

An annotation of an unrecognised subtype SHALL have its `/Rect` transformed and nothing else.

Annotation transformation SHALL be suppressible through the options.

#### Scenario: A link rectangle lands over the same words

- **WHEN** a page carrying a link annotation over a word is resized to A5
- **THEN** the link's `/Rect` covers that word where the word now is

#### Scenario: Ink geometry follows its rectangle

- **WHEN** a page carrying an ink annotation is resized
- **THEN** every point of every stroke in `/InkList` is transformed by the same matrix as `/Rect`

#### Scenario: A highlight keeps covering its text

- **WHEN** a page carrying a highlight annotation is resized
- **THEN** its `/QuadPoints` are transformed alongside its `/Rect`

#### Scenario: Appearance streams are untouched

- **WHEN** an annotation with an `/AP` appearance stream is transformed by a resize
- **THEN** the appearance stream's `/BBox` and `/Matrix` are byte-for-byte what they were

#### Scenario: An unknown annotation subtype survives

- **WHEN** a page carries an annotation of a subtype the resizer does not model
- **THEN** the annotation is still present, its `/Rect` is transformed, and its other entries are
  unchanged

### Requirement: Destinations pointing at a resized page are rescaled

Resizing a page SHALL find every destination in the document that targets that page and transform
the coordinates it carries. Destinations SHALL be sought in every page's `/Annots`, in the
`/Outlines` tree, in the catalog's `/Names` `/Dests` name tree and legacy `/Dests` dictionary, and
in `/OpenAction`.

Coordinates SHALL be transformed per destination form: `/XYZ` transforms `l` and `t` and leaves the
zoom `z` exactly as it is; `/FitR` transforms all four values; `/FitH` and `/FitBH` transform `t`;
`/FitV` and `/FitBV` transform `l`; `/Fit` and `/FitB` carry no coordinates and are left alone.

The zoom is a magnification the reader asked for, not a promise about physical text size. A resize
changes the scale the whole document reads at, and a destination SHALL read at that same scale —
so enlarging a document makes its destinations show larger text, which is the point of enlarging it.

Only `/GoTo` actions SHALL be rewritten. A `/GoToR` action names a page in another file and SHALL be
left alone.

The sweep SHALL be suppressible through the options, so that a caller resizing every page can make
one pass rather than one per page.

#### Scenario: A link from another page still points at the same spot

- **WHEN** page 1 carries a link to `[page3 /XYZ 100 700 0]` and page 3 is resized to half scale
- **THEN** the destination reads `[page3 /XYZ 50 350 0]`

#### Scenario: An /XYZ zoom is left as it is when the page shrinks

- **WHEN** a destination reading `[page /XYZ 100 700 1.0]` targets a page resized to 50%
- **THEN** the rewritten destination reads `[page /XYZ 50 350 1.0]`
- **AND** the zoom is still `1.0`, so the destination displays at the same magnification as the
  rest of the resized document

#### Scenario: An /XYZ zoom is left as it is when the page grows

- **WHEN** a destination reading `[page /XYZ 100 700 1.0]` targets a page resized to 200%
- **THEN** the zoom is still `1.0`, so jumping to the destination shows the larger text the
  enlargement was asked for rather than undoing it

#### Scenario: A zoom of zero is left alone

- **WHEN** a destination reading `[page /XYZ 100 700 0]` targets a resized page
- **THEN** the third value is still `0`, meaning the viewer keeps its current zoom

#### Scenario: A /Fit destination is untouched

- **WHEN** a destination reading `[page /Fit]` targets a resized page
- **THEN** the destination array is unchanged

#### Scenario: An outline entry is rescaled

- **WHEN** a bookmark's destination targets a resized page with explicit coordinates
- **THEN** those coordinates are transformed

#### Scenario: A named destination is rescaled through the name tree

- **WHEN** a link names a destination that the catalog's `/Names` `/Dests` tree resolves to a
  resized page
- **THEN** the coordinates held in the name tree are transformed, and the link still names it

#### Scenario: A remote destination is left alone

- **WHEN** a `/GoToR` action names a page number and a coordinate
- **THEN** the resize does not alter it

#### Scenario: A destination targeting another page is untouched

- **WHEN** page 3 is resized and a link points at page 4
- **THEN** that link's destination is byte-for-byte what it was

#### Scenario: The sweep can be turned off

- **WHEN** a page is resized with the destination pass disabled
- **THEN** no destination anywhere in the document is altered

### Requirement: Orientation is changed by reshaping or by turning

Resizing SHALL accept a target orientation, producing a target box whose width and height are
ordered accordingly. Changing orientation this way reshapes the box and refits the content into it.

`PdfPage.Rotate` SHALL remain the way to turn the paper without touching the content, and SHALL be
unaffected by this change.

`PageResizeOptions.AutoRotate` SHALL, when set, turn the content a quarter rather than letterbox it
when the source and target boxes are of opposite aspect.

#### Scenario: A portrait page reshaped to landscape letterboxes

- **WHEN** an A4 portrait page is resized to A4 landscape without auto-rotate
- **THEN** the media box is 842 × 595 as the viewer sees it
- **AND** the content is scaled uniformly to fit, leaving slack at the left and right

#### Scenario: Auto-rotate turns the content instead

- **WHEN** the same page is resized to A4 landscape with `AutoRotate` set
- **THEN** the content is turned a quarter and fills the landscape box with no slack

#### Scenario: Auto-rotate does nothing when the aspects already agree

- **WHEN** an A4 portrait page is resized to A5 portrait with `AutoRotate` set
- **THEN** the content is not turned

#### Scenario: Turning the paper is still free

- **WHEN** `page.Rotate = 90` is assigned
- **THEN** the content, annotations and destinations of the page are all unchanged

### Requirement: Resizing twice adjusts the wrapper rather than nesting

The wrapper a resize creates SHALL be marked as such and SHALL record the source rectangle it was
made from. Resizing a page whose content is exactly such a wrapper's invocation SHALL rewrite the
wrapper's transform against the recorded source rectangle rather than creating a second wrapper.

Anything unexpected in the content — extra operators, more than one content stream, a wrapper
without the marker — SHALL fall back to wrapping again, which is correct if wasteful.

#### Scenario: A4 to A5 to A4 returns to the original geometry

- **WHEN** an A4 page is resized to A5 and then back to A4
- **THEN** the drawing is at the size and position it started at
- **AND** the page's content contains exactly one wrapper invocation

#### Scenario: Three resizes leave one wrapper

- **WHEN** a page is resized three times in a row
- **THEN** exactly one form XObject wrapper exists in the page's resources

#### Scenario: Drawing between resizes falls back to nesting

- **WHEN** a page is resized, drawn on again, and resized a second time
- **THEN** the result is visually correct, whether or not a second wrapper was created

### Requirement: Encrypted, signed and tagged documents are refused

Resizing SHALL throw `InvalidOperationException`, before anything is mutated, when the document
carries a digital signature, is encrypted, or holds a `/StructTreeRoot`.

A tagged document is refused because the wrap moves page content into a form XObject and breaks the
`/StructParents` mapping into the structure tree, and that damage is invisible: the page renders
correctly, the file size is unremarkable and a golden image cannot see it. A refusal is preferable
to a document that looks right while its accessibility tree no longer describes it.

Each refusal SHALL say which of the three conditions it found.

#### Scenario: A signed document is refused

- **WHEN** `Resize` is called on a page of a document whose `/AcroForm` holds a `/Sig` field
- **THEN** an `InvalidOperationException` is thrown, and the document is unchanged

#### Scenario: An encrypted document is refused

- **WHEN** `Resize` is called on a page of an encrypted document
- **THEN** an `InvalidOperationException` is thrown, and the document is unchanged

#### Scenario: A tagged document is refused

- **WHEN** `Resize` is called on a page of a document whose catalog holds `/StructTreeRoot`
- **THEN** an `InvalidOperationException` is thrown naming the structure tree as the reason
- **AND** the document is unchanged, including its content streams and resources

#### Scenario: ResizePages refuses before altering any page

- **WHEN** `ResizePages` is called on a tagged, signed or encrypted document
- **THEN** the exception is thrown before the first page is touched, so no page is left resized

### Requirement: The page's graphics state is preserved across the wrap

Moving a page's content into a form XObject SHALL preserve the drawing exactly. Content whose
`q`/`Q` pairs are unbalanced SHALL NOT allow the resize transform to leak or to be torn down. The
page's transparency group SHALL travel with the content. The content stream's filter SHALL be
preserved and the bytes SHALL NOT be recompressed.

#### Scenario: Unbalanced q does not break the resize

- **WHEN** a page whose content stream leaves a `q` unmatched is resized
- **THEN** everything the page draws is scaled, including whatever followed the unmatched `q`

#### Scenario: Unbalanced Q does not break the resize

- **WHEN** a page whose content stream has one `Q` too many is resized
- **THEN** everything the page draws is still scaled

#### Scenario: A transparency group travels with the content

- **WHEN** a page carrying a `/Group` entry is resized
- **THEN** the form XObject holding the content carries that group

#### Scenario: Compressed content is not recompressed

- **WHEN** a page whose content stream is flate-encoded is resized
- **THEN** the wrapper's stream holds the same bytes behind the same filter

#### Scenario: Resources shared with another page are not disturbed

- **WHEN** two pages share one resource dictionary and one of them is resized
- **THEN** the other page draws exactly as it did before

#### Scenario: A page asked for its resources before the resize answers with the new ones

- **WHEN** a page's `Resources` property is read, the page is resized, and the property is read
  again
- **THEN** the second read returns the resources holding the wrapper

### Requirement: Resizing every page of a document

`PdfDocument` SHALL offer a method resizing every page, following the opt-in post-processing
precedent of `PruneUnusedResources` and `ConsolidateImages`. It SHALL make one destination sweep
for the whole document rather than one per page.

#### Scenario: Every page ends at the target size

- **WHEN** a document of mixed page sizes has `ResizePages(PageSize.A4, PageOrientation.Portrait)`
  called on it
- **THEN** every page's media box is A4 portrait, and each page's content is fitted into it

#### Scenario: Links between resized pages still land correctly

- **WHEN** a document whose pages link to one another is resized as a whole
- **THEN** every destination points at the same place on the page it pointed at before
