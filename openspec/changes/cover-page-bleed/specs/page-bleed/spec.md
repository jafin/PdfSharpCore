## ADDED Requirements

### Requirement: A trim margin moves the drawing origin to the trimmed page

Where `PdfPage.TrimMargins` is set, the origin of the coordinate space `XGraphics` draws in SHALL be
the top-left corner of the **trimmed** page, not of the sheet.

A caller who never draws outside the trimmed page therefore writes exactly the same code as on a
page with no trim margin at all. Bleeding is reaching past the origin, not rebuilding the page.

#### Scenario: Drawing at the origin lands on the trim corner

- **WHEN** a page has a trim margin of 3mm on every edge and a mark is drawn at `(0, 0)`
- **THEN** it lands on the trim corner, 3mm in from the sheet's edge on both axes

#### Scenario: A negative coordinate reaches into the bleed

- **WHEN** a rectangle is drawn from `(-3mm, -3mm)` on a page with a 3mm trim margin
- **THEN** its corner is flush with the corner of the sheet

#### Scenario: The same drawing is unmoved by a trim margin

- **WHEN** the same content is drawn onto two pages of the same trimmed size, one with a trim margin
  and one without
- **THEN** the content occupies the same position relative to the trimmed page on both

### Requirement: A trim margin does not change the size of the page a caller draws on

`PdfPage.Width` and `PdfPage.Height` SHALL continue to report the size of the trimmed page. The
sheet is larger by the trim margins, and that difference SHALL be visible only in the page boxes.

Stated because the alternative — a page that silently grows when a margin is set — would move every
right-aligned and bottom-aligned thing on it.

#### Scenario: Width and height are the trimmed size

- **WHEN** an A5 page is given a trim margin of 3mm on every edge
- **THEN** `Width` and `Height` still report A5

#### Scenario: The sheet is larger than the page

- **WHEN** that page is saved
- **THEN** the media box is larger than the trimmed page by the trim margins on each edge

### Requirement: A trimmed page is saved with its five boxes

Saving a page with a trim margin SHALL write `/MediaBox`, `/CropBox`, `/BleedBox`, `/TrimBox` and
`/ArtBox`.

`/TrimBox` SHALL be the trimmed page: the media box inset by the trim margin on each edge. `/ArtBox`
SHALL match it. The boxes SHALL nest — every box SHALL lie within `/MediaBox`, and `/TrimBox` SHALL
lie within `/BleedBox`.

#### Scenario: The boxes of a trimmed page

- **WHEN** an A5 page with a 3mm trim margin on every edge is saved
- **THEN** `/MediaBox` measures A5 plus 3mm on each edge
- **AND** `/TrimBox` and `/ArtBox` are both that box inset by 3mm on each edge
- **AND** `/CropBox` and `/BleedBox` are present

#### Scenario: The boxes nest

- **WHEN** a page with a trim margin is saved
- **THEN** `/TrimBox` lies within `/BleedBox`, which lies within `/MediaBox`

#### Scenario: An untrimmed page gains no boxes

- **WHEN** a page with no trim margin is saved
- **THEN** it carries no `/TrimBox`, `/BleedBox` or `/ArtBox` of its own

### Requirement: Content drawn into the bleed is kept

Content drawn outside the trimmed page but within the sheet SHALL be written to the content stream
and SHALL NOT be clipped away.

The whole purpose of a bleed is content that survives as far as the sheet edge so that a cut a
fraction off the mark still lands on ink.

#### Scenario: A band bled off an edge reaches the sheet

- **WHEN** a filled band is drawn from `(-3mm, -3mm)` across the full width of a page with a 3mm
  trim margin
- **THEN** the operators written for it reach the edge of the media box

#### Scenario: A bled page rasterizes with ink to its edge

- **WHEN** such a page is rasterized
- **THEN** the pixels at the edge of the sheet carry the band's colour rather than the paper's

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
