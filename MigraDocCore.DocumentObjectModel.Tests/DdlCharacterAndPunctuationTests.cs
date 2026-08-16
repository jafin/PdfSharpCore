using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel.IO;
using Xunit;

namespace MigraDocCore.DocumentObjectModel.Tests;

/// <summary>
///   Two things that are only reachable through text: <c>\chr(…)</c>, which puts a character in a
///   paragraph by its number rather than by typing it, and the punctuators, which are every
///   bracket, brace, sign and separator the grammar is made of.
///   <para>
///   The scanner reads punctuation twice over, in two near-copies of one another -
///   <c>ScanPunctuator</c> when it is consuming and <c>PeekPunctuator</c> when it is looking
///   ahead. <c>CLAUDE.md</c> names that shape as the one where the copies drift apart, and they
///   had: see the backlog spec's finding F6.
///   </para>
/// </summary>
public class DdlCharacterAndPunctuationTests
{
    static Document Read(string ddl) => DdlReader.DocumentFromString(ddl);

    static Paragraph FirstParagraphOf(string paragraphBody) =>
        Read("\\document{\\section{\\paragraph{" + paragraphBody + "}}}")
            .LastSection.Elements[0] as Paragraph;

    static IReadOnlyList<string> ComplaintsAbout(string ddl)
    {
        var errors = new DdlReaderErrors();
        try
        {
            DdlReader.ObjectFromString(ddl, errors);
        }
        catch (Exception fatal)
        {
            return errors.Cast<DdlReaderError>().Select(e => e.ErrorMessage)
                .Append(fatal.Message).ToList();
        }
        return errors.Cast<DdlReaderError>().Select(e => e.ErrorMessage).ToList();
    }

    // ----- \chr ---------------------------------------------------------------------------------

    [Fact]
    public void ACharacterCanBeWrittenByItsNumber()
    {
        var character = FirstParagraphOf("\\chr(65)").Elements.OfType<Character>().Single();

        character.Char.Should().Be('A');
        character.Count.Should().Be(1, "one is the default");
    }

