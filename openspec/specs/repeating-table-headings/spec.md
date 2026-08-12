# Repeating table headings

## Purpose

Which rows of a MigraDoc table are drawn again at the top of the table's continuation on each later
page. They are the rows carrying `HeadingFormat`, beginning at the table's first row and running
unbroken — the rule the renderer has always followed, written down here where a caller can find it.

Also covers what happens when a document marks a heading row that cannot repeat, such as the column
names below a title band. That used to do nothing at all: the document rendered, and the heading was
simply missing from the second page onwards. It is now refused with an `InvalidOperationException`
naming the row and stating the rule, raised while the document is being formatted so that nothing
has been written when the caller sees it.

## Requirements

### Requirement: A table's heading is the unbroken run of marked rows at its top

The rows a MigraDoc table repeats onto every page it continues onto SHALL be the rows carrying
`HeadingFormat`, beginning at the first row of the table and running unbroken.

This is the rule the renderer already follows. It is stated here because it has never been written
down anywhere a caller would find it, and because the requirement below turns breaking it from
silence into an error.

#### Scenario: One heading row repeats

- **WHEN** a table whose first row carries `HeadingFormat` is long enough to cross a page break
- **THEN** that row is drawn again at the top of the table's continuation on each later page

#### Scenario: Two heading rows repeat together

- **WHEN** a table's first two rows both carry `HeadingFormat` and the table crosses a page break
- **THEN** both rows are drawn again, in order, at the top of the continuation

#### Scenario: A table with no marked rows repeats nothing

- **WHEN** a table with no row carrying `HeadingFormat` crosses a page break
- **THEN** the continuation begins with the next data row and no heading is repeated

#### Scenario: A table that is entirely heading repeats nothing

- **WHEN** every row of a table carries `HeadingFormat`
- **THEN** no row is repeated, since a heading that is the whole table has nothing to head

### Requirement: A heading row that cannot repeat is refused

Formatting the document SHALL throw `InvalidOperationException` where a row carries `HeadingFormat`
but is not part of the unbroken run beginning at the table's first row.

The message SHALL name the index of the offending row and state the rule.

A heading that cannot repeat is always a mistake — the caller asked for a repeating heading and the
document silently does not have one. There is no reading under which the current behaviour is what
was wanted, and the failure is invisible until somebody turns to the second page.

#### Scenario: A title band above the column names

- **WHEN** a table's first row does not carry `HeadingFormat` and its second row does
- **AND** the document is rendered
- **THEN** an `InvalidOperationException` is thrown
- **AND** its message names row 1 and states that heading rows must begin at the first row

#### Scenario: A gap in the run

- **WHEN** rows 0 and 2 carry `HeadingFormat` and row 1 does not
- **THEN** an `InvalidOperationException` is thrown naming row 2

#### Scenario: A heading marked in the middle of the data

- **WHEN** a row in the body of a long table carries `HeadingFormat` and the first row does not
- **THEN** an `InvalidOperationException` is thrown naming that row

#### Scenario: A correct table is unaffected

- **WHEN** a table's heading rows form an unbroken run from the first row
- **THEN** the document renders and no exception is thrown

### Requirement: The error is raised where the caller can act on it

The exception SHALL be thrown during document formatting, before any page is written, so that a
caller never receives a partially rendered document because of it.

#### Scenario: Nothing is written before the throw

- **WHEN** rendering a document whose table has a misplaced heading row is attempted, saving to a
  stream
- **THEN** the exception is thrown
- **AND** no PDF has been written to the stream
