using System;
using System.Collections.Generic;

namespace PdfSharpCore.Drawing.Layout;

/// <summary>
/// A block of text and the things standing in it.
/// </summary>
/// <remarks>
/// <para>
/// Container and exclusions kept apart, because they answer different questions and change at
/// different times: the bounds are where the text may go, the obstacles are what has been put there.
/// </para>
/// <para>
/// <b>This is not a shape model.</b> It takes obstacles, not shapes - no wrap style, no floating,
/// no element. Those describe a shape in a document tree, and there is no document tree here;
/// MigraDoc has one and keeps its own.
/// </para>
/// </remarks>
public sealed class TextFlowRegion
{
    readonly List<IFlowObstacle> _obstacles = new List<IFlowObstacle>();

    /// <summary>
    /// Initializes a region covering the given bounds with nothing standing in it.
    /// </summary>
    public TextFlowRegion(XRect bounds)
    {
        Bounds = bounds;
    }

    /// <summary>Where text may go, before anything is taken out of it.</summary>
    public XRect Bounds { get; }

    /// <summary>What stands in it.</summary>
    public IList<IFlowObstacle> Obstacles => _obstacles;

    /// <summary>
    /// Adds something for the text to flow around, and answers this region so that several can be
    /// added in one expression.
    /// </summary>
    public TextFlowRegion With(IFlowObstacle obstacle)
    {
        if (obstacle == null)
            throw new ArgumentNullException(nameof(obstacle));

        _obstacles.Add(obstacle);
        return this;
    }

    /// <summary>
    /// The horizontal runs a line occupying the given band may be laid out in.
    /// </summary>
    /// <remarks>
    /// <b>Horizontally only.</b> A band below the bottom of <see cref="Bounds"/> is answered as
    /// though it were inside them, and that is deliberate: an empty answer has to mean <i>something
    /// is standing here, move down past it</i>, because that is what the caller does about it. If it
    /// could also mean <i>there is no more block</i> the caller would have to tell the two apart to
    /// know whether to move down or to stop, and moving down when it should stop is a loop that does
    /// not end. Running out of block is the caller's own business and it already knows how to tell.
    /// </remarks>
    public IntervalSet GetAvailableIntervals(FlowBand band)
    {
        IntervalSet free = IntervalSet.Of(Bounds.X, Bounds.X + Bounds.Width);
        if (_obstacles.Count == 0)
            return free;

        foreach (IFlowObstacle obstacle in _obstacles)
        {
            if (obstacle == null)
                continue;

            IReadOnlyList<XInterval> taken = obstacle.GetExcludedIntervals(band);
            if (taken == null || taken.Count == 0)
                continue;

            free = free.Subtract(taken);
            if (free.IsEmpty)
                break;
        }

        return free;
    }
}
