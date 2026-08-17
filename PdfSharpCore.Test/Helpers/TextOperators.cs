using System.Collections.Generic;
using System.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;

namespace PdfSharpCore.Test.Helpers;

/// <summary>
///   Reads the text operators back out of a page's content stream, for the tests that care about
///   what the renderer wrote rather than about where the result lands on the page.
/// </summary>
/// <remarks>
///   The text state operators - Tc, Tw, Tz - are not observable by rasterizing a page and looking
///   at it, at least not without measuring glyph positions to a precision Ghostscript does not
///   promise. Reading them out of the content is exact, and says which of them were written at
///   all, which is half of what these tests are about.
/// </remarks>
internal static class TextOperators
{
    /// <summary>
    ///   The number every occurrence of a one-operand operator was given, in the order they were
    ///   written. Tc, Tw and Tz each take exactly one.
    /// </summary>
    internal static IReadOnlyList<double> NumbersGivenTo(PdfPage page, OpCodeName opCode)
    {
        return Operators(page)
            .Where(op => op.OpCode.OpCodeName == opCode && op.Operands.Count == 1)
            .Select(op => Number(op.Operands[0]))
            .ToList();
    }

    /// <summary>
    ///   The strings shown on the page, in the order they were drawn.
    /// </summary>
    /// <remarks>
    ///   Readable only for a font encoded as WinAnsi, which writes its text as a string literal.
    ///   A font embedded as Identity-H writes glyph identifiers instead, and what comes back is
    ///   the numbers of the glyphs rather than the characters they stand for.
    /// </remarks>
    internal static IReadOnlyList<string> ShownStrings(PdfPage page)
    {
        return Operators(page)
            .Where(op => op.OpCode.OpCodeName is OpCodeName.Tj or OpCodeName.TJ)
            .SelectMany(op => ItemsOf(op.Operands)
                .SelectMany(operand => operand is CArray array ? ItemsOf(array) : new[] { operand })
                .OfType<CString>())
            .Select(text => text.Value)
            .ToList();
    }

    /// <summary>
    ///   Every operand list an operator was given, in the order they were written.
    /// </summary>
    internal static IReadOnlyList<double[]> OperandsGivenTo(PdfPage page, OpCodeName opCode)
    {
        return Operators(page)
            .Where(op => op.OpCode.OpCodeName == opCode)
            .Select(op => ItemsOf(op.Operands).Select(Number).ToArray())
            .ToList();
    }

    /// <summary>
    ///   How far each Tm on the page leans - its M21 component - in the order they were written.
    ///   Zero for an upright text matrix.
    /// </summary>
    internal static IReadOnlyList<double> TextMatrixSkews(PdfPage page)
    {
        return Operators(page)
            .Where(op => op.OpCode.OpCodeName == OpCodeName.Tm && op.Operands.Count == 6)
            .Select(op => Number(op.Operands[2]))
            .ToList();
    }

    /// <summary>
    ///   The offset each Td on the page was given, in order.
    /// </summary>
    internal static IReadOnlyList<(double X, double Y)> TdOffsets(PdfPage page)
    {
        return Operators(page)
            .Where(op => op.OpCode.OpCodeName == OpCodeName.Td && op.Operands.Count == 2)
            .Select(op => (Number(op.Operands[0]), Number(op.Operands[1])))
            .ToList();
    }

    /// <summary>
    ///   How many times an operator appears on the page.
    /// </summary>
    internal static int CountOf(PdfPage page, OpCodeName opCode)
    {
        return Operators(page).Count(op => op.OpCode.OpCodeName == opCode);
    }

    /// <summary>
    ///   The show-text operators used, in order: Tj for a run drawn in one go, TJ for one whose
    ///   parts are moved apart individually.
    /// </summary>
    internal static IReadOnlyList<OpCodeName> ShowTextOperators(PdfPage page)
    {
        return Operators(page)
            .Select(op => op.OpCode.OpCodeName)
            .Where(name => name is OpCodeName.Tj or OpCodeName.TJ)
            .ToList();
    }

