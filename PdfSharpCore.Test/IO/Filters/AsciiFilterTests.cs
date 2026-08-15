using System;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Pdf.Filters;
using Xunit;

namespace PdfSharpCore.Test.IO.Filters;

/// <summary>
///   The two filters that make a stream of bytes printable: ASCIIHexDecode, which spends two
///   characters per byte, and ASCII85Decode, which spends five per four and so costs a quarter
///   rather than double. Both are decoders by name and encoders as well in practice, because
///   PDFsharp writes streams as well as reading them, and the property that matters for both is
///   that the two directions agree.
///   <para>
///   ASCII85 is the one worth testing exhaustively. Its encoder has a separate arm for a trailing
///   group of one, two and three bytes, its decoder has a matching arm for each, and the decoder's
///   arms carry a correction its own author could not derive - "in some rare cases the value must
///   not be increased by one, but I cannot found [sic] a general formula or a proof". Round-tripping
///   every length settles all of it at once.
///   </para>
/// </summary>
public class AsciiFilterTests
{
    /// <summary>
    ///   Bytes that are the same on every run and every machine, and that avoid being accidentally
    ///   well-behaved: they span the whole byte range and are not periodic in four.
    /// </summary>
    static byte[] Bytes(int count)
    {
        var data = new byte[count];
        for (var idx = 0; idx < count; idx++)
            data[idx] = (byte)((idx * 37 + 11) % 256);
        return data;
    }

    public static TheoryData<int> EveryLengthUpTo(int count)
    {
        var data = new TheoryData<int>();
        for (var length = 0; length <= count; length++)
            data.Add(length);
        return data;
    }

    public static TheoryData<int> Lengths => EveryLengthUpTo(24);

    // ----- ASCII85 -------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Ascii85CarriesAnyNumberOfBytesThereAndBack(int length)
    {
        // Four bytes become five characters, and a trailing group of one, two or three becomes two,
        // three or four - so every length mod 4 takes a different arm through both directions.
        var original = Bytes(length);

        var encoded = Filtering.ASCII85Decode.Encode(original);
        var decoded = Filtering.ASCII85Decode.Decode(encoded, (FilterParms)null);

        decoded.Should().Equal(original);
    }

    /// <summary>
    ///   Runs of zero bytes whose length leaves no trailing group, or a trailing group of three.
    ///   The other two tail lengths are the defect below.
    /// </summary>
    public static TheoryData<int> ZeroRunLengthsThatSurvive =>
        new() { 0, 1, 2, 3, 4, 7, 8, 11, 12, 15, 16, 19, 20, 23, 24 };

    [Theory]
    [MemberData(nameof(ZeroRunLengthsThatSurvive))]
    public void Ascii85CarriesRunsOfZeroBytesThereAndBack(int length)
    {
        // Four zero bytes are written as the single character z rather than as five exclamation
        // marks, which is a branch of its own in both directions and the only one where the
        // character count does not follow from the byte count.
        var original = new byte[length];

        var encoded = Filtering.ASCII85Decode.Encode(original);
        var decoded = Filtering.ASCII85Decode.Decode(encoded, (FilterParms)null);

        decoded.Should().Equal(original);
    }

    /// <summary>
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The decoder's main loop runs <c>while (idx + 4 &lt; length)</c>, which assumes every turn
    ///   of it consumes five characters. A <c>z</c> consumes one. So a stream that uses the
    ///   shortcut leaves the loop with four characters still to read, and the code that reads the
    ///   trailing group reads <c>z</c> characters as though they were part of it.
    ///   </para>
    ///   <para>
    ///   It bites when a <c>z</c> is followed by a trailing group of two or three characters -
    ///   lengths of 4n+1 and 4n+2 bytes. A group of four leaves the loop in the right place by
    ///   coincidence, which is why three quarters of the round trips above pass.
    ///   </para>
    ///   <para>
    ///   The encoder in this same class writes those streams, so this is PDFsharp failing to read
    ///   back what PDFsharp wrote, and zero runs are exactly what image and embedded-file data is
    ///   full of. The bytes come back in the wrong places rather than merely mangled at the end.
    ///   </para>
    /// </remarks>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(14)]
    public void Ascii85LosesARunOfZeroBytesThatEndsInAPartialGroup(int length)
    {
        var original = new byte[length];

        var encoded = Filtering.ASCII85Decode.Encode(original);
        var decoded = Filtering.ASCII85Decode.Decode(encoded, (FilterParms)null);

        decoded.Should().HaveCount(length, "only the contents are wrong, not the length");
        decoded.Should().NotEqual(original, "a z shortcut before a partial group is misread");
    }

