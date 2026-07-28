# Spec — PDF dates do not survive a round trip

A date written by PdfSharpCore and read back is not the date that was written. Found while fixing
[#365](https://github.com/ststeiger/PdfSharpCore/issues/365), which is about *when* the modification
date is stamped; this is about *what* a date means once it is in a file.

| item | what | status |
|---|---|---|
| 1 | Offsets are dropped from three date forms that occur in the wild, giving the wrong instant | done, `fix/pdf-date-round-trip` |
| 2 | Dates shorter than a full timestamp are read as `DateTime.MinValue` or lose their time | done, with item 1 |
| 3 | A date cannot be read back as the value it was written from | done, with item 1 |

All three are built. What follows is the design as written; the notes marked *changed* are where it
departs from what was drafted.

---

## What the standard says

**A PDF date is local time, together with the offset that says which local time it is.** It is not
UT, and it is not a bare wall-clock reading either.

ISO 32000-1:2008 §7.9.4 gives the form

```
( D : YYYYMMDDHHmmSSOHH'mm' )
```

and defines `O` as "the relationship of local time to Universal Time (UT)":

> A plus sign (+) as the value of the O field signifies that local time is later than UT, a minus
> sign (-) that local time is earlier than UT, and the letter Z that local time is equal to UT.

Three further rules matter here:

> All fields after the year are optional. (The prefix D:, although also optional, is strongly
> recommended.)

> The default values for MM and DD are both 01; all other numerical fields default to zero values.

> If no UT information is specified, the relationship of the specified time to UT shall be
> considered to be GMT.

So `D:20240601120000+10'00'` is noon in a place ten hours ahead of UT — 02:00 UT. Writing `02:00`
with a `Z` records the same instant but not the same date: it says the document was touched at two
in the morning, which for a reader displaying local time of the author is the wrong answer.

### The trailing apostrophe

PDF 1.7 and earlier make the apostrophe after the minutes part of the syntax: `OHH'mm'`. ISO 32000-2
(PDF 2.0) drops it: `OHH'mm`. Both forms are in circulation, and a reader has to take either.

For writing, the older form is the safer one. Acrobat reads a date without the trailing apostrophe
by rounding the offset down to the whole hour, so `+05'30` is read as `+05'00` — the reason BFO
still emit the apostrophe against the newer standard. PdfSharpCore already writes it. Keep it.

Sources: [ISO 32000-1 §7.9.4 as quoted by
VeryPDF](https://www.verypdf.com/pdfinfoeditor/pdf-date-format.htm) ·
[coherentpdf](https://www.coherentpdf.com/cpdfmanual/cpdfmanualap1.html) · [BFO on the
apostrophe](https://bfo.com/blog/2023/01/17/odds_and_ends_dates_timezones_and_apostrophes/) · [PDF
Association issue 251, on dates with no offset](https://github.com/pdf-association/pdf-issues/issues/251)

---

## What PdfSharpCore does today

Measured against `PdfSharpCore/Pdf/PdfDate.cs` and `Parser.ParseDateTime`
(`PdfSharpCore/Pdf.IO/Parser.cs:1496`) on a machine at UT+10.

### Writing — correct

`PdfDate.ToString` renders the value and appends the offset from `ToString("zzz")`, which follows
the `DateTimeKind`.

| given | written | |
|---|---|---|
| `2024-06-01 12:00:00` Kind=Local | `D:20240601120000+10'00'` | correct |
| `2024-06-01 12:00:00` Kind=Utc | `D:20240601120000+00'00'` | correct; `Z` would be more usual, `+00'00'` is legal |
| `2024-06-01 12:00:00` Kind=Unspecified | `D:20240601120000+10'00'` | assumes local, which is the only sensible guess |

### Reading — wrong in six of thirteen forms

`ParseDateTime` requires the string to be at least 23 characters before it will look at the offset
at all, and at least 16 before it will look at the time. Everything it returns is converted to UT
and marked `DateTimeKind.Utc`.

| string | read as | correct | |
|---|---|---|---|
| `D:20240601120000+10'00'` | 02:00 Utc | 02:00 UT | ok |
| `D:20240601120000-05'30'` | 17:30 Utc | 17:30 UT | ok |
| `D:20240601120000Z` | 12:00 Utc | 12:00 UT | ok |
| `D:20240601120000` | 12:00 Utc | 12:00 UT | ok, GMT is the stated default |
| `D:20240601` | 00:00 Utc | 00:00 UT | ok |
| `D:20240601120000+10'00` | 12:00 Utc | 02:00 UT | **wrong** — PDF 2.0 form, 22 chars, offset dropped |
| `D:20240601120000+1000` | 12:00 Utc | 02:00 UT | **wrong** — no apostrophes, 21 chars, offset dropped |
| `D:20240601120000-0530` | 12:00 Utc | 17:30 UT | **wrong** — as above |
| `D:202406011200` | 00:00 Utc | 12:00 UT | **wrong** — 14 chars, time dropped |
| `D:202406` | `MinValue` | 2024-06-01 00:00 | **wrong** — DD defaults to 01 |
| `D:2024` | `MinValue` | 2024-01-01 00:00 | **wrong** — MM and DD default to 01 |
| `20240601120000+10'00'` | `MinValue` | 02:00 UT | **wrong** — the prefix is optional |
| `D:20240601120000+10'00'junk` | 02:00 Utc | — | tolerated, no rule either way |

The three dropped-offset rows are the serious ones. They are silent, and they are wrong by up to
fourteen hours. The PDF 2.0 form will only become more common.

### The round trip

| written from | file | read back | equal | same instant |
|---|---|---|---|---|
| 12:00 Local | `D:20240601120000+10'00'` | 02:00 Utc | no | yes |
| 12:00 Utc | `D:20240601120000+00'00'` | 12:00 Utc | yes | yes |
| 12:00 Unspecified | `D:20240601120000+10'00'` | 02:00 Utc | no | — |

Nothing is corrupted: the instant survives. What is lost is the offset, and with it the ability to
get back the value that was put in. A caller that writes `DateTime.Now` and reads it back gets a
different `DateTime`, of a different `Kind`, and any comparison it makes fails.

This is what `PdfSharpCore.Test/IO/ModificationDateTests.cs` steps around by comparing one read
against another read rather than against the date it wrote.

---

## Item 1 — Read the offset wherever it is

`ParseDateTime` decides what a string contains by its length, and the lengths it checks are those of
one spelling only. Replace the length arithmetic with a scan: after the seconds, if the next
character is `+`, `-` or `Z`, take it, then take the digits that follow, skipping apostrophes
wherever they fall. The three failing forms differ only in punctuation and all three then read the
same.

Keep the leniency that is there — a month clamped into 1..12 for the "miserable PDF tools", trailing
rubbish ignored — since those exist for files that are already out there.

Also accept a string with no `D:` prefix, which the standard permits.

## Item 2 — Apply the documented defaults

A date may stop after any field. Read what is present and default the rest: month and day to 01,
everything else to zero. `D:2024` is a legal date meaning 2024-01-01, not a parse failure.

Note the existing behaviour returns `DateTime.MinValue` for anything it cannot read, and callers
cannot tell that from a document that genuinely says `0001-01-01`. *Changed*: brought into scope.
`PdfDate.TryParse` is public and answers the question, and the `DateTime` members still return
`MinValue` for a date they cannot read, so no document that opens today stops opening. This is what
the `// TODO: TryParseDateTime` was asking for, and `Parser.ParseDateTime` is gone — reading a date
string now lives with the value it produces rather than with the file parser.

## Item 3 — Let a caller keep the offset

The instant is preserved today; the offset is not. .NET has a type for exactly what a PDF date
holds, and it is `DateTimeOffset`.

Proposed, alongside what exists rather than in place of it:

```csharp
// PdfDate
public DateTimeOffset ValueOffset { get; }
public PdfDate(DateTimeOffset value)

// PdfDocumentInformation
public DateTimeOffset CreationDateOffset { get; set; }
public DateTimeOffset ModificationDateOffset { get; set; }
```

`DateTime CreationDate` and `DateTime ModificationDate` keep returning UT, as they do now, so
nothing that reads them changes. Their documentation should say so, which it currently does not —
"Gets or sets the creation date of the document" gives no hint that what comes out is not what went
in.

A caller that wants a round trip uses the offset members and gets one:

```csharp
document.Info.ModificationDateOffset = DateTimeOffset.Now;
// ... save, reopen ...
document.Info.ModificationDateOffset   // the same DateTimeOffset, offset and all
```

### Why not change `DateTime ModificationDate` to return local time

It would make the naive round trip work on the machine that wrote the file, and quietly break on
any other — the value would then depend on the reader's time zone rather than the writer's. It also
changes what every existing caller sees without any of them asking. UT is the honest answer for a
`DateTime`; the offset belongs in a type that can hold it.

---

## What the parser accepts now

The same thirteen forms, measured after the change. Every row reads as the standard says it should.

| string | read as | |
|---|---|---|
| `D:20240601120000+10'00'` | 02:00 UT | |
| `D:20240601120000+10'00` | 02:00 UT | **was** 12:00 |
| `D:20240601120000+1000` | 02:00 UT | **was** 12:00 |
| `D:20240601120000-05'30'` | 17:30 UT | |
| `D:20240601120000-0530` | 17:30 UT | **was** 12:00 |
| `D:20240601120000Z` | 12:00 UT | |
| `D:20240601120000` | 12:00 UT | |
| `D:202406011200` | 12:00 UT | **was** 00:00 |
| `D:20240601` | 00:00 UT | |
| `D:202406` | 2024-06-01 00:00 UT | **was** `MinValue` |
| `D:2024` | 2024-01-01 00:00 UT | **was** `MinValue` |
| `20240601120000+10'00'` | 02:00 UT | **was** `MinValue` |
| `D:20240601120000+10'00'junk` | 02:00 UT | |

Reading stops at the first field that is not there, or is not digits. The two cannot be told apart
without rules the standard does not give, and stopping is what a date that simply ends looks like.

## Tests

- `Pdfs/PdfDateTests.cs`: every row of the table above, every documented default, the three
  spellings of an offset, what is written for four offsets including two on the half and quarter
  hour, a `DateTimeOffset` round trip, and four strings that are no date at all. The two tests that
  were there for `-02'00'` and `Z` are untouched and still pass.
- `IO/DocumentDateRoundTripTests.cs`: a date written into a document and read back out of it, for
  four offsets; that the `DateTime` members answer in UT; and that a date chosen through the offset
  member is not stamped over when the document is saved.
- `IO/ModificationDateTests.cs` compares in UT rather than local time, since that is what the
  property now says it returns.
