Groups 1 and 2 pin and demonstrate what already works and do not depend on the crop-mark decision.
Group 3 is the only one that changes behaviour, and is gated on that decision being made.

## 1. Pin what is already there

- [ ] 1.1 Write `PdfSharpCore.Test/Drawing/PageBleedTests.cs` against the **current** behaviour: the
      origin on the trim corner, a negative coordinate reaching the sheet edge, `Width`/`Height`
      still reporting the trimmed size, and the five boxes with their measured values. Write it
      before changing anything — the point is to find out what the code does, not to confirm what
      it ought to do.
- [ ] 1.2 Assert the nesting rule — `TrimBox` within `BleedBox` within `MediaBox` — and record in
      the test what the current values make of it. `BleedBox == MediaBox` satisfies nesting; note
      it rather than fix it here.
- [ ] 1.3 Add a rasterizing test in `[Collection(RasterizingCollection.Name)]` that a band bled off
      an edge puts ink on the sheet's outermost pixels. Reading the content stream proves the
      operators were written; only rasterizing proves nothing clipped them away.
- [ ] 1.4 Test that a page with no trim margin gains no `/TrimBox`, `/BleedBox` or `/ArtBox`, so the
      whole feature stays invisible to every document that does not ask for it.
- [ ] 1.5 Report anything found that contradicts `specs/page-bleed/spec.md`. Amend the spec to what
      is true and say so, rather than fixing the code inside this task — a behaviour change here
      needs its own decision.

## 2. Show it, and say where it applies

- [ ] 2.1 Add `SampleApp/Demos/BleedDemo.cs`: one trimmed page, an image bled off three edges, and a
      thin rule marking the trim boundary so the bleed is visible on screen. Label the rule on the
      page as part of the demonstration rather than part of the artwork.
- [ ] 2.2 Register it, give it `Shows` entries and a `PageCount`, and check the demo smoke tests
      cover it. Remember the two rules in `docs/specs/demonstration-app.md`: a demo never registers
      a backend, and its assets are embedded resources.
- [ ] 2.3 Test the MigraDoc route — a caller-made trimmed page, an `XGraphics` on it, and
      `DocumentRenderer.RenderPage` — asserting the layout is measured from the trimmed page and the
      saved page carries the boxes.
- [ ] 2.4 Correct `docs/specs/demonstration-app.md`: bleed comes out of the list of things the
      library cannot do, with a pointer to the demo.
- [ ] 2.5 XML-document `PdfPage.TrimMargins` with what it moves, what it writes, and the point-units
      restriction. It is public API with no documentation at all today.

## 3. Crop marks

- [ ] 3.1 **Settle the open question in `design.md` before writing anything here.** Option 3 — a
      separate opt-in mark allowance, leaving `TrimMargins` writing exactly the boxes it writes
      today — is the recommendation, because it is the only one of the three that is not a breaking
      change to the boxes of every trimmed page.
- [ ] 3.2 If marks are taken up: grow `MediaBox` past `BleedBox` by the mark allowance and draw the
      eight standard marks in the space that opens up, on the sheet and clear of the bleed.
- [ ] 3.3 If marks are taken up: test that a page with the allowance unset is written byte for byte
      as it is today. This is what makes "not a breaking change" a fact rather than an intention.
- [ ] 3.4 If marks are declined: record the reasoning in the spec, state the nesting rule the
      library does follow, and say plainly that a caller wanting marks draws them — so that
      `BleedBox == MediaBox` reads as a decision rather than as an oversight.
- [ ] 3.5 Add a line to the release notes either way. A new option is an addition; a documented
      refusal is worth a note under the bleed entry.

## 4. Close out

- [ ] 4.1 `./ci-build.ps1` clean and `dotnet test` green on both target frameworks.
- [ ] 4.2 Rasterize the `Bleed` demo and look at it. A bleed that is wrong by 3mm is invisible in any
      assertion that does not go to the pixel, and obvious to the eye against the trim rule.
- [ ] 4.3 Open the demo's PDF in a reader that shows page boxes, or dump them, and check the sheet
      and trim are where a prepress operator would expect.
