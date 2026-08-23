# Spec — the open mode enforced where it is named

What making `PdfDocumentOpenMode` mean something at the point it is read covers, and what it
deliberately leaves out.

| item | what | status |
|---|---|---|
| 1 | `CanModify` reading the open mode again | proposed |
| 2 | Every refusal naming the mode it wanted | proposed |
| 3 | Sixteen dead guards made live or removed | proposed |
| 4 | `InformationOnly` implemented or removed | proposed |

## Problem Statement

`PdfReader.Open` takes a `PdfDocumentOpenMode`, and CLAUDE.md warns that picking the wrong one is *"a
common cause of 'this API does nothing'"*. The reason is in the source:

`PdfDocument.CanModify` returns `true` unconditionally, with the real check commented out beside it —
`//get {return _state == DocumentState.Created || _state == DocumentState.Modifyable;}`. Sixteen
call sites in `PdfDocument` guard on it: `Close`, `AddPage`, `InsertPage`, `PlacePage`,
`ImportPage`, `DuplicatePage`, `MovePage` and the rest. Every one of them reads exactly like the mode
check a reader expects to find, and enforces nothing at all.

What actually refuses lives in four other modules and uses a different property. `IsReadOnly` — which
is real, and is `_openMode != Modify && _openMode != Append` — is read by `XGraphics` when creating a
drawing surface and by `PdfPageResizer`. `PdfPages.Import` has its own check. `PdfSigner` and
`SaveIncremental` require `Append`, the latter by way of whether the original bytes were kept.

So a caller who opens `ReadOnly` and calls `AddPage` is not refused. They get a page. What they do
not get is any of it written, or they get a failure much later from a module three layers away that
mentions a different concept. The constraint has no locality: it is named in one place, enforced in
four others, and absent from the rest.

There is a fifth mode, `InformationOnly`, whose declaration carries `// TODO: not yet implemented`.

The wider shape is that five modes grant five different, partly disjoint interfaces over one
implementation type, and all eighteen `Open` overloads return the same `PdfDocument`. `Import` alone
permits `ImportPage`; only `Append` permits `SaveIncremental`; only `Modify` and `Append` permit
`XGraphics.FromPdfPage`. None of that survives into the type, so the compiler can never say what the
doc comment says.

## Solution

Make the guard that names the constraint be the guard that enforces it.

`CanModify` reads the open mode again. Every refusal says which mode the caller needed and which
they have. The sixteen call sites become live, and any that turn out to be wrong about what they
were guarding are corrected or removed rather than left reading as protection they do not provide.

This is deliberately the smaller of the two available changes. The larger one — giving each mode its
own type so the compiler enforces the constraint — is set out under Out of Scope and is not proposed
here.

## User Stories

1. As a developer opening a document read-only, I want `AddPage` to refuse, so that I find out at
   the call rather than by wondering why my output has one page.
2. As a developer, I want the refusal to tell me which open mode I need, so that I can fix it without
   reading the source.
3. As a developer, I want the refusal to tell me which mode I used, so that I can see the mistake.
4. As a developer opening `Import`, I want extraction to work and modification to refuse, so that the
   mode means what it is documented to mean.
5. As a developer opening `Modify`, I want everything that works today to keep working.
6. As a developer using `Append`, I want incremental save to keep working and a full `Save` to be
   the deliberate act it already is.
7. As a developer, I want a consistent concept: one question — may I change this document — asked one
   way.
8. As a maintainer, I want a guard in the source to be a guard in the binary, so that reading the
   code tells the truth.
9. As a maintainer, I want a guard that turns out to be unnecessary removed rather than left
   dormant, so that the next reader is not misled the same way.
10. As a maintainer, I want `InformationOnly` either implemented or removed, so that the enum does
    not offer a mode that does nothing.
11. As a maintainer, I want the demos and the conformance corpus checked against the restored guards
    before release, so that the change is known to be safe on our own output.
12. As a consumer upgrading, I want a release note naming this, so that code that relied on the
    absent guard is not surprised.
13. As a consumer, I want the change to be a refusal rather than a silent behaviour difference, so
    that if I am affected I know immediately.

## Implementation Decisions

**This is a behaviour change, not a refactor, and must be released as one.** Turning the guard on
will refuse code that runs today. Any caller who opened `ReadOnly` or `Import` and then modified the
document was getting a result that was never going to be written correctly, so refusing them is the
repair — but it is still a break, and it belongs behind a release note that names the affected
methods.

**`CanModify` and `IsReadOnly` must become one question.** Two properties answering "may this change"
with different logic is how the current split survived. `IsReadOnly` is the one that works and is
public; `CanModify` is internal. The internal one should be defined in terms of the public one, and
the four modules that check `IsReadOnly` directly should keep doing so.

