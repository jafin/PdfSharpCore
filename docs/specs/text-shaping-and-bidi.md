# Proposal — complex-script shaping, bidirectional text and font fallback

What real script support would cover, and what it would deliberately leave out.
Gap **G3** of `autoresearch/improve-260816-1032/improvement-plan.md`. Nothing here is built.

| item | what | status |
|---|---|---|
| 1 | `ITextShaper` — a fourth static seam beside `GlyphOutlineProvider` | proposed |
| 2 | `HarfBuzzTextShaper` in `PdfSharpCore.Skia` | proposed |
| 3 | UAX #9 bidirectional algorithm, in the core | proposed |
| 4 | UAX #24 script itemisation | proposed |
| 5 | Font fallback chain | proposed, **breaking** |
| 6 | Every measurement path goes through shaped advances | proposed, **breaking** |
| 7 | `ParagraphFormat.TextDirection` on the DOM | proposed |

Estimated effort: **9–13 engineer-weeks.** Shaping seam 3 · bidi and itemisation 3 · fallback 2 ·
measurement rework 3–4 · golden-image churn 1.

---

## The defect

Three related absences, and together they are the oldest unresolved complaint against PdfSharp.

**Nothing shapes.** A string goes to the page one character at a time, each mapped to the glyph the
font's `cmap` gives for that code point. For Latin that is nearly right — it loses kerning and
ligatures. For Arabic it is *wrong*: the alphabet has initial, medial, final and isolated forms of most
letters, and Unicode stores the letter, not the form. So Arabic renders as a row of isolated shapes
that never join. For Devanagari, Thai and Khmer — reordering, conjuncts, split vowels — it is not even
close.

**Nothing reorders.** Right-to-left runs come out in logical order, which is to say backwards. The
documented user experience is that `"سلام"` draws as `"م ا ل س"`. Users work around it by detecting
Arabic, substituting positional forms from a hand-built table, and reversing the string.

**Nothing falls back.** `Drawing/XGlyphTypeface.cs:102` says it outright:

```csharp
// No fallback - just stop.
```

A code point the selected face has no glyph for becomes `.notdef` — an empty box, or nothing — with no
warning at any layer.

`empira/PDFsharp-1.5#144` has tracked the RTL half of this for years.

## Why it is worth doing here

iText does not include this either. It is **`pdfCalligraph`, a paid add-on** on top of a library that
is already AGPL-or-commercial. QuestPDF advertises RTL and bidirectional text but documents no shaping
engine and no font fallback. Shipping real shaping *in the box, under MIT* is a position neither
incumbent occupies, and it is the difference between "supports Arabic" and supports Arabic.

---

## Item 1 — the seam

Follow the pattern this repository already established for optional capability. `README.md` and
`CLAUDE.md` describe three static seams — `FontResolver`, `ImageSourceImpl`, `GlyphOutlineProvider` —
each throwing a descriptive `InvalidOperationException` when read unset, each supplied by a backend, so
the core package carries no font or imaging dependency. This is the fourth:

```csharp
public interface ITextShaper
{
    ShapedRun Shape(ReadOnlySpan<char> text, XFont font,
                    TextDirection direction, string script, string language);
}

public readonly struct ShapedGlyph
{
    public ushort GlyphId { get; }
    public double Advance { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }
    public int Cluster { get; }      // index into the source text
}
```

**Clusters are not optional.** They are the character↔glyph map, and three separate things need it:
line breaking (you may only break at a cluster boundary), `/ToUnicode` construction (a ligature is one
glyph and two characters), and `/ActualText` for tagged output. A shaping API that returns glyphs and
advances but no clusters is not usable for a PDF writer, only for a screen.

Like `GlyphOutlineProvider`, a shaper must take its font bytes **through** `FontResolver` rather than
resolving a family itself, or the two seams disagree about which face a family means.

Unset, the shaper falls back to today's behaviour — one code point, one glyph, no reordering — so
nothing existing breaks by omission.

## Item 2 — the HarfBuzz implementation

`SkiaSharp.HarfBuzz` **4.150.1** exists, matching the `SkiaSharp` 4.150.1 this repo already pins in
`PdfSharpCore.Skia.csproj`, and it targets `netstandard2.1` — so the Unity leg described in `CLAUDE.md`
survives. `HarfBuzzSharp` can also be taken directly, without Skia, which matters for the open
question below.

**DECISION NEEDED — the ImageSharp backend.** `PdfSharpCore.ImageSharp` is pinned to
`SixLabors.Fonts` 1.0.1 for licence reasons (the 2.0 relicensing), and that version's advanced
typography support is limited. Two ways out:

