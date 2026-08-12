## ADDED Requirements

### Requirement: Text can be added to a path as geometry

`XGraphicsPath.AddString` SHALL add the outlines of the glyphs of the string to the path, as
figures made of lines and Bézier curves in the path's coordinate space.

The resulting path SHALL be usable everywhere any other path is: filled with a brush including a
gradient brush, stroked with a pen, intersected as a clip, or widened.

This is the whole reason the method is worth implementing. Stroking or filling text alone is already
possible — `DrawString` with a pen, a brush, or both selects the PDF text rendering mode — and a
caller who wants only outlined text should be told to use that instead.

#### Scenario: The path is not empty

- **WHEN** `AddString` is called with a non-empty string, a resolvable font family and a positive
  em size
- **THEN** the path contains at least one figure
- **AND** its bounds are approximately the size `XGraphics.MeasureString` reports for the same text
  and font

#### Scenario: Filled with a gradient

- **WHEN** a path built by `AddString` is filled with an `XLinearGradientBrush`
- **THEN** the glyphs are painted with the gradient across them, which no `DrawString` overload can
  produce

#### Scenario: Used as a clip

- **WHEN** a path built by `AddString` is passed to `XGraphics.IntersectClip` and a photograph is
  then drawn over the same area
- **THEN** the photograph appears only within the glyph shapes

#### Scenario: An empty string adds nothing

- **WHEN** `AddString` is called with an empty string
- **THEN** the path is unchanged and no exception is thrown

#### Scenario: Both outline formats are served

- **WHEN** `AddString` is called for a family whose outlines are TrueType, and again for a family
  whose outlines are PostScript (CFF)
- **THEN** both produce a non-empty path

### Requirement: Layout of the added glyphs follows the string format

`AddString` SHALL position the glyphs according to the `XStringFormat` it is given, in the same way
`XGraphics.DrawString` positions them for the same rectangle or point and format.

#### Scenario: Alignment within a rectangle

- **WHEN** the same string is added to a path with `XStringFormats.TopLeft` and again with
  `XStringFormats.TopRight`, using the same layout rectangle
- **THEN** the bounds of the two paths differ in their horizontal position by the difference between
  the rectangle's width and the measured width of the text

#### Scenario: The path agrees with what DrawString would draw

- **WHEN** a string is drawn with `DrawString` at a point and the same string is added to a path at
  the same point and filled
- **THEN** the two renderings occupy the same place on the page to within a rounding tolerance

### Requirement: Glyph outlines come from a backend

The core package SHALL NOT acquire a font-rasterizing dependency in order to produce outlines.
Outlines SHALL be supplied through a registered provider, in the same manner as the font resolver
and the image source.

Where no provider is registered, `AddString` SHALL throw an `InvalidOperationException` whose
message names the property to set and the packages that supply an implementation — matching what
`GlobalFontSettings.FontResolver` and `ImageSource.ImageSourceImpl` already do when unset.

#### Scenario: Unregistered provider is reported, not ignored

- **WHEN** `AddString` is called with no outline provider registered
- **THEN** an `InvalidOperationException` is thrown
- **AND** its message names the property to set and at least one package that provides an
  implementation
- **AND** nothing is added to the path

#### Scenario: Both shipped backends supply one

- **WHEN** either the Skia or the ImageSharp backend is registered
- **THEN** `AddString` produces a non-empty path for a resolvable family

#### Scenario: Registering does not disturb the other seams

- **WHEN** an outline provider is registered
- **THEN** the font resolver and image source already registered are unchanged

### Requirement: A caller who only wants outlined text is not sent this way

The documentation of `AddString` SHALL state that stroking text needs no path, and name the
`DrawString` overload that takes a pen.

#### Scenario: The cheaper route is named

- **WHEN** a reader consults the XML documentation of `AddString`
- **THEN** it names `DrawString` with a pen as the way to stroke text without building a path
