#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharp.com
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
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using PdfSharpCore.Fonts.OpenType;
using PdfSharpCore.Internal;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Internal;
using PdfSharpCore.Pdf.Advanced;

// ReSharper disable RedundantNameQualifier
// ReSharper disable CompareOfFloatsByEqualityOperator

namespace PdfSharpCore.Drawing.Pdf;

/// <summary>
/// Represents a drawing surface for PdfPages.
/// </summary>
internal class XGraphicsPdfRenderer : IXGraphicsRenderer
{
    public XGraphicsPdfRenderer(PdfPage page, XGraphics gfx, XGraphicsPdfPageOptions options)
    {
        _page = page;
        _colorMode = page._document.Options.ColorMode;
        _options = options;
        _gfx = gfx;
        _content = new StringBuilder();
        page.RenderContent._pdfRenderer = this;
        _gfxState = new PdfGraphicsState(this);
    }

    public XGraphicsPdfRenderer(XForm form, XGraphics gfx)
    {
        _form = form;
        _colorMode = form.Owner.Options.ColorMode;
        _gfx = gfx;
        _content = new StringBuilder();
        form.PdfRenderer = this;
        _gfxState = new PdfGraphicsState(this);
    }

    /// <summary>
    /// Gets the content created by this renderer.
    /// </summary>
    string GetContent()
    {
        EndPage();
        return _content.ToString();
    }

    public XGraphicsPdfPageOptions PageOptions => _options;

    public void Close()
    {
        if (_page != null)
        {
            PdfContent content2 = _page.RenderContent;
            content2.CreateStream(PdfEncoders.RawEncoding.GetBytes(GetContent()));

            _gfx = null;
            _page.RenderContent._pdfRenderer = null;
            _page.RenderContent = null;
            _page = null;
        }
        else if (_form != null)
        {
            _form._pdfForm.CreateStream(PdfEncoders.RawEncoding.GetBytes(GetContent()));
            _gfx = null;
            _form.PdfRenderer = null;
            _form = null;
        }
    }

    // --------------------------------------------------------------------------------------------

    #region  Drawing

    //void SetPageLayout(down, point(0, 0), unit

    // ----- DrawLine -----------------------------------------------------------------------------

    /// <summary>
    /// Strokes a single connection of two points.
    /// </summary>
    public void DrawLine(XPen pen, double x1, double y1, double x2, double y2)
    {
        DrawLines(pen, new XPoint[] { new(x1, y1), new(x2, y2) });
    }

    // ----- DrawLines ----------------------------------------------------------------------------

    /// <summary>
    /// Strokes a series of connected points.
    /// </summary>
    public void DrawLines(XPen pen, XPoint[] points)
    {
        if (pen == null)
            throw new ArgumentNullException(nameof(pen));
        if (points == null)
            throw new ArgumentNullException(nameof(points));

        int count = points.Length;
        if (count == 0)
            return;

        Realize(pen);

        const string format = Config.SignificantFigures4;
        AppendFormatPoint("{0:" + format + "} {1:" + format + "} m\n", points[0].X, points[0].Y);
        for (int idx = 1; idx < count; idx++)
            AppendFormatPoint("{0:" + format + "} {1:" + format + "} l\n", points[idx].X, points[idx].Y);
        _content.Append("S\n");
    }

    // ----- DrawBezier ---------------------------------------------------------------------------

