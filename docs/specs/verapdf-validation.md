# veraPDF validation in CI

The outside opinion on every conformance claim this library makes. Item 7 of
`docs/specs/tagged-pdf-accessibility.md`, and the step `docs/specs/pdf-a-conformance.md` has been
asking for since it was written — both specs named it, both said it should be decided once for the
two of them, and this is that decision.

| item | what | status |
|---|---|---|
| 1 | A corpus of documents that each claim a profile | done |
| 2 | veraPDF over the corpus, in CI and locally, one script | done |
| 3 | Gate the build on the verdict | **not yet — reports only**, see below |

## Why this was worth doing

Before it, every claim was self-certified. The writer refuses to save a document that breaks a rule
of the profile it claims, and `PdfUaValidator` lists in its own remarks which rules it holds a
document to and which it cannot reach — the largest being that no content sits outside the structure
tree, which needs a content-stream pass it does not make.

It found three real defects on the first run, none of which any test here could have caught. That is
the argument for it, made in one run.

## What it is

**A corpus, built by a program.** `ConformanceCorpus` writes one PDF per claim the library can make:
PDF/A-1b, PDF/A-2b, PDF/A-3b, PDF/A-3b carrying an associated file, and a tagged PDF/UA-1 document.
A program rather than a test, because what it produces is an input to something else — a test asserts
and returns nothing, and these have to exist as files for a validator to open.

Every document **makes a claim**, and that is the entry requirement. veraPDF is run with automatic
flavour detection, so each file is held to the profile it names in its own metadata. A file claiming
nothing would be held to the fallback flavour instead and fail for saying nothing rather than for
being wrong.

They are small on purpose. A corpus is read by whoever is looking at a failure, and a hundred-page
document says no more about a rule than a paragraph does while making the report far harder to place.

**One script, two places.** `verapdf-check.ps1` builds the corpus, runs the validator over it, prints
a summary and writes a report per document into `artifacts/verapdf-reports`. CI runs exactly that
script, so there is one behaviour to understand rather than two, and a developer reproducing a CI
failure runs the thing that failed.

**In Docker, pinned.** veraPDF is a Java application, and requiring a JRE on every machine that wants
to run this — and on Windows, where a developer is least likely to have one — costs more than a
container does. The image is pinned to `verapdf/cli:v1.30.2` so a validator release cannot change
the answer underneath a build; raise it deliberately. A machine with no Docker is told what to
install and nothing fails, because a developer who has not installed Docker has not broken anything.

## The ICC profile, which is its own small story

PDF/A needs an output intent, an output intent needs an ICC profile, and **no profile ships with this
library** — the writer embeds the bytes it is given and says so when given none. So the corpus has to
get one from somewhere, and the three obvious somewheres are all worse than building it:

- A checked-in `.icc` is a binary blob in the repository with a licence of its own to account for.
  That is the decision `docs/specs/pdf-a-conformance.md` records as unmade, and it is still unmade —
  this sidesteps it rather than settling it.
- Downloading one during the build makes validation depend on a third party being up.
- Skia, already a dependency, parses profiles but does not write them. `SKColorSpace.ToProfile()`
  hands back a structure whose buffer is the bytes it was parsed from, so for a colour space that was
  never parsed from anything there are no bytes at all. This was tried first and is why it is written
  down: the failure is silent-looking, an empty buffer rather than an exception.

So `SrgbProfile` builds one: an ICC v2.1 matrix-shaper display profile with sRGB's primaries and
white point already adapted to the D50 connection space, and a single gamma of 2.2 standing in for
sRGB's piecewise transfer curve. **That last is a deliberate approximation** — the curve matters to
colour management and this profile manages no colour; it exists so a document can name the space its
numbers are in, which is what PDF/A asks for.

It has to be a *real* profile, which is the trap worth flagging. The unit tests covering the XMP
writer pass the ASCII bytes `NOT-AN-ICC-PROFILE`, and they are right to — nothing in this library
parses a profile, so a legible stand-in makes those assertions clearer and says plainly that they are
not colour-management tests. A validator does parse it, and reads the colour space out of the header
to check the output intent agrees.

## What it found

Three defects, each hitting several profiles, none of them reachable by any test in this repository.
**All three are in the writer, not in the corpus.**

