using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel.Fields;
using MigraDocCore.DocumentObjectModel.IO;
using MigraDocCore.DocumentObjectModel.Shapes;
using MigraDocCore.DocumentObjectModel.Tables;
using Xunit;

namespace MigraDocCore.DocumentObjectModel.Tests;

/// <summary>
///   Reading MDDDL: the scanner that turns text into tokens and the parser that turns tokens into
///   a document. Between them they are the most involved code in the assembly - the scanner has an
///   arm per punctuator and a switch of escape sequences, and the parser has to decide at every
///   point whether what follows is more of the paragraph or the end of it.
///   <para>
///   Everything here goes in as text and comes out as a model, because that is how the two are
///   used and because the alternative - reaching the scanner directly - would pin the token stream
///   rather than the meaning. A test that reads a document is a test of both halves at once.
///   </para>
/// </summary>
public class DdlReadingTests
{
    static Document Read(string ddl) => DdlReader.DocumentFromString(ddl);

    static Paragraph FirstParagraphOf(string paragraphBody) =>
        Read("\\document{\\section{\\paragraph{" + paragraphBody + "}}}")
            .LastSection.Elements[0] as Paragraph;

    /// <summary>
    ///   A document whose title is the given literal, written the way the serializer writes one:
    ///   the info block is an attribute of the document rather than a keyword of its own. The
    ///   shortest path there is from a quoted string in a file to a string in the model.
    /// </summary>
    static string TitleOf(string quotedLiteral) =>
        Read("\\document[Info{Title = " + quotedLiteral + "}]{\\section{\\paragraph{t}}}").Info.Title;

    static string TextOf(Paragraph paragraph) =>
        string.Concat(paragraph.Elements.OfType<Text>().Select(text => text.Content));

    static string TextOf2(FormattedText formatted) =>
        string.Concat(formatted.Elements.OfType<Text>().Select(text => text.Content));

    // ----- plain text, and what ends it -------------------------------------------------------------

    [Fact]
    public void APlainParagraphIsTheTextInIt()
    {
        TextOf(FirstParagraphOf("Hello")).Should().Be("Hello");
    }

    [Fact]
    public void RunsOfSpaceInAParagraphAreOneSpace()
    {
        // Which is what makes DDL layout-insensitive: a paragraph indented across three lines of a
        // file reads as one line of prose.
        TextOf(FirstParagraphOf("one   two\n   three")).Should().Be("one two three");
    }

    [Theory]
    [InlineData("a\\{b", "a{b")]
    [InlineData("a\\}b", "a}b")]
    [InlineData("a\\\\b", "a\\b")]
    public void TheCharactersThatWouldEndAParagraphCanBeWrittenInIt(string ddl, string expected)
    {
        // A brace is how the parser knows the paragraph is over, so a paragraph containing one has
        // to escape it - the same bargain every quoting scheme makes.
        TextOf(FirstParagraphOf(ddl)).Should().Be(expected);
    }

    [Fact]
    public void TheCharactersThatAreNotLettersAreElementsOfTheirOwn()
    {
        // A tab, a run of spaces and a named symbol are each a Character in the model rather than
        // part of the text around them, so the text either side of one stays in its own run.
        var paragraph = FirstParagraphOf("a\\tab b\\space(2)c\\symbol(Euro)d");

        paragraph.Elements.OfType<Character>().Should().HaveCount(3);
        TextOf(paragraph).Should().Be("abcd", "the characters are not text");
    }

    [Fact]
    public void ALineBreakInsideAParagraphIsAnElementRatherThanTextEnding()
    {
        var paragraph = FirstParagraphOf("before\\linebreak after");

        paragraph.Elements.OfType<Character>().Should().ContainSingle();
        paragraph.Elements.OfType<Text>().Should().HaveCount(2, "one run either side of the break");
        TextOf(paragraph).Should().Be("beforeafter",
            "the space after the keyword separated it from the text and is not text itself");
    }

    // ----- string literals and their escapes ---------------------------------------------------------

    [Theory]
    [InlineData("\\a", "\a")]
    [InlineData("\\b", "\b")]
    [InlineData("\\f", "\f")]
    [InlineData("\\n", "\n")]
    [InlineData("\\r", "\r")]
    [InlineData("\\t", "\t")]
    [InlineData("\\v", "\v")]
    [InlineData("\\'", "'")]
    [InlineData("\\\"", "\"")]
    [InlineData("\\\\", "\\")]
    public void EveryEscapeAStringLiteralAllowsIsRead(string escaped, string expected)
    {
        TitleOf("\"x" + escaped + "y\"").Should().Be("x" + expected + "y");
    }

