## ADDED Requirements

### Requirement: A drop cap and a caller's obstacles narrow the same lines

Where a block carries both a drop cap and obstacles supplied by the caller, every line SHALL be laid
out in the room left by all of them together.

The cap reserves its room the same way a caller's obstacle does, so a line standing against both is
narrowed by both rather than by whichever was considered last. Nothing about the cap's own behaviour
changes: it is still sized to its depth, placed by its ink, and set into the opening lines.

This is worth stating because the two reservations arrive by different routes — one the formatter
works out for itself, one the caller hands it — and a reader would otherwise be entitled to assume
they are handled by different machinery and might disagree.

#### Scenario: A cap and an obstacle both narrow an opening line

- **WHEN** a block is laid out with a drop cap and an obstacle standing against the right edge, at a
  band the cap also stands in
- **THEN** the lines in that band begin to the right of the cap and break before the obstacle

#### Scenario: A cap in a column an obstacle does not reach is narrowed only by the cap

- **WHEN** a two-column block is laid out with a drop cap and an obstacle standing wholly in the
  second column
- **THEN** the opening lines of the first column are narrowed by the cap alone

### Requirement: A drop cap reserves room in the first column only

The room a drop cap reserves SHALL fall in the first column, and no other column SHALL be narrowed
by it.

A cap belongs to the opening of the text. Reserving the same corner of every column would carve a
hole out of each of them, which is not what any caller asking for an initial letter means.

Stated here because the rule is presently a test on the column index and becomes a consequence of
where the cap's reserved region sits. The behaviour is the same either way, which is the point: it
must survive the change of mechanism.

#### Scenario: Later columns run the full measure

- **WHEN** a multi-column block is laid out with a drop cap
- **THEN** every line of the second and later columns begins at that column's left edge
