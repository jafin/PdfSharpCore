using System;
using System.Collections.Generic;
using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;

namespace PdfSharpCore.Pdf.Extraction;

/// <summary>
/// Reads the text back out of a page: what it says and where on the page it says it.
/// </summary>
/// <remarks>
/// <para>
/// The content-stream parser has always been here — <see cref="ContentReader"/> and the operator
/// table in <c>Pdf.Content.Objects</c> — and nothing turned what it produced into text. This walks
/// the operators keeping the text state the specification describes, decodes each show-text operand
/// through the font's <c>/ToUnicode</c> map, and reports where the run landed.
/// </para>
/// <para>
/// <b>One run per show-text operator, not one box per glyph.</b> A per-glyph box is exact only when
/// every glyph's own advance is known, and reporting an approximate one is worse than reporting
/// none: a caller cannot tell the two apart. A run's origin and total width are exact, and are what
/// most callers actually want.
/// </para>
/// <para>
/// <b>Not layout analysis.</b> Runs come back in the order they were drawn, which is the order the
/// producer chose and need not be reading order. Grouping runs into columns, paragraphs and a
/// reading sequence — recursive XY-cut and its relatives — is a separate piece of work and is not
/// here.
/// </para>
/// </remarks>
public static class PdfTextExtractor
{
    /// <summary>
    /// Every run of text on the page, in the order it was drawn.
    /// </summary>
    public static IReadOnlyList<PdfTextRun> ExtractRuns(PdfPage page)
    {
        if (page == null)
            throw new ArgumentNullException(nameof(page));

        var walker = new Walker(page);
        walker.Walk(ContentReader.ReadContent(page));
        return walker.Runs;
    }

    /// <summary>
    /// The text of the page, with a line break wherever the baseline moves.
    /// </summary>
    /// <remarks>
    /// A convenience over <see cref="ExtractRuns"/> and no cleverer than it: runs sharing a
    /// baseline are joined with a space when there is a gap between them and directly when there is
    /// not, and a new baseline starts a new line. That is right for ordinary running text and wrong
    /// for a two-column page, which is what the note about layout analysis above is warning of.
    /// </remarks>
    public static string ExtractText(PdfPage page)
    {
        var text = new StringBuilder();
        double? baseline = null;
        var endOfPrevious = 0.0;

        foreach (var run in ExtractRuns(page))
        {
            if (run.Text.Length == 0)
                continue;

            if (baseline == null || Math.Abs(run.Origin.Y - baseline.Value) > 0.5)
            {
                if (baseline != null)
                    text.Append('\n');
                baseline = run.Origin.Y;
            }
            else if (run.Origin.X - endOfPrevious > run.FontSize * 0.2)
            {
                // A gap wider than a fifth of the type size is a space somebody drew by moving the
                // pen rather than by showing one.
                text.Append(' ');
            }

            text.Append(run.Text);
            endOfPrevious = run.Origin.X + run.Width;
        }

        return text.ToString();
    }

    /// <summary>
    /// Walks a content stream keeping the graphics and text state, and records what was shown.
    /// </summary>
    sealed class Walker
    {
        readonly PdfPage _page;
        readonly Dictionary<string, FontInfo> _fonts = new();
        readonly Stack<XMatrix> _graphicsStack = new();

        XMatrix _ctm = XMatrix.Identity;
        XMatrix _textMatrix = XMatrix.Identity;
        XMatrix _lineMatrix = XMatrix.Identity;

        FontInfo _font;
        string _fontName;
        double _fontSize;
        double _charSpacing;
        double _wordSpacing;
        double _horizontalScale = 1;
        double _leading;
        double _rise;
        int _renderMode;

        public Walker(PdfPage page) => _page = page;

        public List<PdfTextRun> Runs { get; } = new();

        /// <remarks>
        /// Indexed rather than iterated. <see cref="CSequence"/> implements
        /// <c>IEnumerable&lt;CObject&gt;</c> and its generic enumerator throws
        /// <see cref="NotImplementedException"/>, so a foreach over one compiles and then fails at
        /// run time.
        /// </remarks>
        public void Walk(CSequence content)
        {
            for (var index = 0; index < content.Count; index++)
            {
                if (content[index] is COperator op)
                    Execute(op);
            }
        }

