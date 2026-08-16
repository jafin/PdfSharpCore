# Proposal — saving a document as an incremental update

What appending to a PDF instead of rewriting it would cover, and what it would deliberately leave out.
Gap **G6** of `autoresearch/improve-260816-1032/improvement-plan.md`. Nothing here is built.

| item | what | status |
|---|---|---|
| 1 | `PdfDocument.SaveIncremental(Stream)` | proposed |
| 2 | Dirty tracking on `PdfObject` | proposed |
| 3 | `PdfReader.Open` retains the source bytes in `Modify` mode | proposed, **breaking behaviour** |
| 4 | A new xref section with `/Prev`, and a trailer keeping `/ID[0]` | proposed |

Estimated effort: **2 engineer-weeks.** Small, and `docs/specs/pdf-signatures.md` cannot exist
without it.

---

## The defect

All three `PdfDocument.Save` overloads — `Save(string)`, `Save(Stream)`, `Save(Stream, bool)` — write
the whole file from the object graph. There is no path that appends.

For most work that is the right thing and nobody notices. It stops being right in three places:

**Signatures.** A signature covers a byte range of the file. Rewriting the file changes those bytes, so
the second signature invalidates the first, and so does any edit at all. Incremental update is not an
optimisation for signed documents — it is the only legal way to modify one.

**Non-destructive editing.** Fill a form, add an annotation, change a bookmark: rewriting the file
discards the original bytes, and with them anything the reader did not fully understand. An incremental
update preserves the original exactly and appends the change, which is also what makes the change
auditable and reversible.

**Large documents.** Adding one annotation to a 200 MB scanned PDF rewrites 200 MB.

---

## The shape of it

```text
┌─ original bytes, copied verbatim ─────────────────────────┐
│ %PDF-1.7                                                  │
│ 1 0 obj … endobj    2 0 obj … endobj    3 0 obj … endobj  │
│ xref                                                      │
│ trailer <</Root 1 0 R /ID[<A><B>]>>                       │
│ startxref 4096                                            │
├─ appended ────────────────────────────────────────────────┤
│ 3 1 obj … endobj          ← only the objects that changed │
│ 9 0 obj … endobj          ← and any new ones              │
│ xref                                                      │
│ trailer <</Root 1 0 R /Prev 4096 /ID[<A><C>]>>            │
│ startxref 8192            ← readers start here            │
└───────────────────────────────────────────────────────────┘
```

A reader starts at the last `startxref` and walks `/Prev` backwards, so later definitions of an object
shadow earlier ones. `/ID[0]` stays — it identifies the document across its whole life — and `/ID[1]`
is regenerated, because it identifies this revision. `Pdf/PdfDocument.cs` already has
`_trailer.CreateNewDocumentIDs()`; this needs the half that changes only the second element.

Once `docs/specs/cross-reference-streams.md` lands, the appended section can be a cross-reference
stream rather than a classic table. Both must work: a file whose original revision used a classic table
is conventionally updated with another classic table, and the reverse for streams.

## Item 2 — dirty tracking is the actual work

Everything above is bookkeeping. This is the part that needs care, because **an incremental update that
misses a changed object produces a file that is silently, subtly wrong** — the reader keeps using the
old definition and nobody finds out until a customer does.

`PdfObject` has no dirty flag today. Adding one is easy; making it *honest* is not, because mutation
reaches objects by several routes:

- `dictionary.Elements["/Foo"] = value` — `PdfDictionary.DictionaryElements` must mark its owner.
- `array.Elements.Add(…)` — likewise for `PdfArray`.
- `stream.Value = bytes` — and every path that rewrites stream data, including the filters.
- Objects reached through `PdfInternals`, which exists precisely to let callers reach past the API.

The safe default is to treat an object as dirty unless it can be proven clean, and to have a debug
mode that rewrites the file both ways and compares — an expensive check that only has to run in tests.

`PdfObject.IsNew` (objects created since the read) is the easy half and is already implicit in the
object-ID assignment.

## Item 3 — keeping the original bytes

`SaveIncremental` needs the file it is updating, byte for byte. So `PdfReader.Open` in
`PdfDocumentOpenMode.Modify` has to retain the source buffer, or the document has to remember where to
find it.

**This is a memory-behaviour change** — opening a 200 MB PDF would hold 200 MB — so it should be opt-in
rather than a surprise: either a new open mode, or a flag on `Modify`. Given `CLAUDE.md` notes that the
open mode "decides far more than access" and that picking the wrong one is a common cause of "this API
does nothing", adding a fifth thing the mode decides is worth doing explicitly and documenting in the
same place.

`SaveIncremental` must refuse — clearly, not silently — a document opened `Import` or `ReadOnly`, or one
built from scratch.

---

## What this deliberately does not cover

- **Rewriting history.** An incremental update appends. Removing a previous revision is a full save, by
  definition, and for a signed document it is destroying evidence.
- **Reverting to a previous revision.** Readable from `/Prev`, but exposing it is a separate feature.
- **Compacting away superseded objects.** That is what a full `Save` does; a "save flattened" helper is
  a one-line convenience over the existing path.
- **Object-level diffing to decide dirtiness.** Comparing serialised forms to avoid writing an object
  that was touched but not actually changed would shrink the appended section. Measure first; the
  common case is that a touched object did change.

## Tests

`PdfSharpCore.Test`. Open a fixture, change one annotation, save incrementally, and assert: the
original bytes are a prefix of the result; the appended section defines only the changed object; the
reopened document sees the new value; `/ID[0]` is unchanged and `/ID[1]` is not. Then apply **two**
successive updates and assert the `/Prev` chain resolves — one update is easy to get right by accident,
two is not.

The dirty-tracking check earns its own test class: mutate through `Elements`, through `PdfInternals`,
and through stream `Value`, and assert each marks its owner.

## Related

- `docs/specs/cross-reference-streams.md` — the same two-pass writer refactor; do it first.
- Digital signatures (gap G5) are blocked on this and are the main reason to build it.