| clause | what | which documents |
|---|---|---|
| PDF/A-1 6.3.3.2-1, PDF/A-2/3 6.2.11.3.2-1, PDF/UA 7.21.3.2-1 | A Type 2 CIDFont has no `/CIDToGIDMap`. ISO 32000-1 Table 117 requires the entry — a stream, or the name `Identity` — on every embedded CIDFontType2 | **all five** |
| PDF/A-1 6.1.7-1, PDF/A-2/3 6.1.7.1-1 | A stream's `/Length` does not match the bytes actually between `stream` and `endstream`. The context names the XMP metadata stream | all four PDF/A |
| PDF/A-1 6.3.5-3 | A subset CIDFont has no `/CIDSet` in its font descriptor. Required by PDF/A-1 only; PDF/A-2 dropped it | `pdfa-1b` |

Two results are worth reading the other way round, as the things that came back clean:

**The PDF/UA-1 document passes 105 of 106 rules**, and the one failure is the `/CIDToGIDMap` entry
above — a font dictionary, shared with every other document here. Nothing about the structure tree
failed. That answers a question left open when the footnote tagging was written: a `/Lbl` and body
paragraphs nested inside an inline-level `/Note` is the shape MigraDoc produces, the code review
flagged it against ISO 32000-1's table of standard structure types, and **veraPDF does not object to
it**. It is not proof the nesting is ideal, but it is the outside opinion that was wanted, and it
says the tagging is structurally sound.

**The associated-file machinery passes every PDF/A-3 rule about it.** `pdfa-3b-facturx` fails exactly
what `pdfa-3b` fails and nothing more, so `/AFRelationship`, the catalog `/AF` array and the
embedded-files name tree are all right — the part of PDF/A-3 that is easy to get two-thirds correct
and produce a file that opens perfectly and conforms to nothing.

veraPDF also warns `Nested MCID` four times on the tagged document. It is a warning rather than a
failed check and no rule turns on it; it is written down here because it has not been looked into.

## Why it reports rather than gates

The corpus does not conform today, and a step that always fails is a step everybody learns to ignore
— so it prints the verdict and returns success. `continue-on-error` on the CI step is belt to that
braces, and covers the other case: a validator that will not start is not a reason to fail a build it
is only reporting on.

**Turning it into a gate is one flag.** Pass `-Gate` to the script, in CI or locally, and a
non-conforming document fails the run. That is the whole of the change, and it should happen once the
three defects above are fixed rather than on a schedule.

The alternative considered and rejected was baselining the current failures into an allow-list and
gating on anything new. It catches regressions a day sooner and costs a second mechanism that has to
be kept honest — and with three known defects, all in one place, the list would be obsolete about as
soon as it was written.

## Running it

```powershell
./verapdf-check.ps1                 # build the corpus, validate, summarise. Never fails.
./verapdf-check.ps1 -Gate           # the same, but exit non-zero if anything does not conform
./verapdf-check.ps1 -SkipBuild      # validate what is already on disk, for iterating on a failure
./verapdf-check.ps1 -Image verapdf/cli:latest   # try a different validator release
```

Windows, Linux and macOS alike: it is PowerShell, which CI already uses for `ci-build.ps1`, and the
validator is a container. The only requirement is Docker.

Reports land in `artifacts/verapdf-reports`, one XML per document, and CI uploads them as a build
artifact so a failure can be read without reproducing it.

## Deliberately not done

- **Fixing the three defects.** They are findings, and each is a change to the writer with its own
  tests. Recorded here so that whoever picks one up knows what the validator said.
- **A policy file.** veraPDF takes `--policyfile` for a schematron narrowing what counts as a
  failure. That is the machinery an allow-list would be built on, and it is not wanted yet.
- **PDF/A-1a, 2a, 3a and PDF/UA-2.** The `A` levels need a full tagged tree alongside the archival
  claim, which is a combination nothing in the library produces yet — `PdfAConformance` offers only
  the `B` levels. Adding the documents is cheap once it does.
- **Validating what the demos produce.** `SampleApp` writes a PDF per demo and none of them claims a
  profile, so a validator would hold them all to the fallback flavour and report noise.

## Related

- `docs/specs/pdf-a-conformance.md` — the claims being validated, and what enforcement does and does
  not check before the bytes are written.
- `docs/specs/tagged-pdf-accessibility.md` — item 7, and `PdfUaValidator`'s own account of which
  rules it cannot reach.
