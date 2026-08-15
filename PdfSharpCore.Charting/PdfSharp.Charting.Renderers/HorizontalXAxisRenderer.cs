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
using PdfSharpCore.Drawing;

namespace PdfSharpCore.Charting.Renderers;

/// <summary>
/// Represents an axis renderer used for charts of type Column2D or Line.
/// </summary>
internal class HorizontalXAxisRenderer : XAxisRenderer
{
  /// <summary>
  /// Initializes a new instance of the HorizontalXAxisRenderer class with the specified renderer parameters.
  /// </summary>
  internal HorizontalXAxisRenderer(RendererParameters parms) : base(parms)
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

    // Outside the test below, as the Y axis renderers calculate their scale outside theirs. The
    // scale is what the plot area divides its own width by, so a chart that was never asked for an
    // Axis object - Chart.XAxis creates one on first read - would otherwise be laid out against a
    // maximum of zero and drawn at infinite coordinates. What depends on the axis existing is the
    // labelling, not the scale.
    CalculateXAxisValues(chart, xari);

    if (xari.axis != null)
    {
      ChartRendererInfo cri = (ChartRendererInfo)this.rendererParms.RendererInfo;

      InitTickLabels(xari, cri.DefaultFont);
      InitXValues(xari);
      InitAxisTitle(xari, cri.DefaultFont);
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

      // Calculate space used for axis title, through the renderer that draws it rather than by
      // measuring the string here. Measuring it here took no account of the title's orientation,
      // so a caption turned on its side reserved the room it would have taken lying flat.
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
      if (xari.XValues.Count > 0)
      {
        XSeries xs = xari.XValues[0];
        foreach (XValue xv in xs)
        {
          if (xv != null)
          {
            string tickLabel = xv.Value;
            XSize valueSize = this.rendererParms.Graphics.MeasureString(tickLabel, xari.TickLabelsFont);
            size.Height = Math.Max(valueSize.Height, size.Height);
            size.Width += valueSize.Width;
          }
        }
      }

      // Remember space for later drawing.
      xari.TickLabelsHeight = size.Height;
      xari.Height = titleSize.Height + size.Height + xari.MajorTickMarkWidth;
      xari.Width = Math.Max(titleSize.Width, size.Width);
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
    double tickLabelStep = xari.Width;
    if (countTickLabels != 0)
      tickLabelStep = xari.Width / countTickLabels;

    //XPoint startPos = new XPoint(xari.X + tickLabelStep / 2, xari.Y + /*xari.TickLabelsHeight +*/ xari.MajorTickMarkWidth);
    XPoint startPos = new XPoint(xari.X + tickLabelStep / 2, xari.Y + xari.TickLabelsHeight);
    if (xari.MajorTickMark != TickMarkType.None)
      startPos.Y += xari.MajorTickMarkWidth;
    foreach (XSeries xs in xari.XValues)
    {
      for (int idx = 0; idx < countTickLabels && idx < xs.Count; ++idx)
      {
        XValue xv = xs[idx];
        if (xv != null)
        {
          string tickLabel = xv.Value;
          XSize size = gfx.MeasureString(tickLabel, xari.TickLabelsFont);
          gfx.DrawString(tickLabel, xari.TickLabelsFont, xari.TickLabelsBrush, startPos.X - size.Width / 2, startPos.Y);
        }
        startPos.X += tickLabelStep;
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
      double minorTickMarkStep = xari.Width / countMinorTickMarks;
      startPos.X = xari.X;
      for (int x = 0; x <= countMinorTickMarks; x++)
      {
        points[0].X = startPos.X + minorTickMarkStep * x;
        points[0].Y = minorTickMarkStart;
        points[1].X = points[0].X;
        points[1].Y = minorTickMarkEnd;
        lineFormatRenderer.DrawLine(points[0], points[1]);
      }
    }

    // Major ticks.
    if (xari.MajorTickMark != TickMarkType.None)
    {
      int countMajorTickMarks = (int)(xMax / xMajorTick);
      double majorTickMarkStep = xari.Width;
      if (countMajorTickMarks != 0)
        majorTickMarkStep = xari.Width / countMajorTickMarks;
      startPos.X = xari.X;
      for (int x = 0; x <= countMajorTickMarks; x++)
      {
        points[0].X = startPos.X + majorTickMarkStep * x;
        points[0].Y = majorTickMarkStart;
        points[1].X = points[0].X;
        points[1].Y = majorTickMarkEnd;
        lineFormatRenderer.DrawLine(points[0], points[1]);
      }
    }

    // Axis.
    if (xari.LineFormat != null)
    {
      points[0].X = xari.X;
      points[0].Y = xari.Y;
      points[1].X = xari.X + xari.Width;
      points[1].Y = xari.Y;
      if (xari.MajorTickMark != TickMarkType.None)
      {
        points[0].X -= xari.LineFormat.Width / 2;
        points[1].X += xari.LineFormat.Width / 2;
      }
      lineFormatRenderer.DrawLine(points[0], points[1]);
    }

    // Draw axis title, through the renderer written for it rather than by hand. Drawing it here
    // meant an axis title on this axis honoured neither its alignment nor its orientation, both
    // of which are settable and both of which the value axis has always honoured. It also meant
    // the caption was centred on half the axis's right edge instead of on the middle of the axis,
    // which is the same thing only when the axis starts at zero.
    AxisTitleRendererInfo atri = xari.axisTitleRendererInfo;
    if (atri != null && atri.AxisTitleText != null && atri.AxisTitleText.Length > 0)
    {
      // The strip below the tick labels, the full width of the axis, so that an alignment has
      // somewhere to move the caption to.
      atri.Rect = new XRect(xari.Rect.Left, xari.Rect.Bottom - atri.AxisTitleSize.Height,
        xari.Rect.Width, atri.AxisTitleSize.Height);

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
    SeriesCollection seriesCollection = chart.SeriesCollection;

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
        xs.Add(i.ToString(rendererInfo.TickLabelsFormat));
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
    XRect rect = rendererInfo.Rect;

    switch (rendererInfo.MajorTickMark)
    {
      case TickMarkType.Inside:
        majorTickMarkStart = rect.Y;
        majorTickMarkEnd = rect.Y - majorTickMarkWidth;
        break;

      case TickMarkType.Outside:
        majorTickMarkStart = rect.Y;
        majorTickMarkEnd   = rect.Y + majorTickMarkWidth;
        break;

      case TickMarkType.Cross:
        majorTickMarkStart = rect.Y + majorTickMarkWidth;
        majorTickMarkEnd = rect.Y - majorTickMarkWidth;
        break;

      case TickMarkType.None:
        majorTickMarkStart = 0;
        majorTickMarkEnd = 0;
        break;
    }

    switch (rendererInfo.MinorTickMark)
    {
      case TickMarkType.Inside:
        minorTickMarkStart = rect.Y;
        minorTickMarkEnd = rect.Y - minorTickMarkWidth;
        break;

      case TickMarkType.Outside:
        minorTickMarkStart = rect.Y;
        minorTickMarkEnd   = rect.Y + minorTickMarkWidth;
        break;

      case TickMarkType.Cross:
        minorTickMarkStart = rect.Y + minorTickMarkWidth;
        minorTickMarkEnd = rect.Y - minorTickMarkWidth;
        break;

      case TickMarkType.None:
        minorTickMarkStart = 0;
        minorTickMarkEnd = 0;
        break;
    }
  }
}
