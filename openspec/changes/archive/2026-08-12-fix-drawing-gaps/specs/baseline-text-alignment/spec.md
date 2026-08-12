## ADDED Requirements

### Requirement: A baseline-aligned string accepts a layout rectangle of any height

`XGraphics.DrawString` SHALL place the baseline of the text on the **top edge** of the layout
rectangle, and SHALL ignore the rectangle's height, when the `XStringFormat` it is given has a
`LineAlignment` of `XLineAlignment.BaseLine`.

Passing a rectangle with a non-zero height SHALL NOT throw.

The height is meaningless rather than wrong: when the baseline is the anchor there is nothing for
the text to be positioned within, so a height can only be surplus information. Refusing it forces
every caller who wants a baseline to carry a second, flattened rectangle beside the one they
already have.

#### Scenario: A rectangle with height no longer throws

- **WHEN** a string is drawn into a rectangle of height 20 with a format whose `LineAlignment` is
  `BaseLine`
- **THEN** no exception is thrown
- **AND** the baseline of the drawn text sits on the top edge of that rectangle

#### Scenario: Height does not move the text

- **WHEN** the same string is drawn twice with the same `BaseLine` format, into two rectangles with
  the same origin and width but different heights
- **THEN** the text is drawn in the same place both times

#### Scenario: A zero height rectangle is unchanged

- **WHEN** a string is drawn with a `BaseLine` format into a rectangle of height 0
- **THEN** it is drawn exactly where it was drawn before this change

#### Scenario: Horizontal alignment still applies

- **WHEN** a string is drawn with `XStringFormats.BaseLineRight` into a rectangle with a width
- **THEN** the text ends at the rectangle's right edge and its baseline sits on the top edge

### Requirement: The default string format works with any rectangle

`XStringFormats.Default` SHALL work with a layout rectangle of any height, on every `DrawString`
overload that takes one. It is `BaseLineLeft`, so the requirement above governs it.

Called out separately because it is the trap: the format a caller reaches for when they do not want
to think about formats is the one that used to throw.

#### Scenario: Default format into an ordinary rectangle

- **WHEN** a string is drawn into a rectangle of height 20 with `XStringFormats.Default`
- **THEN** no exception is thrown and the text is drawn with its baseline on the rectangle's top edge

### Requirement: Adding baseline-aligned text to a path behaves the same way

`XGraphicsPath.AddString` SHALL apply the same rule: with a `BaseLine` line alignment it SHALL place
the baseline on the rectangle's top edge and ignore the height, rather than throwing.

The two guards are separate code today and would otherwise disagree.

#### Scenario: The path guard matches the graphics guard

- **WHEN** text is added to a path with a `BaseLine` format and a rectangle of non-zero height
- **THEN** no exception is thrown
- **AND** the glyphs are placed as `DrawString` would place them for the same rectangle and format

## REMOVED Requirements

### Requirement: BaseLine alignment requires a zero-height rectangle

**Reason**: The restriction gives the caller nothing. A height cannot conflict with a baseline
anchor — it is simply unused — so rejecting it only forces callers to construct a second rectangle,
and it makes the default string format throw on the most natural overload.

**Migration**: None required. Code that passes a zero-height rectangle continues to behave
identically. Code that previously caught the `InvalidOperationException` can drop the handler; the
exception is no longer thrown for this reason.
