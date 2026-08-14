using System;
using System.Globalization;

namespace PdfSharpCore.Drawing.Layout;

/// <summary>
/// The vertical slice a single line occupies: the top of its box and the bottom of it.
/// </summary>
/// <remarks>
/// <b>A box and not a baseline.</b> A line whose baseline falls below an obstacle still has
/// ascenders reaching up into it, and a line whose baseline clears one can still have descenders
/// hanging into it. Asking the geometry at the baseline puts text through the thing it was supposed
/// to flow around, and the collision is a few points tall - big enough to see and small enough to
/// argue about.
/// <para>
/// <c>XTextFormatter</c> already tests its drop cap this way and says why at the test; this is the
/// same rule given a name, so that every obstacle answers by the same rule rather than each one
/// choosing.
/// </para>
/// </remarks>
public readonly struct FlowBand : IEquatable<FlowBand>
{
    /// <summary>
    /// Initializes the slice a line occupies, from the top of its box to the bottom.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Where the bottom is above the top. A band of no height is allowed - it asks about a
    /// hairline - but an inverted one is a mistake, and y runs down the page here as it does
    /// everywhere else in layout.
    /// </exception>
    public FlowBand(double top, double bottom)
    {
        if (bottom < top)
        {
            throw new ArgumentOutOfRangeException(nameof(bottom), bottom,
                "A band cannot end above where it starts: y runs down the page.");
        }

        Top = top;
        Bottom = bottom;
    }

    /// <summary>The top of the line's box, measured down the page.</summary>
    public double Top { get; }

    /// <summary>The bottom of the line's box, measured down the page.</summary>
    public double Bottom { get; }

    /// <summary>How deep the line's box is.</summary>
    public double Height => Bottom - Top;

    /// <summary>
    /// Whether anything standing between the two given depths would be inside this band.
    /// </summary>
    /// <remarks>
    /// Touching counts for nothing: something whose foot is exactly level with the top of a band is
    /// above the band, not in it. Strict comparison both ways, so a stack of obstacles set end to
    /// end leaves no band claimed by two of them.
    /// </remarks>
    public bool Overlaps(double top, double bottom)
    {
        return top < Bottom && Top < bottom;
    }

    public bool Equals(FlowBand other)
    {
        return Top.Equals(other.Top) && Bottom.Equals(other.Bottom);
    }

    public override bool Equals(object obj)
    {
        return obj is FlowBand other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Top.GetHashCode() * 397) ^ Bottom.GetHashCode();
        }
    }

    public static bool operator ==(FlowBand left, FlowBand right) => left.Equals(right);

    public static bool operator !=(FlowBand left, FlowBand right) => !left.Equals(right);

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "{0}..{1}", Top, Bottom);
    }
}
