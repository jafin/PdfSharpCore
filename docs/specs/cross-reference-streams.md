# Proposal — writing cross-reference streams and object streams

What a compressed write path would cover, and what it would deliberately leave out.
Gap **G1** of `autoresearch/improve-260816-1032/improvement-plan.md`. Nothing here is built.

| item | what | status |
|---|---|---|
| 1 | `PdfDocumentOptions.CrossReferenceFormat` — `Classic` \| `Stream` | proposed |
| 2 | Object-stream writer half for `PdfObjectStream` | proposed |
| 3 | Cross-reference stream emission, with type-2 entries | proposed |
| 4 | Encryption interaction — an object inside an `ObjStm` is not separately encrypted | proposed |
| 5 | Reading a cross-reference stream and writing one back | proposed, **breaking behaviour** |

Estimated effort: **2–3 engineer-weeks.** The cheapest item in the gap analysis, and three later
proposals lean on it.

---

## The defect

The writer emits a PDF 1.4-era classic `trailer` plus a plain cross-reference table, always — even
when the document it is saving was read from a file that used a cross-reference stream.
`Pdf/PdfDocument.cs:336-341` does this on purpose:

```csharp
if (_trailer is PdfCrossReferenceStream)
{
    // Convert cross-reference stream to cross-reference table.
    _trailer = new PdfTrailer((PdfCrossReferenceStream)_trailer);
    ...
}
```

`Pdf.Advanced/PdfObjectStream.cs` is read-only — `ReadCompressedObject` and nothing that writes one.
So the library understands compressed objects perfectly well when someone else produced them and
cannot produce them itself.

Three consequences.

**Files are larger than they need to be.** Every dictionary — page, font, annotation, form field,
resource map — is written out uncompressed, one indirect object at a time, each with its own
`N 0 obj … endobj` frame and its own xref row. On a drawing-heavy document the content streams
dominate and nobody notices. On a document that is mostly *objects* — a filled form, a document with
several hundred link annotations, and above all a **tagged** document, where every paragraph, cell and
list item becomes a structure element — the dictionaries are the file. This is the reason to do it
before tagged PDF rather than after: tagging multiplies the object count, so shipping it on a classic
writer ships a size regression with it.

**A read/write round trip loses information about the file's own shape.** A PDF 1.5+ document opened
and saved comes back as a PDF 1.4-shaped one.

**PDF 2.0 is out of reach.** ISO 32000-2 assumes the cross-reference stream. There is no path to it
from a writer that cannot emit one.

---

## Item 1 — the option

```csharp
document.Options.CrossReferenceFormat = PdfCrossReferenceFormat.Stream;
document.Options.MaxObjectsPerObjectStream = 200;   // what Acrobat uses
```

Default `Classic` for one release so nobody's byte-comparison tests break without warning, then flip
the default and keep `Classic` as the escape hatch. `PdfDocument.Version` moves to 15 when the format
is `Stream`, because a 1.4 header on a file with an xref stream is simply invalid.

## Item 2 — the object stream

An object stream is a stream whose data is a run of object bodies with a small offset table in front:

```text
/Type /ObjStm  /N 4  /First 22  /Filter /FlateDecode
┌──────────────────────────────────────────────────────────┐
│ 12 0  15 48  19 96  23 131 │ <</Type/Page …>> <</Font…>> …│
└─── the /First prologue ────┴── the bodies, concatenated ──┘
  objnum offset pairs           each body starts at /First+offset
```

The whole thing is then Flate-compressed as one unit, which is where the saving comes from: two
hundred small dictionaries share a compression window instead of each getting its own.

**What may not go in.** Streams (a stream cannot nest inside a stream), the `/Encrypt` dictionary, the
document `/ID`, the cross-reference stream itself, and anything an outside reference needs a real file
offset for. Everything else is eligible.

## Item 3 — the cross-reference stream

A stream of fixed-width binary rows, widths declared in `/W`:

| type | field 2 | field 3 | means |
|---|---|---|---|
| 0 | next free object | generation | free-list entry |
| 1 | byte offset in file | generation | an ordinary object, as the classic table had it |
| **2** | **object number of the containing `ObjStm`** | **index within it** | **a compressed object** |

Type 2 is the whole point and the only genuinely new row. `/Index` carries the subsections; the
trailer keys — `/Root`, `/Info`, `/ID`, `/Size` — move onto the stream dictionary itself, which is why
`PdfTrailer` and `PdfCrossReferenceStream` have to stay interchangeable rather than one converting to
the other.

## Item 4 — encryption is the trap

Strings and streams inside an object stream are **not** encrypted individually. The containing
`ObjStm` stream is encrypted, once, and its contents ride along inside. Encrypting them twice produces
a file that opens, appears to work, and yields mojibake for every string in it — the worst failure
shape, because it survives a smoke test.

`Pdf.Security` has to learn the distinction, and the test that pins it writes an AES-256 document with
`CrossReferenceFormat.Stream`, reopens it, and reads a string back out. `Pdf/PdfDocument.cs:352` shows
where the `/Encrypt` reference goes into the trailer today; that path needs the same treatment on the
stream side.

---

## What this deliberately does not cover

- **Linearization.** A different problem — first-page-first object ordering plus hint streams — and of
  little value to a server-side generation workload. Out of scope here and low priority everywhere.
- **Choosing which objects to group.** The first implementation buckets eligible objects in the order
  they are written, capped at `MaxObjectsPerObjectStream`. Grouping by kind (all fonts together, all
  structure elements together) compresses better because it puts similar dictionaries in the same
  window, but it is an optimisation to measure later, not a design constraint now.
- **PDF 2.0 features.** This unblocks them. It does not deliver any of them.

## Tests

`PdfSharpCore.Test`. Round-trip every fixture through both formats and assert the object graphs match;
assert type-2 entries resolve to the right object; assert the size reduction on a form-heavy fixture
is real rather than assumed; assert an AES-256 document survives (item 4). Ghostscript rasterization
comparison for the byte-level sanity check that only an outside reader can give.

## Related

- `docs/specs/incremental-update-save.md` — needs the same two-pass writer refactor.
- `docs/specs/tagged-pdf-accessibility.md` — the object-count multiplier that makes this urgent.