    [Fact]
    public void ACharacterCanSayHowManyTimesItIsRepeated()
    {
        var character = FirstParagraphOf("\\chr(45, 20)").Elements.OfType<Character>().Single();

        character.Char.Should().Be('-');
        character.Count.Should().Be(20);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public void TheEndsOfTheCharacterRangeAreAllowed(int number)
    {
        FirstParagraphOf("\\chr(" + number + ")")
            .Elements.OfType<Character>().Single().Char.Should().Be((char)number);
    }

    [Theory]
    [InlineData("\\chr(0)", "zero is not a character")]
    [InlineData("\\chr(256)", "and the range stops at a byte")]
    public void ANumberOutsideTheCharacterRangeIsRefused(string ddl, string why)
    {
        ComplaintsAbout("\\document{\\section{\\paragraph{" + ddl + "}}}")
            .Should().NotBeEmpty(why);
    }

    [Theory]
    [InlineData("\\chr(\"A\")")]
    [InlineData("\\chr(A)")]
    [InlineData("\\chr 65)")]
    public void SomethingThatIsNotANumberIsNotACharacterNumber(string ddl)
    {
        ComplaintsAbout("\\document{\\section{\\paragraph{" + ddl + "}}}")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void ACharacterByNumberSitsBetweenTheTextEitherSideOfIt()
    {
        var paragraph = FirstParagraphOf("before\\chr(38)after");

        paragraph.Elements.OfType<Character>().Single().Char.Should().Be('&');
        string.Concat(paragraph.Elements.OfType<Text>().Select(t => t.Content))
            .Should().Be("beforeafter");
    }

    // ----- punctuators the grammar is made of -----------------------------------------------------

    [Fact]
    public void EveryBracketingPairIsReadAsItself()
    {
        // Braces nest blocks, brackets carry attributes and parens carry arguments. One document
        // using all three is the shortest statement that the scanner tells them apart.
        var document = Read(
            "\\document{\\section[PageSetup{PageFormat = A5}]{\\paragraph{\\symbol(Euro)}}}");

        document.LastSection.PageSetup.PageFormat.Should().Be(PageFormat.A5);
        (document.LastSection.Elements[0] as Paragraph)
            .Elements.OfType<Character>().Single().SymbolName.Should().Be(SymbolName.Euro);
    }

    [Fact]
    public void AColonSeparatesAStyleFromTheOneItIsBasedOn()
    {
        var document = Read(
            "\\document{\\styles{Quiet : Normal{Font{Size = 8}}}\\section{\\paragraph{t}}}");

        document.Styles["Quiet"].BaseStyle.Should().Be("Normal");
    }

    [Fact]
    public void ADotIsThePointOfARealNumber()
    {
        var document = Read(
            "\\document{\\section{\\paragraph[Format{SpaceBefore = \"1.5cm\"}]{t}}}");

        (document.LastSection.Elements[0] as Paragraph)
            .Format.SpaceBefore.Centimeter.Should().BeApproximately(1.5, 1e-4);
    }

    [Fact]
    public void AnAtSignMakesAStringTakeItsBackslashesLiterally()
    {
        Read("\\document[Info{Title = @\"C:\\temp\\new\"}]{\\section{\\paragraph{t}}}")
            .Info.Title.Should().Be("C:\\temp\\new");
    }

    [Fact]
    public void TwoSlashesBeginAComment()
    {
        Read("\\document{ // and the rest of this line is not read\n\\section{\\paragraph{t}}}")
            .LastSection.Elements.Count.Should().Be(1);
    }

    /// <summary>
    ///   The only place <c>+=</c> and <c>-=</c> mean anything. The parser's own comment calls them
    ///   "hard-coded for TabStops only", and using either anywhere else is refused.
    /// </summary>
    [Fact]
    public void PlusEqualsAddsATabStopToAParagraph()
    {
        var paragraph = Read(
            "\\document{\\section{\\paragraph[Format{TabStops += \"3cm\"}]{t}}}")
            .LastSection.Elements[0] as Paragraph;

        paragraph.Format.TabStops.Count.Should().Be(1);
        paragraph.Format.TabStops[0].Position.Centimeter.Should().BeApproximately(3, 1e-4);
    }

    /// <summary>
    ///   <c>-=</c> marks a tab stop rather than deleting it. It has to: a paragraph's format is
    ///   read through the style it is based on, so cancelling a stop the base style declared means
    ///   recording that it is cancelled, and there would be nothing to delete in any case. The
    ///   flattening visitor is what finally drops the marked ones.
    /// </summary>
    [Fact]
    public void MinusEqualsMarksATabStopAsNotToBeAdded()
    {
        var document = Read(
            "\\document{\\section{\\paragraph[Format{TabStops += \"3cm\" TabStops += \"6cm\""
            + " TabStops -= \"3cm\"}]{t}}}");
        var stops = (document.LastSection.Elements[0] as Paragraph).Format.TabStops;

        stops.Count.Should().Be(2, "the cancelled one is still recorded, as a cancellation");
        stops[0].Position.Centimeter.Should().BeApproximately(3, 1e-4);
        stops[1].Position.Centimeter.Should().BeApproximately(6, 1e-4);

        // Which of the two is the cancellation is held in an internal field, and the DDL written
        // back out is where it shows: the writer emits the same '-=' the reader was given.
        DdlWriter.WriteToString(document).Should().Contain("TabStops -= \"3cm\"");
    }

    [Fact]
    public void PlusEqualsAgainstAnythingButTabStopsIsRefused()
    {
        ComplaintsAbout("\\document{\\section{\\paragraph[Format{SpaceBefore += \"1cm\"}]{t}}}")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void PunctuationInsideAParagraphIsJustTheTextOfIt()
    {
        // Every character the punctuator scanner has an arm for, in a paragraph, where none of
        // them punctuates anything. They must come back as the text they are.
        var paragraph = FirstParagraphOf("a;b.c,d%e$f@g#h/i+j-k=l:m");

        string.Concat(paragraph.Elements.OfType<Text>().Select(t => t.Content))
            .Should().Be("a;b.c,d%e$f@g#h/i+j-k=l:m");
    }

    /// <summary>
    ///   The lookahead used to read one character past the end of the document when the character
    ///   it was asked about was a trailing '+' or '-', because its bound was written
    ///   <c>ddlLength &gt;= index + 1</c> where it needed <c>&gt;</c>. Its twin
    ///   <c>ScanPunctuator</c> never had the fault: it looks at <c>nextChar</c>, which is null at
    ///   the end of the buffer rather than past it. See the backlog spec's finding F6.
    /// </summary>
    [Fact]
    public void ASignAtTheVeryEndOfTheDocumentIsNotReadPastTheEndOfIt()
    {
        foreach (var trailing in new[] { "+", "-", "+=", "-=" })
        {
            var read = () => ComplaintsAbout("\\document{\\section{\\paragraph{t}}}" + trailing);

            read.Should().NotThrow<IndexOutOfRangeException>(
                "a document ending in '{0}' is malformed, not a reason to read off the end",
                trailing);
        }
    }
}
