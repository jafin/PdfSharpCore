# Spec — the demonstration app

Unlike its neighbours this spec is tied to no upstream issue. It records the scope of the fork's own
tooling: what `SampleApp` becomes, and what it deliberately does not try to be. It was written
before the work rather than after it, so the status column tracks progress.

| item | what | status |
|---|---|---|
| 1 | A command line that runs one demo, several, or all of them | done |
| 2 | Fonts that are the same on every machine | done |
| 3 | The source of each demo, printed from the file that ran | done |
| 4 | Twelve demos, one PDF each, covering the drawing surface | done |
| 5 | A smoke test so a broken demo fails the build | done, 26 tests |

---

## The defect

`SampleApp` is the only runnable sample in the repository and it demonstrates almost nothing.

It is one 145-line `Program.cs`. `Main` draws a string, then three `XTextFormatter` boxes with
translucent rectangles over them, and saves `test1.pdf`. Then it calls
`ResizeAnA4DocumentWithLinksToA5`, which is a careful and well-commented piece of work about page
resizing that has no relationship to the text formatting above it, and saves `resized-a5.pdf`. There
is no way to run one without the other, nothing that says what either produces, and the output goes
to `"."` — the current directory, which under `dotnet run` is the project folder and under a
published binary is wherever the user happened to be standing.

Neither `README.md` nor `CLAUDE.md` mentions the project exists.

Meanwhile the capability with no demonstration has been piling up:

```text
  built                                       demonstrated by
  ────────────────────────────────────────    ───────────────
  complete ISO/DIN page sizes                 nothing
  PdfPage.Resize, PdfDocument.ResizePages     SampleApp, by accident
  word and character spacing, Tz, text rise   nothing
  stroke / fill / stroke+fill text            nothing
  underline and strikeout decorations         nothing
  XTextFormatter columns, ellipsis, rotation  nothing
  named destinations, the link helpers        SampleApp, one call
  text markup annotations                     nothing
```

`docs/PdfSharpCore/samples/` describes 21 samples in prose with no code behind any of them, and
`TextLayout.md` documents an `XTextFormatter.DrawString` overload that does not exist. Prose that
nothing compiles is prose that rots, and it has.

---

## Item 1 — the command line

`System.CommandLine` 2.0.11, which is the stable post-GA package and ships `lib/net8.0`, the target
this project builds for. `Spectre.Console` renders the output; it is not `Spectre.Console.Cli`,
because one argument parser is enough.

The app targets `net8.0` alone rather than the library's set, so that `dotnet run` needs no `-f`.
Not `net10.0` alone: `PdfSharpCore.Test` references this project to run the demos and builds a
`net8.0` leg, which could not reference a `net10.0` one.

```text
SampleApp list                              what each demo shows
SampleApp run                               all of them
SampleApp run --example Fonts Text          two of them
SampleApp run -e fonts                      names are matched case-insensitively
SampleApp run --output C:\somewhere         somewhere other than ./output
SampleApp run --no-code                     PDFs only, no source printed
```

Each demo writes exactly one file, `<Name>.pdf`, into the output directory, and an existing file of
that name is deleted before the demo runs rather than written over. Writing over it would leave a
stale file behind if the demo threw halfway, and a stale PDF that opens is worse than no PDF.

An unknown name is rejected by an option validator that names the value and lists the valid ones. A
demo that throws is reported, the rest still run, and the process exits non-zero — a demo suite that
stops at the first failure tells you about one break per run.

## Item 2 — fonts that do not depend on the machine

`SkiaFontResolver` scans the platform's font directories, which is right for the library and wrong
for a demonstration. Arial is resolved on Windows, something else on macOS, and on a bare Linux
container frequently nothing at all. A demo of *fonts* that renders whatever the machine has is not
a demo of anything.

So the app carries its own, as `EmbeddedResource`:

```text
Liberation Sans    Regular Bold Italic BoldItalic    sans, Arial metrics
Liberation Serif   Regular Bold Italic BoldItalic    serif, Times metrics
Source Code Pro    Regular                           mono, and PostScript (CFF) outlines
```