    /// <summary>
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The scanner recognises <c>\x</c> followed by hex digits and then does not convert it. The
    ///   line that would is commented out and five literal question marks are appended in its
    ///   place:
    ///   <code>
    ///   if (hexNrCount &lt;= 2)
    ///     str += "?????"; //(char)AscULongFromHexString(hexString);
    ///   </code>
    ///   </para>
    ///   <para>
    ///   Three things follow. The escape produces <c>?????</c> rather than a character, so text
    ///   comes back longer than it went in. The character after the digits is swallowed, because
    ///   the loop that reads them has already stopped on it and the scanner advances again anyway.
    ///   And the test is the wrong way round: one or two digits give the question marks, while
    ///   three or more throw - so the escapes most likely to be written are the ones that fail
    ///   quietly, and only an over-long one is refused.
    ///   </para>
    ///   <para>
    ///   Nothing in the DOM writes <c>\x</c> - the serializer emits the character itself - so this
    ///   only bites a file written by hand or by another tool.
    ///   </para>
    /// </remarks>
    [Fact]
    public void AHexEscapeIsNotConvertedAndEatsTheCharacterAfterIt()
    {
        TitleOf("\"x\\x41y\"").Should().Be("x?????",
            "the A is five question marks and the y is gone");

        var tooMany = () => TitleOf("\"x\\x0041y\"");
        tooMany.Should().Throw<Exception>("three or more digits is the case that is refused");
    }

