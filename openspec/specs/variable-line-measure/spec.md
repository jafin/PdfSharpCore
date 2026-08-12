# Variable line measure

## Purpose

`XTextFormatter` works out the room available to each line from where that line sits, rather than
once for the whole block. Before this, a block had one measure and every line in it got that measure;
a drop cap, or anything else standing inside the text, had nowhere to be expressed.

A per-line measure names two things and it is worth keeping them apart: where a line starts, and
where it must stop. They are both counted from the column's left edge, because that is what
`HorizontalAlignLine` already subtracts an indent from - a reservation on the left therefore moves
the start and leaves the limit alone, rather than narrowing the width.

A block with nothing narrowing it lays out exactly as it did. That is not a hope: it is pinned byte
for byte across the seventeen ways the formatter can be asked to lay text out.

The MigraDoc equivalent is `shape-side-wrap`, which reaches the same conclusion through `Area`
rather than through the formatter. Different engines and different code, deliberately the same idea.

## Requirements


### Requirement: The width available to a line may depend on where the line sits

`XTextFormatter` SHALL determine the horizontal extent available to each line from that line's
vertical position within the layout, rather than from one extent shared by the whole block.

Every existing behaviour — column breaking, justification, alignment, truncation, vertical overflow
— SHALL be measured against the extent of the line it applies to.

This is stated as a capability of its own because a drop cap is not the only thing that needs it. A
shape that text flows beside needs exactly the same, and two implementations of it would disagree.

#### Scenario: A narrowed line breaks sooner

- **WHEN** a block of text is laid out with the first two lines narrowed
- **THEN** those lines carry fewer words than they would at the block's full width
- **AND** the words displaced by the narrowing appear on later lines rather than being dropped

#### Scenario: Justification fills the line it is on

- **WHEN** justified text is laid out with some lines narrower than others
- **THEN** each justified line reaches the right edge of its own extent
- **AND** no line is spaced to an extent it does not have

#### Scenario: Alignment follows the line's own extent

- **WHEN** right-aligned text is laid out with some lines narrower than others
- **THEN** each line ends at the right edge of its own extent

### Requirement: A block with nothing narrowing it is laid out exactly as before

Where no line's extent is reduced, `XTextFormatter` SHALL produce the same layout, and the same
bytes, as it produced before line extents could vary.

This is what keeps a change to the heart of the layout loop from touching every document the
library has ever written.

#### Scenario: Unchanged output for ordinary text

- **WHEN** a document laid out through `XTextFormatter` with no drop cap is saved
- **AND** the same document is laid out and saved before this change
- **THEN** the two are byte for byte the same

#### Scenario: Columns are unaffected

- **WHEN** text is flowed into several columns with nothing narrowing any line
- **THEN** the column breaks fall exactly where they fell before
