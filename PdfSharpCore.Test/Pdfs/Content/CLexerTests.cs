using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Pdfs.Content;

/// <summary>
/// A content stream can be truncated part way through a token, or hold a character the
/// token it is in the middle of has no use for, and every scan loop has to keep advancing
/// through both. The timeouts turn a regression into a failure rather than into an
/// exhausted heap.
/// </summary>
public class CLexerTests
{
    // A name at the end of a content stream has no delimiter to end it, whether or not one
    // is written: _nextChar is read one character ahead, so a trailing blank is still in it
    // when the content runs out.
    [Theory(Timeout = 5000)]
    [InlineData("/Foo")]
    [InlineData("/Foo ")]
    [InlineData("BT /Foo")]
    public async Task ScanNextToken_terminatesForANameThatEndsTheContentStream(string content)
    {
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

        TokensOf(tokens, CSymbol.Name).Should().Equal("/Foo");
    }

    [Theory(Timeout = 5000)]
    [InlineData("<< /W 16", CSymbol.Dictionary)]
    [InlineData("(unterminated", CSymbol.String)]
    [InlineData("<48656C6C6F", CSymbol.HexString)]
    public async Task ScanNextToken_terminatesForATruncatedToken(string content, CSymbol expected)
    {
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

        tokens.Should().Contain(token => token.Symbol == expected);
    }

    [Fact(Timeout = 5000)]
    public async Task ScanNextToken_terminatesForATruncatedUnicodeString()
    {
        // An opening parenthesis, the UTF-16BE byte order mark that puts ScanLiteralString
        // on its 16-bit path, one character, and then nothing more.
        var content = new byte[] { (byte)'(', 0xFE, 0xFF, 0x00, (byte)'A' };

        var tokens = await ScanAll(new CLexer(content));

        tokens.Should().Contain(token => token.Symbol == CSymbol.String);
    }

    [Theory(Timeout = 5000)]
    [InlineData("<48656C6C6F>", "48,65,6C,6C,6F")]
    // The two digits of a byte may be separated by white space, which is ignored.
    [InlineData("<4 8 65>", "48,65")]
    // A hex string with an odd number of digits ends in a zero, so the digit left over is
    // the high one: '<A>' is 0xA0 rather than 0x0A.
    [InlineData("<48A>", "48,A0")]
    [InlineData("<A>", "A0")]
    // A character that is neither '>' nor a hex digit is stepped over. Before, '*' matched
    // no branch and the scan never advanced, and 'G' was taken for a digit and threw.
    [InlineData("<48*65>", "48,65")]
    [InlineData("<48G65>", "48,65")]
    [InlineData("<48 * 65>", "48,65")]
    // Including when it falls between the two digits of one byte, which is where white
    // space is passed over as well.
    [InlineData("<4*8>", "48")]
    // Truncated, so the closing '>' never arrives and the last digit is the last byte of
    // the content.
    [InlineData("<48656C6C6F", "48,65,6C,6C,6F")]
    [InlineData("<48A", "48,A0")]
    public async Task ScanHexadecimalString_readsTheBytesTheDigitsSpell(string content, string expected)
    {
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

        TokensOf(tokens, CSymbol.HexString).Should().Equal(BytesSpelling(expected));
    }

    [Theory(Timeout = 5000)]
    [InlineData("BT", CSymbol.Operator, "BT")]
    [InlineData("q Q", CSymbol.Operator, "Q")]
    [InlineData("1 0 0 1 20 30 cm", CSymbol.Operator, "cm")]
    [InlineData("/F1 12", CSymbol.Integer, "12")]
    [InlineData("0.5", CSymbol.Real, "0.5")]
    [InlineData("/Foo", CSymbol.Name, "/Foo")]
    public async Task ScanNextToken_readsTheLastCharacterOfTheContent(string content, CSymbol expected, string token)
    {
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

        tokens.Last().Should().Be((expected, token));
    }

