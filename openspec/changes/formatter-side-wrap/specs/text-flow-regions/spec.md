## ADDED Requirements

### Requirement: A caller can reserve regions of a block and have text flow around them

`XTextFormatter` SHALL accept obstacles from the caller and lay text out in the room left over,
line by line.

The caller supplies them because the caller drew whatever is standing there. The formatter is given
regions to avoid, not shapes to understand: it takes no wrap style, no floating and no element,
because those describe a shape in a document tree and there is no document tree here.

An obstacle SHALL narrow the lines whose band it stands in and SHALL NOT move the block's top down
or grow the layout rectangle. Text flows around the reservation; it is not displaced by it.

#### Scenario: A reservation on the left moves those lines right

- **WHEN** a block is laid out with a region reserved against its left edge
- **THEN** the lines whose band the region stands in begin to the right of it
- **AND** the lines below it begin at the block's left edge

#### Scenario: A reservation on the right shortens those lines

- **WHEN** a block is laid out with a region reserved against its right edge
- **THEN** the lines whose band the region stands in break before reaching it
- **AND** no text is drawn inside the region

#### Scenario: Nothing reserved lays out exactly as before

- **WHEN** a block is laid out with no obstacle supplied
- **THEN** the output is byte for byte what it was before obstacles existed

#### Scenario: Reserved room does not move the text down

- **WHEN** a block is laid out with a region reserved inside it
- **THEN** the first line sits where it would have sat with nothing reserved

### Requirement: The room available to a line is asked for as a set of free spans

The room a line has SHALL be determined by asking, for that line's band, which horizontal spans are
free — not by a single pair of numbers describing one span.

A band is a **line box**, a top and a bottom, rather than a baseline: a line whose baseline clears an
obstacle can still have ascenders inside it, and testing at the baseline would let them collide.

This is stated as a requirement rather than left to the design because it is what decides whether an
obstacle that is not a rectangle can ever be added. A geometry that can only answer with one span
cannot describe a shape with a gap in it, and the answer would have to be widened at the layer that
is most expensive to change.

#### Scenario: An obstacle standing clear of both edges leaves two free spans

- **WHEN** the free spans are asked for at a band an obstacle stands in the middle of
- **THEN** two spans are answered, one either side of it

#### Scenario: A band is judged by the line's box and not its baseline

- **WHEN** an obstacle's foot falls between a line's top and its baseline
- **THEN** that line is treated as obstructed

### Requirement: A line is given one span, and it is the widest free one

Where a band has more than one free span, the line SHALL be laid out in the widest of them and the
others SHALL be left empty.

This is a decision and not a limitation. Filling several spans means one logical line spanning them,
which justification, alignment and truncation all currently assume never happens. `shape-side-wrap`
chose the same rule for MigraDoc, and the two engines are kept deliberately alike here.

The geometry above still answers with every free span, so filling them is a change to the layout
loop rather than to the type the geometry is expressed in.

#### Scenario: Text takes the roomier side of an obstacle

- **WHEN** an obstacle stands nearer the left edge of a block than the right
- **THEN** the lines beside it are laid out to its right
- **AND** the narrow span to its left is left empty

### Requirement: An obstacle holds text off itself by its own padding

Padding SHALL be a property of the obstacle rather than of the formatter, and SHALL be counted as
part of the region text may not enter.

A margin is a fact about the thing being avoided: two obstacles in one block can want different
distances, and a single setting on the formatter could not express that. MigraDoc carries four such
distances on `WrapFormat` and they earn their keep.

#### Scenario: Padding widens the room reserved

- **WHEN** a block is laid out with an obstacle carrying padding
- **THEN** the lines beside it clear it by at least that padding

### Requirement: Obstacles are given in the formatter's own layout coordinates

An obstacle SHALL be positioned relative to the layout rectangle, unrotated, in the frame the
formatter lays out in.

Where `Rotation` is not zero, supplying an obstacle SHALL be refused with an error naming both
frames, rather than the obstacle being interpreted in either of them.

The two readings — the rotated frame and the page frame — put text in visibly different places, and
nothing in the call can say which was meant. A wrong page that looks deliberate is the failure this
avoids. Refusal is also the reversible direction: it can be loosened later without moving text on
any document that already exists.

#### Scenario: An obstacle rotates with the text it reserves room in

- **WHEN** a block with an obstacle is drawn, and drawn again with the caller's own rotation applied
  to the drawing surface before the call — the formatter's own rotation being zero in both
- **THEN** the text breaks in the same places in both

This is the route that stays open, and it is the reason the refusal below costs nothing. A transform
the caller applies to the surface rotates the laid-out text and the region reserved inside it
together, because both were worked out in the same unrotated frame.

#### Scenario: An obstacle supplied under rotation is refused

- **WHEN** an obstacle is supplied to a formatter whose rotation is not zero
- **THEN** the call fails with an error naming the coordinate frame obstacles are given in

### Requirement: An obstacle is clipped to the column of the line being measured

Where text is laid out in several columns, an obstacle SHALL reduce each column it overlaps
independently, according to the part of it standing in that column.

No special case is needed for the gutter, because no text was ever drawn there. Excluding columns
from obstacles would mean writing a rejection for something that costs nothing to support.

#### Scenario: An obstacle spanning two columns narrows both

- **WHEN** a two-column block is laid out with an obstacle straddling the gutter
- **THEN** the lines it overlaps in each column are narrowed by the part of it in that column
