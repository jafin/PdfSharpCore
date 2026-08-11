# Task — the test host crashes intermittently at the end of a full run

Not a spec of work done: a brief for work not yet started. Everything below was observed while
building the PDFKit text parity work (`docs/specs/pdfkit-text-parity.md`) and is written down
because chasing it three separate times from scratch is how it will otherwise go.

## The symptom

A full `dotnet test` run ends with

```text
The active test run was aborted. Reason: Test host process crashed

Passed!  - Failed:     0, Passed:  1388, Skipped:     1, Total:  1389, ...
Test Run Aborted.
```

and exit code 1. Every time:

- **`Failed: 0`.** No test fails. The host dies while the run is still going.
- **The counts are short of the total.** 1388 of a suite that reports 1425 when it finishes. How
  many are missing varies from run to run.
- **The time to the crash varies wildly** — 24 seconds to 1 minute 29 on a suite that takes about
  2 minutes 20 to finish. It is not one test, and not the end of the run.

## What is known

**It is not caused by the text parity work.** The decisive run: `git checkout master`, no changes of
any kind, `dotnet test -f net10.0` → crash at 1m11s, 1317 of 1318 passed. It reproduces on the
branch, on the branch with the new work stashed, and on master.

**It is not specific to a target framework.** Seen on both `net8.0` and `net10.0`. An earlier
reading that it was net8.0-only was wrong; net10.0 had simply been lucky for three runs.

**It is intermittent, not flaky-per-test.** The same command passes and fails on the same commit.
Roughly a third to a half of full runs crashed while this was being watched.

**It gets likelier the more pages are rasterized.** The clearest evidence, all on one commit and
one framework:

| what was run | outcome |
|---|---|
| the whole suite | crash, twice running |
| the whole suite, minus the two new rasterizing test classes | clean |
| the whole suite, minus every new test class | clean |
| the two new rasterizing classes on their own — 14 tests | clean |

The new classes are not broken in themselves. Adding about 26 rasterizations to a suite that
already had some moved it from crashing occasionally to crashing often.

**Holding the images open made it worse, and releasing them helped but did not fix it.** The
rendering tests originally kept every `MagickImageCollection` until the class was torn down.
Freeing each one as soon as its pixels had been read (`fb7ccd8`) took net8.0 from two crashes in
two runs to two clean runs in two — and then net10.0 crashed anyway. Mitigation, not a cure.

**`dotnet test --blame` produced no `Sequence.xml`.** Nothing was written anywhere under the repo,
so the run in flight was not recorded. `--blame-crash` was not tried and is the obvious next step.

## Where to look first

The suspicion is native, and at teardown rather than during a test: every failure reports
`Failed: 0`, so nothing was mid-assertion.

- `PdfSharpCore.Test/Helpers/PdfHelper.cs` — `Rasterize` builds a `MagickImageCollection` and calls
  `images.Read(ms, readerSettings)` at 300 dpi, which is what drives Ghostscript.
- `PdfSharpCore.Test/Helpers/GhostscriptSetup.cs` — how Ghostscript is found and configured.
- `PdfSharpCore.Test/Helpers/RasterizingCollection.cs` — the collection exists *because* ImageMagick
  drives one in-process Ghostscript and a second concurrent rasterization falls back to an
  executable. Its comment is the best statement of the constraint anyone has written down. Worth
  re-reading before assuming the collection covers every case: it serializes the tests that declare
  it, and nothing stops a test that rasterizes without declaring it.
- `PdfSharpCore.Test/TestBackendSetup.cs` — the module initializer that registers Skia. Skia is also
  native and also torn down at process exit.

## Things worth trying, roughly in order

1. **`dotnet test --blame-crash`** (and `--blame-crash-collect-always`) to get a dump and a faulting
   module. Everything below is guesswork until that names something.
2. **Windows Event Viewer / WER** for the faulting module of the `testhost` process — cheaper than a
   dump and often enough to point at Ghostscript, Magick.NET or Skia.
3. **Turn xUnit parallelism off** (`maxParallelThreads: 1`, or `parallelizeAssembly: false` in
   `xunit.runner.json`) and see whether it survives. If it does, the fault is concurrency around the
   one in-process Ghostscript rather than a leak.
4. **Make it worse on purpose.** Duplicate the rasterizing tests until the crash is reliable. An
   intermittent fault that can be made near-certain is much cheaper to bisect.
5. **Check the Magick.NET and Ghostscript.NativeAssets versions** against their issue trackers.
   In-process Ghostscript has a long history of being single-instance and unhappy about being
   reinitialized.
6. **Look at what finalizes.** If a `MagickImage` or `MagickImageCollection` reaches a finalizer
   after the native library has been unloaded, the process dies with no managed stack — which
   matches every symptom here.

## What this is not blocking

CI runs on Linux, where Ghostscript is the system `gs` shelled out to rather than
`Ghostscript.NativeAssets` in process — a different path from the one crashing here, which is
Windows. This has not been seen on CI. It costs local full-suite runs, not the build.

Filtering to a subset, or to one test class, has never crashed. `dotnet test --filter` is the
workaround until this is understood.