    [Fact]
    public void Ascii85WritesFourZeroBytesAsOneCharacter()
    {
        var encoded = Filtering.ASCII85Decode.Encode(new byte[8]);

        Encoding.ASCII.GetString(encoded).Should().Be("zz~>");
    }

    [Fact]
    public void Ascii85WritesNothingAsTheEndMarkerAlone()
    {
        Encoding.ASCII.GetString(Filtering.ASCII85Decode.Encode(Array.Empty<byte>())).Should().Be("~>");
        Filtering.ASCII85Decode.Decode(Encoding.ASCII.GetBytes("~>"), (FilterParms)null).Should().BeEmpty();
    }

    [Fact]
    public void Ascii85SpendsFiveCharactersOnFourBytesAndTwoMoreOnTheEndMarker()
    {
        Filtering.ASCII85Decode.Encode(Bytes(4)).Should().HaveCount(5 + 2);
        Filtering.ASCII85Decode.Encode(Bytes(8)).Should().HaveCount(10 + 2);
        Filtering.ASCII85Decode.Encode(Bytes(5)).Should().HaveCount(5 + 2 + 2);
        Filtering.ASCII85Decode.Encode(Bytes(6)).Should().HaveCount(5 + 3 + 2);
        Filtering.ASCII85Decode.Encode(Bytes(7)).Should().HaveCount(5 + 4 + 2);
    }

    [Fact]
    public void Ascii85SkipsCharactersThatAreNotPartOfTheEncoding()
    {
        // A writer is free to break the stream into lines, so the decoder ignores anything outside
        // its own alphabet rather than refusing the stream.
        var original = Bytes(12);
        var encoded = Encoding.ASCII.GetString(Filtering.ASCII85Decode.Encode(original));
        var broken = Encoding.ASCII.GetBytes(
            encoded.Substring(0, 5) + "\r\n" + encoded.Substring(5, 5) + "\n" + encoded.Substring(10));

        Filtering.ASCII85Decode.Decode(broken, (FilterParms)null).Should().Equal(original);
    }

    [Fact]
    public void Ascii85RefusesAStreamThatNeverEnds()
    {
        var act = () => Filtering.ASCII85Decode.Decode(Encoding.ASCII.GetBytes("<+oue"), (FilterParms)null);

        act.Should().Throw<ArgumentException>("the end-of-data marker is what says the stream is whole");
    }

