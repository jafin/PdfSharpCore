#region PDFsharp Charting - A .NET charting library based on PDFsharp
//
// Authors:
//   Niklas Schneider (mailto:Niklas.Schneider@PdfSharpCore.com)
//
// Copyright (c) 2005-2009 empira Software GmbH, Cologne (Germany)
//
// http://www.PdfSharpCore.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Globalization;
using PdfSharpCore.Drawing;

namespace PdfSharpCore.Charting.Renderers;

/// <summary>
/// Represents an axis renderer used for charts of type Bar2D.
/// </summary>
internal class VerticalXAxisRenderer : XAxisRenderer
{
  /// <summary>
  /// Initializes a new instance of the VerticalXAxisRenderer class with the specified renderer parameters.
  /// </summary>
  internal VerticalXAxisRenderer(RendererParameters parms) : base(parms)
  {
  }

  /// <summary>
  /// Returns an initialized rendererInfo based on the X axis.
  /// </summary>
  internal override RendererInfo Init()
  {
    Chart chart = (Chart)this.rendererParms.DrawingItem;

    AxisRendererInfo xari = new AxisRendererInfo();
    xari.axis = chart.xAxis;

    // Outside the test below, for the reason given in HorizontalXAxisRenderer.Init: the scale is
    // what the plot area is laid out against, and a chart with no Axis object still has data.
    CalculateXAxisValues(chart, xari);

    if (xari.axis != null)
    {
      ChartRendererInfo cri = (ChartRendererInfo)this.rendererParms.RendererInfo;

      InitXValues(xari);
      InitAxisTitle(xari, cri.DefaultFont);
      InitTickLabels(xari, cri.DefaultFont);
      InitAxisLineFormat(xari);
      InitGridlines(xari);
    }
    return xari;
  }
    
  /// <summary>
  /// Calculates the space used for the X axis.
  /// </summary>
  internal override void Format()
  {
    AxisRendererInfo xari = ((ChartRendererInfo)this.rendererParms.RendererInfo).xAxisRendererInfo;
    if (xari.axis != null)
    {
      AxisTitleRendererInfo atri = xari.axisTitleRendererInfo;

      // Calculate space used for axis title, through the renderer that draws it, for the reason
      // given in HorizontalXAxisRenderer.Format: measuring the string here took no account of the
      // title's orientation.
      XSize titleSize = new XSize(0, 0);
      if (atri != null && atri.AxisTitleText != null && atri.AxisTitleText.Length > 0)
      {
        RendererParameters parms = new RendererParameters();
        parms.Graphics = this.rendererParms.Graphics;
        parms.RendererInfo = xari;
        new AxisTitleRenderer(parms).Format();
        titleSize = atri.AxisTitleSize;
      }

      // Calculate space used for tick labels.
      XSize size = new XSize(0, 0);
      foreach (XSeries xs in xari.XValues)
      {
        foreach (XValue xv in xs)
        {
          // A category added with XSeries.AddBlank is a null, as it is in Draw below and as the
          // horizontal renderer's own Format already allows for.
          if (xv != null)
          {
            XSize valueSize = this.rendererParms.Graphics.MeasureString(xv.Value, xari.TickLabelsFont);
            size.Height += valueSize.Height;
            size.Width = Math.Max(valueSize.Width, size.Width);
          }
        }
      }

      // Remember space for later drawing.
      if (atri != null)
        atri.AxisTitleSize = titleSize;
      xari.TickLabelsHeight = size.Height;
      xari.Height = size.Height;
      xari.Width = titleSize.Width + size.Width + xari.MajorTickMarkWidth;
    }
  }

