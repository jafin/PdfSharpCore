using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Drawing.Layout.enums;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Drawing.Layout;

/// <summary>
///   An initial letter set into the opening lines of a block, and the room those lines leave for
///   it.
/// </summary>
/// <remarks>
///   <para>
///     Doing this by hand meant drawing the letter separately and then adding one word at a time to
///     a probe rectangle until the answer stopped fitting — thirty lines that re-measure the text
///     once per word and go wrong whenever the cap's depth is not a whole multiple of the line
///     height.
///   </para>
///   <para>
///     Assertions are made on glyph codes rather than on characters. The fonts are embedded as
///     Identity-H, so what a content stream carries is glyph indices into the face; the test
///     resolver pins that face, which makes the codes stable and comparable between two renders of
///     the same text.
///   </para>
///   <para>
///     In the glyph-outline collection because the cap is placed by its ink where a provider is
///     registered, and the unregistered case is one of the things tested — which means clearing the
///     provider and putting it back.
///   </para>
/// </remarks>
[Collection(GlyphOutlineCollection.Name)]
public class DropCapTests
{
    const string Prose =
        "The quick brown fox jumps over the lazy dog, and having jumped it lands and looks about " +
        "for somewhere else to be, which takes rather longer than the jump did and is far less " +
        "impressive to watch from any distance at all, or indeed from close to, where the whole " +
        "business looks distinctly laboured and not at all the effortless bound the saying has " +
        "always promised its readers it would turn out to be on closer inspection.";

    static readonly XRect Area = new XRect(40, 40, 300, 300);

    /// <summary>The glyph a space is in the face the tests are pinned to.</summary>
    const int SpaceGlyph = 3;

    // ----- the room the cap reserves --------------------------------------------------------------

