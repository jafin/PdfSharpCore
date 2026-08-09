# Spec — soft hyphens in justified list items, issue #339

[empira/PDFsharp#339](https://github.com/empira/PDFsharp/issues/339) reports that a hyphenated word
on the first line of a justified list item renders wrongly, and that with a right indent of 0 or
1mm the renderer never finishes. What follows is the design as built, on
`fix/soft-hyphen-in-justified-list`.

| item | what | status |
|---|---|---|
| 1 | `ReMeasureLine` never says where the line starts | done |
| 2 | `ReMeasureLine` re-breaks the line it is only measuring | done |
| 3 | `TextBaselines` could only answer where text sits vertically | done, needed by the tests |

---

## Why a list is involved at all

A list item is laid out around an automatic tab stop: the bullet is drawn at the list's number
position and a tab carries the cursor to the text's left indent. `FormatTab` sets `reMeasureLine`
whenever the paragraph is justified, because how wide a blank is depends on how much of the line is
left over, and that is not known until the line is complete.

So a justified list item — and only a justified paragraph holding a tab — has each of its lines
measured a second time, by `ReMeasureLine`, from inside `Render`. That second pass runs on a
renderer that has done nothing but render. It is a different object from the one that formatted the
paragraph, and it starts with none of the formatting state.

## Item 1 — the second pass was never told where the line starts

`ReMeasureLine` set `currentLeaf` and `endLeaf` from the line it was given, and left `startLeaf`
alone. `startLeaf` is assigned in `InitFormat` and `StartNewLine`, which the rendering renderer
never calls, and in `RenderLine`, which `Render` calls *after* `ReMeasureLine`.

`FormatSoftHyphen` asks two questions of `startLeaf`:

```csharp
if (currentLeaf.Current == startLeaf.Current)      // is the hyphen the first thing on the line?
...
    || prevIter.Current == startLeaf.Current)      // is the word before it the first thing on it?
```

On the first line of the paragraph `startLeaf` is null and the first question throws a
`NullReferenceException`. On every line after that it holds the start of the *previous* line, so
both questions are answered about the wrong line and the hyphen is placed by a decision made about
somewhere else — the garbled first line in the screenshot on the issue.

`ReMeasureLine` now sets `startLeaf` alongside `currentLeaf` and `endLeaf`. It does not restore it,
for the same reason it does not restore `endLeaf`: `RenderLine` sets both immediately afterwards.

## Item 2 — the second pass re-broke the line

```csharp
while (goOn && currentLeaf != null)
{
    FormatElement(currentLeaf.Current);              // result thrown away
    goOn = currentLeaf != null && currentLeaf.Current != endLeaf.Current;
    if (goOn)
        currentLeaf = currentLeaf.GetNextLeaf();
}
```

`FormatElement` is the formatting machinery, and it moves `currentLeaf` when it decides a line
breaks. `FormatSoftHyphen` on a word that does not fit sets `currentLeaf` to the leaf *before* the
hyphen and answers `NewLine`. The loop discarded the answer and stepped forward, landing back on
the hyphen, which did the same thing again. Nothing else in the loop can end it: `currentLeaf`
never reaches `endLeaf` and never becomes null.

That is the second half of the report. The renderer does not hang in one place — it never finishes
one line, so no page is ever completed.

The loop now stops when an element answers anything but `Continue` or `Ignore`. Where the line
breaks was settled while the paragraph was formatted; this pass only measures it again and has no
business breaking it a second time. Stopping also makes the loop terminate by construction: every
iteration either breaks out or steps forward.

### Why the right indent decides whether it hangs

The second pass measures a justified line with the blank width the render phase uses, which is
worked out from how much of the line is left over. At a narrow right indent that width comes out
just wide enough to push the last element past the edge, and the element that reports it — a soft
hyphen — is the one that steps backwards. At 5mm nothing overflows, the loop runs to `endLeaf`, and
only item 1 shows. That is why the reporter saw a bad first line at 2mm and no document at all at
0 or 1mm, and why the fix for item 1 alone leaves 0, 1 and 2mm hanging.

## Item 3 — the tests needed horizontal positions

`TextBaselines` tracked the text line matrix's x all along and returned only the y. It now exposes
`PositionsOf`, which returns both, and `Of` projects the y out of it. Nothing that used the helper
changes.

That is what lets the tests say what the issue's screenshot says: the words of a line run left to
right, nothing is drawn outside the content area, and every line but the last reaches the right
edge.

## Deliberately not done

- `FormatElement`'s ability to move `currentLeaf` is left as it is. Making measurement and line
  breaking separate concerns would be the deeper fix, and a much larger one; the loop that must not
  break lines now simply does not act on a break.
- The blank width the second pass computes is unchanged. It is what makes a narrow right indent
  overflow, but the overflow is a legitimate finding — the answer is to stop measuring, not to
  measure differently.