    [Fact]
    public void AUnicodeEscapeIsNotPartOfTheGrammarAtAll()
    {
        var act = () => TitleOf("\"x\\u0041y\"");

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void AVerbatimStringTakesItsBackslashesLiterally()
    {
        // Which is what makes a Windows path writable without doubling every separator.
        TitleOf("@\"C:\\temp\\new\"").Should().Be("C:\\temp\\new");
    }

    [Fact]
    public void AStringCanBeEmpty()
    {
        TitleOf("\"\"").Should().BeEmpty();
    }

    // ----- comments and whitespace between tokens ------------------------------------------------------

    [Fact]
    public void ACommentInTheCodeIsIgnored()
    {
        var document = Read(
            "\\document\n"
            + "// a note to whoever reads this file\n"
            + "[\n"
            + "  Info { Title = \"Kept\" } // and another\n"
            + "]\n"
            + "{\n"
            + "  \\section{\\paragraph{t}}\n"
            + "}");

        document.Info.Title.Should().Be("Kept");
    }

    [Fact]
    public void TheAttributeBlockCanBeLaidOutHoweverTheWriterLikes()
    {
        // Punctuators are read one at a time with whatever space around them, so these two are the
        // same document written two ways.
        var tight = Read("\\document{\\section[PageSetup{PageFormat=A5}]{\\paragraph{t}}}");
        var loose = Read(
            "\\document{\n  \\section [ PageSetup { PageFormat = A5 } ]\n  {\n    \\paragraph{t}\n  }\n}");

        tight.LastSection.PageSetup.PageFormat.Should().Be(PageFormat.A5);
        loose.LastSection.PageSetup.PageFormat.Should().Be(PageFormat.A5);
    }

    // ----- formatted text ---------------------------------------------------------------------------------

    [Fact]
    public void FormattedTextCarriesItsOwnFontAndTheTextInside()
    {
        var paragraph = FirstParagraphOf("plain \\font[Bold = true Color = Red]{loud} plain");

        var formatted = paragraph.Elements.OfType<FormattedText>().Single();
        formatted.Font.Bold.Should().BeTrue();
        formatted.Font.Color.Should().Be(Colors.Red);
        string.Concat(formatted.Elements.OfType<Text>().Select(t => t.Content)).Should().Be("loud");
    }

    [Fact]
    public void FormattedTextNestsInsideFormattedText()
    {
        // Which is the case the parser has to recurse for: the inner block ends at its own closing
        // brace rather than at the first one it meets.
        var paragraph = FirstParagraphOf("\\font[Bold = true]{bold \\font[Italic = true]{both} bold}");

        var outer = paragraph.Elements.OfType<FormattedText>().Single();
        outer.Font.Bold.Should().BeTrue();
        var inner = outer.Elements.OfType<FormattedText>().Single();
        inner.Font.Italic.Should().BeTrue();
    }

    [Theory]
    [InlineData("\\bold{x}")]
    [InlineData("\\italic{x}")]
    [InlineData("\\underline{x}")]
    public void TheShorthandsForFormattingAreFormattedTextToo(string ddl)
    {
        FirstParagraphOf(ddl).Elements.OfType<FormattedText>().Should().ContainSingle();
    }

    [Fact]
    public void FormattedTextCanNameAStyleRatherThanAFont()
    {
        var paragraph = FirstParagraphOf("\\font(\"Heading1\"){titled}");

        paragraph.Elements.OfType<FormattedText>().Single().Style.Should().Be("Heading1");
    }

    // ----- fields ------------------------------------------------------------------------------------------

    [Fact]
    public void APageFieldIsReadAsOne()
    {
        FirstParagraphOf("page \\field(Page)[] of \\field(NumPages)[]")
            .Elements.OfType<PageField>().Should().ContainSingle();
    }

    [Fact]
    public void EveryKindOfFieldIsReadAsItsOwnKind()
    {
        var paragraph = FirstParagraphOf(
            "\\field(Page)[]\\field(NumPages)[]\\field(Section)[]\\field(Date)[]"
            + "\\field(Bookmark)[Name = \"top\"]\\field(PageRef)[Name = \"top\"]");

        paragraph.Elements.OfType<PageField>().Should().ContainSingle();
        paragraph.Elements.OfType<NumPagesField>().Should().ContainSingle();
        paragraph.Elements.OfType<SectionField>().Should().ContainSingle();
        paragraph.Elements.OfType<DateField>().Should().ContainSingle();
        paragraph.Elements.OfType<BookmarkField>().Should().ContainSingle();
        paragraph.Elements.OfType<PageRefField>().Should().ContainSingle();
    }

    [Fact]
    public void AFieldKeepsTheAttributesGivenToIt()
    {
        var paragraph = FirstParagraphOf("\\field(Date)[Format = \"yyyy-MM-dd\"]");

        paragraph.Elements.OfType<DateField>().Single().Format.Should().Be("yyyy-MM-dd");
    }

    [Fact]
    public void ABookmarkAndTheReferenceToItNameTheSamePlace()
    {
        var paragraph = FirstParagraphOf(
            "\\field(Bookmark)[Name = \"chapter\"]see \\field(PageRef)[Name = \"chapter\"]");

        paragraph.Elements.OfType<BookmarkField>().Single().Name.Should().Be("chapter");
        paragraph.Elements.OfType<PageRefField>().Single().Name.Should().Be("chapter");
    }

    [Fact]
    public void AnInfoFieldNamesWhichPieceOfInformationItWants()
    {
        var paragraph = FirstParagraphOf("\\field(Info)[Name = \"Title\"]");

        paragraph.Elements.OfType<InfoField>().Single().Name.Should().Be("Title");
    }

    // ----- the rest of what a paragraph can hold -------------------------------------------------------------

    [Fact]
    public void AFootnoteHangsAParagraphOffTheTextItFollows()
    {
        var paragraph = FirstParagraphOf("text\\footnote{the note}");

        var footnote = paragraph.Elements.OfType<Footnote>().Single();
        TextOf(footnote.Elements[0] as Paragraph).Should().Be("the note");
    }

    [Fact]
    public void AFootnoteCanCarryAttributesAndMoreThanOneParagraph()
    {
        var paragraph = FirstParagraphOf(
            "text\\footnote[Reference = \"*\"]{\\paragraph{first}\\paragraph{second}}");

        var footnote = paragraph.Elements.OfType<Footnote>().Single();
        footnote.Reference.Should().Be("*");
        footnote.Elements.Count.Should().Be(2);
    }

    [Fact]
    public void TheFontSizeShorthandIsFormattedTextWithASizeOnIt()
    {
        var paragraph = FirstParagraphOf("normal \\fontsize(14){larger} normal");

        var formatted = paragraph.Elements.OfType<FormattedText>().Single();
        formatted.Font.Size.Point.Should().BeApproximately(14, 1e-4);
        TextOf2(formatted).Should().Be("larger");
    }

    [Fact]
    public void TheFontSizeShorthandTakesAMeasureAsWellAsANumber()
    {
        FirstParagraphOf("\\fontsize(\"1cm\"){big}")
            .Elements.OfType<FormattedText>().Single()
            .Font.Size.Centimeter.Should().BeApproximately(1, 1e-4);
    }

    [Fact]
    public void TheFontColourShorthandIsFormattedTextWithAColourOnIt()
    {
        var paragraph = FirstParagraphOf("normal \\fontcolor(Red){loud} normal");

        paragraph.Elements.OfType<FormattedText>().Single().Font.Color.Should().Be(Colors.Red);
    }

    [Fact]
    public void TheFontColourShorthandReadsTheWholeColourGrammar()
    {
        FirstParagraphOf("\\fontcolor(RGB(1, 2, 3)){x}")
            .Elements.OfType<FormattedText>().Single()
            .Font.Color.Should().Be(new Color(1, 2, 3));
    }

    [Fact]
    public void ACharacterCanBeGivenByItsNumber()
    {
        // \chr(65) is the character A, and the count after it repeats one character rather than
        // writing a run of text - which is what keeps a row of dots out of the text runs.
        var paragraph = FirstParagraphOf("a\\chr(65)b");

        paragraph.Elements.OfType<Character>().Should().ContainSingle();
        TextOf(paragraph).Should().Be("ab", "the character is not part of the text either side");
    }

    [Fact]
    public void ASymbolCanBeNamedAndCounted()
    {
        var paragraph = FirstParagraphOf("a\\symbol(Euro, 3)b");

        var symbol = paragraph.Elements.OfType<Character>().Single();
        symbol.SymbolName.Should().Be(SymbolName.Euro);
        symbol.Count.Should().Be(3);
    }

    [Fact]
    public void ARunOfBlanksIsOneElementThatKnowsHowManyItIs()
    {
        var paragraph = FirstParagraphOf("a\\space(5)b");

        paragraph.Elements.OfType<Character>().Single().Count.Should().Be(5);
    }

    [Fact]
    public void ARunOfBlanksCanNameItsWidthInsteadOfItsCount()
    {
        // \space(Em) is a width rather than a count, which is a different arm of the same method.
        var paragraph = FirstParagraphOf("a\\space(Em)b");

        paragraph.Elements.OfType<Character>().Single().SymbolName.Should().Be(SymbolName.Em);
    }

    [Fact]
    public void TheNameOfASpaceWidthIsReadCaseSensitively()
    {
        // "Em" is the member of the enumeration and "em" is not, so the lower-case spelling a
        // person would reach for first is refused rather than taken for the same thing.
        var act = () => FirstParagraphOf("a\\space(em)b");

        act.Should().Throw<Exception>();
    }

    // ----- what a section can hold besides paragraphs ---------------------------------------------------------

    /// <summary>
    ///   Reading <c>\image("...")</c> does not merely record a path: it goes through
    ///   <c>ImageSource.ImageSourceImpl</c>, so a document object model with no imaging backend
    ///   registered cannot read a file that mentions an image at all.
    ///   <para>
    ///   Worth pinning here rather than covering elsewhere, because it is the one place the DOM
    ///   reaches past what a document <em>says</em> and into what it looks like - which is the
    ///   line this project is built on. A test that wants an image read belongs in
    ///   <c>MigraDocCore.Rendering.Tests</c>, where a backend is registered.
    ///   </para>
    /// </summary>
    [Fact]
    public void ReadingAnImageNeedsAnImagingBackendRatherThanJustThePath()
    {
        var act = () => Read("\\document{\\section{\\image(\"logo.png\")}}");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ImageSourceImpl*");
    }

    [Fact]
    public void ABarcodeIsReadWithTheCodeAndOptionallyTheKind()
    {
        var withKind = Read("\\document{\\section{\\barcode(\"12345\", Barcode39)}}");
        var withoutKind = Read("\\document{\\section{\\barcode(\"12345\")}}");

        (withKind.LastSection.Elements[0] as Barcode).Code.Should().Be("12345");
        (withKind.LastSection.Elements[0] as Barcode).Type.Should().Be(BarcodeType.Barcode39);
        (withoutKind.LastSection.Elements[0] as Barcode).Code.Should().Be("12345");
    }

    [Fact]
    public void ATextFrameIsReadWithWhatIsInsideIt()
    {
        var document = Read(
            "\\document{\\section{\\textframe[Width = \"5cm\"]{\\paragraph{floating}}}}");

        var frame = document.LastSection.Elements[0] as TextFrame;
        frame.Width.Centimeter.Should().BeApproximately(5, 1e-4);
        TextOf(frame.Elements[0] as Paragraph).Should().Be("floating");
    }

    [Fact]
    public void APageBreakIsAnElementOfItsOwn()
    {
        var document = Read("\\document{\\section{\\paragraph{one}\\pagebreak\\paragraph{two}}}");

        document.LastSection.Elements.Count.Should().Be(3);
        document.LastSection.Elements[1].Should().BeOfType<PageBreak>();
    }

    // ----- punctuation the grammar does not use -----------------------------------------------------------------

    /// <summary>
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The scanner recognises more punctuation than the grammar uses - a semicolon, a percent
    ///   sign, a dollar, an at, a hash, a dot and a currency sign each have an arm of their own,
    ///   and none of them means anything in a document. One of them between <c>\section</c> and
    ///   the brace that opens it does not stop the read and is not reported: the document comes
    ///   back with the section in it and <em>everything inside the section gone</em>.
    ///   </para>
    ///   <para>
    ///   So a single stray character - a typo, a stray byte from a bad merge - silently empties a
    ///   section. Losing the contents while keeping the container is the worst of the three
    ///   possible outcomes: refusing the file would be safe, reading it whole would be better, and
    ///   this looks like a document that was always empty.
    ///   </para>
    /// </remarks>
    [Theory]
    [InlineData(";")]
    [InlineData("%")]
    [InlineData("$")]
    [InlineData("#")]
    [InlineData("@")]
    [InlineData(".")]
    public void PunctuationThatMeansNothingEmptiesTheSectionWithoutAWord(string punctuation)
    {
        var errors = new DdlReaderErrors();
        var ddl = "\\document{\\section" + punctuation + "{\\paragraph{hello}}}";

        var read = (Document)DdlReader.ObjectFromString(ddl, errors);

        read.Sections.Count.Should().Be(1, "the section survives");
        read.LastSection.Elements.Count.Should().Be(0, "and its contents do not");
        errors.ErrorCount.Should().Be(0, "with nothing said about it");
    }

    [Fact]
    public void TheColonIsWhatSaysWhichStyleAStyleIsBasedOn()
    {
        // The one piece of punctuation with a meaning of its own outside the bracket and brace
        // pairs, which is why it is worth a test rather than a line in the theory above.
        var document = Read(
            "\\document{\\styles{Quiet : Normal{Font{Size = 8}}}\\section{\\paragraph{t}}}");

        document.Styles["Quiet"].BaseStyle.Should().Be("Normal");
    }

    // ----- hyperlinks ---------------------------------------------------------------------------------------

    [Fact]
    public void AHyperlinkKeepsWhereItGoesAndWhatItSays()
    {
        var paragraph = FirstParagraphOf("go \\hyperlink[Name = \"target\" Type = Local]{there}");

        var link = paragraph.Elements.OfType<Hyperlink>().Single();
        link.Name.Should().Be("target");
        link.Type.Should().Be(HyperlinkType.Local);
        string.Concat(link.Elements.OfType<Text>().Select(t => t.Content)).Should().Be("there");
    }

    // ----- the structures around the text ----------------------------------------------------------------------

    [Fact]
    public void ATableIsReadWithItsColumnsAndRowsAndTheCellsBetweenThem()
    {
        var document = Read(
            "\\document{\\section{\\table{"
            + "\\columns{\\column[Width = \"3cm\"]\\column[Width = \"4cm\"]}"
            + "\\rows{\\row{\\cell{one}\\cell{two}}\\row{\\cell{three}\\cell{four}}}"
            + "}}}");

        var table = document.LastSection.Elements[0] as Table;
        table.Columns.Count.Should().Be(2);
        table.Rows.Count.Should().Be(2);
        table.Columns[1].Width.Centimeter.Should().BeApproximately(4, 1e-4);
        TextOf(table[1, 0].Elements[0] as Paragraph).Should().Be("three");
    }

    [Fact]
    public void HeadersAndFootersAreReadOntoTheSectionTheyBelongTo()
    {
        var document = Read(
            "\\document{\\section{"
            + "\\header{\\paragraph{at the top}}"
            + "\\footer{\\paragraph{at the bottom}}"
            + "\\paragraph{in the middle}}}");

        TextOf(document.LastSection.Headers.Primary.Elements[0] as Paragraph).Should().Be("at the top");
        TextOf(document.LastSection.Footers.Primary.Elements[0] as Paragraph).Should().Be("at the bottom");
    }

    [Fact]
    public void MoreThanOneSectionIsReadAsMoreThanOneSection()
    {
        var document = Read("\\document{\\section{\\paragraph{one}}\\section{\\paragraph{two}}}");

        document.Sections.Count.Should().Be(2);
        TextOf(document.Sections[1].Elements[0] as Paragraph).Should().Be("two");
    }

    [Fact]
    public void AStyleBlockIsReadAndTheStyleIsThereToUse()
    {
        var document = Read(
            "\\document{\\styles{Quiet : Normal{Font{Size = 8 Italic = true}}}"
            + "\\section{\\paragraph[Style = \"Quiet\"]{small}}}");

        document.Styles["Quiet"].Should().NotBeNull();
        document.Styles["Quiet"].Font.Size.Point.Should().BeApproximately(8, 1e-4);
        (document.LastSection.Elements[0] as Paragraph).Style.Should().Be("Quiet");
    }

    // ----- what happens when the text is wrong -------------------------------------------------------------------

    [Fact]
    public void SomethingThatIsNotADocumentAtAllIsRefusedRatherThanReadAsAnEmptyOne()
    {
        var act = () => Read("this is not DDL");

        act.Should().Throw<System.Exception>();
    }

    /// <summary>
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   A file that ends in the middle of a section never finishes being read. Not an exception,
    ///   not an entry in the error list - the reader does not return, and the thread that called it
    ///   is gone for good. <c>\document{</c> on its own is reported properly, so the parser knows
    ///   how to complain about a truncated file; it is the loop over a section's contents that
    ///   stops asking whether the end of the file has arrived.
    ///   </para>
    ///   <para>
    ///   Every truncation past that point behaves the same way: <c>\document{\section{</c>,
    ///   <c>\document{\section{\paragraph{x}</c> and a paragraph left open all hang. That makes a
    ///   partly written or corrupt .mdddl file - a download that stopped, a file cut short by a
    ///   full disk - enough to hang whatever is reading it, with no way for the caller to time it
    ///   out short of abandoning the thread.
    ///   </para>
    ///   <para>
    ///   Run on a background thread and given two seconds, because a test that calls it directly
    ///   would take the test host with it. xUnit's own Timeout would not help: it is honoured only
    ///   on async tests, which is why <c>CLexerTests</c> wraps its malformed input the same way.
    ///   </para>
    /// </remarks>
    [Theory]
    [InlineData("\\document{\\section{")]
    [InlineData("\\document{\\section{\\paragraph{x}")]
    [InlineData("\\document{\\section{\\paragraph{never closed")]
    public async Task AFileThatStopsInsideASectionIsNeverFinishedWith(string truncated)
    {
        var reading = Task.Run(() => DdlReader.ObjectFromString(truncated, new DdlReaderErrors()));

        var finished = await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(2)));

