# Spec — What testing the charting renderers found

`PdfSharpCore.Charting` had no tests. The whole assembly measured 0% of lines, and the ten
highest-CRAP methods in the fork were all in it — `YAxisRenderer.FineTuneYAxis` at 2,162, then the
axis, plot area and data label renderers behind it.

`PdfSharpCore.Charting.Tests` covers them. 105 tests on each of `net8.0` and `net10.0`; the
assembly goes from 0% of lines to 71.09%, and the ten hotspot methods from nothing to between 92.7%
and 100%.

Every renderer in the package is `internal` and this repository carries no `InternalsVisibleTo`, so
the tests reach them the way a caller does: a `Chart` handed to a `ChartFrame`, drawn onto a page,
saved, reopened, and read back out of the content stream. That has a consequence worth stating
plainly — **everything below is reachable through public API**. None of it needed reflection to
find, and none of it needs reflection to hit.

Seven defects came out of writing them. All are recorded by a passing test named for the behaviour,
in the manner of `dom-value-model-findings.md`: the test asserts what the code does today, its
remark says why that is wrong, and fixing the code is a matter of flipping the assertion.

| # | finding | severity | pinned by |
|---|---|---|---|
| C1 | A chart with no X axis writes `NaN` coordinates to the page | **high** | `ChartFrameTests.AChartWithNoXAxisDrawsItsColumnsNowhere` |
| C2 | `Series.AddBlank` throws when the chart is drawn | **high** | `ValueAxisScaleTests.ABlankInASeriesThrows` |
| C3 | A blank category throws on a bar chart and is skipped on a column chart | medium | `CategoryAxisTests.ABarChartThrowsOnACategoryWithNoValue` |
| C4 | Fewer categories than values throws on a bar chart and is tolerated on a column chart | medium | `CategoryAxisTests.ABarChartThrowsWhenThereAreFewerCategoriesThanValues` |
| C5 | A frame too small for its axes throws from inside `XRect` | medium | `ColumnPlotAreaTests.AFrameTooSmallForItsAxesThrows` |
| C6 | An axis title's alignment moves it nowhere, or only sometimes | low | `AxisTitleTests.AligningAnUprightCaptionAcrossMovesItNowhere` and two others |
| C7 | `DataLabelPosition.InsideBase` stacks every pie label on one point | low | `DataLabelTests.APieLabelledAtItsBaseStacksEveryLabelOnOneSpot` |

C1, C3 and C4 are one shape seen three times: two renderers written as copies of each other, which
have since drifted apart on which inputs they survive.

---

## C1. A chart with no X axis writes `NaN` to the page

`Chart.XAxis` creates the axis the first time it is read, so a chart nothing configured has none:

```csharp
var chart = new Chart(ChartType.Column2D);
chart.XValues.AddXSeries().Add("A", "B");
chart.SeriesCollection.AddSeries().Add(1.0, 2.0);
// draw it into a ChartFrame and the content stream reads:
//   NaN NaN NaN 200 re
//   f
```

`HorizontalXAxisRenderer.Init` puts its scale calculation inside a null check:

```csharp
xari.axis = chart.xAxis;
if (xari.axis != null)
{
  CalculateXAxisValues(xari);   // sets MinimumScale, MaximumScale, MajorTick, MinorTick
  InitTickLabels(xari, cri.DefaultFont);
  InitAxisTitle(xari, cri.DefaultFont);
  InitAxisLineFormat(xari);
  InitGridlines(xari);
}
```

With no axis, `MaximumScale` keeps its default of zero. `ColumnLikePlotAreaRenderer.Format` then
builds the plot area's matrix by dividing by it:

```csharp
matrix.Scale(plotAreaBox.Width / xMax, plotAreaBox.Height / (yMax - yMin), XMatrixOrder.Append);
```

`width / 0` is infinity, `0 * infinity` is `NaN`, and `NaN` is what `XGraphicsPdfRenderer` writes.
The draw succeeds and the file is written; a reader gets a page whose content stream will not parse.
PdfSharpCore's own content lexer answers it with
`KeyNotFoundException: The given key 'NaN' was not present in the dictionary`.

The height survives because it comes from the value axis, which does not have this problem — and
that is the fix. `VerticalYAxisRenderer.Init` calls `InitScale` **before** it asks whether the axis
object exists:

