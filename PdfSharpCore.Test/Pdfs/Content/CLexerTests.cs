using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using PdfSharpCore.Pdf.Content;
using Xunit;

namespace PdfSharpCore.Test.Pdfs.Content
{
    /// <summary>
    /// A content stream can be truncated part way through a token, or hold a character the
    /// token it is in the middle of has no use for, and every scan loop has to keep advancing
    /// through both. The timeouts turn a regression into a failure rather than into an
    /// exhausted heap.
    /// </summary>
    public class CLexerTests
    {
        // A trailing delimiter is not enough to make this uninteresting: ScanNextChar discards
        // the byte still held in _nextChar once the content is exhausted, so a name at the end
        // of a content stream never sees its own delimiter either way.
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
        // Truncated hex strings are not here: they terminate, but ScanNextChar mangles the last
        // byte of the content, so '<48656C6C6F' scans to "HellF`" rather than to "Hello".
        public async Task ScanHexadecimalString_readsTheBytesTheDigitsSpell(string content, string expected)
        {
            var tokens = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

            TokensOf(tokens, CSymbol.HexString).Should().Equal(BytesSpelling(expected));
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
        /// Scans the whole content on a worker thread, so that the Timeout on these tests can
        /// interrupt a scan that never ends. Each token is taken while it is still current,
        /// since the next scan clears it.
        /// </summary>
        static Task<List<(CSymbol Symbol, string Token)>> ScanAll(CLexer lexer)
        {
            return Task.Run(() =>
            {
                var tokens = new List<(CSymbol, string)>();
                CSymbol symbol;
                while ((symbol = lexer.ScanNextToken()) != CSymbol.Eof)
                    tokens.Add((symbol, lexer.Token));
                return tokens;
            });
        }
    }
}