All three are SIL OFL, and one `LICENSE.txt` covers them.

Source Code Pro is there twice over. It is the mono face the newspaper and magazine layouts want,
and it is the only font here whose outlines are CFF rather than TrueType — a different embedding
path, and one that cannot be subsetted. Shipping only its regular face is also deliberate: a request
for bold or italic comes back with `XStyleSimulations`, so the Fonts demo can show simulated weight
and slant beside real ones and label which is which.

`BundledFontResolver` differs from the test suite's `PinnedFontResolver` in the one way that matters
here. `PinnedFontResolver` answers *every* family with Liberation Sans, because a test wants the
same metrics whatever it asks for. A demo wants the opposite: three families that look like three
families. So the resolver maps each name to its own file, and falls back to the sans for anything it
does not recognise.

### Where the registration lives, and why it matters

`GlobalFontSettings.FontResolver` refuses to change once a font has been used:

```csharp
if (FontFactory.HasFontSources)
    throw new InvalidOperationException("Must not change font resolver after is was once used.");
```

The test assembly installs `PinnedFontResolver` from a `[ModuleInitializer]` for everything in it,
including — once item 5 lands — these demos. If a demo registered a resolver of its own it would
either throw outright, or win the race and quietly move every golden-image test in the suite onto
different font metrics.

So **no demo touches a static seam.** Registration happens once, in `Backends.EnsureRegistered`,
called from `DemoRunner.Run` and from nowhere else — a path only `Program.Main` reaches, and one
`list` and `--help` never take. A demo is handed a document and fills it in. Under the test host it
draws with whatever resolver is already installed, which is what makes it safe to run there.

## Item 3 — printing the source

The app exists to show code as much as output, and code shown beside code that ran is code that will
drift apart from it. So nothing is transcribed. Each demo marks its example:

```csharp
#region example
// ... the drawing, exactly as it runs
#endregion
```

and the printer reads the demo's own `.cs` file, takes what lies between the markers, dedents it and
hands it to Spectre. Region markers rather than parsing out a method body: a scan for two literal
strings cannot be defeated by a brace inside a string literal, and the region means something in the
IDE too.

The file is found by capturing `[CallerFilePath]` in the base constructor, which the compiler fills
in at the derived class's `: base()` call and so records each demo's own file. That path is the
build machine's, so it is used only when it still exists — which makes editing a demo and re-running
show the edit — and otherwise the file's leaf name indexes an embedded copy.

### Why everything is embedded

Fonts, images and sources are all `EmbeddedResource`, read through one loader. The alternative,
`CopyToOutputDirectory`, has to work in three places:

```text
  SampleApp/bin/…/            files land here                  ✓
  publish output              files land here                  ✓
  PdfSharpCore.Test/bin/…/    they do NOT — a referenced        ✗
                              project's content items do not
                              flow to the referencing project
```

The third is where the smoke test runs. Embedding removes the question: there is no path to resolve,
nothing to copy, no dependence on the working directory, and the test gets the assets for free. It
costs about 3MB inside a binary that is never packaged.

## Item 4 — the demos

Twelve, one PDF each.

| name | shows |
|---|---|
| `HelloWorld` | the minimum document, and every field of `PdfDocument.Info` |
| `Fonts` | three families × four styles, a size ramp, the six `XTextDecoration` line styles, simulated versus real bold and italic |
| `Orientation` | portrait and landscape, A3/A4/A6/Letter/Legal captioned with their size in millimetres and points, and a page with `Rotate` set |
| `Images` | one JPEG at natural size, half, double, stretched out of proportion, letterboxed to fit, centre-cropped, and rotated |
| `Text` | colour through RGB, CMYK and grey; rotation; word, character and horizontal spacing; fill against stroke against both; text rise; baseline alignment; web and named links |
| `Layout` | `XTextFormatter` — wrapping, the four alignments including justified, columns, indents and gaps, ellipsis truncation, rotation, and `GetLayout` measuring a box before it is drawn |
| `Tables` | MigraDoc — repeated header rows, merged cells, shading, borders, a totals row |
| `PageResize` | an A4 document with a link, shrunk to A5, content and link and destination moving together |
| `Bleed` | `PdfPage.TrimMargins` — a photograph drawn from negative coordinates onto a sheet larger than the page, with the trim edge marked and the five page boxes listed |
| `Invoice` | MigraDoc — styles, header and footer page fields, tab stops, line items, totals |
| `Newspaper` | masthead, a headline across the measure, five columns of body, a sidebar, a captioned image |
| `Magazine` | a full-bleed image under a gradient scrim, a title built with `AddString`, a slanted pull-quote, a drop cap from `XTextFormatter.DropCap` |
| `SideWrap` | MigraDoc — `WrapFormat.Style` on a text frame, one page for each of the four side-wrapping styles |