  /// <summary>
  /// Draws the horizontal X axis.
  /// </summary>
  internal override void Draw()
  {
    XGraphics gfx = this.rendererParms.Graphics;
    ChartRendererInfo cri = (ChartRendererInfo)this.rendererParms.RendererInfo;
    AxisRendererInfo xari = cri.xAxisRendererInfo;

    double xMin = xari.MinimumScale;
    double xMax = xari.MaximumScale;
    double xMajorTick = xari.MajorTick;
    double xMinorTick = xari.MinorTick;
    double xMaxExtension = xari.MajorTick;

    // Draw tick labels. Each tick label will be aligned centered.
    int countTickLabels = (int)xMax;
    double tickLabelStep = xari.Height / countTickLabels;
    XPoint startPos = new XPoint(xari.X + xari.Width - xari.MajorTickMarkWidth, xari.Y + tickLabelStep / 2);
    foreach (XSeries xs in xari.XValues)
    {
      for (int idx = countTickLabels - 1; idx >= 0; --idx)
      {
        // Both conditions carried across from HorizontalXAxisRenderer.Draw, which this method is
        // otherwise a copy of. The count comes from the longest series rather than from the
        // category list, so there need not be a category at every index; and a category added
        // with XSeries.AddBlank is a null. Neither is unusual enough to throw over.
        XValue xv = idx < xs.Count ? xs[idx] : null;
        if (xv != null)
        {
          string tickLabel = xv.Value;
          XSize size = gfx.MeasureString(tickLabel, xari.TickLabelsFont);
          gfx.DrawString(tickLabel, xari.TickLabelsFont, xari.TickLabelsBrush, startPos.X - size.Width, startPos.Y + size.Height / 2);
        }
        startPos.Y += tickLabelStep;
      }
    }

    // Draw axis.
    // First draw tick marks, second draw axis.
    double majorTickMarkStart = 0, majorTickMarkEnd = 0,
      minorTickMarkStart = 0, minorTickMarkEnd = 0;
    GetTickMarkPos(xari, ref majorTickMarkStart, ref majorTickMarkEnd, ref minorTickMarkStart, ref minorTickMarkEnd);

    LineFormatRenderer lineFormatRenderer = new LineFormatRenderer(gfx, xari.LineFormat);
    XPoint[] points = new XPoint[2];

    // Minor ticks.
    if (xari.MinorTickMark != TickMarkType.None)
    {
      int countMinorTickMarks = (int)(xMax / xMinorTick);
      double minorTickMarkStep = xari.Height / countMinorTickMarks;
      startPos.Y = xari.Y;
      for (int x = 0; x <= countMinorTickMarks; x++)
      {
        points[0].X = minorTickMarkStart;
        points[0].Y = startPos.Y + minorTickMarkStep * x;
        points[1].X = minorTickMarkEnd;
        points[1].Y = points[0].Y;
        lineFormatRenderer.DrawLine(points[0], points[1]);
      }
    }

    // Major ticks.
    if (xari.MajorTickMark != TickMarkType.None)
    {
      int countMajorTickMarks = (int)(xMax / xMajorTick);
      double majorTickMarkStep = xari.Height / countMajorTickMarks;
      startPos.Y = xari.Y;
      for (int x = 0; x <= countMajorTickMarks; x++)
      {
        points[0].X = majorTickMarkStart;
        points[0].Y = startPos.Y + majorTickMarkStep * x;
        points[1].X = majorTickMarkEnd;
        points[1].Y = points[0].Y;
        lineFormatRenderer.DrawLine(points[0], points[1]);
      }
    }

    // Axis.
    if (xari.LineFormat != null)
    {
      points[0].X = xari.X + xari.Width;
      points[0].Y = xari.Y;
      points[1].X = xari.X + xari.Width;
      points[1].Y = xari.Y + xari.Height;
      if (xari.MajorTickMark != TickMarkType.None)
      {
        points[0].Y -= xari.LineFormat.Width / 2;
        points[1].Y += xari.LineFormat.Width / 2;
      }
      lineFormatRenderer.DrawLine(points[0], points[1]);
    }

    // Draw axis title, through the renderer written for it rather than by hand, for the reason
    // given in HorizontalXAxisRenderer.Draw: drawing it here honoured neither the alignment nor
    // the orientation, both of which are settable and both of which the value axis honours.
    AxisTitleRendererInfo atri = xari.axisTitleRendererInfo;
    if (atri != null && atri.AxisTitleText != null && atri.AxisTitleText.Length > 0)
    {
      // The strip to the left of the tick labels, the full height of the axis, so that an
      // alignment has somewhere to move the caption to.
      atri.Rect = new XRect(xari.Rect.Left, xari.Rect.Top,
        atri.AxisTitleSize.Width, xari.Rect.Height);

      RendererParameters parms = new RendererParameters();
      parms.Graphics = gfx;
      parms.RendererInfo = xari;
      new AxisTitleRenderer(parms).Draw();
    }
  }