    [Fact]
    public void TheOpeningLinesBeginToTheRightOfTheCapAndTheRestAtTheMargin()
    {
        var withCap = BodyLinesOf(Prose, cap: 3);
        var margin = BodyLinesOf(Prose, cap: 0)[0].X;

        withCap.Count.Should().BeGreaterThan(3, "there must be lines past the cap to compare");

        foreach (var line in withCap.Take(3))
            line.X.Should().BeGreaterThan(margin + 10, "a line beside the cap clears it");

        withCap[3].X.Should().BeApproximately(margin, 0.01, "the fourth line is back at the margin");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void ExactlyAsManyLinesAreNarrowedAsTheCapIsDeep(int depth)
    {
        var withCap = BodyLinesOf(Prose, cap: depth);
        var margin = BodyLinesOf(Prose, cap: 0)[0].X;

        withCap.TakeWhile(line => line.X > margin + 0.01).Should().HaveCount(depth);
    }

    [Fact]
    public void NothingIsLostToTheCapAndNothingIsRepeated()
    {
        // The cap takes the first character; everything after it is laid out in full and in order.
        var withCap = GlyphsOn(Render(Prose, cap: 3));
        var without = GlyphsOn(Render(Prose, cap: 0));

        withCap.Should().Equal(without, "the cap is the first glyph and the rest follow unchanged");
    }

    [Fact]
    public void ANarrowedLineCarriesFewerWordsThanAFullOne()
    {
        var withCap = BodyLinesOf(Prose, cap: 3);
        var without = BodyLinesOf(Prose, cap: 0);

        withCap[0].Glyphs.Should().BeLessThan(without[0].Glyphs,
            "the first line is shorter for having the cap beside it");
    }

    // ----- the cap itself --------------------------------------------------------------------------

    [Fact]
    public void TheFirstCharacterIsDrawnOnceAtTheCapsSizeAndNotAgainAtBodySize()
    {
        var page = Render(Prose, cap: 3);

        var runs = TextOperators.ShownStrings(page);
        GlyphCountOf(runs[0]).Should().Be(1, "the cap is one glyph drawn on its own");

        var sizes = FontSizesOn(page);
        sizes[0].Should().BeGreaterThan(sizes[1] * 2, "the cap is set far larger than the body");
    }

    [Fact]
    public void TheCapRestsOnTheBaselineOfTheLastLineItIsSetInto()
    {
        var page = Render(Prose, cap: 3);
        var positions = TextBaselines.PositionsOf(page);

        var cap = positions[0].Y;
        var bodyBaselines = positions.Skip(1).Select(p => Math.Round(p.Y, 3)).Distinct()
            .OrderByDescending(y => y).ToList();

        cap.Should().BeApproximately(bodyBaselines[2], 0.01, "the cap's foot is on the third line");
    }

    [Fact]
    public void ADeeperCapIsSetLargerAndReservesMoreRoom()
    {
        var two = Render(Prose, cap: 2);
        var three = Render(Prose, cap: 3);

        FontSizesOn(three)[0].Should().BeGreaterThan(FontSizesOn(two)[0]);
        BodyLinesOf(Prose, cap: 3)[0].X.Should().BeGreaterThan(BodyLinesOf(Prose, cap: 2)[0].X);
    }

    [Fact]
    public void TheCapsInkStartsAtTheBlocksLeftEdgeRatherThanItsPen()
    {
        var page = Render(Prose, cap: 3);
        var capPen = TextBaselines.PositionsOf(page)[0].X;

        // Set flush by ink, so the pen sits a little to the left of the margin by the glyph's own
        // left side bearing. Set flush by pen it would sit exactly on the margin and look indented.
        capPen.Should().BeLessThan(Area.X);
        capPen.Should().BeGreaterThan(Area.X - 12, "the bearing is small, not a whole letter");
    }

    [Fact]
    public void AWiderGutterPushesTheTextFurtherFromTheCap()
    {
        var tight = BodyLinesOf(Prose, cap: 3, arrange: f => f.DropCap.Gutter = 0)[0].X;
        var loose = BodyLinesOf(Prose, cap: 3, arrange: f => f.DropCap.Gutter = 20)[0].X;

        (loose - tight).Should().BeApproximately(20, 0.01);
    }

    // ----- justification and truncation against the narrowed measure --------------------------------

    [Theory]
    [InlineData(XParagraphAlignment.Right)]
    [InlineData(XParagraphAlignment.Justify)]
    public void AnAlignedBlockBesideTheCapStillReachesTheColumnsRightEdge(XParagraphAlignment alignment)
    {
        // The laid-out bounds are the one place a line's real right edge is observable: they are
        // built from each block's position plus its measured width. Drawn positions alone are pen
        // positions, which for aligned text are the edge less a width the test cannot see.
        var withCap = LayoutOf(Prose, cap: 3, f => f.Alignment = alignment);
        var without = LayoutOf(Prose, cap: 0, f => f.Alignment = alignment);

        // The cap narrows its lines on the *left*. Where those lines end does not move, so text
        // aligned to the right edge reaches the same place with the cap as without it.
        (withCap.X + withCap.Width).Should().BeApproximately(Area.Width, 0.01);
        (without.X + without.Width).Should().BeApproximately(Area.Width, 0.01);
    }

    [Fact]
    public void NoJustifiedLineBesideTheCapRunsPastTheColumnsRightEdge()
    {
        var page = Render(Prose, cap: 3, arrange: f => f.Alignment = XParagraphAlignment.Justify);

        // A justified line is drawn one block at a time with room opened between them, so every
        // block's pen must still land inside the measure. Spacing a narrowed line to a measure it
        // does not have pushes the last blocks past the edge.
        var starts = TextBaselines.PositionsOf(page).Skip(1).Select(p => p.X).ToList();

        starts.Should().OnlyContain(x => x <= Area.X + Area.Width + 0.01);
        starts.Where(x => x > Area.X + 10).Should().NotBeEmpty("some blocks sit beside the cap");
    }

    [Fact]
    public void ATruncatedLineBesideTheCapKeepsItsEllipsisInsideTheColumn()
    {
        // Shallow enough that the text runs out of room while still beside the cap.
        var shallow = new XRect(40, 40, 300, 40);
        var page = Render(Prose, cap: 3, area: shallow,
            arrange: f => f.Ellipsis = XTextFormatter.DefaultEllipsis);

        var starts = TextBaselines.PositionsOf(page).Skip(1).Select(p => p.X).ToList();

        starts.Should().NotBeEmpty("the text was truncated, not dropped entirely");
        starts.Should().OnlyContain(x => x < shallow.X + shallow.Width);
    }

    // ----- the edges ------------------------------------------------------------------------------

    [Fact]
    public void TextShorterThanTheCapIsDeepIsDrawnAndDoesNotThrow()
    {
        var draw = () => Render("Two words", cap: 5);

        draw.Should().NotThrow();
        GlyphCountOf(TextOperators.ShownStrings(Render("Two words", cap: 5))[0]).Should().Be(1);
    }

    [Fact]
    public void AnEmptyStringDrawsNothingAndThrowsNothing()
    {
        var draw = () => Render("", cap: 3);

        draw.Should().NotThrow();
        TextOperators.ShownStrings(Render("", cap: 3)).Should().BeEmpty();
    }

    [Fact]
    public void ASingleCharacterBecomesTheCapAndLeavesNoBodyText()
    {
        var runs = TextOperators.ShownStrings(Render("T", cap: 3));

        GlyphCountOf(runs.Should().ContainSingle().Subject).Should().Be(1);
    }

    [Fact]
    public void TextBeginningWithASpaceIsLaidOutWithNoCapAtAll()
    {
        // A space is not an initial letter. Setting one as a cap would reserve room for nothing at
        // all and swallow a character of the caller's text.
        var withLeadingSpace = GlyphsOn(Render(" The quick brown fox", cap: 3));
        var plain = GlyphsOn(Render(" The quick brown fox", cap: 0));

        withLeadingSpace.Should().Equal(plain);
    }

    [Fact]
    public void NoDropCapLeavesEveryLineAtTheMargin()
    {
        var lines = BodyLinesOf(Prose, cap: 0);
        var margin = lines[0].X;

        lines.Should().OnlyContain(line => Math.Abs(line.X - margin) < 0.01);
    }

    [Fact]
    public void ADepthOfLessThanOneLineIsRefused()
    {
        var build = () => new XDropCap(new XFont("Arial", 12), 0);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ACapWithNoFontIsRefused()
    {
        var build = () => new XDropCap(null, 3);

        build.Should().Throw<ArgumentNullException>();
    }

    // ----- without an outline provider ------------------------------------------------------------

    [Fact]
    public void ACapIsStillDrawnAndRoomStillReservedWhenNoOutlineProviderIsRegistered()
    {
        WithoutOutlineProvider(() =>
        {
            var draw = () => Render(Prose, cap: 3);
            draw.Should().NotThrow("a drop cap must not need a backend seam nobody registered");

            GlyphCountOf(TextOperators.ShownStrings(Render(Prose, cap: 3))[0]).Should().Be(1);

            var margin = BodyLinesOf(Prose, cap: 0)[0].X;
            BodyLinesOf(Prose, cap: 3).Take(3).Should().OnlyContain(line => line.X > margin + 10);
        });
    }

    [Fact]
    public void TheCapSitsOnTheSameBaselineWhicheverWayItWasMeasured()
    {
        var withInk = TextBaselines.PositionsOf(Render(Prose, cap: 3))[0].Y;

        double byAdvance = 0;
        WithoutOutlineProvider(() => byAdvance = TextBaselines.PositionsOf(Render(Prose, cap: 3))[0].Y);

        // The foot goes on the third line's baseline either way. What differs is the size the cap
        // is scaled to, since the advance route takes the whole ascent for the letter's height.
        withInk.Should().BeApproximately(byAdvance, 0.01);
    }

    [Fact]
    public void MeasuringByInkSetsTheCapLargerThanMeasuringByAdvance()
    {
        var byInk = FontSizesOn(Render(Prose, cap: 3))[0];

        double byAdvance = 0;
        WithoutOutlineProvider(() => byAdvance = FontSizesOn(Render(Prose, cap: 3))[0]);

        // A capital does not reach the font's ascender, so taking the ascent for the letter's
        // height under-sizes the cap. That is the inaccuracy the fallback accepts, and it is
        // recorded here rather than left to be discovered.
        byInk.Should().BeGreaterThan(byAdvance);
    }

    static void WithoutOutlineProvider(Action test)
    {
        var provider = GlobalFontSettings.GlyphOutlineProvider;
        try
        {
            GlobalFontSettings.GlyphOutlineProvider = null;
            test();
        }
        finally
        {
            GlobalFontSettings.GlyphOutlineProvider = provider;
        }
    }

    // ----- rendering, and reading the page back ---------------------------------------------------

    /// <summary>
    ///   The bounds the formatter measures for the text, relative to the layout rectangle.
    /// </summary>
    static XRect LayoutOf(string text, int cap, Action<XTextFormatter> arrange = null)
    {
        var document = new PdfDocument();
        using var gfx = XGraphics.FromPdfPage(document.AddPage());

        var formatter = new XTextFormatter(gfx);
        if (cap > 0)
            formatter.DropCap = new XDropCap(new XFont("Arial", 12), cap);
        arrange?.Invoke(formatter);

        return formatter.GetLayout(text, new XFont("Arial", 10), XBrushes.Black, Area);
    }

    static PdfPage Render(string text, int cap, XRect? area = null, Action<XTextFormatter> arrange = null)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var formatter = new XTextFormatter(gfx);
            if (cap > 0)
                formatter.DropCap = new XDropCap(new XFont("Arial", 12), cap);
            arrange?.Invoke(formatter);
            formatter.DrawString(text, new XFont("Arial", 10), XBrushes.Black, area ?? Area);
        }
        return page;
    }