`Layout` and `PageResize` are where the two existing samples end up. Neither is deleted; both are
given a name, a description and a file of their own.

The split between the last three is not arbitrary. `Invoice` is MigraDoc's kind of document — flowed
content, styles, a table that breaks across pages. `Newspaper` and `Magazine` are not: **MigraDoc
has no multi-column page setup**, so a newspaper laid out through it would have to be hand-positioned
text frames. `XTextFormatter.Columns` does the job directly, which is why those two are drawn on the
PdfSharp side. The three together are also the honest answer to "which API do I reach for" — the
demos disagree with each other on purpose.

`SideWrap` is the fourth in that argument and the sharpest of them. It is `Magazine`'s pull quote
done the other way: one paragraph, one text frame, one property, and the renderer breaking the lines.
Read the two side by side to see what the choice of engine actually costs.

### Where the drawing surface is thinner than it looks

Worth knowing before writing a demo that promises more than exists:

- There is no fit-to-box or cover helper for images. `Images` computes those rectangles itself, and
  that computation *is* the demonstration.
- There is no list support outside MigraDoc's `ListInfo`. `Layout` draws its bullets by hand;
  `Invoice` has the real one.
- There is no kerning and no OpenType feature support at all — see `pdfkit-text-parity.md`. No demo
  should imply otherwise.
- `XGraphics.DrawString` does not wrap, and draws `\n` literally. Wrapping is `XTextFormatter`'s job
  and the demos should not blur the two.
- **`XTextFormatter` does not flow text beside a shape, and is not going to.** MigraDoc does, as of
  `shape-side-wrap` — `WrapFormat.Style` takes `Left`, `Right`, `Largest` or `Both`, and `SideWrap`
  shows all four. But that lives in the document object model, where a shape is an element of the
  tree the renderer lays out. The formatter draws onto a surface that holds no shapes at all, so
  `Magazine`'s pull quote is still a rectangle with the body text split into two blocks around it by
  hand. That split stays. It is not a gap waiting to be filled: it is what drawing a page looks like
  as against laying one out, and having both demos on the shelf is the clearest statement of the
  difference the app makes.

A drop cap used to be on this list. `XTextFormatter.DropCap` is now a property: it takes the first
character of the text, scales it so its foot rests on the last reserved line's baseline, reserves the
room and shortens the lines beside it. `Magazine` used to spend thirty lines drawing the letter and
then adding one word at a time to a probe rectangle until the answer stopped fitting — a workaround
the demo was teaching as though it were a technique.

### Four gaps this app found, and closed

Four more turned up while building, each of which had a demo claiming something the library did not
do. Three of them failed **silently** — no exception, no warning, just a page missing the thing that
was asked for. They were found because something drew a page and a human looked at it; nothing in
the test suite would have caught any of them.

All four were fixed under the `fix-drawing-gaps` change, which this app is what prompted. They are
recorded here because *finding* them is the clearest argument the app makes for its own existence:

- **A gradient carried no transparency.** `PdfShading` wrote `/C0` and `/C1` and no soft mask, so a
  gradient from a transparent colour to an opaque one rendered as a flat opaque band. `Magazine`
  built its scrim from a hundred and forty translucent solid fills. It now draws one gradient.
