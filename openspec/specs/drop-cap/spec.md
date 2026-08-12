# Drop cap

## Purpose

An initial letter set into the opening lines of a block, with those lines shortened to leave room for
it. `XTextFormatter.DropCap` takes an `XDropCap` naming a font and a depth in **lines**, and the
formatter takes the first character of the text, scales it so its foot rests on the baseline of the
last line it is set into, reserves the room and narrows the lines that stand against it.

The depth is in lines rather than as a font size because lines are what the surrounding text is
measured in, and a size implies a depth that is almost never a whole number of them.

The cap is placed by the glyph's **ink** where `GlobalFontSettings.GlyphOutlineProvider` is
registered, so it sits flush with the margin rather than a side bearing's width inside it. Where no
provider is registered it is placed by the advance instead: a drop cap does not require a backend
seam, it is only better with one.

This is `XTextFormatter`, not MigraDoc. It rests on `variable-line-measure`, which is what lets the
room available to a line depend on where that line sits.

## Requirements


### Requirement: An initial letter can be set into the opening lines of a block

`XTextFormatter` SHALL accept a drop cap: the first character of the text, drawn larger than the
body and set into the opening lines, with those lines shortened to leave room for it.

The caller SHALL say how many lines deep the cap sits. The formatter SHALL size the cap to that
depth, reserve the room, and draw the cap — without the caller measuring anything.

Doing it by hand today means drawing the initial separately and then adding one word at a time to a
probe rectangle until the answer stops fitting. That loop is thirty lines, re-measures the text once
per word, and is wrong whenever the cap's depth is not an exact multiple of the line height.

#### Scenario: The opening lines leave room for the cap

- **WHEN** text is drawn with a drop cap three lines deep
- **THEN** the first three lines begin to the right of the cap
- **AND** the fourth and every later line begin at the block's left edge

#### Scenario: The cap is drawn

- **WHEN** text is drawn with a drop cap
- **THEN** the first character appears once, at the cap's size
- **AND** it does not also appear at body size at the start of the text

#### Scenario: The cap sits on the last reserved line's baseline

- **WHEN** a drop cap three lines deep is drawn
- **THEN** the foot of the cap rests on the baseline of the third line

#### Scenario: No drop cap leaves the text alone

- **WHEN** text is drawn with no drop cap set
- **THEN** every line begins at the block's left edge and the text is unchanged

### Requirement: The cap is aligned by its ink where the library can see its ink

The cap's left edge and the room reserved beside it SHALL be taken from the outlines of the glyph
rather than from its advance width, wherever a glyph outline provider is registered.

A letter's advance includes its side bearings, which is space the letter does not occupy. A display
letter set flush by advance looks indented; set flush by ink it looks flush, which is what a
typesetter means by flush.

#### Scenario: A cap with a side bearing sits flush

- **WHEN** a drop cap is set in a face whose capital has a left side bearing, with an outline
  provider registered
- **THEN** the ink of the cap begins at the block's left edge

#### Scenario: No provider is not an error

- **WHEN** a drop cap is drawn with no outline provider registered
- **THEN** the cap is drawn and the room is reserved, measured from the advance instead
- **AND** no exception is thrown

### Requirement: The text left of the cap is the text the caller gave

The drop cap SHALL consume the first character of the text and no more. The remainder SHALL be laid
out in full, in the order it was given, with nothing dropped and nothing repeated.

#### Scenario: Nothing is lost to the cap

- **WHEN** text is drawn with a drop cap
- **THEN** every word after the first character appears exactly once

#### Scenario: A short block still draws its cap

- **WHEN** the text is shorter than the cap is deep
- **THEN** the cap is drawn and the text is laid out beside it
- **AND** no exception is thrown

#### Scenario: An empty string draws no cap

- **WHEN** an empty string is drawn with a drop cap set
- **THEN** nothing is drawn and no exception is thrown
