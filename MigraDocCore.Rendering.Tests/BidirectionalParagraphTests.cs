using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.Rendering.Tests.Helpers;
using PdfSharpCore.Text;
using Xunit;

namespace MigraDocCore.Rendering.Tests;

/// <summary>
///   A paragraph whose words have to change places to be read.
/// </summary>
/// <remarks>
///   <para>
///     <c>XGraphics.DrawString</c> turns a right-to-left string round on its own, so every word of
///     a Hebrew paragraph has always come out correctly. The words themselves did not: this
///     renderer draws one show-text operator per leaf and advances the pen by its width, so the
///     words stayed in the order they were written and the sentence read inside out.
///   </para>
///   <para>
///     Hebrew because Liberation Sans - which the tests pin so that layout is the same everywhere -
///     draws both Hebrew and Latin. A face answering <c>.notdef</c> for half the line would make a
///     wrong order indistinguishable from a right one. No shaper is registered: Hebrew does not
///     join, and reordering is not shaping.
///   </para>
/// </remarks>
public class BidirectionalParagraphTests
{
    // Three Hebrew words of two letters each. Escapes rather than literals, so that a source file
    // mixing right-to-left text with left-to-right code cannot be misread.
    const string First = "\u05D0\u05D1";
    const string Second = "\u05D2\u05D3";

    static Document Paragraph(string text,
        BidiParagraphDirection direction = BidiParagraphDirection.Automatic)
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.Format.TextDirection = direction;
        paragraph.AddText(text);

        return document;
    }

    /// <summary>The glyph one character draws as, for reading an order back.</summary>
    static int GlyphOf(char letter) => Glyphs.For(letter.ToString()).Single();

    /// <summary>
    ///   The glyphs the given letters draw as, in the order given. Nothing separates the words:
    ///   MigraDoc puts the space between two words in the positioning rather than drawing one, so
    ///   no whitespace glyph is ever shown.
    /// </summary>
    static IReadOnlyList<int> Drawn(string letters) => letters.Select(GlyphOf).ToList();

    // ----- the defect ------------------------------------------------------------------------------

    [Fact]
    public void AParagraphOfHebrewIsLaidOutInTheOrderItIsRead()
    {
        var page = Rendered.FirstPageOf(Paragraph(First + " " + Second));

        Glyphs.AcrossThePage(page).Should().Equal(Drawn("\u05D3\u05D2\u05D1\u05D0"),
            "the word written first is the rightmost, so it is drawn last - and each word's own "
            + "letters were already the right way round");
    }

    [Fact]
    public void AnEnglishPhraseInsideAHebrewParagraphKeepsItsOwnWordOrder()
    {
        // The case that says the words are really being ordered rather than the line being turned
        // round. Reversing a right-to-left line word by word is the obvious implementation and it
        // gets this wrong: two English words inside a Hebrew sentence run left to right between
        // themselves, and only their position in the sentence is right to left.
        var page = Rendered.FirstPageOf(Paragraph(First + " one two " + Second));

        Glyphs.AcrossThePage(page).Should().Equal(Drawn("\u05D3\u05D2" + "one" + "two" + "\u05D1\u05D0"));
    }

    [Fact]
    public void ADeclaredDirectionOverridesWhatTheFirstStrongCharacterSays()
    {
        // "one" is the first strong character, so the algorithm left to itself reads the whole
        // paragraph left to right and puts the Hebrew after it.
        var page = Rendered.FirstPageOf(
            Paragraph("one " + First, BidiParagraphDirection.RightToLeft));

        Glyphs.AcrossThePage(page).Should().Equal(Drawn("\u05D1\u05D0" + "one"),
            "the Hebrew is drawn leftmost although it was written last");
    }

    [Fact]
    public void LeavingItToBeGuessedIsStillWhatHappensByDefault()
    {
        var page = Rendered.FirstPageOf(Paragraph("one " + First));

        Glyphs.AcrossThePage(page).Should().Equal(Drawn("one" + "\u05D1\u05D0"),
            "the first strong character is Latin, so the paragraph reads left to right");
    }

    // ----- what it costs everything else -------------------------------------------------------------

    [Fact]
    public void AParagraphWithNothingRightToLeftInItIsPlacedExactlyAsItAlwaysWas()
    {
        // The guarantee that makes this safe to leave switched on. A line with nothing right to
        // left in it is not even measured twice: the scan that says so runs before anything else
        // does, and answers from the characters alone.
        var page = Rendered.FirstPageOf(Paragraph("one two three"));

        Glyphs.AcrossThePage(page).Should().Equal(Drawn("onetwothree"));
    }

    [Fact]
    public void ALineWithATabInItKeepsTheOrderItWasWritten()
    {
        // The documented limitation. A tab's width comes from a list built while the paragraph was
        // formatted and consumed in order, so a line holding one cannot be walked twice - and where
        // a tabbed line's columns belong in a right-to-left paragraph is a question this does not
        // answer. Such a line is left alone rather than guessed at.
        var page = Rendered.FirstPageOf(Paragraph(First + "\t" + Second));

        Glyphs.AcrossThePage(page).Should().Equal(Drawn("\u05D1\u05D0\u05D3\u05D2"),
            "each word is still turned round inside itself; only their order is left as written");
    }

    [Fact]
    public void TheMarksStayInTheOrderTheTextIsRead()
    {
        // Only where a word lands changed. The leaves are still walked in the order they were
        // written, so the marked content - and the structure tree that points at it - are in
        // reading order, which is what a structure tree is for and what a screen reader announces.
        // The two orders now genuinely differ, which is the whole of this change in one assertion.
        var page = Rendered.FirstPageOf(Paragraph(First + " " + Second));

        Glyphs.On(page).Should().Equal(Drawn("\u05D1\u05D0\u05D3\u05D2"),
            "written in the order the words were written");
        Glyphs.AcrossThePage(page).Should().Equal(Drawn("\u05D3\u05D2\u05D1\u05D0"),
            "and placed in the order they are read");
    }

    [Fact]
    public void AFootnoteMarkOnAReorderedLineIsDrawnOnce()
    {
        // The probing walk is a walk of the same renderers with the drawing turned off, and every
        // one of them has to know it. RenderFootnote did not: it drew its mark during the probe, at
        // the position the probe had reached, and then again for real at the reordered position. So
        // a Hebrew paragraph carrying a footnote grew a second reference mark, in the wrong place,
        // that nothing in the structure tree accounted for.
        var document = new Document();
        var section = document.AddSection();
        var paragraph = section.AddParagraph();
        paragraph.Format.TextDirection = BidiParagraphDirection.RightToLeft;
        paragraph.AddText(First + " " + Second);
        paragraph.AddFootnote("The note.");

        var page = Rendered.FirstPageOf(document);
        var mark = Glyphs.For("1").Single();

        Glyphs.On(page).Count(glyph => glyph == mark).Should().Be(2,
            "one mark beside the text and one at the head of the note, and no third one");
    }
}
