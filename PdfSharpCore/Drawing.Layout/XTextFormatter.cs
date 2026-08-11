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
using System.Collections.Generic;
using System.Linq;
using PdfSharpCore.Drawing.Layout.enums;
using PdfSharpCore.Pdf.IO;

namespace PdfSharpCore.Drawing.Layout;

/// <summary>
/// Represents a very simple text formatter.
/// If this class does not satisfy your needs on formatting paragraphs I recommend to take a look
/// at MigraDoc Foundation. Alternatively you should copy this class in your own source code and modify it.
/// </summary>
public class XTextFormatter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="XTextFormatter"/> class.
    /// </summary>
    public XTextFormatter(XGraphics gfx)
    {
        if (gfx == null)
            throw new ArgumentNullException(nameof(gfx));
        _gfx = gfx;
    }
    readonly XGraphics _gfx;

    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    /// <value>The text.</value>
    public string Text
    {
        get => _text;
        set => _text = value;
    }
    string _text;

    /// <summary>
    /// Gets or sets the font.
    /// </summary>
    public XFont Font
    {
        get => _font;
        set
        {
            if (value == null)
                throw new ArgumentNullException("Font");
            _font = value;

            _lineSpace = _font.GetHeight(); // old: _font.GetHeight(_gfx);
            _cyAscent = _lineSpace * _font.CellAscent / _font.CellSpace;
            _cyDescent = _lineSpace * _font.CellDescent / _font.CellSpace;

            // HACK in XTextFormatter
            _spaceWidth = _gfx.MeasureString("x x", value).Width;
            _spaceWidth -= _gfx.MeasureString("xx", value).Width;
        }
    }
    XFont _font;
    double _lineSpace;
    double _cyAscent;
    double _cyDescent;
    double _spaceWidth;
    double _lineHeight;

    /// <summary>
    /// Gets or sets the bounding box of the layout.
    /// </summary>
    public XRect LayoutRectangle
    {
        get => _layoutRectangle;
        set => _layoutRectangle = value;
    }
    XRect _layoutRectangle;

    /// <summary>
    /// When true, ignore the height of text areas when rendering multiline strings
    /// </summary>
    public bool AllowVerticalOverflow { get; set; } = false;
        
    /// <summary>
    /// Gets or sets the horizontal alignment of the text.
    /// </summary>
    public XParagraphAlignment Alignment { get; set; } = XParagraphAlignment.Left;
        
    /// <summary>
    /// Gets or sets the vertical alignment of the text.
    /// </summary>
    public XVerticalAlignment VerticalAlignment { get; set; } = XVerticalAlignment.Top;

    /// <summary>
    /// Set vertical and horizontal alignment
    /// </summary>
    /// <param name="alignments"></param>
    public void SetAlignment(TextFormatAlignment alignments)
    {
        Alignment = alignments.Horizontal;
        VerticalAlignment = alignments.Vertical;
    }

    // ----- paragraph shape ----------------------------------------------------------------------

    /// <summary>
    /// Gets or sets whether a line too long for the layout rectangle is broken into more lines.
    /// True by default. Set it to false and the text runs on past the right edge; the line breaks
    /// written into the text are still obeyed.
    /// </summary>
    public bool LineBreak { get; set; } = true;

    /// <summary>
    /// Gets or sets how far, in points, the first line of each paragraph is indented.
    /// The default is 0.
    /// </summary>
    public double Indent { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="Indent"/> applies to every line rather than to the first
    /// line of each paragraph. False by default.
    /// </summary>
    public bool IndentAllLines { get; set; }

    /// <summary>
    /// Gets or sets the space, in points, left between one paragraph and the next - that is, at
    /// each line break written into the text. The default is 0.
    /// </summary>
    public double ParagraphGap { get; set; }

    /// <summary>
    /// Gets or sets the space, in points, added between one line and the next. The default is 0.
    /// </summary>
    /// <remarks>
    /// This is added to the line height, where the <c>lineHeight</c> argument of
    /// <see cref="DrawString(string, XFont, XBrush, XRect, XUnit?)"/> replaces it. Both can be used
    /// at once: one says how tall a line is, the other how much room to leave after it.
    /// </remarks>
    public double LineGap { get; set; }

    /// <summary>
    /// Gets or sets the string put at the end of text that did not fit the layout rectangle.
    /// Null or empty, the default, cuts the text off without a mark.
    /// </summary>
    /// <remarks>
    /// A string rather than a flag, because the ellipsis character is not in every font, and
    /// three full stops are the usual answer when it is not. <see cref="DefaultEllipsis"/> is the
    /// character itself.
    /// <para>
    /// Ignored when <see cref="AllowVerticalOverflow"/> is set, since then nothing is cut off.
    /// </para>
    /// </remarks>
    public string Ellipsis { get; set; }

    /// <summary>
    /// The horizontal ellipsis, U+2026 - what <see cref="Ellipsis"/> is usually set to.
    /// </summary>
    public const string DefaultEllipsis = "…";

    /// <summary>
    /// Gets or sets the angle, in degrees, the text is turned through about the top left corner of
    /// the layout rectangle. Positive turns it anticlockwise on the page. The default is 0.
    /// </summary>
    public double Rotation { get; set; }

    /// <summary>
    /// Gets or sets how many columns the layout rectangle is divided into, which the text flows
    /// down in turn. The default is 1.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int Columns
    {
        get => _columns;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Columns must be at least 1.");
            _columns = value;
        }
    }
    int _columns = 1;

    /// <summary>
    /// Gets or sets the space, in points, left between one column and the next.
    /// The default is 18 - a quarter of an inch, as PDFKit uses.
    /// </summary>
    public double ColumnGap { get; set; } = 18;

    /// <summary>
    /// How wide one column is, given the width of the whole layout rectangle.
    /// </summary>
    double ColumnWidthWithin(double rectWidth)
    {
        return (rectWidth - ColumnGap * (Columns - 1)) / Columns;
    }

    /// <summary>
    /// How far the left edge of a column sits from the left edge of the layout rectangle.
    /// </summary>
    double ColumnLeft(int column, double columnWidth)
    {
        return column * (columnWidth + ColumnGap);
    }
        
        
    /// <summary>
    /// Draws the text.
    /// </summary>
    /// <param name="text">The text to be drawn.</param>
    /// <param name="font">The font.</param>
    /// <param name="brush">The text brush.</param>
    /// <param name="layoutRectangle">The layout rectangle.</param>
    /// <param name="lineHeight">The line height.</param>
    /// <remarks>
    /// The text is drawn with the alignment the formatter has been given, which is to the top
    /// left until <see cref="SetAlignment"/> or the properties behind it say otherwise.
    /// </remarks>
    public void DrawString(string text, XFont font, XBrush brush, XRect layoutRectangle, XUnit? lineHeight = null)
    {
        DrawString(text, font, brush, layoutRectangle, new TextFormatAlignment()
        {
            Horizontal = Alignment, Vertical = VerticalAlignment
        }, lineHeight);
    }

    /// <summary>
    /// Get the layout rectangle required.
    /// </summary>
    /// <param name="text">The text to be drawn.</param>
    /// <param name="font">The font.</param>
    /// <param name="brush">The text brush.</param>
    /// <param name="layoutRectangle">The layout rectangle.</param>
    /// <param name="lineHeight">The height of each line</param>
    public XRect GetLayout(string text, XFont font, XBrush brush, XRect layoutRectangle,
        XUnit? lineHeight = null)
    {
        if (text == null)
            throw new ArgumentNullException(nameof(text));
        if (font == null)
            throw new ArgumentNullException(nameof(font));
        if (brush == null)
            throw new ArgumentNullException(nameof(brush));

        Text = text;
        Font = font;
        LayoutRectangle = layoutRectangle;
            
        _lineHeight = lineHeight?.Point ?? _lineSpace;

        if (text.Length == 0)
            return new XRect(layoutRectangle.Location.X, layoutRectangle.Location.Y, 0, 0);

        CreateBlocks();

        CreateLayout();

        return _layoutRectangle;
    }

    /// <summary>
    /// Draws the text.
    /// </summary>
    /// <param name="text">The text to be drawn.</param>
    /// <param name="font">The font.</param>
    /// <param name="brush">The text brush.</param>
    /// <param name="layoutRectangle">The layout rectangle.</param>
    /// <param name="alignments">The alignments.</c></param>
    /// <param name="lineHeight">The height of each line.</param>
    public void DrawString(string text, XFont font, XBrush brush, XRect layoutRectangle, TextFormatAlignment alignments,
        XUnit? lineHeight = null)
    {
        if (alignments == null)
            throw new ArgumentNullException(nameof(alignments));

        if (text.Length == 0)
            return;

        GetLayout(text, font, brush, layoutRectangle, lineHeight);

        SetAlignment(alignments);

        double dx = layoutRectangle.Location.X;
        double dy = layoutRectangle.Location.Y;

        var lines = GetLines(_blocks).ToArray();

        if (VerticalAlignment == XVerticalAlignment.Middle)
        {
            dy += (layoutRectangle.Height - _layoutRectangle.Height) / 2;
        }
        else if (VerticalAlignment == XVerticalAlignment.Bottom)
        {
            // A line is placed by its top, so the last one ends on the bottom of the rectangle
            // once the text as a whole is put that far down.
            dy = layoutRectangle.Location.Y + layoutRectangle.Height - _layoutRectangle.Height;
        }

        // Turning the text turns the whole block of it about the corner it starts from, so the
        // lines stay in the same order and the same distance apart whatever the angle.
        XGraphicsState state = null;
        if (Rotation != 0)
        {
            state = _gfx.Save();
            _gfx.RotateAtTransform(-Rotation, new XPoint(layoutRectangle.X, layoutRectangle.Y));
        }

        var columnWidth = ColumnWidthWithin(layoutRectangle.Width);

        try
        {
            foreach (var line in lines)
            {
                var lineBlocks = line as Block[] ?? line.ToArray();
                var lineY = dy + lineBlocks.First().Location.Y;
                var indent = lineBlocks.First().LineIndent;

                // A line is laid out and aligned within its own column, so that is the left edge it
                // is measured from and the width it is measured against.
                var columnLeft = dx + ColumnLeft(lineBlocks.First().Column, columnWidth);

                // The last line of a paragraph keeps its natural width. Stretching it over the
                // full width of the column would tear the few words it holds apart.
                if (Alignment == XParagraphAlignment.Justify && !lineBlocks[lineBlocks.Length - 1].EndsParagraph)
                {
                    var locationX = columnLeft + indent;
                    var gaps = lineBlocks.Length - 1;
                    var gapSize = gaps > 0
                        ? (columnWidth - indent - lineBlocks.Select(l => l.Width).Sum()) / gaps
                        : 0;
                    foreach (var block in lineBlocks)
                    {
                        _gfx.DrawString(block.Text.Trim(), font, brush, locationX, lineY, XStringFormats.TopLeft);
                        locationX += block.Width + gapSize;
                    }
                }
                else
                {
                    var lineText = string.Join(" ", lineBlocks.Select(l => l.Text));
                    // An indent takes room off the left, so what is left to centre in - or to push a
                    // line to the right end of - is that much narrower.
                    var locationX = columnLeft + indent;
                    if (Alignment == XParagraphAlignment.Center)
                        locationX = columnLeft + indent + (columnWidth - indent) / 2;
                    if (Alignment == XParagraphAlignment.Right)
                        locationX = columnLeft + columnWidth;
                    _gfx.DrawString(lineText, font, brush, locationX, lineY, GetXStringFormat());
                }
            }
        }
        finally
        {
            // In a finally because the turn is on the caller's XGraphics, not on something this
            // method owns. A line that throws half way through would otherwise leave the transform
            // applied, and everything the caller drew afterwards would come out turned.
            if (state != null)
                _gfx.Restore(state);
        }
    }

    private static IEnumerable<IEnumerable<Block>> GetLines(List<Block> blocks)
    {
        // By column as well as by height: the first line of the second column sits level with the
        // first line of the first, and grouping on height alone would draw them as one line.
        return GetLaidOutBlocks(blocks).GroupBy(b => (b.Column, b.Location.Y));
    }

    /// <summary>
    /// Gets the blocks that carry text and were given a location by <see cref="CreateLayout"/>.
    /// Line breaks have neither, and the blocks from the first one marked with <see cref="Block.Stop"/>
    /// onwards did not fit into the layout rectangle and were left unpositioned.
    /// </summary>
    private static IEnumerable<Block> GetLaidOutBlocks(List<Block> blocks)
    {
        return blocks.TakeWhile(b => !b.Stop).Where(b => b.Type != BlockType.LineBreak);
    }

    void CreateBlocks()
    {
        _blocks.Clear();
        int length = _text.Length;
        bool inNonWhiteSpace = false;
        int startIndex = 0, blockLength = 0;
        for (int idx = 0; idx < length; idx++)
        {
            char ch = _text[idx];

            // Treat CR and CRLF as LF
            if (ch == Chars.CR)
            {
                if (idx < length - 1 && _text[idx + 1] == Chars.LF)
                    idx++;
                ch = Chars.LF;
            }
            if (ch == Chars.LF)
            {
                if (blockLength != 0)
                {
                    string token = _text.Substring(startIndex, blockLength);
                    _blocks.Add(new Block(token, BlockType.Text,
                        _gfx.MeasureString(token, _font).Width));
                }
                startIndex = idx + 1;
                blockLength = 0;
                _blocks.Add(new Block(BlockType.LineBreak));
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (inNonWhiteSpace)
                {
                    string token = _text.Substring(startIndex, blockLength);
                    _blocks.Add(new Block(token, BlockType.Text,
                        _gfx.MeasureString(token, _font).Width));
                    startIndex = idx + 1;
                    blockLength = 0;
                }
                else
                {
                    blockLength++;
                }
            }
            else
            {
                inNonWhiteSpace = true;
                blockLength++;
            }
        }
        if (blockLength != 0)
        {
            string token = _text.Substring(startIndex, blockLength);
            _blocks.Add(new Block(token, BlockType.Text,
                _gfx.MeasureString(token, _font).Width));
        }
    }

    /// <summary>
    /// How far the left edge of a line sits from the left edge of the layout rectangle.
    /// </summary>
    double IndentOf(bool firstLineOfParagraph)
    {
        return IndentAllLines || firstLineOfParagraph ? Indent : 0;
    }

    /// <summary>
    /// Moves to the top of the next column when the one being filled has run out of room.
    /// Answers false when there is no next column and nowhere left to put the line.
    /// </summary>
    /// <remarks>
    /// A column has to break at the bottom of the rectangle whatever
    /// <see cref="AllowVerticalOverflow"/> says, or there would be no height to break it at and
    /// every column but the first would stay empty. Overflow decides what becomes of the text
    /// after the <em>last</em> column is full.
    /// </remarks>
    bool MoveToNextColumnIfFull(ref int column, ref double y, double rectHeight)
    {
        if (y <= rectHeight)
            return true;

        if (column + 1 < Columns)
        {
            column++;
            y = 0;
            return true;
        }

        return AllowVerticalOverflow;
    }

    void CreateLayout()
    {
        double rectWidth = _layoutRectangle.Width;
        double rectHeight = _layoutRectangle.Height - _cyAscent - _cyDescent;
        double columnWidth = ColumnWidthWithin(rectWidth);
        int firstIndex = 0;
        int column = 0;
        double lineStart = IndentOf(true);
        double x = lineStart, y = 0;
        int count = _blocks.Count;
        for (int idx = 0; idx < count; idx++)
        {
            Block block = _blocks[idx];
            if (block.Type == BlockType.LineBreak)
            {
                if (idx > firstIndex)
                    _blocks[idx - 1].EndsParagraph = true;
                if (Alignment == XParagraphAlignment.Justify)
                    _blocks[firstIndex].Alignment = XParagraphAlignment.Left;
                HorizontalAlignLine(firstIndex, idx - 1, columnWidth);
                firstIndex = idx + 1;
                // A written line break ends a paragraph, so the next line is indented again and
                // the gap between paragraphs falls here.
                lineStart = IndentOf(true);
                x = lineStart;
                y += _lineHeight + LineGap + ParagraphGap;
                if (!MoveToNextColumnIfFull(ref column, ref y, rectHeight))
                {
                    block.Stop = true;
                    break;
                }
            }
            else
            {
                double width = block.Width;
                // A block that starts a line is placed whether it fits or not, since moving it to
                // a line of its own would not make it any narrower.
                if (!LineBreak || x + width <= columnWidth || x == lineStart)
                {
                    block.Location = new XPoint(ColumnLeft(column, columnWidth) + x, y);
                    block.LineIndent = lineStart;
                    block.Column = column;
                    x += width + _spaceWidth;
                }
                else
                {
                    HorizontalAlignLine(firstIndex, idx - 1, columnWidth);

                    // Begin implicit line break
                    firstIndex = idx;
                    lineStart = IndentOf(false);
                    y += _lineHeight + LineGap;
                    if (!MoveToNextColumnIfFull(ref column, ref y, rectHeight))
                    {
                        block.Stop = true;
                        break;
                    }
                    block.Location = new XPoint(ColumnLeft(column, columnWidth) + lineStart, y);
                    block.LineIndent = lineStart;
                    block.Column = column;
                    x = lineStart + width + _spaceWidth;
                }
            }
        }
        if (firstIndex < count && Alignment != XParagraphAlignment.Justify)
            HorizontalAlignLine(firstIndex, count - 1, columnWidth);

        ApplyEllipsis(columnWidth);

        var laidOutBlocks = GetLaidOutBlocks(_blocks).ToArray();
        if (laidOutBlocks.Length == 0)
        {
            _layoutRectangle = new XRect();
            return;
        }

        laidOutBlocks[laidOutBlocks.Length - 1].EndsParagraph = true;

        var minY = laidOutBlocks.Min(b => b.Location.Y);
        var maxY = laidOutBlocks.Max(b => b.Location.Y + _lineHeight);
        var minX = laidOutBlocks.Min(b => b.Location.X);
        var maxX = laidOutBlocks.Max(b => b.Location.X + b.Width);
        _layoutRectangle = new XRect
        {
            X = minX,
            Y = minY,
            Height = maxY - minY,
            Width = maxX - minX
        };
    }

    /// <summary>
    /// Puts the ellipsis on the end of the last line that fitted, when there was text after it
    /// that did not.
    /// </summary>
    /// <remarks>
    /// The text is trimmed a character at a time until it and the ellipsis together fit the room
    /// left on that line. Trimming rather than measuring once, because how much has to come off
    /// depends on how wide the characters that come off are.
    /// </remarks>
    void ApplyEllipsis(double columnWidth)
    {
        if (string.IsNullOrEmpty(Ellipsis) || AllowVerticalOverflow)
            return;

        // Nothing was dropped, so nothing is being stood in for.
        if (!_blocks.Any(block => block.Stop))
            return;

        var laidOut = GetLaidOutBlocks(_blocks).ToArray();
        if (laidOut.Length == 0)
            return;

        Block last = laidOut[laidOut.Length - 1];
        double available = ColumnLeft(last.Column, columnWidth) + columnWidth - last.Location.X;

        string text = last.Text;
        while (text.Length > 0 && _gfx.MeasureString(text + Ellipsis, _font).Width > available)
            text = text.Substring(0, text.Length - 1);

        last.Text = text + Ellipsis;
        last.Width = _gfx.MeasureString(last.Text, _font).Width;
    }

    /// <summary>
    /// Align center, right, or justify.
    /// </summary>
    void HorizontalAlignLine(int firstIndex, int lastIndex, double layoutWidth)
    {
        XParagraphAlignment blockAlignment = _blocks[firstIndex].Alignment;
        if (Alignment == XParagraphAlignment.Left || blockAlignment == XParagraphAlignment.Left)
            return;

        int count = lastIndex - firstIndex + 1;
        if (count == 0)
            return;

        double totalWidth = -_spaceWidth;
        for (int idx = firstIndex; idx <= lastIndex; idx++)
            totalWidth += _blocks[idx].Width + _spaceWidth;

        // An indented line has that much less room to be centred, pushed right or stretched in.
        double dx = Math.Max(layoutWidth - _blocks[firstIndex].LineIndent - totalWidth, 0);
        //Debug.Assert(dx >= 0);
        if (Alignment != XParagraphAlignment.Justify)
        {
            if (Alignment == XParagraphAlignment.Center)
                dx /= 2;
            for (int idx = firstIndex; idx <= lastIndex; idx++)
            {
                Block block = _blocks[idx];
                block.Location += new XSize(dx, 0);
            }
        }
        else if (count > 1) // case: justify
        {
            dx /= count - 1;
            for (int idx = firstIndex + 1, i = 1; idx <= lastIndex; idx++, i++)
            {
                Block block = _blocks[idx];
                block.Location += new XSize(dx * i, 0);
            }
        }
    }

    readonly List<Block> _blocks = new();

    // TODO:
    // - more XStringFormat variations
    // - left and right indent
    // - first line indent
    // - margins and paddings
    // - background color
    // - text background color
    // - border style
    // - hyphens, soft hyphens, hyphenation
    // - kerning
    // - change font, size, text color etc.
    // - underline and strike-out variation
    // - super- and sub-script
    // - ...

    private XStringFormat GetXStringFormat()
    {
        switch (Alignment)
        {
            case XParagraphAlignment.Center:
                return XStringFormats.TopCenter;
            case XParagraphAlignment.Right:
                return XStringFormats.TopRight;
            case XParagraphAlignment.Default:
            case XParagraphAlignment.Justify:
            case XParagraphAlignment.Left:
            default:
                return XStringFormats.TopLeft;
        }
    }
}

public class TextFormatAlignment
{
    public XParagraphAlignment Horizontal { get; set; } = XParagraphAlignment.Left;
    public XVerticalAlignment Vertical { get; set; } = XVerticalAlignment.Top;
}