**Each of the sixteen sites is a decision.** They are not uniformly correct. Some guard operations
that genuinely require modification; `Close` may not be one of them, since closing a read-only
document is reasonable. Every site is either confirmed, corrected to a different mode requirement, or
removed. Leaving one dormant reproduces the problem this spec exists to fix.

**Refusals name both modes.** *"This document was opened `ReadOnly` and adding a page needs
`Modify`."* The existing `PSSR.CannotModify` string says neither and should be replaced or
parameterised.

**`Append` is not a lesser `Modify`.** Its own doc comment already warns that `Modify` cannot be used
for incremental save and that the reason is easy to miss. `IsReadOnly` already treats `Append` as
modifiable and that stays.

**`InformationOnly` gets decided, not deferred again.** Either it behaves as `ReadOnly` with a
narrower promise, or it is removed from the enum. A mode marked "not yet implemented" that silently
behaves like something else is the same class of problem as the dead guard.

**Nothing here changes the reader.** `PdfReader.Open` already records the mode correctly. The defect
is entirely on the consuming side.

**Run the corpus and the demos before believing it.** `ConformanceCorpus` and `SampleApp` between
them open documents in every mode the library offers — `Assemble`, `Import`, `Extract`, `Revise`,
`Signing`, `Inspect`, `PageResize` — and are the cheapest available evidence that the restored guards
do not refuse something legitimate.

## Testing Decisions

**A good test here opens a document in a mode and asserts what it may do.** The observable behaviour
is which calls succeed and which throw, and what the exception says. That is a small, complete
matrix and it should be written as one.

**Modules under test.** `PdfDocument` for the guarded operations, and the four modules that already
enforce — `XGraphics`, `PdfPages`, `PdfPageResizer`, `PdfSigner` — for the ones they own.

**Prior art to follow rather than reinvent.** `PdfSharpCore.Test/IO/` holds the reader and mode
tests; `PageResizeTests` already exercises a document opened for modification and is a model for
arranging one. `PdfSharpCore.Test/Helpers/PdfHelper.cs` builds documents to open. `RawPdf.cs` builds
byte-exact files where the document must be malformed or minimal.

**The matrix worth pinning.** For each of the five modes, assert the outcome of: `AddPage`,
`InsertPage`, `ImportPage`, `MovePage`, `DuplicatePage`, `XGraphics.FromPdfPage`, `Save`,
`SaveIncremental`, `Close`. Most cells are one line. This matrix is the durable artefact — it is what
makes the mode a specified thing rather than an emergent one, and it is what a future change to any
of those methods will be measured against.

**Assert on the message, not only the type.** The point of the change is that the refusal names the
mode. A test that only checks `InvalidOperationException` would pass against the unhelpful message
this spec exists to replace.

**The demo smoke tests are the integration proof.**
`PdfSharpCore.Test/Demos/DemoSmokeTests.cs` fails the build when a demo throws or changes its page
count, and the demos exercise real open-mode usage across `Assemble`, `Extract`, `Revise` and
`Signing`. If the restored guards are wrong, that is where it shows.

**veraPDF still gates.** `./verapdf-check.ps1` builds its corpus by opening and saving documents, so
a wrongly restored guard would show there too.

## Out of Scope

- **Giving each mode its own type.** The larger and better answer: `Open` returning a type whose
  interface is what that mode permits, so the compiler enforces it. It is a breaking API change
  across eighteen overloads and every consumer, and it should not be smuggled in behind a guard
  repair. Worth its own proposal.
- **The `DocumentState` enum the commented-out code refers to.** Whether that concept should return
  is a separate question from whether the open mode should be honoured.
- **`PdfDocument`'s width.** Eight page methods that are a guard plus one call into `PdfPages`, and
  two nested helpers — `ImageInfo` and `DocumentHandle` — that fail the deletion test in opposite
  directions. Real, and not this.
- **`PdfPage`'s six responsibilities**, including the copying `TrimMargins` setter. Also real, also
  not this.
- **The simple-type immutability rule.** `PdfItem` carries it as a bare comment, `PdfString` breaks
  it and says so. Unenforced invariant, different problem.
- **Making `Parser` testable without a `PdfDocument`.** Worth doing; unrelated.

## Further Notes

This is the smallest diff of the strong candidates and the one most likely to be argued about,
because the current behaviour is permissive and permissive behaviour has users. The argument for
doing it anyway is that the permissiveness is not a decision anybody made — it is a commented-out
line — and the sixteen guards that read as protection are actively misleading to anyone reading the
code to find out what the mode does.

If the matrix in the testing section turns out to refuse something the demos or the corpus rely on,
that is not a reason to abandon the change. It is the change finding its first real caller, and the
question then is whether that caller was right.
