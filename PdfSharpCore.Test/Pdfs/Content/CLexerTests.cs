using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using PdfSharpCore.Pdf.Content;
using Xunit;

namespace PdfSharpCore.Test.Pdfs.Content
{
    /// <summary>
    /// A content stream can be truncated part way through a token, and every scan loop has to
    /// give up at the end of the content instead of scanning Chars.EOF for ever. The timeouts
    /// turn a regression into a failure rather than into an exhausted heap.
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
            var lexer = new CLexer(Encoding.ASCII.GetBytes(content));
            var names = new List<string>();

            var symbols = await ScanAll(lexer, () => names.Add(lexer.Token));

            symbols.Should().Contain(CSymbol.Name);
            names.Should().Equal("/Foo");
        }

        [Theory(Timeout = 5000)]
        [InlineData("<< /W 16", CSymbol.Dictionary)]
        [InlineData("(unterminated", CSymbol.String)]
        [InlineData("<48656C6C6F", CSymbol.HexString)]
        public async Task ScanNextToken_terminatesForATruncatedToken(string content, CSymbol expected)
        {
            var symbols = await ScanAll(new CLexer(Encoding.ASCII.GetBytes(content)));

            symbols.Should().Contain(expected);
        }

        [Fact(Timeout = 5000)]
        public async Task ScanNextToken_terminatesForATruncatedUnicodeString()
        {
            // An opening parenthesis, the UTF-16BE byte order mark that puts ScanLiteralString
            // on its 16-bit path, one character, and then nothing more.
            var content = new byte[] { (byte)'(', 0xFE, 0xFF, 0x00, (byte)'A' };

            var symbols = await ScanAll(new CLexer(content));

            symbols.Should().Contain(CSymbol.String);
        }

        /// <summary>
        /// Scans the whole content on a worker thread, so that the Timeout on these tests can
        /// interrupt a scan that never ends. The token of every name is handed to onName while
        /// it is still current, since the next scan clears it.
        /// </summary>
        static Task<List<CSymbol>> ScanAll(CLexer lexer, Action onName = null)
        {
            return Task.Run(() =>
            {
                var symbols = new List<CSymbol>();
                CSymbol symbol;
                while ((symbol = lexer.ScanNextToken()) != CSymbol.Eof)
                {
                    symbols.Add(symbol);
                    if (symbol == CSymbol.Name && onName != null)
                        onName();
                }
                return symbols;
            });
        }
    }
}
