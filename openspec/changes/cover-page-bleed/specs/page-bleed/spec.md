## ADDED Requirements

### Requirement: A trim margin moves the drawing origin to the trimmed page

Where `PdfPage.TrimMargins` is set, the origin of the coordinate space `XGraphics` draws in SHALL be
the top-left corner of the **trimmed** page, not of the sheet.

A caller who never draws outside the trimmed page therefore writes exactly the same code as on a
page with no trim margin at all. Bleeding is reaching past the origin, not rebuilding the page.

#### Scenario: Drawing at the origin lands on the trim corner

- **WHEN** a page has a trim margin of 3mm on every edge and a mark is drawn at `(0, 0)`
- **THEN** it lands on the trim corner, one bleed and one mark allowance in from the sheet's edge on
  both axes

#### Scenario: A negative coordinate reaches into the bleed

- **WHEN** a rectangle is drawn from `(-3mm, -3mm)` on a page with a 3mm trim margin
- **THEN** its corner is flush with the corner of the bleed

#### Scenario: The same drawing is unmoved by a trim margin

- **WHEN** the same content is drawn onto two pages of the same trimmed size, one with a trim margin
  and one without
- **THEN** the content occupies the same position relative to the trimmed page on both

### Requirement: A trim margin does not change the size of the page a caller draws on

`PdfPage.Width` and `PdfPage.Height` SHALL report the size of the trimmed page, before the document
is saved and after. The sheet is larger, and that difference SHALL be visible only in the page boxes.

Stated because the alternative — a page that silently grows when a margin is set — moves every
right-aligned and bottom-aligned thing measured off it. That is what the library used to do: `Width`
reads the media box, and saving overwrote the media box with the sheet.

#### Scenario: Width and height are the trimmed size

- **WHEN** an A5 page is given a trim margin of 3mm on every edge
- **THEN** `Width` and `Height` still report A5

#### Scenario: The page still reports itself after it has been saved

- **WHEN** that page is saved and its `Width` and `Height` are read afterwards
- **THEN** they still report A5

#### Scenario: The sheet is larger than the page

- **WHEN** that page is saved
- **THEN** the media box is larger than the trimmed page by the bleed and the mark allowance on each
  edge

#### Scenario: Saving twice writes the same sheet

- **WHEN** a document containing a trimmed page is saved twice — to a stream and then to a file, say
- **THEN** both files carry the same media box

### Requirement: A trimmed page is saved with its five boxes, and they nest

Saving a page with a trim margin SHALL write `/MediaBox`, `/CropBox`, `/BleedBox`, `/TrimBox` and
`/ArtBox`, and they SHALL nest: `/MediaBox` ⊇ `/BleedBox` ⊇ `/TrimBox`, with `/ArtBox` equal to
`/TrimBox` and `/CropBox` equal to `/MediaBox`.

There are three areas, and each is the answer to a different question. The **sheet** is what goes
through the press. The **bleed** is how far the artwork may run; the room between it and the sheet
edge is where printer's marks go. The **trim** is where the guillotine cuts, and is the page the
caller asked for.

Each edge SHALL be inset by its own margin. `/TrimBox` Y1 is the *bottom* edge in PDF space, so the
bottom margins belong there and the top margins come off Y2 — the arithmetic used to have the two
the other way round, which no page with an even margin could show.

`/ArtBox` equals `/TrimBox` deliberately: `/ArtBox` bounds the meaningful content, which for a
designed page is the page.

#### Scenario: The boxes of a trimmed page

- **WHEN** an A5 page with a 3mm trim margin on every edge is saved
- **THEN** `/MediaBox` measures A5 plus the bleed and the mark allowance on each edge
- **AND** `/BleedBox` is that box inset by the mark allowance
- **AND** `/TrimBox` and `/ArtBox` are `/BleedBox` inset by the bleed, and measure A5

#### Scenario: The boxes nest

- **WHEN** a page with a trim margin is saved
- **THEN** `/TrimBox` lies within `/BleedBox`, which lies within `/MediaBox`

#### Scenario: Each edge is inset by its own margin

- **WHEN** a page is given different trim margins at its top and bottom
- **THEN** `/TrimBox`'s top edge is one top margin below the sheet's top edge, which is where the
  drawing origin is
- **AND** its bottom edge is one bottom margin above the sheet's bottom edge

#### Scenario: An untrimmed page gains no boxes

- **WHEN** a page with no trim margin is saved
- **THEN** it carries no `/TrimBox`, `/BleedBox` or `/ArtBox` of its own
- **AND** its media box is the size the page was asked for

### Requirement: The sheet leaves room outside the bleed for printer's marks