        void Execute(COperator op)
        {
            switch (op.OpCode.OpCodeName)
            {
                case OpCodeName.q:
                    _graphicsStack.Push(_ctm);
                    break;

                case OpCodeName.Q:
                    if (_graphicsStack.Count > 0)
                        _ctm = _graphicsStack.Pop();
                    break;

                case OpCodeName.cm:
                    _ctm = Multiply(MatrixOf(op, 0), _ctm);
                    break;

                case OpCodeName.BT:
                    _textMatrix = _lineMatrix = XMatrix.Identity;
                    break;

                case OpCodeName.Tf:
                    SelectFont(op);
                    break;

                case OpCodeName.Td:
                    Displace(Number(op, 0), Number(op, 1));
                    break;

                case OpCodeName.TD:
                    _leading = -Number(op, 1);
                    Displace(Number(op, 0), Number(op, 1));
                    break;

                case OpCodeName.Tm:
                    _textMatrix = _lineMatrix = MatrixOf(op, 0);
                    break;

                case OpCodeName.Tx:   // T*, next line
                    Displace(0, -_leading);
                    break;

                case OpCodeName.TL:
                    _leading = Number(op, 0);
                    break;

                case OpCodeName.Tc:
                    _charSpacing = Number(op, 0);
                    break;

                case OpCodeName.Tw:
                    _wordSpacing = Number(op, 0);
                    break;

                case OpCodeName.Tz:
                    _horizontalScale = Number(op, 0) / 100.0;
                    break;

                case OpCodeName.Ts:
                    _rise = Number(op, 0);
                    break;

                case OpCodeName.Tr:
                    _renderMode = (int)Number(op, 0);
                    break;

                case OpCodeName.Tj:
                    Show(op.Operands);
                    break;

                case OpCodeName.TJ:
                    Show(op.Operands.Count > 0 && op.Operands[0] is CArray array ? array : op.Operands);
                    break;

                case OpCodeName.QuoteSingle:
                    Displace(0, -_leading);
                    Show(op.Operands);
                    break;

                case OpCodeName.QuoteDbl:
                    // " sets word and character spacing before showing, and moves to the next line.
                    _wordSpacing = Number(op, 0);
                    _charSpacing = Number(op, 1);
                    Displace(0, -_leading);
                    Show(op.Operands);
                    break;
            }
        }

        void SelectFont(COperator op)
        {
            _fontSize = Number(op, 1);
            _fontName = op.Operands.Count > 0 && op.Operands[0] is CName name ? name.Name : null;
            _font = _fontName == null ? null : FontFor(_fontName);
        }

        void Displace(double tx, double ty)
        {
            _lineMatrix = Multiply(new XMatrix(1, 0, 0, 1, tx, ty), _lineMatrix);
            _textMatrix = _lineMatrix;
        }

        /// <summary>
        /// Shows a string, or the mixture of strings and kerning adjustments a <c>TJ</c> array is.
        /// </summary>
        void Show(CSequence operands)
        {
            // Text render mode 3 is invisible — the OCR layer under a scan is drawn that way, and a
            // caller asking what the page says usually does not want it twice. It is skipped rather
            // than reported, which is a decision this class makes and does not expose; the day
            // somebody needs it, it becomes an option.
            if (_font == null || _renderMode == 3)
                return;

            var text = new StringBuilder();
            var advance = 0.0;
            var origin = OriginOfCurrentPoint();

            for (var index = 0; index < operands.Count; index++)
            {
                switch (operands[index])
                {
                    case CString shown:
                        advance += Append(text, shown.Value);
                        break;

                    case CInteger kerning:
                        advance += -kerning.Value / 1000.0 * _fontSize * _horizontalScale;
                        break;

                    case CReal kerning:
                        advance += -kerning.Value / 1000.0 * _fontSize * _horizontalScale;
                        break;
                }
            }

            if (text.Length > 0)
            {
                Runs.Add(new PdfTextRun(text.ToString(), origin, advance * ScaleOf(_textMatrix),
                    _fontSize * ScaleOf(Multiply(_textMatrix, _ctm)), _fontName));
            }

            _textMatrix = Multiply(new XMatrix(1, 0, 0, 1, advance, 0), _textMatrix);
        }

        /// <summary>
        /// Decodes one shown string and answers how far it advances the pen.
        /// </summary>
        double Append(StringBuilder text, string shown)
        {
            var advance = 0.0;
            var codeLength = _font.CodeLength;

            for (var at = 0; at + codeLength <= shown.Length; at += codeLength)
            {
                var code = 0;
                for (var b = 0; b < codeLength; b++)
                    code = (code << 8) | (shown[at + b] & 0xFF);

                text.Append(_font.TextFor(code));

                // The advance of a glyph, then the spacing the state adds after it. Word spacing
                // applies to the single byte 32 and to nothing else — not to a two-byte code whose
                // low byte happens to be 32, which is the trap in this arithmetic.
                var width = _font.WidthOf(code) * _fontSize + _charSpacing;
                if (code == 32 && codeLength == 1)
                    width += _wordSpacing;

                advance += width * _horizontalScale;
            }

            return advance;
        }

        XPoint OriginOfCurrentPoint() =>
            Multiply(_textMatrix, _ctm).Transform(new XPoint(0, _rise));

        FontInfo FontFor(string name)
        {
            if (_fonts.TryGetValue(name, out var known))
                return known;

            var fonts = _page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font")
                        ?? _page.Resources?.Elements.GetDictionary("/Font");
            var dictionary = fonts?.Elements.GetDictionary(name);

            var info = dictionary == null ? null : FontInfo.From(dictionary);
            _fonts[name] = info;
            return info;
        }

        static XMatrix Multiply(XMatrix first, XMatrix second) => XMatrix.Multiply(first, second);

        /// <summary>
        /// How much a matrix scales along its own X axis, which is what turns a width in text space
        /// into one in user space.
        /// </summary>
        static double ScaleOf(XMatrix matrix) =>
            Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);

        static XMatrix MatrixOf(COperator op, int from) => new(
            Number(op, from), Number(op, from + 1), Number(op, from + 2),
            Number(op, from + 3), Number(op, from + 4), Number(op, from + 5));

        static double Number(COperator op, int index) =>
            index < op.Operands.Count && op.Operands[index] is CNumber number
                ? (number is CInteger integer ? integer.Value : ((CReal)number).Value)
                : 0;
    }
}
