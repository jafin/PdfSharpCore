using System;
using System.Collections.Generic;

namespace PdfSharpCore.Drawing.Layout;

/// <summary>
/// The room left on a line by the things standing in it.
/// </summary>
/// <remarks>
/// <para>
/// This is the one piece of arithmetic the two layout engines in this solution genuinely share.
/// MigraDoc's <c>ObstructedArea</c> asks it once per line to place a paragraph beside a floating
/// shape; <see cref="XTextFormatter"/> asks it once per line to place text beside a reserved
/// region. Everything else about the two is different - MigraDoc's obstacles are page-absolute and
/// the formatter's are relative to the block, and that difference is real rather than incidental,
/// so gathering the obstacles and deciding which of them stand in a band stays with each engine.
/// </para>
/// <para>
/// It takes and answers in plain doubles for that reason. A signature mentioning
/// <c>Rectangle</c>, <see cref="XRect"/> or <see cref="XUnit"/> would belong to one engine and have
/// to be converted by the other; a pair of coordinates belongs to neither.
/// </para>
/// <para>
/// Public because <c>MigraDocCore.Rendering</c> is a different assembly and this repository
/// deliberately carries no <c>InternalsVisibleTo</c>. Pure and stateless, so being public costs
/// little.
/// </para>
/// </remarks>
public static class LineSpans
{
    /// <summary>
    /// Finds the widest run of a line that no obstacle stands in.
    /// </summary>
    /// <param name="left">The left edge of the line.</param>
    /// <param name="right">The right edge of the line.</param>
    /// <param name="blocked">
    /// The horizontal spans obstacles take up. May overlap each other and may run outside
    /// <paramref name="left"/> and <paramref name="right"/>; both are handled.
    /// <b>Sorted in place</b> - the list is the caller's working list, sorted rather than copied so
    /// that a per-line call does not allocate a second one.
    /// </param>
    /// <param name="tolerance">
    /// How wide a run has to be to count as room. A run no wider than this is treated as no room at
    /// all, which is what keeps a sliver left by two obstacles that all but meet from being offered
    /// to a line.
    /// </param>
    /// <param name="start">Where the widest free run begins. Meaningless unless this returns true.</param>
    /// <param name="width">How wide the widest free run is. Meaningless unless this returns true.</param>
    /// <returns>
    /// False where every part of the line is taken. That is not an error: the caller moves down past
    /// whatever is standing there and asks again lower down.
    /// </returns>
    /// <remarks>
    /// <b>The widest run, not every run.</b> A band with an obstacle in its middle has two free runs
    /// and this answers with the wider, leaving the other empty. Both engines want that today, and
    /// both record it as a decision rather than a limitation: text that hops across a pull quote and
    /// back in the middle of a line is unreadable whatever the geometry says.
    /// <para>
    /// Ties go to the run found first, which is the leftmost, because the comparison is strictly
    /// greater than.
    /// </para>
    /// </remarks>
    public static bool TryWidestFree(double left, double right, List<(double Start, double End)> blocked,
        double tolerance, out double start, out double width)
    {
        start = 0;
        width = 0;

        if (blocked == null)
            throw new ArgumentNullException(nameof(blocked));

        // A line may have no width - that is an ordinary answer of "no room", and there is a test
        // for it - but it cannot end to the left of where it began. Left unchecked the scan walks
        // a negative line and reports no room, which is the right answer arrived at by accident
        // and hides the caller's mistake.
        if (right < left)
        {
            throw new ArgumentOutOfRangeException(nameof(right), right,
                "A line cannot end to the left of where it starts.");
        }

        // A negative tolerance is worse than useless: the test at the end is "wider than the
        // tolerance", so a run of no width at all would pass it and be offered to the caller as
        // room. Zero is allowed and means exactly that - any width at all will do.
        if (tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance,
                "A tolerance narrower than nothing would make a run of no width count as room.");
        }

        blocked.Sort((first, second) => first.Start.CompareTo(second.Start));

        double cursor = left;

        foreach ((double Start, double End) span in blocked)
        {
            // A gap between where the last obstacle ended and where this one starts. Clipped to the
            // right edge, since an obstacle may begin outside the line entirely.
            if (span.Start > cursor)
                Consider(cursor, Math.Min(span.Start, right), ref start, ref width);

            // Max, not assignment: the spans are sorted by where they start, so a wide obstacle can
            // still end further right than the one after it.
            cursor = Math.Max(cursor, span.End);
            if (cursor >= right)
                break;
        }

        if (cursor < right)
            Consider(cursor, right, ref start, ref width);

        if (width > tolerance)
            return true;

        start = 0;
        width = 0;
        return false;
    }

    static void Consider(double start, double end, ref double widestStart, ref double widestWidth)
    {
        double width = end - start;
        if (width <= widestWidth)
            return;

        widestStart = start;
        widestWidth = width;
    }
}
