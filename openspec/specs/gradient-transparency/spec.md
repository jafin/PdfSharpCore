# Gradient transparency

## Purpose

The alpha component of the colours given to `XLinearGradientBrush` and `XRadialGradientBrush`, and
what the library writes to the file to honour it. A gradient between a translucent colour and an
opaque one fades out as well as across, so what lies beneath it shows through in the proportion the
colours ask for.

Also covers the shading function's colour ramp, which used to carry alpha as a fourth value in an
RGB space and so produced a function wider than the space it fed. Alpha now goes into a luminosity
soft mask above the shading, which is where the format puts it and where a reader of a produced file
will expect to find it. A gradient both of whose colours are opaque is written exactly as it was
written before, with no mask, no extended graphics state and no transparency group.

## Requirements

### Requirement: A gradient between translucent colours fades out as well as across

`XLinearGradientBrush` and `XRadialGradientBrush` SHALL honour the alpha component of their colours.
Where a colour's alpha is below fully opaque, what lies beneath the gradient SHALL show through in
that proportion.

The alpha ramp SHALL follow the same geometry as the colour ramp: the same axis or circles, the same
extent, the same interpolation.

#### Scenario: Transparent to opaque over a filled rectangle

- **WHEN** a red rectangle is filled, and over it a rectangle is filled with a linear gradient whose
  first colour is fully transparent black and whose second is fully opaque black
- **THEN** the region under the first colour is red
- **AND** the region under the second colour is black
- **AND** the region between the two is a blend, darkening along the gradient's axis

#### Scenario: A half transparent gradient over white

- **WHEN** a gradient from 50% alpha black to 50% alpha black is drawn over white
- **THEN** the result is a uniform mid grey rather than black

#### Scenario: Radial gradients behave the same way

- **WHEN** an `XRadialGradientBrush` whose outer colour is fully transparent is drawn over an image
- **THEN** the image is visible at the outer edge of the gradient and progressively obscured towards
  its centre

### Requirement: A gradient's colour ramp carries one value per colour component

The interpolation function of a shading SHALL return exactly as many values as the shading's colour
space has components: three for `/DeviceRGB`, four for `/DeviceCMYK`.

An RGB ramp was given a fourth value, the colour's alpha, which is not a colour component. A
function wider than the space it feeds is malformed, and a conformant reader answers it by painting
nothing at all — so **no gradient this library has ever written appears in Ghostscript**. Alpha now
goes where alpha belongs, into the soft mask above.

#### Scenario: An RGB ramp has three values

- **WHEN** a document containing a `/DeviceRGB` gradient is saved
- **THEN** the `/C0` and `/C1` entries of its shading function each hold three numbers

#### Scenario: A CMYK ramp is unchanged

- **WHEN** the document's colour mode is CMYK
- **THEN** the ramp holds four numbers, which is what that colour space has always required

### Requirement: An opaque gradient carries no transparency machinery

A gradient both of whose colours are fully opaque SHALL be written to the content stream without a
soft mask, an extended graphics state, or a transparency group.

This is what keeps the change from touching every gradient ever written by the library.

#### Scenario: No transparency machinery for an opaque gradient

- **WHEN** a document containing only fully opaque gradients is saved
- **THEN** its content streams contain no `/SMask` entry introduced by the gradient
- **AND** no transparency group form XObject is added to the document
- **AND** its content stream, shading geometry and pattern matrices match what the library produced
  before this change, byte for byte — the ramp above being the one and only difference

### Requirement: Transparency is realised as a luminosity soft mask

Where alpha is present, the shading pattern SHALL be drawn with an extended graphics state whose
`/SMask` is a soft mask dictionary of subtype `/Luminosity`, whose group is a transparency group
form XObject in `/DeviceGray` painting a shading of the same geometry whose grey values are the
alpha values of the gradient's colours.

Stating the mechanism rather than only the appearance is deliberate: it is the mechanism a reader
of a produced file will find, and the one every other PDF producer uses.

#### Scenario: The mask dictionary is well formed

- **WHEN** a gradient with alpha is drawn and the document saved
- **THEN** an `ExtGState` is referenced whose `/SMask` has `/S /Luminosity` and a `/G` entry
- **AND** the object `/G` names is a form XObject with `/Group << /S /Transparency /CS /DeviceGray >>`
- **AND** that form's content paints a shading whose colour values correspond to the source
  gradient's alpha values

#### Scenario: The file opens in a conformant reader

- **WHEN** a document containing a gradient with alpha is rasterized by Ghostscript
- **THEN** rasterization succeeds without error
- **AND** the rendered pixels show the blend rather than a flat band

### Requirement: Alpha in a gradient does not disturb the surrounding graphics state

Drawing a gradient with alpha SHALL leave the graphics state as it was found. Anything drawn after
it SHALL be unaffected by the mask, the extended graphics state, or the transparency group used to
draw it.

#### Scenario: The next shape is not masked

- **WHEN** a gradient with alpha is drawn, and an opaque black rectangle is drawn after it
- **THEN** the rectangle is fully opaque black

#### Scenario: Two gradients on one page

- **WHEN** two gradients with different alpha ramps are drawn on the same page
- **THEN** each is masked by its own ramp and neither is affected by the other