`PdfPage.MarkMargins` SHALL give the room on the sheet outside the bleed, and SHALL default to 5mm
on each edge. It SHALL apply only to a page that has a trim margin.

Without it there is no such room: the library used to write `/BleedBox` equal to `/MediaBox`, which
satisfies the nesting rule and leaves nowhere for a crop mark to go. A press needs somewhere to put
its marks, and the artwork needs somewhere to stop.

Setting it to zero SHALL take the room away, reproducing exactly the boxes the library wrote before
crop marks existed — which is the setting for a caller whose downstream tooling expects them.

#### Scenario: The room is outside the bleed

- **WHEN** a trimmed page is saved
- **THEN** `/BleedBox` is `/MediaBox` inset by the mark allowance on each edge

#### Scenario: A page with no bleed is untouched by it

- **WHEN** a page with no trim margin is saved
- **THEN** the mark allowance does nothing to it and no boxes are written

#### Scenario: Clearing the allowance gives back the old boxes

- **WHEN** a trimmed page's mark allowance is set to zero and it is saved
- **THEN** `/BleedBox` equals `/MediaBox`
- **AND** `/TrimBox` is `/MediaBox` inset by the bleed alone

### Requirement: A trimmed page is given crop marks

Saving a page that has both a bleed and a mark allowance SHALL draw the eight standard crop marks in
the room outside the bleed, and `PdfPage.DrawCropMarks` SHALL be public so a caller can draw them
elsewhere.

Two marks meet at each corner of the trimmed page, one on each of its edges, and each runs outward
from the bleed to the edge of the sheet. None crosses the bleed, so none can be mistaken for artwork
or land on any part of the page that survives the cut.

The marks SHALL be drawn once however many times the document is saved.

#### Scenario: The eight marks

- **WHEN** a trimmed page with a mark allowance is saved
- **THEN** it carries eight marks, two at each corner

#### Scenario: The marks line up with the cuts

- **WHEN** those marks are read back
- **THEN** four lie on the two horizontal cuts and four on the two vertical cuts
- **AND** every one of them lies wholly outside `/BleedBox`

#### Scenario: No room means no marks

- **WHEN** a trimmed page whose mark allowance is zero is saved
- **THEN** no marks are drawn
- **AND** asking for them explicitly throws, saying that `MarkMargins` leaves no room for them

#### Scenario: No bleed means no marks

- **WHEN** a page with no trim margin is saved
- **THEN** no marks are drawn
- **AND** asking for them explicitly throws, saying that the page has no `TrimMargins`

#### Scenario: Saving twice draws them once

- **WHEN** a document containing a trimmed page is saved twice
- **THEN** the second file carries eight marks, not sixteen

### Requirement: Content drawn into the bleed is kept

Content drawn outside the trimmed page but within the bleed SHALL be written to the content stream
and SHALL NOT be clipped away.

The whole purpose of a bleed is content that survives as far as its edge so that a cut a fraction
off the mark still lands on ink.

#### Scenario: A band bled off an edge reaches the bleed

- **WHEN** a filled band is drawn from `(-3mm, -3mm)` across the full width of a page with a 3mm
  trim margin
- **THEN** the operators written for it reach the edge of the bleed box

#### Scenario: A bled page rasterizes with ink to the edge of its bleed

- **WHEN** such a page is rasterized
- **THEN** the pixels at the edge of the bleed carry the band's colour rather than the paper's
- **AND** the pixels in the mark allowance outside it carry the paper's

### Requirement: A trimmed page is measured in points

`TrimMargins` SHALL be used only with a page whose `XGraphics` measures in points.

This is a restriction the implementation already enforces; it is written down so a caller meets it
as documentation rather than as an assertion failure in a debug build.

#### Scenario: The restriction is stated

- **WHEN** a caller consults the documentation of `TrimMargins`
- **THEN** it states that the page unit must be points

### Requirement: A MigraDoc document can be rendered onto a trimmed page

Rendering a MigraDoc document onto a page the caller has created and given a trim margin SHALL lay
the document out to the trimmed page, leaving the caller free to draw into the bleed around it.

MigraDoc's own `PdfDocumentRenderer` creates each page itself and sets no trim margin, so this route
is the one that exists. It is specified because it works and nothing says so.

#### Scenario: A document laid out on a trimmed page

- **WHEN** a page is created with a trim margin, an `XGraphics` is opened on it, and a MigraDoc
  document is rendered to that surface a page at a time
- **THEN** the document's margins are measured from the trimmed page, not from the sheet
- **AND** the saved page carries the boxes of a trimmed page