- **`XGraphicsPath.AddString` was not implemented.** It did not throw — it reported through
  `DiagnosticsHelper` and drew nothing, so a title built that way silently disappeared. `Magazine`
  stroked its title with a pen instead. It now builds a path and fills it with a gradient, which is
  the thing a path can do that no `DrawString` overload can.
- **`XLineAlignment.BaseLine` demanded a zero-height rectangle.** Passing a height threw rather than
  being ignored — including for `XStringFormats.Default`, which *is* `BaseLineLeft`. The height is
  now unread rather than refused.
- **A repeated table heading had to start at row zero and said nothing when it did not.**
  `TableRenderer.CalcLastHeaderRow` walks from the first row and stops at the first without
  `HeadingFormat`. A title band above the column names ended the run before it started and nothing
  repeated. The rule is unchanged; a row marked outside the run is now refused rather than discarded.

A fifth turned up while fixing the first: **no gradient the library wrote was visible in a
conformant reader at all.** An RGB shading's ramp carried a fourth value, the colour's alpha, which
is not a colour component — and Ghostscript answers a function wider than its colour space by
painting nothing. That is why the scrim above was built from solid fills rather than from a gradient
that merely looked wrong.

### And one that worked all along

`Magazine`'s photograph is described above as "bled off three edges". It is not, in the sense a
printer means: it runs to the edge of an ordinary page, so a cut landing a fraction inside it leaves
a white line. A real bleed needs a sheet larger than the page and artwork drawn past the page's edge
onto it.

`PdfPage.TrimMargins` does exactly that, and always has. It moves the drawing origin to the trimmed
page's corner, grows the sheet by the margins, and writes all five page boxes on save. It had **no
test of any kind** and no demo, and nothing in the documentation said it existed — so a caller
wanting a bleed had no way to find out that the library already did it.

This is the same problem as the five above, arrived at from the other side. There, a feature
silently did nothing. Here, a feature silently did the right thing and nobody knew. Both are one
refactor away from being a broken feature nobody notices, and the demo is what makes the difference
in each case: `Bleed` exists so that the feature is discoverable, and the tests behind it exist so
that it stays working.

Writing those tests found three defects in it, all sharing one cause — `PdfPage.PrepareForSave`
derived the sheet from `Width`, and `Width` reads the media box it then overwrote. A trimmed page
therefore grew every time it was saved, stopped reporting its own size once it had been, and put
`/TrimBox` on the wrong edges whenever the top and bottom margins differed. All three are fixed, and
the tests that recorded them now assert the fix. A fourth thing the tests made plain was that
`/BleedBox` was written equal to `/MediaBox`, leaving nowhere on the sheet for a crop mark; the boxes
now nest properly and the marks are drawn. See
`openspec/changes/cover-page-bleed/specs/page-bleed/spec.md`.

## Item 5 — the smoke test

One theory in `PdfSharpCore.Test`, over the demo registry, running each demo into the test
assembly's output directory and asserting the PDF opens and has the page count the demo declares.
Adding a demo enrols it; there is no list to keep in step. A second test asserts each demo's source
region was found and is not empty, so a demo whose markers went missing fails rather than printing
nothing.

That is a test that a demo still *runs*, not that it still demonstrates what it claims. Nothing
automatic can check the second thing, which is why item 4's table is written in terms of what a
reader should look for.

---

## Deliberately not done

- **No golden images.** The machinery exists, and using it here would make every deliberate
  improvement to a demo's appearance a reference-image update. Demos are meant to be edited.
- **No reflection over the assembly to discover demos.** An explicit registry is one line longer per
  demo and gives the smoke test a real data source instead of an implicit one.
- **No `Spectre.Console.Cli`.** It is a fine argument parser and this project already chose one.
- **The prose samples under `docs/` are not deleted or rewritten.** They cover ground the demos do
  not — splitting, concatenating, protecting, exporting images — and that is a separate piece of
  work. `TextLayout.md`, which documents an overload that no longer exists, is worth fixing on its
  own account.
- **No charting demo.** `PdfSharpCore.Charting` is in the solution and would deserve one, but the
  brief was the drawing surface and adding it would mean a fourth project reference for one page.
