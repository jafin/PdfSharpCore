## ADDED Requirements

### Requirement: Text can flow beside a shape

A shape SHALL be able to ask that the text around it flow beside it rather than above and below it.
The lines the shape stands against SHALL be shortened to clear it, and the lines above and below it
SHALL run the full measure.

Today a shape either interrupts the column across its full width or is ignored by the text and
overlapped by it. There is no third behaviour, so a sidebar, a pull quote with text down one side,
and an image with text beside it all have to be built by splitting the text into separate flows by
hand.

#### Scenario: Text runs down one side of a shape

- **WHEN** a shape half the width of the measure is placed against a run of text and asked to wrap
- **THEN** the lines level with the shape are shortened to clear it
- **AND** the lines above and below it run the full measure

#### Scenario: The text is not overlapped

- **WHEN** text flows beside a shape
- **THEN** no line is drawn across the shape's area

#### Scenario: Nothing is lost to the wrap

- **WHEN** text flows beside a shape
- **THEN** every word of the text appears exactly once
- **AND** the words displaced by the narrowing appear on later lines

### Requirement: The document chooses which side the text runs down

A shape SHALL be able to ask for the text on its left, on its right, on whichever side has the more
room, or on either side.

`Left` and `Right` SHALL name the side the **text** occupies, not the side the shape sits on.

Named explicitly because the opposite reading is equally natural, and a caller who guesses wrong
gets a page that looks deliberate and is backwards.

#### Scenario: Text on the left of the shape

- **WHEN** a shape is placed to the right of the measure and asks for the text on its left
- **THEN** the shortened lines begin at the measure's left edge and end before the shape

#### Scenario: Text on the right of the shape

- **WHEN** a shape is placed at the left of the measure and asks for the text on its right
- **THEN** the shortened lines begin after the shape and end at the measure's right edge

#### Scenario: The larger side is chosen

- **WHEN** a shape sits off-centre and asks for whichever side has the more room
- **THEN** the text runs down the wider of the two sides

### Requirement: The wrap distances hold the text off the shape

`WrapFormat.DistanceLeft`, `DistanceRight`, `DistanceTop` and `DistanceBottom` SHALL hold text away
from a side-wrapped shape on all four sides.

All four are public, settable and serialised today, and two of them have never meant anything,
because nothing ever put text beside a shape for them to hold off.

#### Scenario: A horizontal distance is honoured

- **WHEN** a shape wrapping on its left is given a left distance
- **THEN** the shortened lines end that distance short of the shape

#### Scenario: A distance of zero touches the shape

- **WHEN** a side-wrapped shape is given no distances
- **THEN** the text runs up to the edge of the shape

### Requirement: A shape that asks for no side wrap behaves exactly as it does today

A document in which no shape asks for a side wrap SHALL be laid out, and written, exactly as it was
before side wrapping existed.

This is what keeps a change in the heart of the layout from touching every document the library has
produced.

#### Scenario: An unchanged document

- **WHEN** a document whose shapes use the existing wrap styles is rendered and saved
- **THEN** its bytes match those produced before this change

#### Scenario: The existing styles keep their meanings

- **WHEN** a shape uses the wrap style that places it between its neighbours
- **THEN** the text continues to run above and below it and not beside it

### Requirement: A shape too tall to wrap falls back rather than misplacing the text

A side-wrapped shape that cannot be placed as an obstacle within its area SHALL instead be laid out
as though it had asked to be placed between its neighbours. It cannot be so placed where it is
taller than the area remaining, or where it would span a page break.

A predictable degradation is worth more than a wrong page. The alternative is an obstacle that
outlives the area holding it, and text laid out around a shape that is no longer there.

#### Scenario: A shape taller than its area

- **WHEN** a shape asking for a side wrap is taller than the area remaining on the page
- **THEN** it is placed as a full-width element instead
- **AND** the text around it is laid out above and below it, with nothing overlapped

### Requirement: A wrap style survives a round trip through MDDDL

A document written to MDDDL with a side wrap style and read back SHALL carry the same style.

The document object model's serialisation is generated, so a new enumeration value is not
automatically carried by it.

#### Scenario: A side wrap round-trips

- **WHEN** a document containing a side-wrapped shape is written to MDDDL and read back
- **THEN** the shape's wrap style is the one it was given
