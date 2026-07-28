using System;
using System.Collections.Generic;
using System.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;

namespace PdfSharpCore.Test.Helpers
{
    /// <summary>
    ///   Works out where on a page each run of text is drawn, for the tests that care about what
    ///   is laid out where rather than about what it says. Reading the positions out of the
    ///   content is exact, where rasterizing and looking is neither exact nor available on every
    ///   machine.
    /// </summary>
    internal static class TextBaselines
    {
        /// <summary>
        ///   The vertical position of every run of text on the page, in the order it is drawn.
        ///   Positions are in points from the bottom of the page, as PDF measures them.
        /// </summary>
        internal static IReadOnlyList<double> Of(PdfPage page)
        {
            var baselines = new List<double>();

            // The text line matrix, which Td moves and every run of text is drawn against. Only
            // the translation is tracked: nothing here draws text turned or scaled.
            double x = 0, y = 0;

            foreach (var item in ContentReader.ReadContent(ContentOf(page)))
            {
                if (item is not COperator op)
                    continue;

                switch (op.OpCode.OpCodeName)
                {
                    case OpCodeName.BT:
                        x = y = 0;
                        break;

                    case OpCodeName.Td:
                    case OpCodeName.TD:
                        if (op.Operands.Count >= 2)
                        {
                            x += Number(op.Operands[0]);
                            y += Number(op.Operands[1]);
                        }
                        break;

                    case OpCodeName.Tm:
                        if (op.Operands.Count >= 6)
                        {
                            x = Number(op.Operands[4]);
                            y = Number(op.Operands[5]);
                        }
                        break;

                    case OpCodeName.Tj:
                    case OpCodeName.TJ:
                    case OpCodeName.QuoteSingle:
                    case OpCodeName.QuoteDbl:
                        baselines.Add(y);
                        break;
                }
            }

            return baselines;
        }

        /// <summary>
        ///   The distinct lines the page draws text on, from the top of the page downwards.
        /// </summary>
        internal static IReadOnlyList<double> LinesOf(PdfPage page)
        {
            return Of(page)
                .Select(baseline => Math.Round(baseline, 3))
                .Distinct()
                .OrderByDescending(baseline => baseline)
                .ToList();
        }

        static byte[] ContentOf(PdfPage page)
        {
            var item = page.Elements["/Contents"];
            if (item is PdfReference reference)
                item = reference.Value;

            if (item is PdfArray streams)
            {
                var parts = new List<byte[]>();
                for (var idx = 0; idx < streams.Elements.Count; idx++)
                    parts.Add(streams.Elements.GetDictionary(idx).Stream.UnfilteredValue);

                // The streams of a page are one stream broken up, and a token may span the break.
                var joined = new List<byte>();
                foreach (var part in parts)
                {
                    joined.AddRange(part);
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
