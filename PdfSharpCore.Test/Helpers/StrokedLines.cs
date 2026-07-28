using System;
using System.Collections.Generic;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;

namespace PdfSharpCore.Test.Helpers
{
    /// <summary>
    ///   Works out which straight lines a page strokes, for the tests that care about where rules
    ///   and borders are drawn. Reading the segments out of the content is exact, where rasterizing
    ///   and looking is neither exact nor available on every machine.
    /// </summary>
    internal static class StrokedLines
    {
        internal readonly struct Line
        {
            internal Line(double x1, double y1, double x2, double y2, double width)
            {
                X1 = x1;
                Y1 = y1;
                X2 = x2;
                Y2 = y2;
                Width = width;
            }

            internal double X1 { get; }
            internal double Y1 { get; }
            internal double X2 { get; }
            internal double Y2 { get; }
            internal double Width { get; }

            internal bool IsVertical => Math.Abs(X1 - X2) < 0.001;
            internal bool IsHorizontal => Math.Abs(Y1 - Y2) < 0.001;

            /// <summary>The lower of the two ends, which for a vertical line is its foot.</summary>
            internal double Bottom => Math.Min(Y1, Y2);

            /// <summary>The higher of the two ends, which for a vertical line is its head.</summary>
            internal double Top => Math.Max(Y1, Y2);

            public override string ToString()
            {
                var kind = IsVertical ? "V" : IsHorizontal ? "H" : "?";
                return $"{kind} ({X1:F2},{Y1:F2})-({X2:F2},{Y2:F2}) w={Width:F2}";
            }
        }

        /// <summary>
        ///   Every straight segment the page strokes, in the order it is drawn. Positions are in
        ///   points from the bottom left of the page, as PDF measures them.
        /// </summary>
        internal static IReadOnlyList<Line> Of(PdfPage page)
        {
            var lines = new List<Line>();

            // The segments of the path being built. A path is not painted until its operator says
            // how, so they are held here and kept only if that operator strokes.
            var path = new List<Line>();

            // The current point, the point the subpath began at, and the current line width.
            double x = 0, y = 0, startX = 0, startY = 0, width = 1;
            var inSubpath = false;

            // Closing a subpath draws the segment back to where it began.
            void CloseSubpath()
            {
                if (inSubpath && (x != startX || y != startY))
                    path.Add(new Line(x, y, startX, startY, width));

                x = startX;
                y = startY;
            }

            foreach (var item in ContentReader.ReadContent(ContentOf(page)))
            {
                if (item is not COperator op)
                    continue;

                switch (op.OpCode.OpCodeName)
                {
                    case OpCodeName.w:
                        if (op.Operands.Count >= 1)
                            width = Number(op.Operands[0]);
                        break;

                    case OpCodeName.m:
                        if (op.Operands.Count >= 2)
                        {
                            x = startX = Number(op.Operands[0]);
                            y = startY = Number(op.Operands[1]);
                            inSubpath = true;
                        }
                        break;

                    case OpCodeName.l:
                        if (inSubpath && op.Operands.Count >= 2)
                        {
                            var toX = Number(op.Operands[0]);
                            var toY = Number(op.Operands[1]);
                            path.Add(new Line(x, y, toX, toY, width));
                            x = toX;
                            y = toY;
                        }
                        break;

                    case OpCodeName.h:
                        CloseSubpath();
                        break;

                    // Close the path and then stroke it.
                    case OpCodeName.s:
                    case OpCodeName.b:
                    case OpCodeName.bx:
                        CloseSubpath();
                        goto case OpCodeName.S;

                    // Stroke the path, filling it first or not.
                    case OpCodeName.S:
                    case OpCodeName.B:
                    case OpCodeName.Bx:
                        lines.AddRange(path);
                        path.Clear();
                        inSubpath = false;
                        break;

                    // Fill the path, or paint nothing at all: either way nothing is stroked.
                    case OpCodeName.f:
                    case OpCodeName.F:
                    case OpCodeName.fx:
                    case OpCodeName.n:
                        path.Clear();
                        inSubpath = false;
                        break;
                }
            }

            return lines;
        }

        static byte[] ContentOf(PdfPage page)
        {
            var item = page.Elements["/Contents"];
            if (item is PdfReference reference)
                item = reference.Value;

            if (item is PdfArray streams)
            {
                // The streams of a page are one stream broken up, and a token may span the break.
                var joined = new List<byte>();
                for (var idx = 0; idx < streams.Elements.Count; idx++)
                {
                    joined.AddRange(streams.Elements.GetDictionary(idx).Stream.UnfilteredValue);
                    joined.Add((byte)'\n');
                }
                return joined.ToArray();
            }

            return ((PdfDictionary)item).Stream.UnfilteredValue;
        }

        static double Number(CObject operand)
        {
            return operand switch
            {
                CInteger integer => integer.Value,
                CReal real => real.Value,
                _ => 0.0
            };
        }
    }
}
