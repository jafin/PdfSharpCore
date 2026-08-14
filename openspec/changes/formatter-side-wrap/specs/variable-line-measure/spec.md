## ADDED Requirements

### Requirement: A line with no room is moved past the obstruction rather than filled

Where the room available to a line is reduced to nothing, `XTextFormatter` SHALL move the line down
past what blocks it and lay it out at the first band below that has room, rather than placing text
on a line that has no room for it.

**At the first band that has room, not at the foot of what blocked it.** A block may hold more than
one obstacle and their reservations combine, so the band at the foot of one of them can be blocked
by another. The band SHALL therefore be measured again after each move, and the move repeated while
it is still blocked. Moving once and laying out unconditionally would put text in a blocked band —
the very thing this requirement exists to prevent — whenever two obstacles overlap in depth.

A measure that starts at or past its own limit is not a narrow line, and SHALL NOT be treated as
one. The two are different answers and only one of them can be drawn: a block that begins a line is
placed whether it fits or not — which is right for a word wider than its measure, and is what puts
text outside the column when the measure is nothing at all.

Each move SHALL advance by at least one line, so that an obstruction whose foot is level with the
band it blocks cannot be asked about indefinitely. This is what makes the repetition above safe to
require: every pass moves down by a bounded-below amount, so the worst case is the line-at-a-time
advance the move replaces rather than a loop that does not end.

Where the move runs out of layout, the text SHALL be treated as not fitting, exactly as text that
runs past the last column is today.

This is reachable now and not only by the wrapping this change goes on to add: a drop cap is scaled
to its own depth and nothing holds its width to the measure, so a deep cap in a narrow column
leaves the lines beside it no room at all.

#### Scenario: A cap too wide for its column keeps its text inside the column

- **WHEN** a drop cap is set into a column narrower than the cap
- **THEN** every line of the text begins inside the column
- **AND** no line is drawn across the cap

#### Scenario: Text with no room beside an obstruction begins below it

- **WHEN** a line's band has no room in it at all
- **THEN** the first line of text sits below the foot of what blocked it

#### Scenario: Moving past an obstruction loses no text on the way

- **WHEN** text is laid out where the opening lines have no room in them
- **THEN** what is set is the beginning of the text, in order and unbroken
- **AND** any text that does not fit is dropped from the end, as truncated text always is

#### Scenario: Two obstructions overlapping in depth are both cleared

- **WHEN** a band has no room in it, and the band at the foot of what blocks it has no room either
- **THEN** the line is laid out lower still, at the first band that has room
- **AND** no text is drawn in either blocked band

#### Scenario: An obstruction with nothing below it stops rather than loops

- **WHEN** a band has no room in it and there is no room below it either
- **THEN** the layout finishes and the text that could not be placed is dropped
- **AND** this holds whether or not vertical overflow is allowed