    [Fact]
    public void Ascii85RefusesAnEndMarkerThatIsNotOne()
    {
        var act = () => Filtering.ASCII85Decode.Decode(Encoding.ASCII.GetBytes("<+oue~x"), (FilterParms)null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ascii85RefusesATrailingGroupOfOneCharacter()
    {
        // A group of one carries no whole byte, so a stream ending in one was mis-encoded.
        var act = () => Filtering.ASCII85Decode.Decode(Encoding.ASCII.GetBytes("<+oue!~>"), (FilterParms)null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Ascii85RefusesAGroupThatWouldNotFitInFourBytes()
    {
        // "uuuuu" is 85^4 x 84 + ... which overflows the four bytes a group stands for.
        var act = () => Filtering.ASCII85Decode.Decode(Encoding.ASCII.GetBytes("uuuuu~>"), (FilterParms)null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Ascii85DecodingCompactsTheBufferItWasGivenRatherThanACopyOfIt()
    {
        // The decoder squeezes the characters it keeps down to the front of the caller's array
        // before decoding them, so a stream that had anything to skip comes back changed. Worth
        // knowing before handing it a buffer somebody else still holds, or decoding one twice.
        var encoded = Filtering.ASCII85Decode.Encode(Bytes(8));
        var withLineBreaks = Encoding.ASCII.GetBytes(
            Encoding.ASCII.GetString(encoded).Insert(5, "\r\n"));
        var copy = (byte[])withLineBreaks.Clone();

        Filtering.ASCII85Decode.Decode(withLineBreaks, (FilterParms)null).Should().Equal(Bytes(8));

        withLineBreaks.Should().NotEqual(copy);
    }

    // ----- ASCIIHex ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Lengths))]
    public void AsciiHexCarriesAnyNumberOfBytesThereAndBack(int length)
    {
        var original = Bytes(length);

        var encoded = Filtering.ASCIIHexDecode.Encode(original);
        var decoded = Filtering.ASCIIHexDecode.Decode(encoded, (FilterParms)null);

        encoded.Should().HaveCount(2 * length, "hex spends two characters on every byte");
        decoded.Should().Equal(original);
    }

    [Fact]
    public void AsciiHexWritesTheDigitsInUpperCase()
    {
        Encoding.ASCII.GetString(Filtering.ASCIIHexDecode.Encode(new byte[] { 0x00, 0x0F, 0xA5, 0xFF }))
            .Should().Be("000FA5FF");
    }

    [Fact]
    public void AsciiHexReadsLowerCaseDigitsToo()
    {
        Filtering.ASCIIHexDecode.Decode(Encoding.ASCII.GetBytes("00afA5ff"), (FilterParms)null)
            .Should().Equal(new byte[] { 0x00, 0xAF, 0xA5, 0xFF });
    }

    [Fact]
    public void AsciiHexIgnoresTheWhiteSpaceAWriterBreaksTheStreamWith()
    {
        // Null, tab, line feed, form feed, carriage return and space are all white space to a
        // content stream, and the filter drops the lot before reading digits.
        var withSpace = Encoding.ASCII.GetBytes("41 42\t43\r\n44\f45") .Concat(new byte[] { 0 }).ToArray();

        Filtering.ASCIIHexDecode.Decode(withSpace, (FilterParms)null)
            .Should().Equal(Encoding.ASCII.GetBytes("ABCDE"));
    }

    [Fact]
    public void AsciiHexStopsAtTheEndOfDataMarker()
    {
        Filtering.ASCIIHexDecode.Decode(Encoding.ASCII.GetBytes("414243>"), (FilterParms)null)
            .Should().Equal(Encoding.ASCII.GetBytes("ABC"));
    }

    [Fact]
    public void AsciiHexDecodesNothingFromNothing()
    {
        Filtering.ASCIIHexDecode.Decode(Array.Empty<byte>(), (FilterParms)null).Should().BeEmpty();
        Filtering.ASCIIHexDecode.Decode(Encoding.ASCII.GetBytes(">"), (FilterParms)null).Should().BeEmpty();
    }

    /// <summary>
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   The reference is explicit: if the filter reaches the end of data having read an odd number
    ///   of hexadecimal digits, it must behave as though a <c>0</c> digit followed the last one. So
    ///   "4" is 0x40. This decoder instead lengthens its byte array, which pads with the byte 0x00
    ///   rather than the character '0', and 0x00 goes through the digit arithmetic as -48. Every
    ///   odd-length stream therefore ends in a byte 48 too small.
    /// </remarks>
    [Theory]
    [InlineData("4", 0x40, 0x10)]
    [InlineData("41424", 0x40, 0x10)]
    [InlineData("F", 0xF0, 0xC0)]
    public void AsciiHexGetsTheLastByteOfAnOddNumberOfDigitsWrong(
        string hex, int required, int actual)
    {
        var decoded = Filtering.ASCIIHexDecode.Decode(Encoding.ASCII.GetBytes(hex), (FilterParms)null);

        decoded.Last().Should().Be((byte)actual);
        decoded.Last().Should().NotBe((byte)required, "the reference asks for a trailing zero digit");
    }

    // ----- what every filter has in common -------------------------------------------------------

    [Fact]
    public void AFilterEncodesAStringByItsBytesAndDecodesBackToTheSameString()
    {
        const string text = "Hello, filter.";

        var encoded = Filtering.ASCIIHexDecode.Encode(text);

        Filtering.ASCIIHexDecode.DecodeToString(encoded).Should().Be(text);
        Filtering.ASCIIHexDecode.DecodeToString(encoded, null).Should().Be(text);
    }

    [Fact]
    public void EveryFilterRefusesToWorkOnNothingAtAll()
    {
        var hexEncode = () => Filtering.ASCIIHexDecode.Encode((byte[])null);
        var hexDecode = () => Filtering.ASCIIHexDecode.Decode(null, (FilterParms)null);
        var a85Encode = () => Filtering.ASCII85Decode.Encode((byte[])null);
        var a85Decode = () => Filtering.ASCII85Decode.Decode(null, (FilterParms)null);

        hexEncode.Should().Throw<ArgumentNullException>();
        hexDecode.Should().Throw<ArgumentNullException>();
        a85Encode.Should().Throw<ArgumentNullException>();
        a85Decode.Should().Throw<ArgumentNullException>();
    }
}