    public void DrawBezier(XPen pen, double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
    {
        DrawBeziers(pen, new XPoint[] { new(x1, y1), new(x2, y2), new(x3, y3), new(x4, y4) });
    }

    // ----- DrawBeziers --------------------------------------------------------------------------

    public void DrawBeziers(XPen pen, XPoint[] points)
    {
        if (pen == null)
            throw new ArgumentNullException(nameof(pen));
        if (points == null)
            throw new ArgumentNullException(nameof(points));

        int count = points.Length;
        if (count == 0)
            return;

        if ((count - 1) % 3 != 0)
            throw new ArgumentException("Invalid number of points for bezier curves. Number must fulfil 4+3n.", nameof(points));

        Realize(pen);

        const string format = Config.SignificantFigures4;
        AppendFormatPoint("{0:" + format + "} {1:" + format + "} m\n", points[0].X, points[0].Y);
        for (int idx = 1; idx < count; idx += 3)
            AppendFormat3Points("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} c\n",
                points[idx].X, points[idx].Y,
                points[idx + 1].X, points[idx + 1].Y,
                points[idx + 2].X, points[idx + 2].Y);

        AppendStrokeFill(pen, null, XFillMode.Alternate, false);
    }

    // ----- DrawCurve ----------------------------------------------------------------------------

    public void DrawCurve(XPen pen, XPoint[] points, double tension)
    {
        if (pen == null)
            throw new ArgumentNullException(nameof(pen));
        if (points == null)
            throw new ArgumentNullException(nameof(points));

        int count = points.Length;
        if (count == 0)
            return;
        if (count < 2)
            throw new ArgumentException("Not enough points", nameof(points));

        // See http://pubpages.unh.edu/~cs770/a5/cardinal.html  // Link is down...
        tension /= 3;

        Realize(pen);

        const string format = Config.SignificantFigures4;
        AppendFormatPoint("{0:" + format + "} {1:" + format + "} m\n", points[0].X, points[0].Y);
        if (count == 2)
        {
            // Just draws a line.
            AppendCurveSegment(points[0], points[0], points[1], points[1], tension);
        }
        else
        {
            AppendCurveSegment(points[0], points[0], points[1], points[2], tension);
            for (int idx = 1; idx < count - 2; idx++)
                AppendCurveSegment(points[idx - 1], points[idx], points[idx + 1], points[idx + 2], tension);
            AppendCurveSegment(points[count - 3], points[count - 2], points[count - 1], points[count - 1], tension);
        }
        AppendStrokeFill(pen, null, XFillMode.Alternate, false);
    }

    // ----- DrawArc ------------------------------------------------------------------------------

    public void DrawArc(XPen pen, double x, double y, double width, double height, double startAngle, double sweepAngle)
    {
        if (pen == null)
            throw new ArgumentNullException(nameof(pen));

        Realize(pen);

        AppendPartialArc(x, y, width, height, startAngle, sweepAngle, PathStart.MoveTo1st, new XMatrix());
        AppendStrokeFill(pen, null, XFillMode.Alternate, false);
    }

    // ----- DrawRectangle ------------------------------------------------------------------------

    public void DrawRectangle(XPen pen, XBrush brush, double x, double y, double width, double height)
    {
        if (pen == null && brush == null)
            throw new ArgumentNullException("pen and brush");

        const string format = Config.SignificantFigures3;

        Realize(pen, brush);
        //AppendFormat123("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} re\n", x, y, width, -height);
        AppendFormatRect("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} re\n", x, y + height, width, height);

        if (pen != null && brush != null)
            _content.Append("B\n");
        else if (pen != null)
            _content.Append("S\n");
        else
            _content.Append("f\n");
    }

    // ----- DrawRectangles -----------------------------------------------------------------------

    public void DrawRectangles(XPen pen, XBrush brush, XRect[] rects)
    {
        int count = rects.Length;
        for (int idx = 0; idx < count; idx++)
        {
            XRect rect = rects[idx];
            DrawRectangle(pen, brush, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    // ----- DrawRoundedRectangle -----------------------------------------------------------------

    public void DrawRoundedRectangle(XPen pen, XBrush brush, double x, double y, double width, double height, double ellipseWidth, double ellipseHeight)
    {
        XGraphicsPath path = new XGraphicsPath();
        path.AddRoundedRectangle(x, y, width, height, ellipseWidth, ellipseHeight);
        DrawPath(pen, brush, path);
    }

    // ----- DrawEllipse --------------------------------------------------------------------------

    public void DrawEllipse(XPen pen, XBrush brush, double x, double y, double width, double height)
    {
        Realize(pen, brush);

        // Useful information is here http://home.t-online.de/home/Robert.Rossmair/ellipse.htm (note: link was dead on November 2, 2015)
        // or here http://www.whizkidtech.redprince.net/bezier/circle/
        // Deeper but more difficult: http://www.tinaja.com/cubic01.asp
        XRect rect = new XRect(x, y, width, height);
        double δx = rect.Width / 2;
        double δy = rect.Height / 2;
        double fx = δx * Const.κ;
        double fy = δy * Const.κ;
        double x0 = rect.X + δx;
        double y0 = rect.Y + δy;

        // Approximate an ellipse by drawing four cubic splines.
        const string format = Config.SignificantFigures4;
        AppendFormatPoint("{0:" + format + "} {1:" + format + "} m\n", x0 + δx, y0);
        AppendFormat3Points("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} c\n",
            x0 + δx, y0 + fy, x0 + fx, y0 + δy, x0, y0 + δy);
        AppendFormat3Points("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} c\n",
            x0 - fx, y0 + δy, x0 - δx, y0 + fy, x0 - δx, y0);
        AppendFormat3Points("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} c\n",
            x0 - δx, y0 - fy, x0 - fx, y0 - δy, x0, y0 - δy);
        AppendFormat3Points("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} c\n",
            x0 + fx, y0 - δy, x0 + δx, y0 - fy, x0 + δx, y0);
        AppendStrokeFill(pen, brush, XFillMode.Winding, true);
    }

    // ----- DrawPolygon --------------------------------------------------------------------------

    public void DrawPolygon(XPen pen, XBrush brush, XPoint[] points, XFillMode fillmode)
    {
        Realize(pen, brush);

        int count = points.Length;
        if (points.Length < 2)
            throw new ArgumentException("points", PSSR.PointArrayAtLeast(2));

        const string format = Config.SignificantFigures4;
        AppendFormatPoint("{0:" + format + "} {1:" + format + "} m\n", points[0].X, points[0].Y);
        for (int idx = 1; idx < count; idx++)
            AppendFormatPoint("{0:" + format + "} {1:" + format + "} l\n", points[idx].X, points[idx].Y);

        AppendStrokeFill(pen, brush, fillmode, true);
    }

    // ----- DrawPie ------------------------------------------------------------------------------

    public void DrawPie(XPen pen, XBrush brush, double x, double y, double width, double height,
        double startAngle, double sweepAngle)
    {
        Realize(pen, brush);

        const string format = Config.SignificantFigures4;
        AppendFormatPoint("{0:" + format + "} {1:" + format + "} m\n", x + width / 2, y + height / 2);
        AppendPartialArc(x, y, width, height, startAngle, sweepAngle, PathStart.LineTo1st, new XMatrix());
        AppendStrokeFill(pen, brush, XFillMode.Alternate, true);
    }

    // ----- DrawClosedCurve ----------------------------------------------------------------------

    public void DrawClosedCurve(XPen pen, XBrush brush, XPoint[] points, double tension, XFillMode fillmode)
    {
        int count = points.Length;
        if (count == 0)
            return;
        if (count < 2)
            throw new ArgumentException("Not enough points.", nameof(points));

        // Simply tried out. Not proofed why it is correct.
        tension /= 3;

        Realize(pen, brush);

        const string format = Config.SignificantFigures4;
        AppendFormatPoint("{0:" + format + "} {1:" + format + "} m\n", points[0].X, points[0].Y);
        if (count == 2)
        {
            // Just draw a line.
            AppendCurveSegment(points[0], points[0], points[1], points[1], tension);
        }
        else
        {
            AppendCurveSegment(points[count - 1], points[0], points[1], points[2], tension);
            for (int idx = 1; idx < count - 2; idx++)
                AppendCurveSegment(points[idx - 1], points[idx], points[idx + 1], points[idx + 2], tension);
            AppendCurveSegment(points[count - 3], points[count - 2], points[count - 1], points[0], tension);
            AppendCurveSegment(points[count - 2], points[count - 1], points[0], points[1], tension);
        }
        AppendStrokeFill(pen, brush, fillmode, true);
    }

    // ----- DrawPath -----------------------------------------------------------------------------

    public void DrawPath(XPen pen, XBrush brush, XGraphicsPath path)
    {
        if (pen == null && brush == null)
            throw new ArgumentNullException(nameof(pen));

        Realize(pen, brush);
        AppendPath(path._corePath);
        AppendStrokeFill(pen, brush, path.FillMode, false);
    }

    // ----- DrawString ---------------------------------------------------------------------------

    public void DrawString(string s, XFont font, XPen pen, XBrush brush, XRect rect, XStringFormat format)
    {
        double lineSpace = font.GetHeight();
        double cyAscent = lineSpace * font.CellAscent / font.CellSpace;
        double cyDescent = lineSpace * font.CellDescent / font.CellSpace;
        // Measured through the same format the text is drawn with: alignment and the underline and
        // strikeout rules below are all placed from this width.
        double width = _gfx.MeasureString(s, font, format).Width;

        //bool bold = (font.Style & XFontStyle.Bold) != 0;
        //bool italic = (font.Style & XFontStyle.Italic) != 0;
        bool italicSimulation = (font.GlyphTypeface.StyleSimulations & XStyleSimulations.ItalicSimulation) != 0;
        bool boldSimulation = (font.GlyphTypeface.StyleSimulations & XStyleSimulations.BoldSimulation) != 0;
        // The format's decoration wins; leaving it at None keeps whatever the font's style asks
        // for, which is where underlining lived before the format could carry it.
        XTextDecoration underline = format.Underline != XTextDecoration.None
            ? format.Underline
            : (font.Style & XFontStyle.Underline) != 0 ? XTextDecoration.Single : XTextDecoration.None;
        XTextDecoration strikeout = format.Strikeout != XTextDecoration.None
            ? format.Strikeout
            : (font.Style & XFontStyle.Strikeout) != 0 ? XTextDecoration.Single : XTextDecoration.None;

        Realize(font, brush, pen, boldSimulation, format);

        // The same arithmetic XGraphicsPath.AddString places its glyphs by, so that a string added
        // to a path lands where the same string drawn here lands.
        XPoint origin = TextOrigin.For(rect, width, font, format, Gfx.PageDirection == XPageDirection.Downwards);
        double x = origin.X;
        double y = origin.Y;

        PdfFont realizedFont = _gfxState._realizedFont;
        Debug.Assert(realizedFont != null);
        realizedFont.AddChars(s);

        const string format2 = Config.SignificantFigures4;
        OpenTypeDescriptor descriptor = realizedFont.FontDescriptor._descriptor;

        // The whole show-text operation, its operator included: usually a Tj, but a TJ array when
        // the words have to be spaced out by hand. See PdfGraphicsState.NeedsWordSpacingByHand.
        string text = null;
        if (font.Unicode)
        {
            StringBuilder sb = new StringBuilder();
            bool isSymbolFont = descriptor.FontFace.cmap.symbol;
            for (int idx = 0; idx < s.Length; idx++)
            {
                char ch = s[idx];
                if (isSymbolFont)
                {
                    // Remap ch for symbol fonts.
                    ch = (char)(ch | (descriptor.FontFace.os2.usFirstCharIndex & 0xFF00));  // @@@ refactor
                }
                int glyphID = descriptor.CharCodeToGlyphIndex(ch);
                sb.Append((char)glyphID);
            }
            string glyphs = sb.ToString();

            text = PdfGraphicsState.NeedsWordSpacingByHand(font, format)
                ? WordSpacedGlyphRun(s, glyphs, font.Size, format.WordSpacing)
                : GlyphRunToHexString(glyphs) + " Tj";
        }
        else
        {
            byte[] bytes = PdfEncoders.WinAnsiEncoding.GetBytes(s);
            text = PdfEncoders.ToStringLiteral(bytes, false, null) + " Tj";
        }

        // Map absolute position to PDF world space.
        XPoint pos = new XPoint(x, y);
        pos = WorldToView(pos);

        double verticalOffset = 0;
        if (boldSimulation)
        {
            // Adjust baseline in case of bold simulation???
            // No, because this would change the center of the glyphs.
            //verticalOffset = font.Size * Const.BoldEmphasis / 2;
        }

        // How far the glyphs lean, as the tangent of the angle. Italic simulation contributes a
        // fixed lean and the caller may ask for one of their own; two shears compose by adding
        // their tangents, so the two are one number from here on.
        double skew = SkewOf(italicSimulation, format.ObliqueAngle);

        if (skew == _gfxState.RealizedTextSkew)
        {
            // The text matrix already leans the right amount, so moving to the next position is
            // all that is needed - and Td is shorter than Tm.
            AdjustTdOffset(ref pos, verticalOffset, _gfxState.RealizedTextSkew);
            AppendFormatArgs("{0:" + format2 + "} {1:" + format2 + "} Td {2}\n", pos.X, pos.Y, text);
        }
        else
        {
            // Only Tm can set the lean, and it sets the position absolutely while it is there.
            XMatrix m = new XMatrix(1, 0, skew, 1, pos.X, pos.Y);
            AppendFormatArgs("{0:" + format2 + "} {1:" + format2 + "} {2:" + format2 + "} {3:" + format2 + "} {4:" + format2 + "} {5:" + format2 + "} Tm\n{6}\n",
                m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY, text);
            _gfxState.RealizedTextSkew = skew;
            AdjustTdOffset(ref pos, verticalOffset, 0);
        }

        // The rules below are rectangles drawn in graphics mode, so they do not go through the
        // text matrix and have to be moved by the text rise themselves. Raising text moves it up
        // the page, which is towards smaller y only when y runs downwards.
        double rise = Gfx.PageDirection == XPageDirection.Downwards ? -format.TextRise : format.TextRise;

        // Built only where there is a rule to draw, which is almost never - every string drawn
        // otherwise paid for a brush nothing used.
        XBrush ruleBrush = underline == XTextDecoration.None && strikeout == XTextDecoration.None
            ? null
            : RuleBrushFor(brush, pen, format);

        if (underline != XTextDecoration.None)
        {
            double underlinePosition = lineSpace * realizedFont.FontDescriptor._descriptor.UnderlinePosition / font.CellSpace;
            double underlineThickness = lineSpace * realizedFont.FontDescriptor._descriptor.UnderlineThickness / font.CellSpace;
            //DrawRectangle(null, brush, x, y - underlinePosition, width, underlineThickness);
            double underlineRectY = Gfx.PageDirection == XPageDirection.Downwards
                ? y - underlinePosition
                : y + underlinePosition - underlineThickness;
            DrawTextRule(underline, s, font, format, ruleBrush, x, underlineRectY + rise, width, underlineThickness);
        }

        if (strikeout != XTextDecoration.None)
        {
            double strikeoutPosition = lineSpace * realizedFont.FontDescriptor._descriptor.StrikeoutPosition / font.CellSpace;
            double strikeoutSize = lineSpace * realizedFont.FontDescriptor._descriptor.StrikeoutSize / font.CellSpace;
            //DrawRectangle(null, brush, x, y - strikeoutPosition - strikeoutSize, width, strikeoutSize);
            double strikeoutRectY = Gfx.PageDirection == XPageDirection.Downwards
                ? y - strikeoutPosition
                : y + strikeoutPosition - strikeoutSize;
            DrawTextRule(strikeout, s, font, format, ruleBrush, x, strikeoutRectY + rise, width, strikeoutSize);
        }
    }

    /// <summary>
    /// What an underline or strikeout rule is painted with.
    /// </summary>
    /// <remarks>
    /// A colour asked for outright wins. Failing that the rule follows the text, which is the brush
    /// filling it, or the pen outlining it when there is no brush - and a pen carrying a brush of
    /// its own leaves its Color empty, so that has to be looked at before the colour is.
    /// </remarks>
    static XBrush RuleBrushFor(XBrush brush, XPen pen, XStringFormat format)
    {
        if (!format.DecorationColor.IsEmpty)
            return new XSolidBrush(format.DecorationColor);

        if (brush != null)
            return brush;

        return pen.Brush ?? new XSolidBrush(pen.Color);
    }

    /// <summary>
    /// Draws the rule that underlines or strikes out a run of text.
    /// </summary>
    /// <param name="top">Where the top of the rule sits, in world coordinates.</param>
    /// <param name="thickness">How thick the rule is, from the font's own metrics.</param>
    void DrawTextRule(XTextDecoration decoration, string s, XFont font, XStringFormat format,
        XBrush brush, double x, double top, double width, double thickness)
    {
        if (decoration == XTextDecoration.Words)
        {
            // Under the words and not under the spaces between them, so the run has to be broken
            // up and each piece measured to find out where it starts.
            foreach (var (wordX, wordWidth) in WordRunsOf(s, font, format, x))
                DrawTextRule(XTextDecoration.Single, null, font, format, brush, wordX, top, wordWidth, thickness);
            return;
        }

        XDashStyle dashStyle = DashStyleOf(decoration);
        if (dashStyle == XDashStyle.Solid)
        {
            // Filled rather than stroked, which is how this has always been drawn and what every
            // document made with it looks like.
            DrawRectangle(null, brush, x, top, width, thickness);
            return;
        }

        // A broken rule has to be stroked, since a rectangle cannot be dotted. The pen is as thick
        // as the rule and runs down the middle of where the rectangle would have been.
        XColor colour = brush is XSolidBrush solid ? solid.Color : XColors.Black;
        XPen pen = new XPen(colour, thickness) { DashStyle = dashStyle };
        double middle = top + thickness / 2;
        DrawLine(pen, x, middle, x + width, middle);
    }

    static XDashStyle DashStyleOf(XTextDecoration decoration)
    {
        switch (decoration)
        {
            case XTextDecoration.Dotted: return XDashStyle.Dot;
            case XTextDecoration.Dash: return XDashStyle.Dash;
            case XTextDecoration.DotDash: return XDashStyle.DashDot;
            case XTextDecoration.DotDotDash: return XDashStyle.DashDotDot;
            default: return XDashStyle.Solid;
        }
    }

    /// <summary>
    /// Where each run of non-blank characters in <paramref name="s"/> starts and how wide it is,
    /// measured through the same format the text is drawn with.
    /// </summary>
    /// <remarks>
    /// Each stretch of the string - blank or not - is measured once and the widths added up, which
    /// is exact rather than close enough: a glyph advances by its own width plus the character
    /// spacing, and a space by the word spacing on top, so the width of a run really is the sum of
    /// the widths of its parts. Measuring each word's prefix from the start of the string instead
    /// would answer the same and cost a measurement of the whole string per word.
    /// </remarks>
    IEnumerable<(double X, double Width)> WordRunsOf(string s, XFont font, XStringFormat format, double x)
    {
        int idx = 0;
        while (idx < s.Length)
        {
            int blankStart = idx;
            while (idx < s.Length && char.IsWhiteSpace(s[idx]))
                idx++;
            if (idx > blankStart)
                x += _gfx.MeasureString(s.Substring(blankStart, idx - blankStart), font, format).Width;
            if (idx == s.Length)
                yield break;

            int wordStart = idx;
            while (idx < s.Length && !char.IsWhiteSpace(s[idx]))
                idx++;

            double width = _gfx.MeasureString(s.Substring(wordStart, idx - wordStart), font, format).Width;
            yield return (x, width);
            x += width;
        }
    }

    /// <summary>
    /// How far text leans to the right, as the tangent of the angle, given whether the font is
    /// having its italic drawn on for it and what the caller asked for on top of that.
    /// </summary>
    /// <remarks>
    /// Italic simulation contributes sin(20°) rather than tan(20°). That is what PDFsharp has
    /// always skewed by and what every document built with it looks like, so it is left alone; a
    /// caller asking for an angle gets its tangent, which is the skew that angle actually means
    /// and what PDFKit's oblique produces. The two add because shearing by a and then by b is
    /// shearing by a + b.
    /// </remarks>
    static double SkewOf(bool italicSimulation, double obliqueAngle)
    {
        double skew = italicSimulation ? Const.ItalicSkewAngleSinus : 0;
        if (obliqueAngle != 0)
            skew += Math.Tan(obliqueAngle * Math.PI / 180);
        return skew;
    }

    // ----- DrawImage ----------------------------------------------------------------------------

    //public void DrawImage(Image image, Point point);
    //public void DrawImage(Image image, PointF point);
    //public void DrawImage(Image image, Point[] destPoints);
    //public void DrawImage(Image image, PointF[] destPoints);
    //public void DrawImage(Image image, Rectangle rect);
    //public void DrawImage(Image image, RectangleF rect);
    //public void DrawImage(Image image, int x, int y);
    //public void DrawImage(Image image, float x, float y);
    //public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit);
    //public void DrawImage(Image image, Rectangle destRect, Rectangle srcRect, GraphicsUnit srcUnit);
    //public void DrawImage(Image image, RectangleF destRect, RectangleF srcRect, GraphicsUnit srcUnit);
    //public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit);
    //public void DrawImage(Image image, int x, int y, Rectangle srcRect, GraphicsUnit srcUnit);
    //public void DrawImage(Image image, float x, float y, RectangleF srcRect, GraphicsUnit srcUnit);
    //public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit, ImageAttributes imageAttr);
    //public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit, ImageAttributes imageAttr);
    //public void DrawImage(Image image, int x, int y, int width, int height);
    //public void DrawImage(Image image, float x, float y, float width, float height);
    //public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit, ImageAttributes imageAttr, DrawImageAbort callback);
    //public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit, ImageAttributes imageAttr, DrawImageAbort callback);
    //public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit);
    //public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit);
    //public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit, ImageAttributes imageAttr, DrawImageAbort callback, int callbackData);
    //public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit, ImageAttributes imageAttr, DrawImageAbort callback, int callbackData);
    //public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, ImageAttributes imageAttr);
    //public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit, ImageAttributes imageAttrs);
    //public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, ImageAttributes imageAttr, DrawImageAbort callback);
    //public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit, ImageAttributes imageAttrs, DrawImageAbort callback);
    //public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, ImageAttributes imageAttrs, DrawImageAbort callback, IntPtr callbackData);
    //public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit, ImageAttributes

    public void DrawImage(XImage image, double x, double y, double width, double height)
    {
        const string format = Config.SignificantFigures4;

        string name = Realize(image);
        if (!(image is XForm))
        {
            if (_gfx.PageDirection == XPageDirection.Downwards)
            {
                AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                    x, y + height, width, height, name);
            }
            else
            {
                AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                    x, y, width, height, name);
            }
        }
        else
        {
            BeginPage();

            XForm form = (XForm)image;
            form.Finish();

            PdfFormXObject pdfForm = Owner.FormTable.GetForm(form);

            double cx = width / image.PointWidth;
            double cy = height / image.PointHeight;

            if (cx != 0 && cy != 0)
            {
                XPdfForm xForm = image as XPdfForm;
                if (_gfx.PageDirection == XPageDirection.Downwards)
                {
                    // If we have an XPdfForm, then we take the MediaBox into account.
                    double xDraw = x;
                    double yDraw = y;
                    if (xForm != null)
                    {
                        // Yes, it is an XPdfForm - adjust the position where the page will be drawn.
                        xDraw -= xForm.Page.MediaBox.X1;
                        yDraw += xForm.Page.MediaBox.Y1;
                    }
                    AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm 100 Tz {4} Do Q\n",
                        xDraw, yDraw + height, cx, cy, name);
                }
                else
                {
                    // TODO Translation for MediaBox.
                    AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                        x, y, cx, cy, name);
                }
            }
        }
    }

