using System;
using System.Linq;
using AwesomeAssertions;
using PdfSharpCore.Fonts;
using PdfSharpCore.Text;
using Xunit;

namespace PdfSharpCore.Test.Text;

/// <summary>
///   Cutting mixed text into the runs a shaper can be handed: one direction and one script each,
///   in the order they are drawn.
/// </summary>
/// <remarks>
///   The bidirectional algorithm has its own conformance suite and is tested by it; what is left
///   here is script itemisation, which has no such suite, and the join between the two.
/// </remarks>
public class ItemizationTests
{
    // "arabi", four Arabic letters. Escapes rather than literals throughout, so that a source file
    // mixing right-to-left text with left-to-right code cannot be misread.
    const string Arabic = "\u0639\u0631\u0628\u064A";
    const string Hebrew = "\u05D0\u05D1";

    // ----- script itemisation ----------------------------------------------------------------------

    [Fact]
    public void TextOfOneScriptIsOneRun()
    {
        var runs = ScriptItemizer.Itemize("Hello");

        runs.Should().HaveCount(1);
        runs[0].Script.Should().Be(UnicodeScript.Latin);
        runs[0].Start.Should().Be(0);
        runs[0].Length.Should().Be(5);
    }

    [Fact]
    public void AChangeOfScriptStartsANewRun()
    {
        var runs = ScriptItemizer.Itemize("Hi" + Arabic);

        runs.Select(run => run.Script).Should().Equal(new[]
        {
            UnicodeScript.Latin, UnicodeScript.Arabic,
        });
        runs[0].Length.Should().Be(2);
        runs[1].Length.Should().Be(4);
    }

    [Fact]
    public void PunctuationAndSpacesGoWithWhateverTheyAreNextTo()
    {
        // A space and a full stop are script Common and say nothing about which script they are
        // in. Sweeping them into the preceding run is the whole of UAX #24's difficulty.
        var runs = ScriptItemizer.Itemize("Hi. " + Arabic);

        runs.Should().HaveCount(2);
        runs[0].Length.Should().Be(4, "the full stop and the space belong to the Latin run");
        runs[1].Script.Should().Be(UnicodeScript.Arabic);
    }

    [Fact]
    public void LeadingPunctuationBelongsToTheScriptThatFollowsIt()
    {
        // Nothing has said what script the text is in yet, so the opening bracket waits for the
        // first character that does and is taken into that run rather than left in one of its own.
        var runs = ScriptItemizer.Itemize("(Hello)");

        runs.Should().HaveCount(1);
        runs[0].Script.Should().Be(UnicodeScript.Latin);
        runs[0].Length.Should().Be(7);
    }

    [Fact]
    public void ACombiningMarkTakesTheScriptOfTheLetterItSitsOn()
    {
        // A combining acute is script Inherited, which is Common's counterpart for marks.
        var runs = ScriptItemizer.Itemize("\u0065\u0301");

        runs.Should().HaveCount(1);
        runs[0].Script.Should().Be(UnicodeScript.Latin);
    }

    [Fact]
    public void TextWithNoLettersInItIsOneCommonRun()
    {
        var runs = ScriptItemizer.Itemize("12 + 34!");

        runs.Should().HaveCount(1);
        runs[0].Script.Should().Be(UnicodeScript.Common);
        runs[0].ScriptCode.Should().Be("zyyy");
    }