        finished.Should().NotBeSameAs(reading,
            "the reader does not come back from a file that stops inside a section");
    }

    [Fact]
    public async Task AFileThatStopsBeforeTheFirstSectionIsReportedProperly()
    {
        // The same truncation one token earlier, which is handled - so the parser can complain
        // about a short file, and the defect above is the section loop rather than the idea.
        var reading = Task.Run(() =>
        {
            var act = () => DdlReader.ObjectFromString("\\document{", new DdlReaderErrors());
            act.Should().Throw<Exception>();
        });

        var finished = await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.Should().BeSameAs(reading, "this one comes back");
        await reading;
    }

    /// <summary>
    ///   An attribute the model has no property for is discarded without a word: no exception, and
    ///   nothing in the error list either. A misspelt attribute name in a hand-written file is
    ///   therefore invisible - the document reads, and the setting it was meant to carry is gone.
    ///   Recorded rather than argued with, because a reader that tolerates what it does not
    ///   understand is a defensible choice; being silent about it is the part worth knowing.
    /// </summary>
    [Fact]
    public void AnAttributeThatNamesNothingIsDiscardedWithoutAWord()
    {
        var errors = new DdlReaderErrors();

        var read = DdlReader.ObjectFromString(
            "\\document{\\section[PageSetup{NoSuchThing = 3}]{\\paragraph{t}}}", errors);

        read.Should().NotBeNull("the document still reads");
        errors.ErrorCount.Should().Be(0, "and nothing says the setting was dropped");
    }
}
