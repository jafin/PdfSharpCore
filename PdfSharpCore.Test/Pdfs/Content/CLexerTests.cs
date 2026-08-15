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
    [InlineData(@"(\1)", "")]
    [InlineData(@"(\12)", "\n")]
    [InlineData(@"(\0)", "\0")]
    [InlineData(@"(\377)", "ÿ")]
    [InlineData(@"(\1012)", "A2")]
    [InlineData(@"(\101\102)", "AB")]
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