    /// <summary>
    ///   Every glyph the page shows, in the order it was drawn, with the spaces taken out.
    /// </summary>
    /// <remarks>
    ///   Spaces go because a line break eats one, so two renders of the same text that break in
    ///   different places carry different numbers of them. What must not differ is the letters.
    /// </remarks>
    /// <summary>How many glyphs a drawn run holds. Identity-H writes two bytes for each.</summary>
    static int GlyphCountOf(string run) => run.Length / 2;

    static List<int> GlyphsOn(PdfPage page)
    {
        var glyphs = new List<int>();

        foreach (var run in TextOperators.ShownStrings(page))
        {
            // Identity-H writes two bytes per glyph and the string holds one byte per char, so a
            // glyph is a pair. Reading them singly leaves the high byte of a filtered space behind
            // and shifts everything after it by one.
            for (var idx = 0; idx + 1 < run.Length; idx += 2)
            {
                var glyph = (run[idx] << 8) | run[idx + 1];
                if (glyph != SpaceGlyph)
                    glyphs.Add(glyph);
            }
        }

        return glyphs;
    }

    /// <summary>Each line of body text, top of the page first. The cap is not one of them.</summary>
    static List<(double X, double Y, int Glyphs)> BodyLinesOf(string text, int cap,
        Action<XTextFormatter> arrange = null)
    {
        var page = Render(text, cap, arrange: arrange);
        var positions = TextBaselines.PositionsOf(page);
        var runs = TextOperators.ShownStrings(page);

        var lines = new List<(double X, double Y, int Glyphs)>();
        for (var idx = cap > 0 ? 1 : 0; idx < positions.Count && idx < runs.Count; idx++)
            lines.Add((positions[idx].X, positions[idx].Y, runs[idx].Length));

        return lines.OrderByDescending(line => line.Y).ToList();
    }

    /// <summary>The size given to each <c>Tf</c>, in the order they were written.</summary>
    static List<double> FontSizesOn(PdfPage page)
    {
        var content = Encoding.ASCII.GetString(PageContent.Of(page));
        return Regex.Matches(content, @"/F\d+ ([\d.]+) Tf")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
    }

}