- **Accept that shaping requires the Skia backend.** Cheapest, and honest, but it makes the two
  backends unequal in a way they currently are not.
- **A standalone `PdfSharpCore.HarfBuzz` package** over `HarfBuzzSharp` alone, usable from either
  backend. Cleaner, one more package to ship, and it duplicates a native dependency Skia users already
  have.

The second is better and neither is wrong.

## Items 3 and 4 — bidi and itemisation belong in the core

The Unicode Bidirectional Algorithm is pure text processing. It touches no font, no image and no
backend, so it goes in `PdfSharpCore` itself rather than behind the seam. Roughly 1,500 lines.

The reason to be confident about that estimate: **it is exactly testable.** The Unicode Character
Database ships `BidiTest.txt` and `BidiCharacterTest.txt`, conformance suites of hundreds of thousands
of cases with expected levels and reorderings. This is a data-driven test class, not a judgement call —
either the implementation passes the suite or it does not.

UAX #24 script itemisation splits a mixed string into runs the shaper can handle one at a time
("this much is Arabic, this much is Latin"). Smaller, and it feeds both shaping and fallback.

## Item 5 — fallback

A run whose primary face lacks glyphs gets split and re-resolved against a chain. This needs `cmap`
coverage queries, which `Fonts.OpenType/OpenTypeDescriptor.cs` can already answer.

**Breaking:** either `IFontResolver` grows coverage and fallback members — breaking every existing
implementer, including `Utils/FontResolverBase` and both backends' resolvers — or a separate
`IFontFallbackResolver` is introduced and the seam stays two interfaces wide. Given breaking changes
are acceptable, widening `IFontResolver` is the better design; it keeps one seam meaning one thing.

## Item 6 — the invasive part

Everything above is additive and self-contained. This is neither.

Every measurement path has to go through shaped advances rather than summing per-character widths:

- `Drawing/XGraphics.cs` — `MeasureString`
- `Drawing.Layout/XTextFormatter.cs` — the line breaker, and the obstacle/band logic in
  `openspec/specs/text-flow-regions`
- `MigraDocCore.Rendering/ParagraphRenderer.cs`
- `PdfSharpCore.Charting` — axis and data labels

Kerning and ligatures change advance widths, which changes where lines break, which changes where
everything below them sits. **This will move existing golden images**, including ones for features that
have nothing to do with text shaping. That is not a regression to be fixed; it is the correct output
appearing for the first time, and the budget must include regenerating and re-reviewing them. Per
`CLAUDE.md`, `PinnedFontResolver` serves Liberation Sans precisely because glyph widths decide where a
line wraps — this change is that observation applied at scale.

The write path is in better shape: `XGraphicsPdfRenderer` already emits glyph IDs for Type0 fonts, so
handing it shaped GIDs is close to what it does now. `Pdf.Advanced/PdfToUnicodeMap.cs` must be built
from clusters rather than one-to-one, or extracted text and screen readers see ligatures as garbage.

---

## What this deliberately does not cover

- **Vertical writing mode** (CJK `vert`/`vrt2`, `/WMode 1`). A separate and substantial piece of work.
- **Automatic language detection.** The caller says what language a run is, or the shaper is told
  nothing and uses script defaults. Guessing is how you get Turkish `i` wrong.
- **Justification by kashida** (Arabic elongation) rather than by inter-word space. Correct for Arabic
  typography, well beyond a first implementation.
- **OpenType feature control** (`ss01`, `onum`, `smcp` …). The seam can carry a feature list from day
  one; exposing it on `XFont` is later work.
- **Hyphenation.** Related and separate — see the gap analysis; it is 2–3 weeks on its own and blocked
  on pattern-file licensing.
- **CFF subsetting.** Shaping makes CJK OpenType faces *more* attractive, which makes the whole-face
  embed described in `README.md` hurt more. Still a separate 4–6 week item.

## Tests

`PdfSharpCore.Test` for the shaping seam and fallback; a dedicated data-driven class for the Unicode
bidi conformance files. Per `CLAUDE.md`, a test needing its own font calls `PinnedFontResolver.Register`
rather than swapping the resolver out from under everything running beside it — and this work needs
several: an Arabic face, a Devanagari face, and one deliberately missing coverage to exercise fallback.
Licensing of those test fonts needs checking before they are committed.

## Related

- `docs/specs/font-embedding-gaps.md` — the existing account of what embedding does and does not do.
- `docs/specs/layout-api-decision.md` — a new layout API should not be built on unshaped text.