    // TODO: incomplete - srcRect not used
    public void DrawImage(XImage image, XRect destRect, XRect srcRect, XGraphicsUnit srcUnit)
    {
        const string format = Config.SignificantFigures4;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image);
        if (!(image is XForm))
        {
            if (_gfx.PageDirection == XPageDirection.Downwards)
            {
                AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do\nQ\n",
                    x, y + height, width, height, name);
            }
            else
            {
                AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                    x, y, width, height, name);
            }
        }
        else
        {
            BeginPage();

            XForm form = (XForm)image;
            form.Finish();

            PdfFormXObject pdfForm = Owner.FormTable.GetForm(form);

            double cx = width / image.PointWidth;
            double cy = height / image.PointHeight;

            if (cx != 0 && cy != 0)
            {
                XPdfForm xForm = image as XPdfForm;
                if (_gfx.PageDirection == XPageDirection.Downwards)
                {
                    double xDraw = x;
                    double yDraw = y;
                    if (xForm != null)
                    {
                        // Yes, it is an XPdfForm - adjust the position where the page will be drawn.
                        xDraw -= xForm.Page.MediaBox.X1;
                        yDraw += xForm.Page.MediaBox.Y1;
                    }
                    AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                        xDraw, yDraw + height, cx, cy, name);
                }
                else
                {
                    // TODO Translation for MediaBox.
                    AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                        x, y, cx, cy, name);
                }
            }
        }
    }

    #endregion

    // --------------------------------------------------------------------------------------------

    #region Save and Restore

    /// <summary>
    /// Clones the current graphics state and push it on a stack.
    /// </summary>
    public void Save(XGraphicsState state)
    {
        // Before saving, the current transformation matrix must be completely realized.
        BeginGraphicMode();
        RealizeTransform();
        // Associate the XGraphicsState with the current PdgGraphicsState.
        _gfxState.InternalState = state.InternalState;
        SaveState();
    }

    public void Restore(XGraphicsState state)
    {
        BeginGraphicMode();
        RestoreState(state.InternalState);
    }

    public void BeginContainer(XGraphicsContainer container, XRect dstrect, XRect srcrect, XGraphicsUnit unit)
    {
        // Before saving, the current transformation matrix must be completely realized.
        BeginGraphicMode();
        RealizeTransform();
        _gfxState.InternalState = container.InternalState;
        SaveState();
    }

    public void EndContainer(XGraphicsContainer container)
    {
        BeginGraphicMode();
        RestoreState(container.InternalState);
    }

    #endregion

    // --------------------------------------------------------------------------------------------

    #region Transformation

    //public void SetPageTransform(XPageDirection direction, XPoint origion, XGraphicsUnit unit)
    //{
    //  if (_gfxStateStack.Count > 0)
    //    throw new InvalidOperationException("PageTransformation can be modified only when the graphics stack is empty.");

    //  throw new NotImplementedException("SetPageTransform");
    //}

    public XMatrix Transform
    {
        get
        {
            if (_gfxState.UnrealizedCtm.IsIdentity)
                return _gfxState.EffectiveCtm;
            return _gfxState.UnrealizedCtm * _gfxState.RealizedCtm;
        }
    }

    public void AddTransform(XMatrix value, XMatrixOrder matrixOrder)
    {
        _gfxState.AddTransform(value, matrixOrder);
    }

    #endregion

    // --------------------------------------------------------------------------------------------

    #region Clipping

    public void SetClip(XGraphicsPath path, XCombineMode combineMode)
    {
        if (path == null)
            throw new NotImplementedException("SetClip with no path.");

        // Ensure that the graphics state stack level is at least 2, because otherwise an error
        // occurs when someone set the clip region before something was drawn.
        if (_gfxState.Level < GraphicsStackLevelWorldSpace)
            RealizeTransform();  // TODO: refactor this function

        if (combineMode == XCombineMode.Replace)
        {
            if (_clipLevel != 0)
            {
                if (_clipLevel != _gfxState.Level)
                    throw new NotImplementedException("Cannot set new clip region in an inner graphic state level.");
                else
                    ResetClip();
            }
            _clipLevel = _gfxState.Level;
        }
        else if (combineMode == XCombineMode.Intersect)
        {
            if (_clipLevel == 0)
                _clipLevel = _gfxState.Level;
        }
        else
        {
            Debug.Assert(false, "Invalid XCombineMode in internal function.");
        }
        _gfxState.SetAndRealizeClipPath(path);
    }

    /// <summary>
    /// Sets the clip path empty. Only possible if graphic state level has the same value as it has when
    /// the first time SetClip was invoked.
    /// </summary>
    public void ResetClip()
    {
        // No clip level means no clipping occurs and nothing is to do.
        if (_clipLevel == 0)
            return;

        // Only at the clipLevel the clipping can be reset.
        if (_clipLevel != _gfxState.Level)
            throw new NotImplementedException("Cannot reset clip region in an inner graphic state level.");

        // Must be in graphical mode before popping the graphics state.
        BeginGraphicMode();

        // Save InternalGraphicsState and transformation of the current graphical state.
        InternalGraphicsState state = _gfxState.InternalState;
        XMatrix ctm = _gfxState.EffectiveCtm;
        // Empty clip path by switching back to the previous state.
        RestoreState();
        SaveState();
        // Save internal state
        _gfxState.InternalState = state;
        // Restore CTM
        // TODO: check rest of clip
        //GfxState.Transform = ctm;
    }

    /// <summary>
    /// The nesting level of the PDF graphics state stack when the clip region was set to non empty.
    /// Because of the way PDF is made the clip region can only be reset at this level.
    /// </summary>
    int _clipLevel;

    #endregion

    // --------------------------------------------------------------------------------------------

    #region Miscellaneous

    /// <summary>
    /// Writes a comment to the PDF content stream. May be useful for debugging purposes.
    /// </summary>
    public void WriteComment(string comment)
    {
        comment = comment.Replace("\n", "\n% ");
        // TODO: Some more checks necessary?
        Append("% " + comment + "\n");
    }

    #endregion

    // --------------------------------------------------------------------------------------------

    #region Append to PDF stream

    /// <summary>
    /// Appends one or up to five Bézier curves that interpolate the arc.
    /// </summary>
    void AppendPartialArc(double x, double y, double width, double height, double startAngle, double sweepAngle, PathStart pathStart, XMatrix matrix)
    {
        // Normalize the angles
        double α = startAngle;
        if (α < 0)
            α = α + (1 + Math.Floor((Math.Abs(α) / 360))) * 360;
        else if (α > 360)
            α = α - Math.Floor(α / 360) * 360;
        Debug.Assert(α >= 0 && α <= 360);

        double β = sweepAngle;
        if (β < -360)
            β = -360;
        else if (β > 360)
            β = 360;

        if (α == 0 && β < 0)
            α = 360;
        else if (α == 360 && β > 0)
            α = 0;

        // Is it possible that the arc is small starts and ends in same quadrant?
        bool smallAngle = Math.Abs(β) <= 90;

        β = α + β;
        if (β < 0)
            β = β + (1 + Math.Floor((Math.Abs(β) / 360))) * 360;

        bool clockwise = sweepAngle > 0;
        int startQuadrant = Quadrant(α, true, clockwise);
        int endQuadrant = Quadrant(β, false, clockwise);

        if (startQuadrant == endQuadrant && smallAngle)
            AppendPartialArcQuadrant(x, y, width, height, α, β, pathStart, matrix);
        else
        {
            int currentQuadrant = startQuadrant;
            bool firstLoop = true;
            do
            {
                if (currentQuadrant == startQuadrant && firstLoop)
                {
                    double ξ = currentQuadrant * 90 + (clockwise ? 90 : 0);
                    AppendPartialArcQuadrant(x, y, width, height, α, ξ, pathStart, matrix);
                }
                else if (currentQuadrant == endQuadrant)
                {
                    double ξ = currentQuadrant * 90 + (clockwise ? 0 : 90);
                    AppendPartialArcQuadrant(x, y, width, height, ξ, β, PathStart.Ignore1st, matrix);
                }
                else
                {
                    double ξ1 = currentQuadrant * 90 + (clockwise ? 0 : 90);
                    double ξ2 = currentQuadrant * 90 + (clockwise ? 90 : 0);
                    AppendPartialArcQuadrant(x, y, width, height, ξ1, ξ2, PathStart.Ignore1st, matrix);
                }

                // Don't stop immediately if arc is greater than 270 degrees
                if (currentQuadrant == endQuadrant && smallAngle)
                    break;

                smallAngle = true;

                if (clockwise)
                    currentQuadrant = currentQuadrant == 3 ? 0 : currentQuadrant + 1;
                else
                    currentQuadrant = currentQuadrant == 0 ? 3 : currentQuadrant - 1;

                firstLoop = false;
            } while (true);
        }
    }

    /// <summary>
    /// Gets the quadrant (0 through 3) of the specified angle. If the angle lies on an edge
    /// (0, 90, 180, etc.) the result depends on the details how the angle is used.
    /// </summary>
    int Quadrant(double φ, bool start, bool clockwise)
    {
        Debug.Assert(φ >= 0);
        if (φ > 360)
            φ = φ - Math.Floor(φ / 360) * 360;

        int quadrant = (int)(φ / 90);
        if (quadrant * 90 == φ)
        {
            if ((start && !clockwise) || (!start && clockwise))
                quadrant = quadrant == 0 ? 3 : quadrant - 1;
        }
        else
            quadrant = clockwise ? ((int)Math.Floor(φ / 90)) % 4 : (int)Math.Floor(φ / 90);
        return quadrant;
    }

    /// <summary>
    /// Appends a Bézier curve for an arc within a quadrant.
    /// </summary>
    void AppendPartialArcQuadrant(double x, double y, double width, double height, double α, double β, PathStart pathStart, XMatrix matrix)
    {
        Debug.Assert(α >= 0 && α <= 360);
        Debug.Assert(β >= 0);
        if (β > 360)
            β = β - Math.Floor(β / 360) * 360;
        Debug.Assert(Math.Abs(α - β) <= 90);

        // Scanling factor
        double δx = width / 2;
        double δy = height / 2;

        // Center of ellipse
        double x0 = x + δx;
        double y0 = y + δy;

        // We have the following quarters:
        //     |
        //   2 | 3
        // ----+-----
        //   1 | 0
        //     |
        // If the angles lie in quarter 2 or 3, their values are subtracted by 180 and the
        // resulting curve is reflected at the center. This algorithm works as expected (simply tried out).
        // There may be a mathematically more elegant solution...
        bool reflect = false;
        if (α >= 180 && β >= 180)
        {
            α -= 180;
            β -= 180;
            reflect = true;
        }

        double sinα, sinβ;
        if (width == height)
        {
            // Circular arc needs no correction.
            α = α * Calc.Deg2Rad;
            β = β * Calc.Deg2Rad;
        }
        else
        {
            // Elliptic arc needs the angles to be adjusted such that the scaling transformation is compensated.
            α = α * Calc.Deg2Rad;
            sinα = Math.Sin(α);
            if (Math.Abs(sinα) > 1E-10)
                α = Math.PI / 2 - Math.Atan(δy * Math.Cos(α) / (δx * sinα));
            β = β * Calc.Deg2Rad;
            sinβ = Math.Sin(β);
            if (Math.Abs(sinβ) > 1E-10)
                β = Math.PI / 2 - Math.Atan(δy * Math.Cos(β) / (δx * sinβ));
        }

        double κ = 4 * (1 - Math.Cos((α - β) / 2)) / (3 * Math.Sin((β - α) / 2));
        sinα = Math.Sin(α);
        double cosα = Math.Cos(α);
        sinβ = Math.Sin(β);
        double cosβ = Math.Cos(β);

        const string format = Config.SignificantFigures3;
        XPoint pt1, pt2, pt3;
        if (!reflect)
        {
            // Calculation for quarter 0 and 1
            switch (pathStart)
            {
                case PathStart.MoveTo1st:
                    pt1 = matrix.Transform(new XPoint(x0 + δx * cosα, y0 + δy * sinα));
                    AppendFormatPoint("{0:" + format + "} {1:" + format + "} m\n", pt1.X, pt1.Y);
                    break;

                case PathStart.LineTo1st:
                    pt1 = matrix.Transform(new XPoint(x0 + δx * cosα, y0 + δy * sinα));
                    AppendFormatPoint("{0:" + format + "} {1:" + format + "} l\n", pt1.X, pt1.Y);
                    break;

                case PathStart.Ignore1st:
                    break;
            }
            pt1 = matrix.Transform(new XPoint(x0 + δx * (cosα - κ * sinα), y0 + δy * (sinα + κ * cosα)));
            pt2 = matrix.Transform(new XPoint(x0 + δx * (cosβ + κ * sinβ), y0 + δy * (sinβ - κ * cosβ)));
            pt3 = matrix.Transform(new XPoint(x0 + δx * cosβ, y0 + δy * sinβ));
            AppendFormat3Points("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} c\n",
                pt1.X, pt1.Y, pt2.X, pt2.Y, pt3.X, pt3.Y);
        }
        else
        {
            // Calculation for quarter 2 and 3.
            switch (pathStart)
            {
                case PathStart.MoveTo1st:
                    pt1 = matrix.Transform(new XPoint(x0 - δx * cosα, y0 - δy * sinα));
                    AppendFormatPoint("{0:" + format + "} {1:" + format + "} m\n", pt1.X, pt1.Y);
                    break;

                case PathStart.LineTo1st:
                    pt1 = matrix.Transform(new XPoint(x0 - δx * cosα, y0 - δy * sinα));
                    AppendFormatPoint("{0:" + format + "} {1:" + format + "} l\n", pt1.X, pt1.Y);
                    break;

                case PathStart.Ignore1st:
                    break;
            }
            pt1 = matrix.Transform(new XPoint(x0 - δx * (cosα - κ * sinα), y0 - δy * (sinα + κ * cosα)));
            pt2 = matrix.Transform(new XPoint(x0 - δx * (cosβ + κ * sinβ), y0 - δy * (sinβ - κ * cosβ)));
            pt3 = matrix.Transform(new XPoint(x0 - δx * cosβ, y0 - δy * sinβ));
            AppendFormat3Points("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} c\n",
                pt1.X, pt1.Y, pt2.X, pt2.Y, pt3.X, pt3.Y);
        }
    }

    /// <summary>
    /// Appends a Bézier curve for a cardinal spline through pt1 and pt2.
    /// </summary>
    void AppendCurveSegment(XPoint pt0, XPoint pt1, XPoint pt2, XPoint pt3, double tension3)
    {
        const string format = Config.SignificantFigures4;
        AppendFormat3Points("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} c\n",
            pt1.X + tension3 * (pt2.X - pt0.X), pt1.Y + tension3 * (pt2.Y - pt0.Y),
            pt2.X - tension3 * (pt3.X - pt1.X), pt2.Y - tension3 * (pt3.Y - pt1.Y),
            pt2.X, pt2.Y);
    }

    /// <summary>
    /// Appends the content of a GraphicsPath object.
    /// </summary>
    internal void AppendPath(CoreGraphicsPath path)
    {
        AppendPath(path.PathPoints, path.PathTypes);
        //XPoint[] points = path.PathPoints;
        //Byte[] types = path.PathTypes;

        //int count = points.Length;
        //if (count == 0)
        //    return;

        //for (int idx = 0; idx < count; idx++)
        //{
        //    // From GDI+ documentation:
        //    const byte PathPointTypeStart = 0; // move
        //    const byte PathPointTypeLine = 1; // line
        //    const byte PathPointTypeBezier = 3; // default Bezier (= cubic Bezier)
        //    const byte PathPointTypePathTypeMask = 0x07; // type mask (lowest 3 bits).
        //    //const byte PathPointTypeDashMode = 0x10; // currently in dash mode.
        //    //const byte PathPointTypePathMarker = 0x20; // a marker for the path.
        //    const byte PathPointTypeCloseSubpath = 0x80; // closed flag

        //    byte type = types[idx];
        //    switch (type & PathPointTypePathTypeMask)
        //    {
        //        case PathPointTypeStart:
        //            //PDF_moveto(pdf, points[idx].X, points[idx].Y);
        //            AppendFormat("{0:" + format + "} {1:" + format + "} m\n", points[idx].X, points[idx].Y);
        //            break;

        //        case PathPointTypeLine:
        //            //PDF_lineto(pdf, points[idx].X, points[idx].Y);
        //            AppendFormat("{0:" + format + "} {1:" + format + "} l\n", points[idx].X, points[idx].Y);
        //            if ((type & PathPointTypeCloseSubpath) != 0)
        //                Append("h\n");
        //            break;

        //        case PathPointTypeBezier:
        //            Debug.Assert(idx + 2 < count);
        //            //PDF_curveto(pdf, points[idx].X, points[idx].Y, 
        //            //                 points[idx + 1].X, points[idx + 1].Y, 
        //            //                 points[idx + 2].X, points[idx + 2].Y);
        //            AppendFormat("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} c\n", points[idx].X, points[idx].Y,
        //                points[++idx].X, points[idx].Y, points[++idx].X, points[idx].Y);
        //            if ((types[idx] & PathPointTypeCloseSubpath) != 0)
        //                Append("h\n");
        //            break;
        //    }
        //}
    }

    void AppendPath(XPoint[] points, Byte[] types)
    {
        const string format = Config.SignificantFigures4;
        int count = points.Length;
        if (count == 0)
            return;

        for (int idx = 0; idx < count; idx++)
        {
            // ReSharper disable InconsistentNaming
            // From GDI+ documentation:
            const byte PathPointTypeStart = 0; // move
            const byte PathPointTypeLine = 1; // line
            const byte PathPointTypeBezier = 3; // default Bezier (= cubic Bezier)
            const byte PathPointTypePathTypeMask = 0x07; // type mask (lowest 3 bits).
            //const byte PathPointTypeDashMode = 0x10; // currently in dash mode.
            //const byte PathPointTypePathMarker = 0x20; // a marker for the path.
            const byte PathPointTypeCloseSubpath = 0x80; // closed flag
            // ReSharper restore InconsistentNaming

            byte type = types[idx];
            switch (type & PathPointTypePathTypeMask)
            {
                case PathPointTypeStart:
                    //PDF_moveto(pdf, points[idx].X, points[idx].Y);
                    AppendFormatPoint("{0:" + format + "} {1:" + format + "} m\n", points[idx].X, points[idx].Y);
                    break;

                case PathPointTypeLine:
                    //PDF_lineto(pdf, points[idx].X, points[idx].Y);
                    AppendFormatPoint("{0:" + format + "} {1:" + format + "} l\n", points[idx].X, points[idx].Y);
                    if ((type & PathPointTypeCloseSubpath) != 0)
                        Append("h\n");
                    break;

                case PathPointTypeBezier:
                    Debug.Assert(idx + 2 < count);
                    //PDF_curveto(pdf, points[idx].X, points[idx].Y, 
                    //                 points[idx + 1].X, points[idx + 1].Y, 
                    //                 points[idx + 2].X, points[idx + 2].Y);
                    AppendFormat3Points("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} c\n", points[idx].X, points[idx].Y,
                        points[++idx].X, points[idx].Y, points[++idx].X, points[idx].Y);
                    if ((types[idx] & PathPointTypeCloseSubpath) != 0)
                        Append("h\n");
                    break;
            }
        }
    }

    internal void Append(string value)
    {
        _content.Append(value);
    }

    internal void AppendFormatArgs(string format, params object[] args)
    {
        foreach (object arg in args)
        {
            if (arg is double number && !IsWritable(number))
                throw NotAFiniteNumber(format, args);
        }

        _content.AppendFormat(CultureInfo.InvariantCulture, format, args);
    }

    internal void AppendFormatString(string format, string s)
    {
        _content.AppendFormat(CultureInfo.InvariantCulture, format, s);
    }

    internal void AppendFormatFont(string format, string s, double d)
    {
        if (!IsWritable(d))
            throw NotAFiniteNumber(format, s, d);

        _content.AppendFormat(CultureInfo.InvariantCulture, format, s, d);
    }

    internal void AppendFormatInt(string format, int n)
    {
        _content.AppendFormat(CultureInfo.InvariantCulture, format, n);
    }

    internal void AppendFormatDouble(string format, double d)
    {
        if (!IsWritable(d))
            throw NotAFiniteNumber(format, d);

        _content.AppendFormat(CultureInfo.InvariantCulture, format, d);
    }

    internal void AppendFormatPoint(string format, double x, double y)
    {
        XPoint result = WorldToView(new XPoint(x, y));
        if (!IsWritable(result.X) || !IsWritable(result.Y))
            throw NotAFiniteNumber(format, result.X, result.Y);

        _content.AppendFormat(CultureInfo.InvariantCulture, format, result.X, result.Y);
    }

    internal void AppendFormatRect(string format, double x, double y, double width, double height)
    {
        XPoint point1 = WorldToView(new XPoint(x, y));
        if (!IsWritable(point1.X) || !IsWritable(point1.Y) || !IsWritable(width) || !IsWritable(height))
            throw NotAFiniteNumber(format, point1.X, point1.Y, width, height);

        _content.AppendFormat(CultureInfo.InvariantCulture, format, point1.X, point1.Y, width, height);
    }

    internal void AppendFormat3Points(string format, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        XPoint point1 = WorldToView(new XPoint(x1, y1));
        XPoint point2 = WorldToView(new XPoint(x2, y2));
        XPoint point3 = WorldToView(new XPoint(x3, y3));
        if (!IsWritable(point1.X) || !IsWritable(point1.Y) || !IsWritable(point2.X)
            || !IsWritable(point2.Y) || !IsWritable(point3.X) || !IsWritable(point3.Y))
        {
            throw NotAFiniteNumber(format,
                point1.X, point1.Y, point2.X, point2.Y, point3.X, point3.Y);
        }

        _content.AppendFormat(CultureInfo.InvariantCulture, format, point1.X, point1.Y, point2.X, point2.Y, point3.X, point3.Y);
    }

    internal void AppendFormat(string format, XPoint point)
    {
        XPoint result = WorldToView(point);
        if (!IsWritable(result.X) || !IsWritable(result.Y))
            throw NotAFiniteNumber(format, result.X, result.Y);

        _content.AppendFormat(CultureInfo.InvariantCulture, format, result.X, result.Y);
    }

    internal void AppendFormat(string format, double x, double y, string s)
    {
        XPoint result = WorldToView(new XPoint(x, y));
        if (!IsWritable(result.X) || !IsWritable(result.Y))
            throw NotAFiniteNumber(format, result.X, result.Y, s);

        _content.AppendFormat(CultureInfo.InvariantCulture, format, result.X, result.Y, s);
    }

    internal void AppendFormatImage(string format, double x, double y, double width, double height, string name)
    {
        XPoint result = WorldToView(new XPoint(x, y));
        if (!IsWritable(result.X) || !IsWritable(result.Y) || !IsWritable(width) || !IsWritable(height))
            throw NotAFiniteNumber(format, result.X, result.Y, width, height, name);

        _content.AppendFormat(CultureInfo.InvariantCulture, format, result.X, result.Y, width, height, name);
    }

    /// <summary>
    /// Whether a number can be written into a content stream at all.
    /// </summary>
    /// <remarks>
    /// PDF has no syntax for NaN or for infinity. Writing one produces an operand a viewer cannot
    /// parse, and a viewer's response to that is to stop drawing - silently, and usually for the
    /// rest of the content stream rather than for the one operator. A page that arrives blank is
    /// the symptom, which is a very long way from the cause.
    /// </remarks>
    static bool IsWritable(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>
    /// Refuses a number on a path that assembles its operator as text rather than formatting it,
    /// and so cannot be checked on the way through one of the Append methods.
    /// </summary>
    internal static void EnsureWritable(double value, string writing)
    {
        if (!IsWritable(value))
            throw RefuseOperator(writing);
    }

    /// <summary>
    /// The refusal, quoting the operator that was about to be written.
    /// </summary>
    /// <remarks>
    /// Formatting the doomed operator into the message is the whole value of this: "NaN NaN m"
    /// names the operator, and the position of the NaN among the operands says which coordinate
    /// went wrong. This is only reached on the way to throwing, so it costs nothing to draw with.
    /// </remarks>
    static InvalidOperationException NotAFiniteNumber(string format, params object[] args)
    {
        string operatorText;
        try
        {
            // Numbers only. The show-text operators are assembled elsewhere and handed here whole,
            // so a string operand is a run of the document's own text - and this message names an
            // exception that a caller will log. The numbers are what the message exists to carry.
            object[] withoutText = new object[args.Length];
            for (int idx = 0; idx < args.Length; idx++)
                withoutText[idx] = args[idx] is string ? "..." : args[idx];

            operatorText = string.Format(CultureInfo.InvariantCulture, format, withoutText).Trim();
        }
        catch (FormatException)
        {
            // The message is a diagnostic, not a contract. If the format and the arguments
            // disagree, the refusal still has to happen.
            operatorText = format.Trim();
        }

        return RefuseOperator(operatorText);
    }

    static InvalidOperationException RefuseOperator(string operatorText) =>
        new InvalidOperationException(
            $"Cannot write \"{operatorText}\" into a content stream: an operand is not a finite "
            + "number. PDF cannot express NaN or infinity, and a viewer handed one stops drawing "
            + "rather than complaining, so the page arrives blank or half-finished with nothing to "
            + "say why. Either a coordinate passed to the drawing call was already not a number, or "
            + "a transform made it one - a scale derived from a range of zero width divides by zero "
            + "and turns every point that goes through it into NaN.");

    void AppendStrokeFill(XPen pen, XBrush brush, XFillMode fillMode, bool closePath)
    {
        if (closePath)
            _content.Append("h ");

        if (fillMode == XFillMode.Winding)
        {
            if (pen != null && brush != null)
                _content.Append("B\n");
            else if (pen != null)
                _content.Append("S\n");
            else
                _content.Append("f\n");
        }
        else
        {
            if (pen != null && brush != null)
                _content.Append("B*\n");
            else if (pen != null)
                _content.Append("S\n");
            else
                _content.Append("f*\n");
        }
    }
    #endregion

    // --------------------------------------------------------------------------------------------

    #region Realizing graphical state

    /// <summary>
    /// Initializes the default view transformation, i.e. the transformation from the user page
    /// space to the PDF page space.
    /// </summary>
    void BeginPage()
    {
        if (_gfxState.Level == GraphicsStackLevelInitial)
        {
            // TODO: Is PageOriging and PageScale (== Viewport) useful? Or just public DefaultViewMatrix (like Presentation Manager has had)
            // May be a BeginContainer(windows, viewport) is useful for userer that are not familar with maxtrix transformations.

            // Flip page horizontally and mirror text.

            // PDF uses a standard right-handed Cartesian coordinate system with the y axis directed up
            // and the rotation counterclockwise. Windows uses the opposite convertion with y axis
            // directed down and rotation clockwise. When I started with PDFsharp I flipped pages horizontally
            // and then mirrored text to compensate the effect that the fipping turns text upside down.
            // I found this technique during analysis of PDF documents generated with PDFlib. Unfortunately
            // this technique leads to several problems with programms that compose or view PDF documents
            // generated with PdfSharpCore.
            // In PDFsharp 1.4 I implement a revised technique that does not need text mirroring any more.

            DefaultViewMatrix = new XMatrix();
            if (_gfx.PageDirection == XPageDirection.Downwards)
            {
                // Take TrimBox into account.
                PageHeightPt = VisiblePageSize.Height;
                XPoint trimOffset = new XPoint();
                if (_page != null && _page.TrimMargins.AreSet)
                {
                    // The sheet is the page plus the bleed plus the room for printer's marks, and
                    // the origin is the corner of the page rather than of the sheet. Both come
                    // from the page so that this and XGraphics.Initialize cannot disagree.
                    PageHeightPt += _page.SheetExtraHeight;
                    trimOffset = _page.SheetOffset;
                }

                // Scale with page units.
                switch (_gfx.PageUnit)
                {
                    case XGraphicsUnit.Point:
                        // Factor is 1.
                        // DefaultViewMatrix.ScalePrepend(XUnit.PointFactor);
                        break;

                    case XGraphicsUnit.Presentation:
                        DefaultViewMatrix.ScalePrepend(XUnit.PresentationFactor);
                        break;

                    case XGraphicsUnit.Inch:
                        DefaultViewMatrix.ScalePrepend(XUnit.InchFactor);
                        break;

                    case XGraphicsUnit.Millimeter:
                        DefaultViewMatrix.ScalePrepend(XUnit.MillimeterFactor);
                        break;

                    case XGraphicsUnit.Centimeter:
                        DefaultViewMatrix.ScalePrepend(XUnit.CentimeterFactor);
                        break;
                }

                if (trimOffset != new XPoint())
                {
                    Debug.Assert(_gfx.PageUnit == XGraphicsUnit.Point, "With TrimMargins set the page units must be Point. Ohter cases nyi.");
                    DefaultViewMatrix.TranslatePrepend(trimOffset.X, -trimOffset.Y);
                }

                // Save initial graphic state.
                SaveState();

                // Turn the page the way the viewer will show it, so that the origin is the
                // corner the reader sees first. It has to be concatenated before the matrix
                // below, because that one still works in the units of the caller.
                AppendPageRotation();

                // Set default page transformation, if any.
                if (!DefaultViewMatrix.IsIdentity)
                {
                    Debug.Assert(_gfxState.RealizedCtm.IsIdentity);
                    //_gfxState.RealizedCtm = DefaultViewMatrix;
                    const string format = Config.SignificantFigures7;
                    double[] cm = DefaultViewMatrix.GetElements();
                    AppendFormatArgs("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} cm ",
                        cm[0], cm[1], cm[2], cm[3], cm[4], cm[5]);
                }

                // Set page transformation
                //double[] cm = DefaultViewMatrix.GetElements();
                //AppendFormat("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} cm ",
                //  cm[0], cm[1], cm[2], cm[3], cm[4], cm[5]);
            }
            else
            {
                // Scale with page units.
                switch (_gfx.PageUnit)
                {
                    case XGraphicsUnit.Point:
                        // Factor is 1.
                        // DefaultViewMatrix.ScalePrepend(XUnit.PointFactor);
                        break;

                    case XGraphicsUnit.Presentation:
                        DefaultViewMatrix.ScalePrepend(XUnit.PresentationFactor);
                        break;

                    case XGraphicsUnit.Inch:
                        DefaultViewMatrix.ScalePrepend(XUnit.InchFactor);
                        break;

                    case XGraphicsUnit.Millimeter:
                        DefaultViewMatrix.ScalePrepend(XUnit.MillimeterFactor);
                        break;

                    case XGraphicsUnit.Centimeter:
                        DefaultViewMatrix.ScalePrepend(XUnit.CentimeterFactor);
                        break;
                }

                // Save initial graphic state.
                SaveState();
                AppendPageRotation();
                // Set page transformation.
                const string format = Config.SignificantFigures7;
                double[] cm = DefaultViewMatrix.GetElements();
                AppendFormat3Points("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} cm ",
                    cm[0], cm[1], cm[2], cm[3], cm[4], cm[5]);
            }
        }
    }

    /// <summary>
    /// Concatenates the matrix that maps the page as the viewer shows it onto the page as it is
    /// stored, undoing the /Rotate entry of the page. Without it everything drawn on a rotated
    /// page ends up turned, and on a page rotated by 90 or 270 degrees it is also displaced,
    /// because such a page reports the width and the height the viewer shows, not the ones the
    /// media box holds.
    /// </summary>
    void AppendPageRotation()
    {
        XMatrix rotation = PageRotationMatrix();
        if (rotation.IsIdentity)
            return;

        const string format = Config.SignificantFigures7;
        double[] cm = rotation.GetElements();
        AppendFormatArgs("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} cm ",
            cm[0], cm[1], cm[2], cm[3], cm[4], cm[5]);
    }

    /// <summary>
    /// Gets the matrix that turns the page the way the viewer shows it. The corners of the
    /// visible page are mapped onto the corners of the media box that the /Rotate entry sends
    /// them to.
    /// </summary>
    XMatrix PageRotationMatrix()
    {
        XSize mediaBox = StoredPageSize;
        switch (PageRotation)
        {
            case 90:
                return new XMatrix(0, 1, -1, 0, mediaBox.Width, 0);
            case 180:
                return new XMatrix(-1, 0, 0, -1, mediaBox.Width, mediaBox.Height);
            case 270:
                return new XMatrix(0, -1, 1, 0, 0, mediaBox.Height);
            default:
                return new XMatrix();
        }
    }

    /// <summary>
    /// Gets the /Rotate entry of the page, normalized to 0, 90, 180 or 270.
    /// </summary>
    int PageRotation
    {
        get
        {
            if (_page == null)
                return 0;
            int rotation = _page.Rotate % 360;
            return rotation < 0 ? rotation + 360 : rotation;
        }
    }

    /// <summary>
    /// The extended graphics state that takes a gradient's soft mask off again, made once per
    /// page or form so that the reset is one resource however many gradients ask for it.
    /// </summary>
    internal PdfExtGState NoSoftMaskState
    {
        get
        {
            if (_noSoftMaskState == null)
            {
                _noSoftMaskState = new PdfExtGState(Owner);
                _noSoftMaskState.Elements.SetName(PdfExtGState.Keys.SMask, "/None");
            }
            return _noSoftMaskState;
        }
    }
    PdfExtGState _noSoftMaskState;

    /// <summary>
    /// Gets the size of this page or form as it is written to the file. It is the area drawing
    /// ends up in, before the viewer turns the page.
    /// </summary>
    internal XSize StoredPageSize => _page != null ? _page.StoredSize : _form.Size;

    /// <summary>
    /// The transform the content stream has in force where something is drawn, given the matrix
    /// that maps the caller's world onto the page. It is that matrix with the turn a rotated page
    /// is drawn through concatenated after it, because that turn is written straight into the
    /// content stream rather than tracked as part of the graphics state.
    /// </summary>
    /// <remarks>
    /// Wanted by anything that has to undo the transform rather than work within it - a soft mask
    /// group, which a reader evaluates under whatever transform was in force when the mask was
    /// set, while the pattern that paints it is anchored to the page and ignores that transform.
    /// </remarks>
    internal XMatrix RealizedTransformOf(XMatrix worldToPage)
    {
        XMatrix realized = PageRotationMatrix();
        realized.Prepend(worldToPage);
        return realized;
    }

    /// <summary>
    /// Gets the size of this page or form as the viewer shows it, which is the size the caller
    /// draws on. It differs from the stored size when the page is turned by a quarter.
    /// </summary>
    XSize VisiblePageSize => _page != null ? new XSize(_page.Width, _page.Height) : _form.Size;

    /// <summary>
    /// Ends the content stream, i.e. ends the text mode and balances the graphic state stack.
    /// </summary>
    void EndPage()
    {
        if (_streamMode == StreamMode.Text)
        {
            _content.Append("ET\n");
            _streamMode = StreamMode.Graphic;
        }

        while (_gfxStateStack.Count != 0)
            RestoreState();
    }

    /// <summary>
    /// Begins the graphic mode (i.e. ends the text mode).
    /// </summary>
    internal void BeginGraphicMode()
    {
        if (_streamMode != StreamMode.Graphic)
        {
            if (_streamMode == StreamMode.Text)
                _content.Append("ET\n");

            _streamMode = StreamMode.Graphic;
        }
    }

    /// <summary>
    /// Begins the graphic mode (i.e. ends the text mode).
    /// </summary>
    internal void BeginTextMode()
    {
        if (_streamMode != StreamMode.Text)
        {
            _streamMode = StreamMode.Text;
            _content.Append("BT\n");
            // Text matrix is empty after BT
            _gfxState.RealizedTextPosition = new XPoint();
            _gfxState.RealizedTextSkew = 0;
        }
    }

    StreamMode _streamMode;

    /// <summary>
    /// Makes the specified pen and brush to the current graphics objects.
    /// </summary>
    private void Realize(XPen pen, XBrush brush)
    {
        BeginPage();
        BeginGraphicMode();
        RealizeTransform();

        if (pen != null)
            _gfxState.RealizePen(pen, _colorMode); // page.document.Options.ColorMode);

        if (brush != null)
        {
            // Render mode is 0 except for bold simulation.
            _gfxState.RealizeBrush(brush, _colorMode, 0, 0); // page.document.Options.ColorMode);
        }
    }

    /// <summary>
    /// Makes the specified pen to the current graphics object.
    /// </summary>
    void Realize(XPen pen)
    {
        Realize(pen, null);
    }

    /// <summary>
    /// Makes the specified brush to the current graphics object.
    /// </summary>
    void Realize(XBrush brush)
    {
        Realize(null, brush);
    }

    /// <summary>
    /// Makes the specified font and brush to the current graphics objects.
    /// </summary>
    void Realize(XFont font, XBrush brush, XPen pen, bool boldSimulation, XStringFormat format)
    {
        BeginPage();
        RealizeTransform();
        BeginTextMode();
        _gfxState.RealizeFont(font, brush, pen, boldSimulation, format);
    }

    /// <summary>
    /// Encodes a run of glyph identifiers as the hexadecimal string a Tj or a TJ array takes.
    /// </summary>
    static string GlyphRunToHexString(string glyphs)
    {
        byte[] bytes = PdfEncoders.RawUnicodeEncoding.GetBytes(glyphs);
        bytes = PdfEncoders.FormatStringLiteral(bytes, true, false, true, null);
        return PdfEncoders.RawEncoding.GetString(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Builds a TJ array that draws <paramref name="glyphs"/> with <paramref name="wordSpacing"/>
    /// points of extra room after every space, for the fonts whose Tw cannot say so.
    /// </summary>
    /// <remarks>
    /// A number in a TJ array moves the pen back by n/1000 of the font size, so the number that
    /// buys one word spacing is -wordSpacing * 1000 / size. The horizontal scaling multiplies that
    /// displacement and the one Tw produces alike, so it cancels and is not compensated for here.
    /// <para>
    /// <paramref name="text"/> is the string as the caller wrote it and <paramref name="glyphs"/>
    /// the glyph identifiers it was mapped to, one per character, so an index into one is an index
    /// into the other.
    /// </para>
    /// </remarks>
    static string WordSpacedGlyphRun(string text, string glyphs, double fontSize, double wordSpacing)
    {
        Debug.Assert(text.Length == glyphs.Length, "One glyph per character, or the split lands in the wrong place.");

        // A font of no size has no displacement to divide into, and nothing to show either.
        if (fontSize <= 0)
            return GlyphRunToHexString(glyphs) + " Tj";

        double adjustment = -wordSpacing * 1000 / fontSize;

        StringBuilder tj = new StringBuilder("[");
        int start = 0;
        for (int idx = 0; idx < text.Length; idx++)
        {
            if (!IsWordSpace(text[idx]))
                continue;

            // The run up to and including the space, then the room that follows it.
            tj.Append(GlyphRunToHexString(glyphs.Substring(start, idx - start + 1)));
            tj.Append(' ');
            tj.Append(adjustment.ToString(Config.SignificantFigures3, CultureInfo.InvariantCulture));
            tj.Append(' ');
            start = idx + 1;
        }
        if (start < glyphs.Length)
            tj.Append(GlyphRunToHexString(glyphs.Substring(start)));
        tj.Append("] TJ");
        return tj.ToString();
    }

    /// <summary>
    /// True for the characters a word spacing is paid out for. Kept in step with
    /// FontHelper.MeasureString, which maps a tab to a space before it counts one.
    /// </summary>
    static bool IsWordSpace(char ch) => ch == ' ' || ch == '\t';

    /// <summary>
    /// PDFsharp uses the Td operator to set the text position. Td just sets the offset of the text matrix
    /// and produces lesser code as Tm.
    /// </summary>
    /// <param name="pos">The absolute text position.</param>
    /// <param name="dy">The dy.</param>
    /// <param name="skew">
    /// How far the text matrix currently leans, as the tangent of the angle, or 0 for upright
    /// text. Td's offset is taken through that lean, so it has to be corrected for it.
    /// </param>
    void AdjustTdOffset(ref XPoint pos, double dy, double skew)
    {
        pos.Y += dy;
        // Reference: TABLE 5.5  Text-positioning operators / Page 406
        XPoint posSave = pos;
        // Map from absolute to relative position.
        pos = pos - new XVector(_gfxState.RealizedTextPosition.X, _gfxState.RealizedTextPosition.Y);
        if (skew != 0)
        {
            // A leaning text matrix carries the Td offset sideways by the height it moves through,
            // so that much has to come off the offset for the text to land where it was asked for.
            pos.X -= skew * pos.Y;
        }
        _gfxState.RealizedTextPosition = posSave;
    }

    /// <summary>
    /// Makes the specified image to the current graphics object.
    /// </summary>
    string Realize(XImage image)
    {
        BeginPage();
        BeginGraphicMode();
        RealizeTransform();

        // The transparency set for a brush also applies to images. Set opacity to 100% so image will be drawn without transparency.
        _gfxState.RealizeNonStrokeTransparency(1, _colorMode);

        XForm form = image as XForm;
        return form != null ? GetFormName(form) : GetImageName(image);
    }

    /// <summary>
    /// Realizes the current transformation matrix, if necessary.
    /// </summary>
    void RealizeTransform()
    {
        BeginPage();

        if (_gfxState.Level == GraphicsStackLevelPageSpace)
        {
            BeginGraphicMode();
            SaveState();
        }

        //if (gfxState.MustRealizeCtm)
        if (!_gfxState.UnrealizedCtm.IsIdentity)
        {
            BeginGraphicMode();
            _gfxState.RealizeCtm();
        }
    }

    /// <summary>
    /// Convert a point from Windows world space to PDF world space.
    /// </summary>
    internal XPoint WorldToView(XPoint point)
    {
        // If EffectiveCtm is not yet realized InverseEffectiveCtm is invalid.
        Debug.Assert(_gfxState.UnrealizedCtm.IsIdentity, "Somewhere a RealizeTransform is missing.");
        // See in #else case why this is correct.
        XPoint pt = _gfxState.WorldTransform.Transform(point);
        return _gfxState.InverseEffectiveCtm.Transform(new XPoint(pt.X, PageHeightPt / DefaultViewMatrix.M22 - pt.Y));
    }
    #endregion

    /// <summary>
    /// Gets the owning PdfDocument of this page or form.
    /// </summary>
    internal PdfDocument Owner
    {
        get
        {
            if (_page != null)
                return _page.Owner;
            return _form.Owner;
        }
    }

    internal XGraphics Gfx => _gfx;

    /// <summary>
    /// Gets the PdfResources of this page or form.
    /// </summary>
    internal PdfResources Resources
    {
        get
        {
            if (_page != null)
                return _page.Resources;
            return _form.Resources;
        }
    }

    /// <summary>
    /// Gets the resource name of the specified font within this page or form.
    /// </summary>
    internal string GetFontName(XFont font, out PdfFont pdfFont)
    {
        if (_page != null)
            return _page.GetFontName(font, out pdfFont);
        return _form.GetFontName(font, out pdfFont);
    }

    /// <summary>
    /// Gets the resource name of the specified image within this page or form.
    /// </summary>
    internal string GetImageName(XImage image)
    {
        if (_page != null)
            return _page.GetImageName(image);
        return _form.GetImageName(image);
    }

    /// <summary>
    /// Gets the resource name of the specified form within this page or form.
    /// </summary>
    internal string GetFormName(XForm form)
    {
        if (_page != null)
            return _page.GetFormName(form);
        return _form.GetFormName(form);
    }

    internal PdfPage _page;
    internal XForm _form;
    internal PdfColorMode _colorMode;
    XGraphicsPdfPageOptions _options;
    XGraphics _gfx;
    readonly StringBuilder _content;

    /// <summary>
    /// The q/Q nesting level is 0.
    /// </summary>
    const int GraphicsStackLevelInitial = 0;

    /// <summary>
    /// The q/Q nesting level is 1.
    /// </summary>
    const int GraphicsStackLevelPageSpace = 1;

    /// <summary>
    /// The q/Q nesting level is 2.
    /// </summary>
    const int GraphicsStackLevelWorldSpace = 2;

    #region PDF Graphics State

    /// <summary>
    /// Saves the current graphical state.
    /// </summary>
    void SaveState()
    {
        Debug.Assert(_streamMode == StreamMode.Graphic, "Cannot save state in text mode.");

        _gfxStateStack.Push(_gfxState);
        _gfxState = _gfxState.Clone();
        _gfxState.Level = _gfxStateStack.Count;
        Append("q\n");
    }

    /// <summary>
    /// Restores the previous graphical state.
    /// </summary>
    void RestoreState()
    {
        Debug.Assert(_streamMode == StreamMode.Graphic, "Cannot restore state in text mode.");

        _gfxState = _gfxStateStack.Pop();
        Append("Q\n");
    }

    PdfGraphicsState RestoreState(InternalGraphicsState state)
    {
        int count = 1;
        PdfGraphicsState top = _gfxStateStack.Pop();
        while (top.InternalState != state)
        {
            Append("Q\n");
            count++;
            top = _gfxStateStack.Pop();
        }
        Append("Q\n");
        _gfxState = top;
        return top;
    }

    /// <summary>
    /// The current graphical state.
    /// </summary>
    PdfGraphicsState _gfxState;

    /// <summary>
    /// The graphical state stack.
    /// </summary>
    readonly Stack<PdfGraphicsState> _gfxStateStack = new();

    #endregion

    /// <summary>
    /// The height of the PDF page in point including the trim box.
    /// </summary>
    public double PageHeightPt;

    /// <summary>
    /// The final transformation from the world space to the default page space.
    /// </summary>
    public XMatrix DefaultViewMatrix;
}