```csharp
yari.axis = chart.yAxis;
InitScale(yari);              // outside the check
if (yari.axis != null)
{
  InitTickLabels(yari, cri.DefaultFont);
  ...
}
```

So a chart with no *value* axis is drawn correctly and merely goes unlabelled — pinned by
`ValueAxisScaleTests.AChartWithNoValueAxisObjectIsScaledAllTheSameAndMerelyGoesUnlabelled`. One line
apart, and the two renderers disagree about which side of it the scale calculation belongs on.

**Fix:** move `CalculateXAxisValues(xari)` out of the null check in both
`HorizontalXAxisRenderer.Init` and `VerticalXAxisRenderer.Init`, matching the Y axis renderers. It
reads the series collection through `rendererInfo.axis.parent`, so it needs the chart passed
another way — `this.rendererParms.DrawingItem`, which `Init` already has in hand.

**Why nothing had noticed:** MigraDoc's chart mapper builds both axes unconditionally, so a chart
reached through a `Document` never takes this path. It is only reachable by using
`PdfSharpCore.Charting` directly, which is exactly what the package is for.

---

## C2. `Series.AddBlank` throws when the chart is drawn

```csharp
var series = chart.SeriesCollection.AddSeries();
series.Add(1.0);
series.AddBlank();
// NullReferenceException in VerticalYAxisRenderer.CalcYAxis
```

`AddBlank` is public, is documented as "Adds a blank to the series", and puts a `null` into the
element collection. The pass that finds the smallest and largest value walks those elements testing
each for `NaN` without first testing it for `null`:

```csharp
foreach (Point point in series.Elements)
{
  if (!double.IsNaN(point.value))     // point is null for a blank
  {
    yMin = Math.Min(yMin, point.Value);
    yMax = Math.Max(yMax, point.Value);
  }
}
```

So the one thing the method exists to permit is the one thing the renderer cannot survive.

**Fix:** `if (point != null && !double.IsNaN(point.value))`. The same walk appears in
`HorizontalYAxisRenderer.CalcYAxis` and in the stacked renderers' overrides of it; all want the
same guard.

**Side effect:** this also leaves the first case of `FineTuneYAxis` unreachable —

```csharp
if (yMin == double.MaxValue && yMax == double.MinValue)
{
  // No series data given.
  yMin = 0.0f;
  yMax = 0.9f;
}
```

— because a series of nothing but blanks is the only way to get there, and a series of nothing but
blanks throws first. It is the one part of the method the tests leave uncovered, and fixing C2 is
what would make covering it possible.

---

## C3, C4. The vertical category axis renderer is missing two guards

`HorizontalXAxisRenderer.Draw` and `VerticalXAxisRenderer.Draw` are the same method with the axes
swapped. Where the horizontal one reads:

```csharp
for (int idx = 0; idx < countTickLabels && idx < xs.Count; ++idx)
{
  XValue xv = xs[idx];
  if (xv != null)
  {
    ...
  }
}
```

the vertical one reads:

```csharp
for (int idx = countTickLabels - 1; idx >= 0; --idx)
{
  XValue xv = xs[idx];
  string tickLabel = xv.Value;
  ...
}
```

Two guards short, and each is a throw rather than a wrong picture:

- **C3** — a category added with `XSeries.AddBlank` is `null`. The column chart skips it; the bar
  chart throws `NullReferenceException`.
- **C4** — `countTickLabels` comes from the longest *series*, not from the category list, so a chart
  with three values and two categories asks for a third that is not there. The column chart stops at
  the end of the list; the bar chart throws `ArgumentOutOfRangeException`.

**Fix:** carry both conditions across. Reversed iteration makes the bound
`idx >= 0 && idx < xs.Count` rather than a second clause on the same side.

---

## C5. A frame too small for its axes throws from inside `XRect`

Both plot area renderers open by returning if there is no room to draw in:

```csharp
XRect plotAreaBox = cri.plotAreaRendererInfo.Rect;
if (plotAreaBox.IsEmpty)
  return;
```

which reads as a decision that a chart too small to draw should draw nothing. It is unreachable from
this direction. `ColumnLikeChartRenderer.CalcLayout` subtracts the axes from the frame and assigns
the remainder as a width, and `XRect.Width` refuses a negative one:

```csharp
Drawn.Page(Charts.Of(ChartType.Column2D, 1.0, 5.0, 3.0), width: 20, height: 15);
// System.ArgumentException: WidthCannotBeNegative
//   at PdfSharpCore.Drawing.XRect.set_Width
//   at PdfSharpCore.Charting.Renderers.AreaRendererInfo.set_Width
//   at PdfSharpCore.Charting.Renderers.ColumnLikeChartRenderer.CalcLayout
```

What a caller gets is an exception three frames down naming a rectangle rather than the chart. The
size at which it starts depends on how wide the tick labels measure in the resolved font, so it is
not a fixed threshold a caller could be told about, and thirty-five points is not an absurd size for
a chart on a dense page.

**Fix:** clamp to zero in `CalcLayout` and let the existing `IsEmpty` guards do what they were
written to do.

---

## C6. An axis title's alignment moves it nowhere, or only sometimes

`AxisTitle.Alignment` and `AxisTitle.VerticalAlignment` are public and settable. What they do
depends on which axis the title is on and whether it is turned:

| | horizontal alignment | vertical alignment |
|---|---|---|
| value axis, upright | no effect | works |
| value axis, rotated | Center and Right coincide | Center and Bottom coincide |
| category axis | unread | unread |

Three separate causes:

- **Upright, horizontally.** The caption is drawn into `atri.Rect`, and `Format` set that rectangle
  to exactly the size the caption measured. There is no slack across it to align within.
- **Rotated.** The two cases are written separately but reduce to the same expression, because
  `layout` is the title's own rectangle and so `layout.Width` is the width being halved either way:

  ```csharp
  case HorizontalAlignment.Center: x = atri.X + atri.Width / 2;                     break;
  case HorizontalAlignment.Right:  x = atri.X + atri.Width - layout.Width / 2;      break;
  ```

- **Category axis.** `AxisTitleRenderer` is never constructed for it. `HorizontalXAxisRenderer.Draw`
  and `VerticalXAxisRenderer.Draw` each end by drawing the caption themselves, at the middle of the
  axis, reading neither the alignment nor the orientation. A caption on a category axis is written
  flat whatever it was asked for.

**Fix:** the first two want the layout rectangle to be the space available rather than the space
occupied. The third wants the category axis renderers to hand off to `AxisTitleRenderer` as the
value axis renderers do — which would fix the orientation as well, and would remove two copies of
the same drawing code.

---

## C7. `DataLabelPosition.InsideBase` stacks every pie label on one point

```csharp
case DataLabelPosition.InsideBase:
  // Aligned at the base/center of the circle
  dleri.X = origin.X;
  dleri.Y = origin.Y;
  if (dleri.X < origin.X)          // cannot be: it was just set to origin.X
    dleri.X -= dleri.Width;
  if (dleri.Y < origin.Y)          // likewise
    dleri.Y -= dleri.Height;
  break;
```

The two adjustments are copied from the `OutsideEnd` case above, where the comparison means
something because the position came from an angle. Here it is being compared against itself, so
neither runs, and every label is drawn at the centre of the pie on top of every other. A four-wedge
pie asked for `InsideBase` produces four labels at one point.

**Fix:** decide what the position is meant to mean — the wording suggests the inner end of each
wedge, which would be the `InsideEnd` arithmetic at a small radius rather than at zero — and either
implement it or remove the case so that the enum does not offer what it cannot do.

---

## What is deliberately not covered

- **Rasterization.** Nothing here renders a chart to an image. The test project references no
  backend and needs neither Ghostscript nor ImageMagick, which is what lets it run anywhere. A chart
  is asserted through the operators it wrote, not through the pixels they would produce.
- **Line, area and pie geometry.** `EveryChartTypeDrawsSomething` covers that each type draws, and
  the pie's data labels are covered in detail because `PieDataLabelRenderer.CalcPositions` was on the
  list. Where the wedges and the line segments themselves land is not asserted; those renderers were
  not among the ten and would want a path reader rather than a rectangle reader.
- **Legends.** `LegendRenderer` and its three subclasses draw an entry per series with a swatch
  beside it. One test reaches the swatch incidentally; the docking, wrapping and entry layout are
  untested.
- **`Chart.Clone`, `XValue` conversions and the rest of the object model.** Those belong with
  `MigraDocCore.DocumentObjectModel.Tests`, which already covers the *MigraDoc* chart object model
  from the DOM side.
