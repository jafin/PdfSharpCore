# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
dotnet build PdfSharpCore.slnx           # SDK is pinned to 10.0.100 by global.json
dotnet test                              # whole suite, both test target frameworks
dotnet test -f net10.0                   # one framework; the test project targets net8.0;net10.0
dotnet test --filter "FullyQualifiedName~CLexerTests"                  # one class
dotnet test --filter "FullyQualifiedName~CLexerTests.ScanNextToken"    # one test
./ci-build.ps1                           # what CI builds: clean + Release build
```

CI (`.github/workflows/build.yml`) runs on Linux only, builds with `ci-build.ps1`, installs
Ghostscript, then runs `dotnet test` with coverlet/opencover coverage.

There is no lint or format step in the build or in CI.

**Judge a test run by its exit code, not by the word `Passed`.** Every rasterization on Windows
runs Ghostscript *inside* the test host, and its way of giving up is to end the process — so a
document it will not draw reads as `Test host process crashed`, exit 1, no failing test, and a
`Passed!` line printed anyway with a total short of what was discovered. Reading the tail of
`dotnet test` reports such a run as green. A run whose total is below what `dotnet test
--list-tests` finds did not pass; it stopped. `--blame-crash` then names the test that never
finished, and **whether that name repeats is the diagnosis**: the same one twice is a document to go
and look at, and it settled this twice. A different name every run is the machine rather than the
suite — the third episode crashed five runs in eight at a tenth of the memory of the first two, was
reproduced on a commit predating everything suspected, and then stopped for good with nothing
changed. Re-run before believing it. See `docs/specs/test-host-crash-investigation.md`.

A lexer or parser change can hang the test host rather than fail it. Tests that scan malformed
input carry `[Fact(Timeout = …)]`, which xUnit honours only on `async` tests — hence the
`Task.Run` wrappers in `CLexerTests`.

## Layout and dependency direction

```
PdfSharpCore ─────────────┬── PdfSharpCore.Skia        (SkiaSharp; the default backend)
   (no imaging or font    └── PdfSharpCore.ImageSharp   (ImageSharp 2.1.x, Fonts 1.0.1)
    dependency of its own)
   ▲       ▲
   │       └── MigraDocCore.DocumentObjectModel ── MigraDocCore.Rendering ── PdfSharpCore.Charting
   │              ▲
   │              └── MigraDocCore.DocumentObjectModel.Tests  (the DOM alone; no backend)
   └── PdfSharpCore.Test  (the broad one; covers MigraDoc and SampleApp too)
           ▲
           └── SampleApp  (the demonstration CLI; net8.0 alone, so both test legs can reference it)