  /// <summary>
  /// Calculates the X axis describing values like minimum/maximum scale, major/minor tick and
  /// major/minor tick mark width.
  /// </summary>
  private void CalculateXAxisValues(Chart chart, AxisRendererInfo rendererInfo)
  {
    // The chart is passed in rather than reached through rendererInfo.axis.parent, because this
    // runs for a chart that has no axis to be reached through.
    SeriesCollection seriesCollection = chart.seriesCollection;

    // Calculates the maximum number of data points over all series.
    int count = 0;
    foreach (Series series in seriesCollection)
      count = Math.Max(count, series.Count);

    rendererInfo.MinimumScale = 0;
    rendererInfo.MaximumScale = count; // At least 0
    rendererInfo.MajorTick = 1;
    rendererInfo.MinorTick = 0.5;
    rendererInfo.MajorTickMarkWidth = DefaultMajorTickMarkWidth;
    rendererInfo.MinorTickMarkWidth = DefaultMinorTickMarkWidth;
  }

  /// <summary>
  /// Initializes the rendererInfo's xvalues. If not set by the user xvalues will be simply numbers
  /// from minimum scale + 1 to maximum scale.
  /// </summary>
  private void InitXValues(AxisRendererInfo rendererInfo)
  {
    rendererInfo.XValues = ((Chart)rendererInfo.axis.parent).xValues;
    if (rendererInfo.XValues == null)
    {
      rendererInfo.XValues = new XValues();
      XSeries xs = rendererInfo.XValues.AddXSeries();
      for (double i = rendererInfo.MinimumScale + 1; i <= rendererInfo.MaximumScale; ++i)
        xs.Add(i.ToString(CultureInfo.InvariantCulture));
    }
  }

  /// <summary>
  /// Calculates the starting and ending y position for the minor and major tick marks.
  /// </summary>
  private void GetTickMarkPos(AxisRendererInfo rendererInfo,
    ref double majorTickMarkStart, ref double majorTickMarkEnd,
    ref double minorTickMarkStart, ref double minorTickMarkEnd)
  {
    double majorTickMarkWidth = rendererInfo.MajorTickMarkWidth;
    double minorTickMarkWidth = rendererInfo.MinorTickMarkWidth;
    double x = rendererInfo.Rect.X + rendererInfo.Rect.Width;

    switch (rendererInfo.MajorTickMark)
    {
      case TickMarkType.Inside:
        majorTickMarkStart = x;
        majorTickMarkEnd = x + majorTickMarkWidth;
        break;

      case TickMarkType.Outside:
        majorTickMarkStart = x - majorTickMarkWidth;
        majorTickMarkEnd   = x;
        break;

      case TickMarkType.Cross:
        majorTickMarkStart = x - majorTickMarkWidth;
        majorTickMarkEnd = x + majorTickMarkWidth;
        break;

      case TickMarkType.None:
        majorTickMarkStart = 0;
        majorTickMarkEnd = 0;
        break;
    }

    switch (rendererInfo.MinorTickMark)
    {
      case TickMarkType.Inside:
        minorTickMarkStart = x;
        minorTickMarkEnd = x + minorTickMarkWidth;
        break;

      case TickMarkType.Outside:
        minorTickMarkStart = x - minorTickMarkWidth;
        minorTickMarkEnd = x;
        break;

      case TickMarkType.Cross:
        minorTickMarkStart = x - minorTickMarkWidth;
        minorTickMarkEnd = x + minorTickMarkWidth;
        break;

      case TickMarkType.None:
        minorTickMarkStart = 0;
        minorTickMarkEnd = 0;
        break;
    }
  }
}