    [Fact(Timeout = 5000)]
    public async Task ScanNextToken_readsALastCharacterThatEndsALine()
    {
        // A lone CR is a line feed, and the one that ends the content has nothing to pair
        // with. It ends the operator rather than being scanned as part of it.
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes("BT\r")));

        tokens.Last().Should().Be((CSymbol.Operator, "BT"));
    }

    // The escape sequences a literal string may carry. Each is written into the content as a
    // backslash and a character, and comes out of the scanner as the one character it stands
    // for, so a token the same length as the text that spelled it means an escape was missed.
    [Theory(Timeout = 5000)]
    [InlineData(@"(a\nb)", "a\nb")]
    [InlineData(@"(a\rb)", "a\rb")]
    [InlineData(@"(a\tb)", "a\tb")]
    [InlineData(@"(a\bb)", "a\bb")]
    [InlineData(@"(a\fb)", "a\fb")]
    [InlineData(@"(a\(b)", "a(b")]
    [InlineData(@"(a\)b)", "a)b")]
    [InlineData(@"(a\\b)", @"a\b")]
    public async Task ScanLiteralString_readsAnEscapedCharacterAsTheOneItStandsFor(string content, string expected)
    {
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

        TokensOf(tokens, CSymbol.String).Should().Equal(expected);
    }

    // A backslash and up to three octal digits are one character. The scan stops at the third
    // digit whether or not a fourth follows, so the digits after it are text.
    [Theory(Timeout = 5000)]
    [InlineData(@"(\101)", "A")]
    [InlineData(@"(\1)", "\u0001")]
    [InlineData(@"(\12)", "\n")]
    [InlineData(@"(\0)", "\0")]
    [InlineData(@"(\377)", "ÿ")]
    [InlineData(@"(\1012)", "A2")]
    [InlineData(@"(\101\102)", "AB")]
    // Octal runs to '7'. An '8' or a '9' cannot belong to a code, so it ends one already begun
    // and otherwise loses only its backslash, like any escape the scanner does not know. The
    // test for a digit used to be char.IsDigit, which let both in: '\8' came out as a backspace
    // rather than as the digit it is, and '\18' as a tab rather than as two characters.
    [InlineData(@"(\8)", "8")]
    [InlineData(@"(\9)", "9")]
    [InlineData(@"(\18)", "\u0001" + "8")]
    [InlineData(@"(\118)", "\t" + "8")]
    public async Task ScanLiteralString_readsAnOctalCodeAsOneCharacter(string content, string expected)
    {
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

        TokensOf(tokens, CSymbol.String).Should().Equal(expected);
    }

    /// <summary>
    /// A backslash at the end of a line continues the string onto the next one, and neither the
    /// backslash nor the line feed is part of it.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task ScanLiteralString_joinsTheLinesABackslashContinues()
    {
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes("(a\\\nb)")));

        TokensOf(tokens, CSymbol.String).Should().Equal("ab");
    }

    /// <summary>
    /// A backslash before anything else is dropped and the character is kept, which is how a
    /// string carrying an escape the specification does not define still scans.
    /// </summary>
    [Theory(Timeout = 5000)]
    [InlineData(@"(a\qb)", "aqb")]
    [InlineData(@"(a\ b)", "a b")]
    public async Task ScanLiteralString_keepsTheCharacterAfterAnEscapeItDoesNotKnow(string content, string expected)
    {
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

        TokensOf(tokens, CSymbol.String).Should().Equal(expected);
    }

    // Parentheses nest, so an inner pair is part of the string and only the one that closes the
    // outermost level ends it.
    [Theory(Timeout = 5000)]
    [InlineData("(a(b)c)", "a(b)c")]
    [InlineData("((nested))", "(nested)")]
    [InlineData("(a(b(c)d)e)", "a(b(c)d)e")]
    [InlineData("()", "")]
    public async Task ScanLiteralString_readsNestedParenthesesAsPartOfTheString(string content, string expected)
    {
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

        TokensOf(tokens, CSymbol.String).Should().Equal(expected);
    }

    /// <summary>
    /// A byte order mark of FE FF puts the scan on its 16-bit path, where every character is two
    /// bytes rather than one.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task ScanLiteralString_readsAUnicodeStringTwoBytesAtATime()
    {
        var content = new byte[] { (byte)'(', 0xFE, 0xFF, 0x00, (byte)'H', 0x00, (byte)'i', (byte)')' };

        var tokens = await ScanAll(new CLexer(content));

        TokensOf(tokens, CSymbol.String).Should().Equal("Hi");
    }

    /// <summary>
    /// Characters whose high byte is zero would read the same whether the two bytes were combined
    /// or the high one simply dropped, so a string of them cannot tell the two apart. These are
    /// above the Latin block and fail if the high byte is not carried.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task ScanLiteralString_carriesTheHighByteOfAUnicodeCharacter()
    {
        // U+03A9 GREEK CAPITAL LETTER OMEGA and U+20AC EURO SIGN.
        var content = new byte[] { (byte)'(', 0xFE, 0xFF, 0x03, 0xA9, 0x20, 0xAC, (byte)')' };

        var tokens = await ScanAll(new CLexer(content));

        TokensOf(tokens, CSymbol.String).Should().Equal("Ω€");
    }

    [Fact(Timeout = 5000)]
    public async Task ScanLiteralString_readsAUnicodeStringWithNothingInIt()
    {
        var content = new byte[] { (byte)'(', 0xFE, 0xFF, (byte)')' };

        var tokens = await ScanAll(new CLexer(content));

        TokensOf(tokens, CSymbol.String).Should().Equal("");
    }

    /// <summary>
    /// Builds the token a run of bytes scans to, one char per byte, from a comma separated
    /// list of hexadecimal byte values.
    /// </summary>
    static string BytesSpelling(string byteValues)
    {
        return new string(byteValues.Split(',')
            .Select(value => (char)Convert.ToInt32(value, 16))
            .ToArray());
    }

    // ----- literal strings ------------------------------------------------------------------------

    [Theory(Timeout = 5000)]
    [InlineData("(plain)", "plain")]
    [InlineData("()", "")]
    [InlineData("(a(nested)b)", "a(nested)b", "a bracketed run is part of the string")]
    [InlineData("(a((two deep))b)", "a((two deep))b")]
    [InlineData("(a\\(b)", "a(b", "and an escaped bracket needs no partner")]
    [InlineData("(a\\)b)", "a)b")]
    [InlineData("(a\\\\b)", "a\\b")]
    public async Task ScanLiteralString_readsBracketsWhetherBalancedOrEscaped(
        string content, string expected, string because = "")
    {
        var tokens = await ScanAll(new CLexer(Encoding.Latin1.GetBytes(content)));

        TokensOf(tokens, CSymbol.String).Should().Equal(new[] { expected }, because);
    }

    [Theory(Timeout = 5000)]
    [InlineData("(a\\nb)", "a\nb")]
    [InlineData("(a\\rb)", "a\rb")]
    [InlineData("(a\\tb)", "a\tb")]
    [InlineData("(a\\bb)", "a\bb")]
    [InlineData("(a\\fb)", "a\fb")]
    public async Task ScanLiteralString_readsEveryNamedEscape(string content, string expected)
    {
        var tokens = await ScanAll(new CLexer(Encoding.Latin1.GetBytes(content)));

        TokensOf(tokens, CSymbol.String).Should().Equal(expected);
    }

    [Theory(Timeout = 5000)]
    [InlineData("(\\101)", "A", "three digits")]
    [InlineData("(\\10)", "\b", "two")]
    [InlineData("(\\7)", "\a", "and one")]
    [InlineData("(\\1011)", "A1", "a fourth digit is text, not part of the code")]
    [InlineData("(\\0)", "\0", "and nought is a character like any other")]
    public async Task ScanLiteralString_readsAnOctalCodeOfOneTwoOrThreeDigits(
        string content, string expected, string because)
    {
        var tokens = await ScanAll(new CLexer(Encoding.Latin1.GetBytes(content)));

        TokensOf(tokens, CSymbol.String).Should().Equal(new[] { expected }, because);
    }

    [Theory(Timeout = 5000)]
    [InlineData("(a\\8b)", "a8b")]
    [InlineData("(a\\9b)", "a9b")]
    public async Task ScanLiteralString_keepsTheDigitWhenItIsNotAnOctalOne(
        string content, string expected)
    {
        // Eight and nine end an octal code rather than extending one, so the backslash is
        // dropped and the digit kept as the text it is.
        var tokens = await ScanAll(new CLexer(Encoding.Latin1.GetBytes(content)));

        TokensOf(tokens, CSymbol.String).Should().Equal(expected);
    }

    [Theory(Timeout = 5000)]
    [InlineData("(a\\\nb)")]
    [InlineData("(a\\\rb)")]
    [InlineData("(a\\\r\nb)")]
    public async Task ScanLiteralString_treatsABackslashBeforeAnEndOfLineAsAContinuation(
        string content)
    {
        // A long string may be broken across lines of the file without the break becoming part
        // of it. All three spellings of an end of line have to work, which they do because the
        // scanner turns them all into one before the escape is looked at.
        var tokens = await ScanAll(new CLexer(Encoding.Latin1.GetBytes(content)));

        TokensOf(tokens, CSymbol.String).Should().Equal("ab");
    }

    /// <summary>
    ///   Content that stops in the middle of a string. The scanner gives up at the end rather
    ///   than scanning for ever, and what it has read so far is the string - but a backslash
    ///   immediately before the end used to put the end-of-file marker itself into the text,
    ///   because the escape read past the guard that watches for it. See the backlog spec's
    ///   finding F16.
    /// </summary>
    [Theory(Timeout = 5000)]
    [InlineData("(unterminated", "unterminated")]
    [InlineData("(a\\", "a")]
    [InlineData("(", "")]
    [InlineData("(a(unclosed inner", "a(unclosed inner")]
    public async Task ScanLiteralString_endsAtTheEndOfTheContentWithoutInventingCharacters(
        string content, string expected)
    {
        var tokens = await ScanAll(new CLexer(Encoding.Latin1.GetBytes(content)));

        TokensOf(tokens, CSymbol.String).Should().Equal(expected);
    }

    [Fact(Timeout = 5000)]
    public async Task ScanLiteralString_readsAStringThatIsNothingButEscapes()
    {
        var tokens = await ScanAll(new CLexer(Encoding.Latin1.GetBytes("(\\n\\r\\t\\\\\\(\\))")));

        TokensOf(tokens, CSymbol.String).Should().Equal("\n\r\t\\()");
    }

    /// <summary>
    ///   <c>d0</c> and <c>d1</c> are the only content operators with a digit in them, and a
    ///   Type 3 glyph description has to begin with one of the two. The scanner ended an operator
    ///   at the first character that was not a letter, so it read the setdash operator <c>d</c>
    ///   and left the digit to become an operand of whatever came next - which meant every
    ///   operator in every Type 3 glyph was handed one operand too many, and the first of them
    ///   was a number where a name should be. See the backlog spec's finding F15.
    /// </summary>
    [Theory(Timeout = 5000)]
    [InlineData("1000 0 d0 /Im1 Do", "d0")]
    [InlineData("1000 0 0 0 200 200 d1 /Im1 Do", "d1")]
    public async Task ScanNextToken_readsTheGlyphMetricOperatorsAsOneTokenEach(
        string content, string expected)
    {
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

        TokensOf(tokens, CSymbol.Operator).Should().Equal(expected, "Do");
    }

    [Fact(Timeout = 5000)]
    public async Task ScanNextToken_stillReadsSetdashAsItself()
    {
        // The operator the two are told apart from. A digit only joins a 'd' when it follows it
        // with nothing in between, which is never how setdash and its next operand are written.
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes("[3 3] 0 d 0 0 m")));

        TokensOf(tokens, CSymbol.Operator).Should().Equal("d", "m");
    }

    [Theory(Timeout = 5000)]
    [InlineData("d2")]
    [InlineData("d9")]
    public async Task ScanNextToken_joinsNoOtherDigitToAnOperator(string content)
    {
        // There is no d2, so the digit is an operand of whatever follows rather than part of the
        // operator - which is what the scanner did for d0 and d1 too, and should not have.
        var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

        TokensOf(tokens, CSymbol.Operator).Should().Equal("d");
    }

    static IEnumerable<string> TokensOf(IEnumerable<(CSymbol Symbol, string Token)> tokens, CSymbol symbol)
    {
        return tokens.Where(token => token.Symbol == symbol).Select(token => token.Token);
    }

    /// <summary>
    /// Scans the whole content on a thread of its own, so that the Timeout on these tests can
    /// interrupt a scan that never ends. Each token is taken while it is still current,
    /// since the next scan clears it.
    /// </summary>
    static Task<List<(CSymbol Symbol, string Token)>> ScanAll(CLexer lexer)
    {
        return Interruptibly.Run(() =>
        {
            var tokens = new List<(CSymbol, string)>();
            CSymbol symbol;
            while ((symbol = lexer.ScanNextToken()) != CSymbol.Eof)
                tokens.Add((symbol, lexer.Token));
            return tokens;
        });
    }
}
