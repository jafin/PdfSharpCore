using System;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Drawing;

/// <summary>
///   <see cref="XGraphicsPath"/> is the shape-building half of the drawing API, and what it holds
///   is not reachable from outside the library - the path itself is a wrapper round an internal
///   <c>CoreGraphicsPath</c> whose points and types nothing public exposes. So a path is observed
///   the only way anyone else could: by drawing it onto a page and reading the operators back,
///   which is also the only thing about a path that ever matters.
///   <para>
///   Two habits of the underlying path show through repeatedly and are worth stating once.
///   A segment that ends where the previous one ended is dropped rather than written, so a shape
///   that names a corner twice still has one point there. And a figure that has been closed makes
///   the next segment start a new figure, which is how one path comes to hold several contours.
///   </para>
/// </summary>
public class XGraphicsPathTests
{
    // ----- building the page ---------------------------------------------------------------------

    static PdfPage PageWith(Action<XGraphicsPath> build)
    {
        var path = new XGraphicsPath();
        build(path);
        return PageWith(path);
    }

    static PdfPage PageWith(XGraphicsPath path)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawPath(XPens.Black, path);
        return page;
    }

    static int PointCount(Action<XGraphicsPath> build) => PathGeometry.PointsOf(PageWith(build)).Count;

    static int FigureCount(Action<XGraphicsPath> build) => PathGeometry.FigureCountOf(PageWith(build));

    static XRect Bounds(Action<XGraphicsPath> build) => PathGeometry.BoundsOf(PageWith(build));

    // ----- lines ---------------------------------------------------------------------------------

    [Fact]
    public void AnEmptyPathDrawsNothing()
    {
        PointCount(_ => { }).Should().Be(0);
        FigureCount(_ => { }).Should().Be(0);
    }

    [Fact]
    public void ALineIsAMoveAndALineTo()
    {
        var page = PageWith(path => path.AddLine(100, 100, 300, 200));

        PathGeometry.FigureCountOf(page).Should().Be(1);
        PathGeometry.PointsOf(page).Should().HaveCount(2);
        var bounds = PathGeometry.BoundsOf(page);
        bounds.Width.Should().BeApproximately(200, 1e-3);
        bounds.Height.Should().BeApproximately(100, 1e-3);
    }

    [Fact]
    public void ALineBetweenTwoPointsIsTheSameLineAsBetweenFourNumbers()
    {
        var byPoints = Bounds(path => path.AddLine(new XPoint(100, 100), new XPoint(300, 200)));
        var byNumbers = Bounds(path => path.AddLine(100, 100, 300, 200));

        byPoints.Should().Be(byNumbers);
    }

    [Fact]
    public void ASecondLineJoinsTheFirstRatherThanStartingAgain()
    {
        // Both lines are one figure, and the second one's start is dropped because it is where
        // the first one ended - three points rather than four.
        FigureCount(path =>
        {
            path.AddLine(100, 100, 200, 100);
            path.AddLine(200, 100, 200, 200);
        }).Should().Be(1);

        PointCount(path =>
        {
            path.AddLine(100, 100, 200, 100);
            path.AddLine(200, 100, 200, 200);
        }).Should().Be(3);
    }

    [Fact]
    public void ClosingAFigureMakesTheNextSegmentStartANewOne()
    {
        FigureCount(path =>
        {
            path.AddLine(100, 100, 200, 100);
            path.CloseFigure();
            path.AddLine(300, 300, 400, 300);
        }).Should().Be(2);
    }

    [Fact]
    public void ClosingAnEmptyFigureIsHarmless()
    {
        var act = () => PointCount(path => path.CloseFigure());

        act.Should().NotThrow();
    }

    [Fact]
    public void AMoveOnItsOwnBeginsAFigureWhereverItIsAsked()
    {
        // AddMove exists so a caller can start a contour away from where the last one ended
        // without closing anything, which no other Add does.
        FigureCount(path =>
        {
            path.AddLine(100, 100, 200, 100);
            path.AddMove(300, 300);
            path.AddLine(300, 300, 400, 300);
        }).Should().Be(2);
    }

    [Fact]
    public void ASeriesOfLinesIsOneFigureWithOnePointEach()
    {
        var points = new[] { new XPoint(100, 100), new XPoint(200, 100), new XPoint(200, 200) };

        FigureCount(path => path.AddLines(points)).Should().Be(1);
        PointCount(path => path.AddLines(points)).Should().Be(3);
    }

    [Fact]
    public void ASeriesOfNoLinesAddsNothingAndASeriesOfNoneAtAllIsRefused()
    {
        PointCount(path => path.AddLines(Array.Empty<XPoint>())).Should().Be(0);

        var act = () => new XGraphicsPath().AddLines(null);
        act.Should().Throw<ArgumentNullException>();
    }

    // ----- curves --------------------------------------------------------------------------------

    [Fact]
    public void ABezierIsAMoveAndThreeControlPoints()
    {
        PointCount(path => path.AddBezier(100, 100, 150, 50, 250, 50, 300, 100)).Should().Be(4);
        FigureCount(path => path.AddBezier(100, 100, 150, 50, 250, 50, 300, 100)).Should().Be(1);
    }

    [Fact]
    public void ABezierBetweenFourPointsIsTheSameCurveAsBetweenEightNumbers()
    {
        var byPoints = Bounds(path => path.AddBezier(
            new XPoint(100, 100), new XPoint(150, 50), new XPoint(250, 50), new XPoint(300, 100)));
        var byNumbers = Bounds(path => path.AddBezier(100, 100, 150, 50, 250, 50, 300, 100));

        byPoints.Should().Be(byNumbers);
    }

    [Fact]
    public void ChainedBeziersShareTheirJoins()
    {
        // 4 + 3n points in, 4 + 3n points out: the first curve carries the move, and each later
        // one adds only its two controls and its end.
        var four = new[]
        {
            new XPoint(100, 100), new XPoint(150, 50), new XPoint(250, 50), new XPoint(300, 100),
        };
        var seven = new[]
        {
            new XPoint(100, 100), new XPoint(150, 50), new XPoint(250, 50), new XPoint(300, 100),
            new XPoint(350, 150), new XPoint(450, 150), new XPoint(500, 100),
        };

        PointCount(path => path.AddBeziers(four)).Should().Be(4);
        PointCount(path => path.AddBeziers(seven)).Should().Be(7);
        FigureCount(path => path.AddBeziers(seven)).Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void ABezierChainOfTheWrongLengthIsRefused(int count)
    {
        var points = new XPoint[count];

        var act = () => new XGraphicsPath().AddBeziers(points);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ABezierChainOfNothingAtAllIsRefused()
    {
        var act = () => new XGraphicsPath().AddBeziers(null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ASplineThroughTwoPointsIsOneCurve()
    {
        var points = new[] { new XPoint(100, 100), new XPoint(300, 200) };

        PointCount(path => path.AddCurve(points)).Should().Be(4);
        FigureCount(path => path.AddCurve(points)).Should().Be(1);
    }

    [Fact]
    public void ASplineThroughFourPointsIsThreeCurves()
    {
        // One segment between each neighbouring pair, and the first and last are given a
        // duplicated end so the spline starts and finishes flat rather than overshooting.
        var points = new[]
        {
            new XPoint(100, 100), new XPoint(200, 50), new XPoint(300, 150), new XPoint(400, 100),
        };

        PointCount(path => path.AddCurve(points)).Should().Be(10);
    }

    [Fact]
    public void TensionDecidesHowFarTheSplineBulges()
    {
        var points = new[]
        {
            new XPoint(100, 100), new XPoint(200, 50), new XPoint(300, 150), new XPoint(400, 100),
        };

        var slack = Bounds(path => path.AddCurve(points, 0.0));
        var taut = Bounds(path => path.AddCurve(points, 1.5));

        // A tension of zero puts every control point on the point it belongs to, so the curve
        // stays inside the points it passes through; a large one throws the controls well clear.
        taut.Height.Should().BeGreaterThan(slack.Height);
    }

    [Fact]
    public void ASplineThroughFewerThanTwoPointsIsRefused()
    {
        var act = () => new XGraphicsPath().AddCurve(new[] { new XPoint(1, 1) });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheSegmentedSplineOverloadIsNotImplementedAndSaysSo()
    {
        var points = new[] { new XPoint(100, 100), new XPoint(300, 200) };

        var act = () => new XGraphicsPath().AddCurve(points, 0, 1, 0.5);

        act.Should().Throw<NotImplementedException>();
    }

    // ----- arcs ----------------------------------------------------------------------------------

    [Fact]
    public void AQuarterArcIsASingleBezier()
    {
        PointCount(path => path.AddArc(100, 100, 200, 200, 0, 90)).Should().Be(4);
        FigureCount(path => path.AddArc(100, 100, 200, 200, 0, 90)).Should().Be(1);
    }

    [Fact]
    public void AnArcInARectangleIsTheSameArcAsInFourNumbers()
    {
        var byRect = Bounds(path => path.AddArc(new XRect(100, 100, 200, 200), 0, 90));
        var byNumbers = Bounds(path => path.AddArc(100, 100, 200, 200, 0, 90));

        byRect.Should().Be(byNumbers);
    }

    [Fact]
    public void AQuarterArcSpansAQuarterOfTheBoxItIsDrawnIn()
    {
        var bounds = Bounds(path => path.AddArc(100, 100, 200, 200, 0, 90));

        bounds.Width.Should().BeApproximately(100, 0.5);
        bounds.Height.Should().BeApproximately(100, 0.5);
    }

    [Fact]
    public void AWholeTurnComesRoundToWhereItStarted()
    {
        var bounds = Bounds(path => path.AddArc(100, 100, 200, 200, 0, 360));

        bounds.Width.Should().BeApproximately(200, 0.5);
        bounds.Height.Should().BeApproximately(200, 0.5);
    }

    [Theory]
    [InlineData(0, 45)]
    [InlineData(0, -45)]
    [InlineData(45, 180)]
    [InlineData(45, -180)]
    [InlineData(-90, 270)]
    [InlineData(370, 30)]
    [InlineData(0, 400)]
    [InlineData(0, -400)]
    public void AnArcIsDrawnWhicheverQuadrantItStartsInAndWhicheverWayItGoes(
        double startAngle, double sweepAngle)
    {
        // The arc is cut at every quadrant boundary it crosses, and which boundary comes next
        // depends on the direction. Sweeps beyond a full turn are clipped to one.
        var points = PathGeometry.PointsOf(PageWith(path =>
            path.AddArc(100, 100, 200, 200, startAngle, sweepAngle)));

        points.Should().NotBeEmpty();
        ((points.Count - 1) % 3).Should().Be(0, "an arc is a move followed by whole Béziers");
    }

    [Fact]
    public void AnEllipticalArcNeedsItsAnglesCorrectingAndStillFitsItsBox()
    {
        var bounds = Bounds(path => path.AddArc(100, 100, 400, 100, 0, 360));

        bounds.Width.Should().BeApproximately(400, 1);
        bounds.Height.Should().BeApproximately(100, 1);
    }

    [Fact]
    public void AnArcCanBeGivenAsTheTwoPointsItRunsBetween()
    {
        // The WPF spelling: where it starts, where it ends, how big the ellipse is, and which of
        // the four arcs that fit those is meant. The chord here is shorter than the diameter, or
        // both arcs would be the same semicircle and the choice would not show.
        var small = Bounds(path => path.AddArc(new XPoint(100, 200), new XPoint(170, 200),
            new XSize(50, 50), 0, false, XSweepDirection.Clockwise));
        var large = Bounds(path => path.AddArc(new XPoint(100, 200), new XPoint(170, 200),
            new XSize(50, 50), 0, true, XSweepDirection.Clockwise));

        small.Height.Should().BeLessThan(large.Height,
            "the large arc goes the long way round and so reaches further from the chord");
    }

    [Fact]
    public void TheTwoSweepDirectionsGiveTheTwoDifferentArcs()
    {
        var clockwise = PathGeometry.PointsOf(PageWith(path => path.AddArc(
            new XPoint(100, 200), new XPoint(170, 200),
            new XSize(50, 50), 0, false, XSweepDirection.Clockwise)));
        var counterclockwise = PathGeometry.PointsOf(PageWith(path => path.AddArc(
            new XPoint(100, 200), new XPoint(170, 200),
            new XSize(50, 50), 0, false, XSweepDirection.Counterclockwise)));

        clockwise.Should().NotEqual(counterclockwise,
            "the two directions put the arc on opposite sides of the chord");
    }

    // ----- closed shapes -------------------------------------------------------------------------

    [Fact]
    public void ARectangleIsFourPointsAndOneClosedFigure()
    {
        var page = PageWith(path => path.AddRectangle(new XRect(100, 100, 200, 50)));

        PathGeometry.FigureCountOf(page).Should().Be(1);
        PathGeometry.PointsOf(page).Should().HaveCount(4);
        var bounds = PathGeometry.BoundsOf(page);
        bounds.Width.Should().BeApproximately(200, 1e-3);
        bounds.Height.Should().BeApproximately(50, 1e-3);
    }

    [Fact]
    public void ARectangleFromFourNumbersIsTheSameRectangle()
    {
        Bounds(path => path.AddRectangle(100, 100, 200, 50))
            .Should().Be(Bounds(path => path.AddRectangle(new XRect(100, 100, 200, 50))));
    }

    [Fact]
    public void SeveralRectanglesAreSeveralFigures()
    {
        var rects = new[]
        {
            new XRect(100, 100, 50, 50), new XRect(200, 100, 50, 50), new XRect(300, 100, 50, 50),
        };

        FigureCount(path => path.AddRectangles(rects)).Should().Be(3);
        PointCount(path => path.AddRectangles(rects)).Should().Be(12);
    }

    [Fact]
    public void AnEllipseIsFourQuarterBeziersRoundOneMove()
    {
        var page = PageWith(path => path.AddEllipse(100, 100, 200, 100));

        PathGeometry.FigureCountOf(page).Should().Be(1);
        PathGeometry.PointsOf(page).Should().HaveCount(13);
        var bounds = PathGeometry.BoundsOf(page);
        bounds.Width.Should().BeApproximately(200, 1e-3);
        bounds.Height.Should().BeApproximately(100, 1e-3);
    }

    [Fact]
    public void AnEllipseInARectangleIsTheSameEllipse()
    {
        Bounds(path => path.AddEllipse(new XRect(100, 100, 200, 100)))
            .Should().Be(Bounds(path => path.AddEllipse(100, 100, 200, 100)));
    }

    [Fact]
    public void ARoundedRectangleFillsItsBoxAndCutsItsCorners()
    {
        var page = PageWith(path => path.AddRoundedRectangle(100, 100, 200, 100, 40, 40));

        PathGeometry.FigureCountOf(page).Should().Be(1);
        var bounds = PathGeometry.BoundsOf(page);
        bounds.Width.Should().BeApproximately(200, 1e-3);
        bounds.Height.Should().BeApproximately(100, 1e-3);

        // Four straight sides and four quarter-ellipses rather than a rectangle's four points.
        PathGeometry.PointsOf(page).Should().HaveCount(16);
    }

    [Fact]
    public void APolygonNamesEachCornerOnceAndClosesItself()
    {
        var points = new[] { new XPoint(100, 100), new XPoint(300, 100), new XPoint(200, 250) };

        var page = PageWith(path => path.AddPolygon(points));

        PathGeometry.FigureCountOf(page).Should().Be(1);
        PathGeometry.PointsOf(page).Should().HaveCount(3,
            "the first corner is the move, and the line back to it is dropped as a repeat");
        var bounds = PathGeometry.BoundsOf(page);
        bounds.Width.Should().BeApproximately(200, 1e-3);
        bounds.Height.Should().BeApproximately(150, 1e-3);
    }

    [Fact]
    public void APolygonOfNoCornersAddsNothing()
    {
        PointCount(path => path.AddPolygon(Array.Empty<XPoint>())).Should().Be(0);
    }

    [Fact]
    public void APolygonIsClosedSoWhatFollowsItStartsAgain()
    {
        var points = new[] { new XPoint(100, 100), new XPoint(300, 100), new XPoint(200, 250) };

        FigureCount(path =>
        {
            path.AddPolygon(points);
            path.AddLine(400, 400, 500, 500);
        }).Should().Be(2);
    }

    // ----- the members that do nothing -----------------------------------------------------------

    [Fact]
    public void APieIsNotImplementedAndQuietlyAddsNothing()
    {
        // Upstream reports through the not-implemented seam, which by default does nothing at
        // all, so a caller asking for a pie gets an empty path and no word about why. Pinned so
        // that the day it grows an implementation the change is visible here.
        PointCount(path => path.AddPie(100, 100, 200, 200, 0, 90)).Should().Be(0);
        PointCount(path => path.AddPie(new XRect(100, 100, 200, 200), 0, 90)).Should().Be(0);
    }

    [Fact]
    public void AClosedCurveIsNotImplementedAndQuietlyAddsNothing()
    {
        var points = new[] { new XPoint(100, 100), new XPoint(300, 100), new XPoint(200, 250) };

        PointCount(path => path.AddClosedCurve(points)).Should().Be(0);
        PointCount(path => path.AddClosedCurve(points, 0.75)).Should().Be(0);
        PointCount(path => path.AddClosedCurve(Array.Empty<XPoint>())).Should().Be(0);

        var act = () => new XGraphicsPath().AddClosedCurve(null);
        act.Should().Throw<ArgumentNullException>();

        var tooFew = () => new XGraphicsPath().AddClosedCurve(new[] { new XPoint(1, 1) });
        tooFew.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddingOnePathToAnotherIsNotImplementedAndQuietlyAddsNothing()
    {
        var other = new XGraphicsPath();
        other.AddRectangle(100, 100, 50, 50);

        PointCount(path => path.AddPath(other, true)).Should().Be(0);
    }

    [Fact]
    public void FlatteningAndWideningDoNothingAndSayNothing()
    {
        // Both are stubs upstream. They are called by real drawing code, so they have to be
        // harmless rather than absent, and a path is the same path afterwards.
        var before = PointCount(path => path.AddEllipse(100, 100, 200, 100));

        var after = PointCount(path =>
        {
            path.AddEllipse(100, 100, 200, 100);
            path.Flatten();
            path.Flatten(XMatrix.Identity);
            path.Flatten(XMatrix.Identity, 0.1);
            path.Widen(XPens.Black);
            path.Widen(XPens.Black, XMatrix.Identity);
            path.Widen(XPens.Black, XMatrix.Identity, 0.1);
        });

        after.Should().Be(before);
    }

    [Fact]
    public void StartingAFigureExplicitlyDoesNothing()
    {
        // Unlike AddMove, StartFigure is a stub - it does not begin a contour. Worth pinning
        // because the name promises otherwise.
        FigureCount(path =>
        {
            path.AddLine(100, 100, 200, 100);
            path.StartFigure();
            path.AddLine(300, 300, 400, 300);
        }).Should().Be(1);
    }

    // ----- the path as an object -----------------------------------------------------------------

    [Fact]
    public void AClonedPathHoldsTheSameShapeAndGoesItsOwnWayAfterwards()
    {
        var original = new XGraphicsPath();
        original.AddRectangle(100, 100, 50, 50);

        var clone = original.Clone();
        clone.AddRectangle(200, 200, 50, 50);

        PathGeometry.FigureCountOf(PageWith(original)).Should().Be(1);
        PathGeometry.FigureCountOf(PageWith(clone)).Should().Be(2);
    }

    [Fact]
    public void APathRemembersHowItIsToBeFilled()
    {
        var path = new XGraphicsPath();

        path.FillMode.Should().Be(XFillMode.Alternate, "the default matches GDI+");

        path.FillMode = XFillMode.Winding;
        path.FillMode.Should().Be(XFillMode.Winding);
    }

    [Fact]
    public void APathCanBeAskedForItsInternalsEvenThoughThereIsNothingThere()
    {
        // The accessor exists so that the public surface is not cluttered with internals; there
        // are none to hand out yet, and asking must still not throw.
        new XGraphicsPath().Internals.Should().NotBeNull();
    }

    [Fact]
    public void DrawingAPathWithNeitherAPenNorABrushIsRefused()
    {
        var path = new XGraphicsPath();
        path.AddRectangle(100, 100, 50, 50);
        var document = new PdfDocument();
        using var gfx = XGraphics.FromPdfPage(document.AddPage());

        var act = () => gfx.DrawPath((XPen)null, (XBrush)null, path);

        act.Should().Throw<ArgumentNullException>();
    }
}