    /// <summary>
    ///   The numbers inside every TJ array on the page - the amounts the pen is moved by between
    ///   the runs of glyphs, in negated thousandths of the font size.
    /// </summary>
    internal static IReadOnlyList<double> TJAdjustments(PdfPage page)
    {
        return TJArrays(page)
            .SelectMany(array => ItemsOf(array).OfType<CNumber>())
            .Select(Number)
            .ToList();
    }

    /// <summary>
    ///   How many separate runs of glyphs each TJ array on the page is broken into.
    /// </summary>
    internal static IReadOnlyList<int> TJRunCounts(PdfPage page)
    {
        return TJArrays(page)
            .Select(array => ItemsOf(array).OfType<CString>().Count())
            .ToList();
    }

    /// <summary>
    ///   The strings shown on the page, in the order a reader sees them: down the page, and left to
    ///   right along a line.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     Not the same list as <see cref="ShownStrings"/>, which is in the order the renderer wrote
    ///     them. The two differ exactly when something is drawn somewhere other than after the thing
    ///     before it - which is what laying a right-to-left line out in the order it is read amounts
    ///     to, and is the whole of what a test of that wants to see.
    ///   </para>
    ///   <para>
    ///     The position is accumulated from the text-positioning operators: <c>Tm</c> sets it
    ///     outright and <c>Td</c> moves it, both relative to the start of the text object, which is
    ///     enough to order what is on one page against itself.
    ///   </para>
    /// </remarks>
    internal static IReadOnlyList<string> ShownAcrossThePage(PdfPage page)
    {
        var shown = new List<(double X, double Y, int Written, string Text)>();
        double x = 0, y = 0;

        foreach (var op in Operators(page))
        {
            var operands = ItemsOf(op.Operands);
            switch (op.OpCode.OpCodeName)
            {
                case OpCodeName.BT:
                    x = y = 0;
                    break;

                case OpCodeName.Tm when operands.Count == 6:
                    x = Number(operands[4]);
                    y = Number(operands[5]);
                    break;

                case OpCodeName.Td when operands.Count == 2:
                case OpCodeName.TD when operands.Count == 2:
                    x += Number(operands[0]);
                    y += Number(operands[1]);
                    break;

                case OpCodeName.Tj:
                case OpCodeName.TJ:
                    foreach (var text in operands
                                 .SelectMany(o => o is CArray array ? ItemsOf(array) : new[] { o })
                                 .OfType<CString>())
                    {
                        shown.Add((x, y, shown.Count, text.Value));
                    }
                    break;
            }
        }

        return shown
            .OrderByDescending(run => run.Y)
            .ThenBy(run => run.X)
            .ThenBy(run => run.Written)
            .Select(run => run.Text)
            .ToList();
    }

    static IEnumerable<CArray> TJArrays(PdfPage page)
    {
        return Operators(page)
            .Where(op => op.OpCode.OpCodeName == OpCodeName.TJ)
            .SelectMany(op => ItemsOf(op.Operands).OfType<CArray>());
    }

    static IEnumerable<COperator> Operators(PdfPage page)
    {
        return ItemsOf(ContentReader.ReadContent(PageContent.Of(page))).OfType<COperator>();
    }

    /// <summary>
    ///   The items of a sequence as a plain list.
    /// </summary>
    /// <remarks>
    ///   CSequence implements IList&lt;CObject&gt;, but its explicit
    ///   IEnumerable&lt;CObject&gt;.GetEnumerator throws NotImplementedException, so LINQ - which
    ///   asks for exactly that one - cannot be pointed at a sequence directly. Its public
    ///   GetEnumerator, the one foreach binds to, works.
    /// </remarks>
    static IReadOnlyList<CObject> ItemsOf(CSequence sequence)
    {
        var items = new List<CObject>();
        foreach (CObject item in sequence)
            items.Add(item);
        return items;
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
