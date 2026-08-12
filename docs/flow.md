I’d model this as a general flow-region engine, not as “XTextFormatter with a few rectangle exclusions.” That gives you a clean path from simple image wrapping to arbitrary paths later.

PDF itself does not define paragraph/text-flow semantics. A PDF page is essentially fixed-position graphics/text emitted into content streams; your library decides where each line and glyph goes. So rectangular versus irregular wrapping is entirely a feature of your layout engine, not a PDF restriction.

For the structure, I’d use something like:

TextFormatter
    |
    +-- TextLayoutEngine
            |
            +-- TextShaper / FontMetrics
            +-- LineBreaker
            +-- FlowRegion
                    |
                    +-- RectangleFlowRegion
                    +-- PolygonFlowRegion
                    +-- PathFlowRegion
                    +-- CompositeFlowRegion

The important abstraction would be something like:

public interface IFlowRegion
{
    IReadOnlyList<XInterval> GetAvailableIntervals(
        double y,
        double lineHeight);
}

public readonly record struct XInterval(
    double Start,
    double End);

That sounds almost too simple, but it maps very well to the actual problem.

For every prospective line, ask:

At this vertical position, what horizontal spans are available for text?

For a plain rectangle:

┌──────────────────────────────────┐
│ available: [0 -----------------] │
│ available: [0 -----------------] │
│ available: [0 -----------------] │
└──────────────────────────────────┘

For a rectangular exclusion:

┌──────────────────────────────────┐
│ text text text text text text     │
│ text text ┌──────────┐ text text │
│ text text │  image   │ text text │
│ text text └──────────┘ text text │
│ text text text text text text     │
└──────────────────────────────────┘

Your query might return:

y = 10 -> [0, 500]

y = 30 -> [0, 180], [320, 500]

y = 50 -> [0, 180], [320, 500]

y = 80 -> [0, 500]

That immediately gives you irregular shape support too.

For example, a circle:

             text text text
        text text      text text
     text text     ○○      text text
     text text   ○○○○○     text text
        text text      text text
             text text

At each line Y coordinate:

y1: [0 -------------------------- 500]

y2: [0 ------- 210] [290 ------- 500]

y3: [0 --- 180]       [320 ----- 500]

y4: [0 ------- 210] [290 ------- 500]

y5: [0 -------------------------- 500]

That means the formatter doesn't actually need to understand circles, rectangles, polygons, Bézier paths, images, etc. It just understands available horizontal intervals.

I would separate “container” and “exclusions”

Something along these lines:

public sealed class TextFlowRegion
{
    public XRect Bounds { get; init; }

    public IList<IFlowObstacle> Obstacles { get; } = [];
}

Then:

public interface IFlowObstacle
{
    IReadOnlyList<XInterval> GetExcludedIntervals(
        double y,
        double lineHeight);
}

Implementations:

RectangleObstacle
EllipseObstacle
PolygonObstacle
PathObstacle

Then the layout engine effectively does:

available = region.Bounds.HorizontalInterval;

foreach (var obstacle in obstacles)
{
    var excluded = obstacle.GetExcludedIntervals(y, lineHeight);

    available = Subtract(available, excluded);
}

That gives you a very reusable geometry primitive:

IntervalSet
    .Subtract(...)
    .Intersect(...)
    .Union(...)

I'd make IntervalSet a first-class internal type. You'll use it everywhere.

One design decision: what does a "line" intersect?

Don't test the shape only at the baseline.

You really want the line box:

y
│
│   ┌──────────────────────── line box
│   │ The quick brown fox
│   └────────────────────────
│

So your API should probably conceptually be:

GetAvailableIntervals(
    double lineTop,
    double lineBottom);

rather than just:

GetAvailableIntervals(double y);

For a polygon/path, determine any X extent occupied by the obstacle anywhere within that line-height band.

Otherwise you can get subtle collisions like:

        /
 text  /          <- baseline doesn't intersect
──────/────────
     /

The glyph ascenders can collide even though the baseline doesn't.

I'd therefore lean toward:

public readonly record struct FlowBand(
    double Top,
    double Bottom);

public interface IFlowRegion
{
    IntervalSet GetAvailableIntervals(FlowBand band);
}

That is a strong core abstraction.

Then your line-layout algorithm becomes straightforward

Something roughly like:

while (!paragraph.End)
{
    var lineHeight = CalculateLineHeight(paragraph);

    var band = new FlowBand(
        cursorY,
        cursorY + lineHeight);

    var spans = flowRegion.GetAvailableIntervals(band);

    if (spans.IsEmpty)
    {
        cursorY += lineHeight;
        continue;
    }

    foreach (var span in spans)
    {
        var line = lineBreaker.Fill(
            paragraph,
            span.Width);

        if (line.HasContent)
        {
            DrawLine(
                line,
                x: span.Start,
                y: cursorY);
        }

        if (paragraph.End)
            break;
    }

    cursorY += lineHeight;
}

There is one subtlety here: whether multiple intervals at the same Y constitute one line or effectively multiple columns.

For normal wrapping around an image, you typically want:

text text    IMAGE    text text
text text    IMAGE    text text

meaning the text can continue from the left interval into the right interval on the same visual line.

So internally I'd probably represent:

FlowLine
{
    FlowBand Band;
    IReadOnlyList<FlowSpan> Spans;
}

and let one logical text line consume multiple spans.

Support these wrap modes separately

I would not bake behaviour directly into the shape type.

Have something like:

public enum TextWrapMode
{
    None,
    Square,
    Tight,
    Through,
    TopAndBottom
}

Possibly eventually:

public enum TextWrapSide
{
    Both,
    Left,
    Right,
    Largest
}

That mirrors the concepts users expect from Word/InDesign-like layout.

For example:

Square

Uses the object's bounding rectangle:

   ┌─────────────┐
   │   circle    │
   │      ○      │
   │             │
   └─────────────┘

Text sees the rectangle.

Tight

Uses actual geometry:

       ○
    ○     ○
   ○       ○
    ○     ○
       ○

Text follows the silhouette.

TopAndBottom

No text beside the shape:

text text text text

       IMAGE

text text text text

That last one shouldn't even require expensive geometry calculation; just block the entire X range over the object's vertical extent.

What I'd implement first

I'd stage it like this:

Rectangular flow region
Multiple rectangular exclusions
Left/right/both wrap modes
Polygon exclusions
Ellipse/circle
General XGraphicsPath exclusion
Optional padding around the shape

For padding, avoid putting it into the formatter. Put it into the obstacle:

new RectangleObstacle(rect)
{
    Margin = new Thickness(6)
};

or:

new FlowObstacle(path)
{
    DistanceFromText = 5
};

For rectangles this is trivial inflation:

rect.Inflate(margin);

For arbitrary paths it becomes an offset/stroked outline problem, which you can add later.

For irregular paths, flatten first

I would not initially try to analytically intersect Bézier curves against every line.

Flatten:

XGraphicsPath
      ↓
Flatten()
      ↓
Polygon / line segments
      ↓
Scanline intersections
      ↓
X intervals

For each band/scanline:

polygon edges
    ↓
find intersections with Y
    ↓
sort X
    ↓
pair intersections
    ↓
excluded intervals

Classic scanline geometry:

X1     X2         X3       X4
 |======|          |========|

Then subtract those intervals from the page/container interval.

This also makes clipping paths, SVG-like paths, irregular images, callouts, etc. much easier later.

One important distinction I'd keep

I would separate:

Text formatting

from:

Document layout

So XTextFormatter shouldn't eventually know things like:

image.Wrap = TextWrapMode.Tight;
image.Margin = 5;

Instead:

var layout = new TextLayoutEngine(...);

layout.FlowRegion = new FlowRegion(bounds)
{
    Obstacles =
    {
        new FlowObstacle(image.Bounds)
        {
            WrapMode = TextWrapMode.Square
        }
    }
};

Then XTextFormatter could remain a convenient façade:

formatter.DrawString(
    text,
    font,
    brush,
    bounds,
    flowOptions);

Internally:

XTextFormatter
      ↓
TextLayoutEngine
      ↓
FlowRegion
      ↓
LineBreaker
      ↓
XGraphics

That will age much better than adding increasingly complicated wrapping logic directly into DrawString().

And to your second question: PDF doesn't limit this to rectangles

You can absolutely have text visually wrap around circles, polygons, Bézier paths, images with irregular contours, or any other geometry because you're choosing the coordinates at which the text is painted. PDF itself is a fixed-layout graphics model rather than a reflow/layout engine; page content ultimately consists of positioned text/graphics operators.

If this were my library, the core abstraction I'd build everything around would be:

IntervalSet GetAvailableIntervals(FlowBand band);

That one API gives you rectangles today and essentially arbitrary flow geometry later without redesigning the text formatter.