```

Four test projects, and which one a new test belongs in is worth a moment. `PdfSharpCore.Test`
is the broad one and the default. `MigraDocCore.DocumentObjectModel.Generators.Tests` drives the
DOM's source generator through `CSharpGeneratorDriver`. `MigraDocCore.Rendering.Tests` covers
MigraDoc's own layout — paragraphs, tables, fields, the paragraph iterator — and deliberately
rasterizes nothing, so it needs neither Ghostscript nor ImageMagick. It links four content-stream
readers out of `PdfSharpCore.Test/Helpers` rather than keeping copies; edit those in place and both
projects get the change.

`MigraDocCore.DocumentObjectModel.Tests` covers the DOM itself — `Unit`, page sizes, the chart
object model, MDDDL reading and writing, and the flattening visitors. It references the DOM **and
nothing else**: no renderer, and so no backend, no Ghostscript and no font files. The one
qualification is `NamedFontsOnly.cs`, a module initializer serving a font *name*, because building
a `Document` builds its standard styles and the Normal style asks the resolver what the default
font is called. It resolves no face and throws if asked to, which is the line saying a test needing
a real font belongs in `MigraDocCore.Rendering.Tests`. Note that `PdfSharpCore.Test/Dom/` also
covers the DOM, from the other side: the value model, colours, styles and the generated property
machinery. The two do not overlap.

`SampleApp` is the demonstration app: `dotnet run --project SampleApp -- list` says what it covers,
`… -- run` writes one PDF per demo into `SampleApp/output` and prints the source that drew each. Its
demos are covered by `PdfSharpCore.Test/Demos/DemoSmokeTests.cs`, so a demo that throws or changes
its page count fails the build.

Two rules there are load-bearing rather than stylistic, both explained in
`docs/specs/demonstration-app.md`: **a demo never registers a backend** (the smoke test runs demos
inside a test host that has already installed `PinnedFontResolver`, and `GlobalFontSettings
.FontResolver` throws once a font has been used), and **its fonts, images and sources are embedded
resources, not content files** (a referenced project's content items do not reach the referencing
project's output directory).

The core package deliberately carries no imaging or font dependency. All three seams are static, and
each throws a descriptive `InvalidOperationException` when read unset:

- `GlobalFontSettings.FontResolver` — an `IFontResolver`; backends supply `SkiaFontResolver` /
  `ImageSharpFontResolver`, both built on `Utils/FontResolverBase`. Must be set before any font is
  created: the setter throws once one has been.
- `ImageSource.ImageSourceImpl` — an `ImageSource`; backends supply `SkiaImageSource` /
  `ImageSharpImageSource`. Must be set before any image is loaded.
- `GlobalFontSettings.GlyphOutlineProvider` — an `IGlyphOutlineProvider`; backends supply
  `SkiaGlyphOutlineProvider` / `ImageSharpGlyphOutlineProvider`. Wanted by `XGraphicsPath.AddString`
  and nothing else, so it can be set, replaced or cleared at any time. A provider takes its font
  bytes **through** `FontResolver` rather than resolving a family itself, or the two seams will
  disagree about which face a family means.

`ImageSource` is a trap for the eye: the file is `PdfSharpCore/Drawing/ImageSource.cs` and it ships
in the **PdfSharpCore** assembly, but its namespace is
`MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes`. Registering it needs that
`using`, from code that otherwise has nothing to do with MigraDoc.

## PDF object model and IO

`PdfItem` is the root of everything. Types derived from `PdfItem` but not from `PdfObject` are
simple types and **must be immutable** — `PdfName`, `PdfString`, `PdfInteger` and friends. `PdfObject`
adds identity and indirect references; `PdfDictionary`/`PdfArray` compose them.

`PdfReader.Open` takes a `PdfDocumentOpenMode` that decides far more than access: `Modify` reads
everything into memory and lets pages be inserted or deleted but not extracted, `Import` allows
extraction but no modification, `ReadOnly` preserves the original internal structure. Picking the
wrong one is a common cause of "this API does nothing".

**Strings and names are byte strings, one char per byte.** `Lexer` reads with
`(char)stream.ReadByte()`, `PdfEncoders.RawEncoding` converts back, and `PdfWriter` asserts
`ch < 256` on the way out. Nothing in the name path re-decodes to Unicode, so a name written in a
legacy code page keeps its bytes across a read/write round trip. Do not "fix" this by decoding to
UTF-8 — it breaks the writer. `LexerNameEncodingTests` pins the invariant down.

There are two independent lexers, and a change to one usually belongs in the other:

- `Pdf.IO/Lexer.cs` + `Parser.cs` — the document body, reading from a `Stream`.
- `Pdf.Content/CLexer.cs` + `CParser.cs` — content streams, reading from a `byte[]`, with its own
  `Chars` and `CSymbol`. It is the older and rougher of the two; the document lexer usually has the
  guard the content lexer is missing.

## Drawing

`XGraphics` is the drawing surface and holds an `IXGraphicsRenderer`; `XGraphicsPdfRenderer`
(`Drawing.Pdf/`) is the only implementation and emits content-stream operators. MigraDoc renders
through the same surface, so a layout fix lands in `MigraDocCore.Rendering` and a drawing fix in
`Drawing.Pdf`.

Fonts are always embedded, with no setting to disable it. TrueType outlines are subsetted;
PostScript (CFF) outlines cannot be and embed whole. A weight or slant with no font file is
simulated by stroking or skewing.

## Multi-targeting

Every shipped package targets `netstandard2.1;net8.0;net10.0`. `netstandard2.1` exists **for Unity**,
whose scripting runtime cannot consume a `net8.0` assembly — check with the maintainer before
dropping it. Two consequences:

- `Directory.Build.props` sets `LangVersion=latest` for every leg, or the netstandard2.1 leg would
  compile shared source as C# 8. This unlocks *syntax* only. Anything needing a BCL type
  netstandard2.1 lacks (`IsExternalInit` for records and `init`, `RequiredMemberAttribute`,
  `InlineArray`) still fails as a missing-predefined-type error, and has to be polyfilled.
- `PdfSharpCore/!internal/` and `MigraDocCore.DocumentObjectModel/CompileFixes/` hold those
  polyfills behind `#if !NET5_0_OR_GREATER`. They look like dead code on a modern-target glance and
  are not. Both copies exist because each is `internal` with no `InternalsVisibleTo`.

## Tests

`TestBackendSetup` is a `[ModuleInitializer]` that registers the Skia backend and Ghostscript for
the whole assembly — individual tests do not set up backends.

`PinnedFontResolver` serves Liberation Sans (Arial metrics) from `Assets/Fonts` instead of the
machine's fonts, because glyph widths decide where a line wraps and therefore what a layout
assertion sees. A test needing its own font calls `PinnedFontResolver.Register` rather than
swapping the resolver out from under everything else running beside it.

`[GoldenImageFact]` marks tests comparing against checked-in reference images; it self-skips with a
reason when Ghostscript cannot rasterize on the current machine. Anything that rasterizes belongs
to `[Collection(RasterizingCollection.Name)]` — ImageMagick drives one in-process Ghostscript, so a
second concurrent rasterization silently falls back to an executable that may not be installed.

Ghostscript comes from `Ghostscript.NativeAssets` on Windows. Linux and macOS shell out to the
system `gs`, so those tests need `apt-get install ghostscript` / `brew install ghostscript` locally.

Assertions use **AwesomeAssertions**, a FluentAssertions fork — the API is the same, the `using` is
not.

## Conventions

Commit subjects describe the behaviour change in plain prose, in the imperative, without a
conventional-commit prefix: *"Read a hex string by the digits in it rather than by what follows
them"*, *"Follow a composite glyph down to the glyphs it is really drawn from"*. Bodies explain what
was wrong and why the new way is right. Pure housekeeping does use `chore:` / `refactor:`.

Sources use file-scoped namespaces. `.editorconfig` enforces a final newline — a bulk conversion
once stripped it from 725 files, and every later diff touching the end of a file showed the damage.

`docs/specs/` holds design notes for this fork's features, each tied to an upstream issue number,
recording what was built and what was deliberately left out. Read the relevant one before extending
that feature area.