    [Fact]
    public void NothingIsNoRuns()
    {
        ScriptItemizer.Itemize(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void AnAstralCharacterIsOneCharacterAndNotTwo()
    {
        // U+1D400 MATHEMATICAL BOLD CAPITAL A is Common, and its two UTF-16 units must not be
        // read as two characters of possibly different scripts.
        var runs = ScriptItemizer.Itemize("A\U0001D400B");

        runs.Should().HaveCount(1);
        runs[0].Script.Should().Be(UnicodeScript.Latin);
        runs[0].Length.Should().Be(4, "two Latin letters and one astral character of two units");
    }

    // ----- the join with the bidirectional algorithm --------------------------------------------------

    [Fact]
    public void ARunIsBothOneDirectionAndOneScript()
    {
        var runs = TextItemizer.Itemize("Hi " + Arabic);

        runs.Should().HaveCount(2);
        runs[0].Direction.Should().Be(XTextDirection.LeftToRight);
        runs[0].ScriptCode.Should().Be("latn");
        runs[1].Direction.Should().Be(XTextDirection.RightToLeft);
        runs[1].ScriptCode.Should().Be("arab");
    }

    [Fact]
    public void TheRunsComeBackInTheOrderTheyAreDrawn()
    {
        // A right-to-left paragraph with Latin inside it: the Latin sits to the left of the Arabic
        // on the page although it was written after it, and a renderer drawing the runs in the
        // order they are handed over gets that right without knowing anything about bidi.
        var runs = TextItemizer.Itemize(Arabic + " Hi", BidiParagraphDirection.RightToLeft);

        runs.Should().HaveCount(2);
        runs[0].ScriptCode.Should().Be("latn", "the Latin is drawn leftmost");
        runs[1].ScriptCode.Should().Be("arab");
        runs[0].Start.Should().BeGreaterThan(runs[1].Start,
            "even though it was written last");
    }

    [Fact]
    public void TwoRightToLeftScriptsSideBySideAreTwoRuns()
    {
        // Same direction, different scripts, so the bidirectional algorithm sees one run and
        // itemisation has to cut it - which is the case that says the two are really composed
        // rather than one merely following the other.
        var runs = TextItemizer.Itemize(Hebrew + Arabic, BidiParagraphDirection.RightToLeft);

        runs.Should().HaveCount(2);
        // BeEquivalentTo rather than Equal, because what order they come back in is the next
        // test's question and this one is only about there being two of them. That was once the
        // only assertion here, and it is why the order being wrong went unnoticed.
        runs.Select(run => run.ScriptCode).Should().BeEquivalentTo(new[] { "hebr", "arab" });
        runs.Should().OnlyContain(run => run.Direction == XTextDirection.RightToLeft);
    }

    [Fact]
    public void TwoRightToLeftScriptsComeBackInTheOrderTheyAreDrawn()
    {
        // Not just cut into two runs - cut and then turned round. A right-to-left run whose script
        // changes half way along is still one direction, so the piece written first is the
        // rightmost and has to be handed over last. Nothing said so until a line of Hebrew
        // followed by Arabic came out with the Hebrew on the left.
        var runs = TextItemizer.Itemize(Hebrew + Arabic, BidiParagraphDirection.RightToLeft);

        runs.Select(run => run.ScriptCode).Should().Equal(new[] { "arab", "hebr" });
    }

    [Fact]
    public void ASpaceInsideARightToLeftRunDoesNotCutIt()
    {
        // The space is script Common and takes the script of what it is beside - but "beside" is
        // decided inside the bidirectional run, not across the paragraph. Asked of the paragraph,
        // this space goes with the Latin that precedes it in the text; and the algorithm then puts
        // it in the middle of the Arabic. Cutting there would have been a boundary that is not one,
        // and it would have left the real boundary uncut.
        var runs = TextItemizer.Itemize("one " + Arabic, BidiParagraphDirection.RightToLeft);

        runs.Should().HaveCount(2);
        runs[0].ScriptCode.Should().Be("arab", "the space belongs to the run it lands in");
        runs[0].Length.Should().Be(5, "which is four letters and the space in front of them");
        runs[1].ScriptCode.Should().Be("latn");
    }

    [Fact]
    public void EveryCharacterEndsUpInExactlyOneRun()
    {
        const string mixed = "Hi " + Arabic + " 12 " + Hebrew + "!";

        var runs = TextItemizer.Itemize(mixed);

        runs.Sum(run => run.Length).Should().Be(mixed.Length);
        runs.SelectMany(run => Enumerable.Range(run.Start, run.Length))
            .OrderBy(index => index)
            .Should().Equal(Enumerable.Range(0, mixed.Length),
                "nothing is dropped and nothing is covered twice");
    }

    [Fact]
    public void ALeftToRightParagraphOfPlainTextIsOneRun()
    {
        // The overwhelmingly common case, and the one that must not have been made expensive:
        // no reordering, no script change, one run.
        var runs = TextItemizer.Itemize("The quick brown fox.");

        runs.Should().HaveCount(1);
        runs[0].Start.Should().Be(0);
        runs[0].Length.Should().Be(20);
        runs[0].Direction.Should().Be(XTextDirection.LeftToRight);
    }

    [Fact]
    public void NothingIsNoTextRuns()
    {
        TextItemizer.Itemize(string.Empty).Should().BeEmpty();
    }

    // ----- what the bidirectional algorithm answers for a caller who only wants the direction -----

    [Fact]
    public void AParagraphReadsItsOwnDirectionOffItsFirstStrongCharacter()
    {
        BidiAlgorithm.Resolve("Hello " + Arabic).IsRightToLeft.Should().BeFalse();
        BidiAlgorithm.Resolve(Arabic + " Hello").IsRightToLeft.Should().BeTrue();
        BidiAlgorithm.Resolve("123 " + Arabic).IsRightToLeft.Should().BeTrue(
            "a number is not a strong character, so the Arabic is the first thing that counts");
    }

    [Fact]
    public void ACallerCanOverrideWhatTheTextSaysAboutItself()
    {
        BidiAlgorithm.Resolve(Arabic, BidiParagraphDirection.LeftToRight)
            .IsRightToLeft.Should().BeFalse();
        BidiAlgorithm.Resolve("Hello", BidiParagraphDirection.RightToLeft)
            .IsRightToLeft.Should().BeTrue();
    }

    [Fact]
    public void RightToLeftTextComesBackReversed()
    {
        // The documented complaint this whole gap exists for: "salam" drawn as "m a l s".
        var result = BidiAlgorithm.Resolve(Hebrew);

        result.VisualOrder.Should().Equal(new[] { 1, 0 });
    }
}
