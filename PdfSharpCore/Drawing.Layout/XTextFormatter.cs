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
            _cyCapHeight = CapHeightOf(_font);

            // HACK in XTextFormatter
            _spaceWidth = _gfx.MeasureString("x x", value).Width;
            _spaceWidth -= _gfx.MeasureString("xx", value).Width;
        }
    }
    XFont _font;
    double _lineSpace;
    double _cyAscent;
    double _cyDescent;
    double _cyCapHeight;
    double _spaceWidth;
    double _lineHeight;

    /// <summary>
    /// How tall a capital letter stands above the baseline in <paramref name="font"/>, in points.
    /// </summary>
    /// <remarks>
    /// This, and not the ascent, is where the eye reads the top of a line of text as being. The
    /// ascent is higher by the room the face keeps for accents and for the tall lowercase letters,
    /// which is empty above a capital.
    /// <para>
    /// A face whose OS/2 table is older than <c>sCapHeight</c>, or fills it in with nonsense, gets
    /// the ascent instead - which is what the font descriptor already substitutes, and what this
    /// code used before there was a better number to ask for.
    /// </para>
    /// </remarks>
    static double CapHeightOf(XFont font)
    {
        double lineSpace = font.GetHeight();
        double ascent = lineSpace * font.CellAscent / font.CellSpace;
        double capHeight = lineSpace * font.Metrics.CapHeight / font.CellSpace;

        return capHeight > 0 && capHeight <= ascent ? capHeight : ascent;
    }

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
    /// Gets or sets an initial letter set into the opening lines of the text. Null, the default,
    /// draws no cap and leaves every line at its full measure.
    /// </summary>
    /// <remarks>
    /// The cap consumes the first character of the text and nothing else. The remainder is laid
    /// out in full, beside the cap for as many lines as it is deep and at the full measure after
    /// that.
    /// </remarks>
    public XDropCap DropCap { get; set; }

    /// <summary>
    /// What a drop cap has been worked out to be for the text and layout in hand: the room it
    /// reserves, the font it is drawn in, and how far its ink sits from the pen.
    /// </summary>
    sealed class DropCapMetrics
    {
        internal string Character;
        internal XFont Font;

        /// <summary>The room reserved beside the cap, from the top left of the first column.</summary>
        internal XRect Reserved;

        /// <summary>
        /// How far the glyph's ink begins to the right of where the pen would be put. Subtracted
        /// when the cap is drawn, so that its ink and not its advance sits flush with the margin.
        /// </summary>
        internal double InkOffset;

        /// <summary>Where the cap's foot goes, measured down from the top of the layout.</summary>
        internal double Baseline;
    }

    DropCapMetrics _dropCap;

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

        // The cap takes the first character and the layout takes the rest, so the letter appears
        // once rather than twice. Worked out before the blocks are made, because it decides both
        // what text there is to break and how wide the opening lines may be.
        _dropCap = MeasureDropCap(text);
        if (_dropCap != null)
            _text = text.Substring(1);

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
            DrawDropCap(brush, dx, dy);

            foreach (var line in lines)
            {
                var lineBlocks = line as Block[] ?? line.ToArray();
                var lineY = dy + lineBlocks.First().Location.Y;
                var indent = lineBlocks.First().LineIndent;

                // A line is laid out and aligned within its own column, so that is the left edge it
                // is measured from.
                var columnLeft = dx + ColumnLeft(lineBlocks.First().Column, columnWidth);

                // ...and against the measure that line was broken to, which is the column's width
                // until something narrows it. Aligning to the column while breaking to something
                // narrower is what makes justified text ragged in a way no assertion on the text
                // itself would notice.
                var lineWidth = lineBlocks.First().LineWidth;

                // The last line of a paragraph keeps its natural width. Stretching it over the
                // full width of the column would tear the few words it holds apart.
                if (Alignment == XParagraphAlignment.Justify && !lineBlocks[lineBlocks.Length - 1].EndsParagraph)
                {
                    var locationX = columnLeft + indent;
                    var gaps = lineBlocks.Length - 1;
                    var gapSize = gaps > 0
                        ? (lineWidth - indent - lineBlocks.Select(l => l.Width).Sum()) / gaps
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
                        locationX = columnLeft + indent + (lineWidth - indent) / 2;
                    if (Alignment == XParagraphAlignment.Right)
                        locationX = columnLeft + lineWidth;
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

    /// <summary>
    /// Draws the initial letter, with its foot on the baseline of the last line it is set into and
    /// its ink flush with the left edge of the text.
    /// </summary>
    /// <remarks>
    /// Flush by ink rather than by pen: the left side bearing is space the font leaves so that a
    /// letter does not touch the one before it, and a display letter has nothing before it. Set by
    /// the pen it looks indented by an amount that grows with the size of the cap, which is exactly
    /// the size at which the eye notices.
    /// </remarks>
    void DrawDropCap(XBrush brush, double dx, double dy)
    {
        if (_dropCap == null)
            return;

        _gfx.DrawString(_dropCap.Character, _dropCap.Font, brush,
            dx - _dropCap.InkOffset, dy + _dropCap.Baseline, XStringFormats.BaseLineLeft);
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
    /// Where a line begins and how wide it may be, within its column.
    /// </summary>
    /// <remarks>
    /// Both are measured from the left edge of the column the line sits in, so a line running the
    /// full measure has <see cref="Start"/> equal to its indent and <see cref="Width"/> equal to
    /// the column's width.
    /// </remarks>
    readonly struct LineMeasure
    {
        internal LineMeasure(double start, double width)
            : this(start, width, blocked: false, clearsAt: 0)
        {
        }

        LineMeasure(double start, double width, bool blocked, double clearsAt)
        {
            Start = start;
            Width = width;
            IsBlocked = blocked;
            ClearsAt = clearsAt;
        }

        /// <summary>
        /// A band with no room in it at all, and how far down the thing standing in it reaches.
        /// </summary>
        internal static LineMeasure Blocked(double start, double width, double clearsAt)
        {
            return new LineMeasure(start, width, blocked: true, clearsAt);
        }

        /// <summary>How far in from the left edge of the column the line's first block goes.</summary>
        internal double Start { get; }

        /// <summary>
        /// How far from the left edge of the column the line may reach - the position of its right
        /// limit, not the room between the two. The room is <c>Width - Start</c>.
        /// </summary>
        internal double Width { get; }

        /// <summary>
        /// Whether the band this was measured for has no room for text at all, which is a
        /// different answer from a narrow measure and cannot be expressed as one.
        /// </summary>
        /// <remarks>
        /// Without it the loop has only "here is your measure" to say, and a measure that starts
        /// at or past its own limit reads to the loop as a very narrow line rather than as no
        /// line - so the loop puts a word on it anyway, outside the column.
        /// </remarks>
        internal bool IsBlocked { get; }

        /// <summary>
        /// The line top at which whatever blocks this band stops blocking it. Meaningless unless
        /// <see cref="IsBlocked"/>.
        /// </summary>
        internal double ClearsAt { get; }
    }

    /// <summary>
    /// How much room a line has to have before anything is put on it.
    /// </summary>
    /// <remarks>
    /// Zero, give or take what arithmetic on doubles leaves behind. A merely narrow line is not
    /// this: a word wider than its measure is placed anyway and always has been, and a threshold
    /// that turned "narrow" into "no room" would move text that is pinned where it is.
    /// </remarks>
    const double MinimumRoom = 1e-6;

    /// <summary>
    /// The measure available to a line, given how far down its column the line's top sits.
    /// </summary>
    /// <remarks>
    /// This is the one thing a drop cap needs that the formatter did not have: until it existed,
    /// the width was computed once for the whole layout and the start varied by exactly one bit -
    /// first line of a paragraph, or not. Everything else the loop does is already expressed in
    /// terms of those two, so a measure that depends on the line's position is the whole of what
    /// makes text flow around something.
    /// <para>
    /// With nothing reserved it returns the same pair for every line, and the loop behaves exactly
    /// as it did. That is what makes "unchanged when unused" a property of the code rather than a
    /// hope; <c>FormatterLayoutPinTests</c> is what checks it.
    /// </para>
    /// </remarks>
    LineMeasure MeasureOfLineAt(double yTop, bool firstLineOfParagraph, double columnWidth, int column)
    {
        double start = IndentOf(firstLineOfParagraph);

        // Only the first column: a cap belongs to the opening of the text, and reserving the same
        // corner of every column would carve a hole out of each of them.
        if (_dropCap != null && column == 0)
        {
            // By the line's box and not by its baseline. A line whose baseline falls below the
            // reserved depth still has ascenders inside it, and they would collide with the cap.
            bool overlaps = yTop < _dropCap.Reserved.Bottom && yTop + _lineHeight > _dropCap.Reserved.Top;
            if (overlaps)
            {
                // The start moves right and the width does not move at all. Both are measured from
                // the left edge of the column, so the width is where the line may reach rather than
                // how much room it has - which is what makes a reservation on the left a change to
                // one number. A reservation on the *right*, which is what a shape wrapped on its
                // left side would want, is the other one.
                double besideTheCap = start + _dropCap.Reserved.Width;

                // A cap can be wider than the column it is set in - it is scaled to its depth and
                // nothing holds its width to the measure - and then there is no line beside it to
                // narrow, only the lines below it.
                if (besideTheCap >= columnWidth - MinimumRoom)
                    return LineMeasure.Blocked(besideTheCap, columnWidth, _dropCap.Reserved.Bottom);

                return new LineMeasure(besideTheCap, columnWidth);
            }
        }

        return new LineMeasure(start, columnWidth);
    }

    /// <summary>
    /// Works out how big the cap has to be, how much room it takes and where its foot goes, for
    /// the text and line height in hand. Answers null when there is no cap to draw.
    /// </summary>
    /// <remarks>
    /// The cap is scaled so that its ink spans from the cap height of the first line to the
    /// baseline of the last line it is set into, which is what makes it look set <i>into</i> the
    /// text rather than floating above it.
    /// </remarks>
    DropCapMetrics MeasureDropCap(string text)
    {
        if (DropCap == null || string.IsNullOrEmpty(text))
            return null;

        string character = text.Substring(0, 1);
        if (char.IsWhiteSpace(character[0]))
            return null;

        // The foot of the cap goes on the baseline of the last line it is set into. Lines are
        // placed by the top of their box and the baseline sits an ascent below that.
        double baseline = (DropCap.Lines - 1) * (_lineHeight + LineGap) + _cyAscent;

        // The head goes on the cap height of the first line - the top of the letter standing
        // beside it, not the top of the box that letter is set in. The two differ by the room the
        // face keeps above a capital for accents, and a cap hung from the box stands that much
        // clear of its neighbour: about a fifth of an em in Liberation Sans, which at the size a
        // cap is set to is a gap nobody has to measure to see.
        double depth = baseline - (_cyAscent - _cyCapHeight);
        if (depth <= 0)
            return null;

        // Measured at a probe size and scaled, because what has to span the depth is the height of
        // this glyph's ink - the font's own metrics describe the whole face, and a cap letter fills
        // neither the ascent nor the em box exactly.
        const double probe = 100;
        XFont probeFont = new XFont(DropCap.Font.FontFamily.Name, probe, DropCap.Font.Style);

        XRect probeInk = InkOf(character, probeFont);
        if (probeInk.Height <= 0 || probeInk.Width <= 0)
            return null;

        double size = probe * depth / probeInk.Height;
        XFont capFont = new XFont(DropCap.Font.FontFamily.Name, size, DropCap.Font.Style);
        XRect ink = InkOf(character, capFont);

        double gutter = DropCap.Gutter ?? _spaceWidth;

        return new DropCapMetrics
        {
            Character = character,
            Font = capFont,
            InkOffset = ink.X,
            Baseline = baseline,
            Reserved = new XRect(0, 0, ink.Width + gutter,
                (DropCap.Lines - 1) * (_lineHeight + LineGap) + _lineHeight),
        };
    }

    /// <summary>
    /// The box a string's ink occupies, relative to the pen and with y measured down from the
    /// baseline.
    /// </summary>
    /// <remarks>
    /// Taken from the glyph's outlines where <see cref="Fonts.GlobalFontSettings.GlyphOutlineProvider"/>
    /// is registered, because a letter's advance includes side bearings - space the letter does not
    /// occupy - and a display letter set flush by its advance looks indented. Where no provider is
    /// registered the advance is all there is, and the small inset is accepted: a drop cap that
    /// demanded a backend seam would be a worse failure than the arithmetic it replaces.
    /// <para>
    /// The bounds come from the segment endpoints alone. Fonts place points at the extremes of
    /// their curves by convention, so those are the extremes; the control points of a curve lie
    /// outside the ink it draws.
    /// </para>
    /// </remarks>
    XRect InkOf(string text, XFont font)
    {
        Fonts.IGlyphOutlineProvider provider = OutlineProviderOrNull();
        if (provider == null)
        {
            // The advance across, and the cap height down: the nearest the font's own metrics come
            // to the box a capital's ink fills. The ascent would be the whole box the letter is set
            // in, which is taller than the letter by the room kept above it, and scaling that box
            // to the cap's depth sets the letter itself short of the depth by the same amount.
            XSize measured = _gfx.MeasureString(text, font);
            double capHeight = CapHeightOf(font);
            return new XRect(0, -capHeight, measured.Width, capHeight);
        }

        double left = double.MaxValue, right = double.MinValue;
        double top = double.MinValue, bottom = double.MaxValue;

        foreach (Fonts.XGlyphOutline outline in provider.GetOutlines(text, font.FontFamily.Name,
                     (font.Style & XFontStyle.Bold) != 0, (font.Style & XFontStyle.Italic) != 0, font.Size))
        {
            foreach (Fonts.XGlyphSegment segment in outline.Segments)
            {
                if (segment.Kind == Fonts.XGlyphSegmentKind.Close)
                    continue;

                left = Math.Min(left, segment.End.X);
                right = Math.Max(right, segment.End.X);
                top = Math.Max(top, segment.End.Y);
                bottom = Math.Min(bottom, segment.End.Y);
            }
        }

        if (left > right)
            return new XRect();

        // The outlines measure y upwards from the baseline; the layout measures it down.
        return new XRect(left, -top, right - left, top - bottom);
    }

    /// <summary>
    /// The registered glyph outline provider, or null where there is none. Reading the property
    /// throws when it is unset, which is right for a caller who asked for outlines and wrong here:
    /// a drop cap without a provider is drawn by advance rather than refused.
    /// </summary>
    static Fonts.IGlyphOutlineProvider OutlineProviderOrNull()
    {
        try
        {
            return Fonts.GlobalFontSettings.GlyphOutlineProvider;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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

    /// <summary>
    /// Measures the line at <paramref name="y"/>, moving down past anything that leaves its band
    /// no room at all and on to the next column where that runs out of one. Answers false when
    /// there is nowhere left to put the line.
    /// </summary>
    /// <remarks>
    /// With nothing in the way this measures once and returns, which is what the loop did before
    /// there was anything to be in the way.
    /// <para>
    /// Where there is something, the skip goes to the foot of it rather than down a line at a
    /// time, so how many measures it takes to get clear is a fact about the obstruction rather
    /// than about the line height. The floor of one line under that is what guarantees the loop
    /// ends: a foot level with the band it blocks would otherwise be asked about for ever, and
    /// with the floor the worst case is the line-at-a-time advance this replaces.
    /// </para>
    /// </remarks>
    bool MeasureLineWithRoom(bool firstLineOfParagraph, double columnWidth, double rectHeight,
        ref LineMeasure measure, ref int column, ref double y)
    {
        measure = MeasureOfLineAt(y, firstLineOfParagraph, columnWidth, column);
        while (measure.IsBlocked)
        {
            y = Math.Max(measure.ClearsAt, y + _lineHeight + LineGap);
            if (!MoveToNextColumnIfFull(ref column, ref y, rectHeight))
                return false;

            measure = MeasureOfLineAt(y, firstLineOfParagraph, columnWidth, column);
        }

        return true;
    }

    void CreateLayout()
    {
        double rectWidth = _layoutRectangle.Width;
        double rectHeight = _layoutRectangle.Height - _cyAscent - _cyDescent;
        double columnWidth = ColumnWidthWithin(rectWidth);
        int firstIndex = 0;
        int column = 0;

        // The measure of the line being filled. Re-asked for at every line, because a drop cap -
        // or anything else the text has to flow beside - makes the answer depend on how far down
        // the column the line sits.
        LineMeasure measure = default;
        double y = 0;
        int count = _blocks.Count;

        // Asked before the first block rather than at it: a first band with no room in it is
        // answered by starting the text further down, and there is no text placed yet to move.
        // Where that runs out of layout there is nowhere for any of the text to go.
        int placeable = MeasureLineWithRoom(true, columnWidth, rectHeight, ref measure, ref column, ref y)
            ? count
            : 0;
        if (placeable == 0 && count > 0)
            _blocks[0].Stop = true;

        double lineStart = measure.Start;
        double x = lineStart;
        for (int idx = 0; idx < placeable; idx++)
        {
            Block block = _blocks[idx];
            if (block.Type == BlockType.LineBreak)
            {
                if (idx > firstIndex)
                    _blocks[idx - 1].EndsParagraph = true;
                if (Alignment == XParagraphAlignment.Justify)
                    _blocks[firstIndex].Alignment = XParagraphAlignment.Left;
                HorizontalAlignLine(firstIndex, idx - 1, measure.Width);
                firstIndex = idx + 1;
                // A written line break ends a paragraph, so the next line is indented again and
                // the gap between paragraphs falls here.
                y += _lineHeight + LineGap + ParagraphGap;
                // After the column move, not before: a line carried to the top of the next column
                // is a line somewhere else, and its measure is whatever is free there.
                if (!MoveToNextColumnIfFull(ref column, ref y, rectHeight)
                    || !MeasureLineWithRoom(true, columnWidth, rectHeight, ref measure, ref column, ref y))
                {
                    block.Stop = true;
                    break;
                }
                lineStart = measure.Start;
                x = lineStart;
            }
            else
            {
                double width = block.Width;
                // A block that starts a line is placed whether it fits or not, since moving it to
                // a line of its own would not make it any narrower.
                if (!LineBreak || x + width <= measure.Width || x == lineStart)
                {
                    block.Location = new XPoint(ColumnLeft(column, columnWidth) + x, y);
                    block.LineIndent = lineStart;
                    block.LineWidth = measure.Width;
                    block.Column = column;
                    x += width + _spaceWidth;
                }
                else
                {
                    HorizontalAlignLine(firstIndex, idx - 1, measure.Width);

                    // Begin implicit line break
                    firstIndex = idx;
                    y += _lineHeight + LineGap;
                    if (!MoveToNextColumnIfFull(ref column, ref y, rectHeight)
                        || !MeasureLineWithRoom(false, columnWidth, rectHeight, ref measure, ref column, ref y))
                    {
                        block.Stop = true;
                        break;
                    }
                    lineStart = measure.Start;
                    block.Location = new XPoint(ColumnLeft(column, columnWidth) + lineStart, y);
                    block.LineIndent = lineStart;
                    block.LineWidth = measure.Width;
                    block.Column = column;
                    x = lineStart + width + _spaceWidth;
                }
            }
        }
        if (firstIndex < placeable && Alignment != XParagraphAlignment.Justify)
            HorizontalAlignLine(firstIndex, placeable - 1, measure.Width);

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

        // To the right edge of the line rather than of the column. A truncated line that falls
        // inside a drop cap's depth has less room than the column does, and measuring against the
        // column would let the ellipsis run past the edge the line was broken to.
        double available = ColumnLeft(last.Column, columnWidth) + last.LineWidth - last.Location.X;

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
