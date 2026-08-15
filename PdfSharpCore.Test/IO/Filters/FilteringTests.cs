using System;
using System.Collections.Generic;
using System.Text;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Filters;
using Xunit;

namespace PdfSharpCore.Test.IO.Filters;

/// <summary>
///   <see cref="Filtering"/> is the switchboard: a stream's <c>/Filter</c> entry names one filter
///   or a chain of them, and this is what turns those names into objects and runs the data through
///   in order. Getting a name wrong is the difference between a stream that decodes and one that
///   comes back as raw deflate output, so what the names map to is worth stating.
/// </summary>
public class FilteringTests
{
    [Theory]
    [InlineData("ASCIIHexDecode", typeof(AsciiHexDecode))]
    [InlineData("AHx", typeof(AsciiHexDecode))]
    [InlineData("ASCII85Decode", typeof(Ascii85Decode))]
    [InlineData("A85", typeof(Ascii85Decode))]
    [InlineData("LZWDecode", typeof(LzwDecode))]
    [InlineData("LZW", typeof(LzwDecode))]
    [InlineData("FlateDecode", typeof(FlateDecode))]
    [InlineData("Fl", typeof(FlateDecode))]
    public void EveryFilterNameAndItsAbbreviationReachTheSameFilter(string name, Type expected)
    {
        // The abbreviations are not in the reference. Some tools write them anyway, and a reader
        // that does not know them treats a perfectly good stream as undecodable.
        Filtering.GetFilter(name).Should().BeOfType(expected);
        Filtering.GetFilter("/" + name).Should().BeOfType(expected, "a name from a dictionary carries its slash");
    }

    [Fact]
    public void AFilterIsTheSameObjectEveryTimeItIsAskedFor()
    {
        // They hold no per-stream state, so one of each is kept rather than made per call.
        Filtering.GetFilter("FlateDecode").Should().BeSameAs(Filtering.FlateDecode);
        Filtering.GetFilter("ASCII85Decode").Should().BeSameAs(Filtering.ASCII85Decode);
        Filtering.GetFilter("ASCIIHexDecode").Should().BeSameAs(Filtering.ASCIIHexDecode);
        Filtering.GetFilter("LZWDecode").Should().BeSameAs(Filtering.LzwDecode);
    }

    [Theory]
    [InlineData("RunLengthDecode")]
    [InlineData("CCITTFaxDecode")]
    [InlineData("JBIG2Decode")]
    [InlineData("DCTDecode")]
    [InlineData("JPXDecode")]
    [InlineData("Crypt")]
    public void AFilterThatIsRealButUnimplementedComesBackAsNothing(string name)
    {
        // Named in the reference and not written here. The caller gets null rather than an
        // exception, and every Encode and Decode overload passes that null straight through - so a
        // JPEG stream comes back as null rather than as its own bytes.
        Filtering.GetFilter(name).Should().BeNull();

        Filtering.Decode(new byte[] { 1, 2, 3 }, name).Should().BeNull();
        Filtering.Decode(new byte[] { 1, 2, 3 }, name, null).Should().BeNull();
        Filtering.DecodeToString(new byte[] { 1, 2, 3 }, name).Should().BeNull();
        Filtering.DecodeToString(new byte[] { 1, 2, 3 }, name, null).Should().BeNull();
        Filtering.Encode(new byte[] { 1, 2, 3 }, name).Should().BeNull();
        Filtering.Encode("abc", name).Should().BeNull();
    }

    [Fact]
    public void AFilterNobodyHasHeardOfIsRefused()
    {
        var act = () => Filtering.GetFilter("MakeItSmallerDecode");

        act.Should().Throw<NotImplementedException>().WithMessage("*MakeItSmallerDecode*");
    }

    [Fact]
    public void EncodingAndDecodingByNameAgreeWithTheFilterItself()
    {
        var data = Encoding.ASCII.GetBytes("something to squeeze");

        var byName = Filtering.Encode(data, "FlateDecode");

        Filtering.Decode(byName, "FlateDecode").Should().Equal(data);
        Filtering.DecodeToString(byName, "FlateDecode", new FilterParms(null))
            .Should().Be("something to squeeze");
    }

    /// <summary>
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   <see cref="FlateDecode"/> and <see cref="LzwDecode"/> finish by asking their parameters
    ///   whether a predictor was applied - <c>if (parms.DecodeParms != null)</c> - without first
    ///   asking whether they were given any parameters at all. Every other filter ignores the
    ///   argument, so only these two care.
    ///   </para>
    ///   <para>
    ///   Null is not an unusual thing to pass them. It is what
    ///   <see cref="Filter.DecodeToString(byte[])"/> passes, and what
    ///   <see cref="Filtering.DecodeToString(byte[], string)"/> passes, both of which are public
    ///   and neither of which offers a way to supply parameters. So the shortest correct-looking
    ///   way to read a deflated stream as text throws a NullReferenceException every time.
    ///   </para>
    ///   <para>
    ///   Passing <c>new FilterParms(null)</c> instead works, which is what the rest of this class
    ///   does.
    ///   </para>
    /// </remarks>
    [Fact]
    public void TheTwoFiltersThatReadTheirParametersThrowWhenGivenNoneAtAll()
    {
        var deflated = Filtering.FlateDecode.Encode(Encoding.ASCII.GetBytes("something to squeeze"));
        var lzw = new byte[] { 0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01 };

        var flateDirectly = () => Filtering.FlateDecode.Decode(deflated, (FilterParms)null);
        var flateToString = () => Filtering.FlateDecode.DecodeToString(deflated);
        var flateByName = () => Filtering.DecodeToString(deflated, "FlateDecode");
        var lzwDirectly = () => Filtering.LzwDecode.Decode(lzw, (FilterParms)null);

        flateDirectly.Should().Throw<NullReferenceException>();
        flateToString.Should().Throw<NullReferenceException>();
        flateByName.Should().Throw<NullReferenceException>();
        lzwDirectly.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void AStringIsEncodedByItsRawBytes()
    {
        Filtering.Encode("abc", "ASCIIHexDecode").Should().Equal(Encoding.ASCII.GetBytes("616263"));
    }

    // ----- a filter named by a dictionary entry --------------------------------------------------

    [Fact]
    public void ASingleFilterCanBeNamedByAPdfName()
    {
        var data = Encoding.ASCII.GetBytes("abc");
        var encoded = Filtering.ASCIIHexDecode.Encode(data);

        Filtering.Decode(encoded, new PdfName("/ASCIIHexDecode"), null).Should().Equal(data);
    }

    [Fact]
    public void AChainOfFiltersIsUndoneInTheOrderItWasApplied()
    {
        // /Filter [/ASCII85Decode /FlateDecode] means the data was deflated and then made
        // printable, so it is read back the same way round: un-ASCII85 first, then inflate.
        var document = new PdfDocument();
        var data = Encoding.ASCII.GetBytes("a stream worth compressing, compressing, compressing");
        var encoded = Filtering.ASCII85Decode.Encode(Filtering.FlateDecode.Encode(data));

        var chain = new PdfArray(document, new PdfName("/ASCII85Decode"), new PdfName("/FlateDecode"));

        Filtering.Decode(encoded, chain, null).Should().Equal(data);
    }

    [Fact]
    public void AChainWhoseParametersDoNotMatchItIsLeftAlone()
    {
        // One set of decode parameters per filter, or the reader cannot tell which belongs to
        // which. Rather than guess, the data comes back untouched.
        var document = new PdfDocument();
        var data = Encoding.ASCII.GetBytes("untouched");
        var chain = new PdfArray(document, new PdfName("/ASCII85Decode"), new PdfName("/FlateDecode"));
        var parms = new PdfArray(document, PdfNull.Value);

        Filtering.Decode(data, chain, parms).Should().BeSameAs(data);
    }

    [Fact]
    public void AChainCanCarryOneSetOfParametersPerFilter()
    {
        var document = new PdfDocument();
        var data = Encoding.ASCII.GetBytes("abc");
        var encoded = Filtering.ASCII85Decode.Encode(Filtering.ASCIIHexDecode.Encode(data));
        var chain = new PdfArray(document, new PdfName("/ASCII85Decode"), new PdfName("/ASCIIHexDecode"));
        var parms = new PdfArray(document, new PdfDictionary(document), new PdfDictionary(document));

        Filtering.Decode(encoded, chain, parms).Should().Equal(data);
    }

    [Fact]
    public void SomethingThatNamesNoFilterAtAllDecodesToNothing()
    {
        var data = Encoding.ASCII.GetBytes("abc");

        // Fully qualified: this test assembly has a PdfInteger of its own.
        Filtering.Decode(data, new PdfSharpCore.Pdf.PdfInteger(7), null).Should().BeNull();
        Filtering.Decode(data, (PdfItem)null, null).Should().BeNull();
    }

    // ----- Flate ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(PdfFlateEncodeMode.Default)]
    [InlineData(PdfFlateEncodeMode.BestSpeed)]
    [InlineData(PdfFlateEncodeMode.BestCompression)]
    public void EveryCompressionSettingProducesSomethingThatInflatesBackAgain(PdfFlateEncodeMode mode)
    {
        var data = Encoding.ASCII.GetBytes(new string('a', 500) + new string('b', 500));

        var encoded = Filtering.FlateDecode.Encode(data, mode);

        encoded.Length.Should().BeLessThan(data.Length, "a thousand bytes of two characters must compress");
        Filtering.FlateDecode.Decode(encoded, new FilterParms(null)).Should().Equal(data);
    }

    [Fact]
    public void InflatingNothingGivesNothingBack()
    {
        Filtering.FlateDecode.Decode(Array.Empty<byte>(), (FilterParms)null).Should().BeEmpty();
    }

    // ----- LZW -----------------------------------------------------------------------------------

    /// <summary>
    ///   Packs a run of LZW codes into bytes the way the reference asks for them: nine bits each,
    ///   most significant bit first, run together with no padding between one code and the next.
    /// </summary>
    /// <remarks>
    ///   Streams are built here rather than pasted in as hex because nothing in PdfSharpCore can
    ///   encode LZW - <see cref="LzwDecode.Encode"/> throws - so there is nothing to round-trip
    ///   against, and a stream written out by hand is one nobody can check by reading it. Nine
    ///   bits is the whole story for these tests: the width only grows once the table passes 511
    ///   entries, which takes a stream far longer than any of them.
    /// </remarks>
    static byte[] Packed(params int[] codes)
    {
        var bits = new List<bool>();
        foreach (var code in codes)
            for (var bit = 8; bit >= 0; bit--)
                bits.Add(((code >> bit) & 1) == 1);
        while (bits.Count % 8 != 0)
            bits.Add(false);

        var bytes = new byte[bits.Count / 8];
        for (var idx = 0; idx < bits.Count; idx++)
            if (bits[idx])
                bytes[idx / 8] |= (byte)(1 << (7 - idx % 8));
        return bytes;
    }

    const int ClearTable = 256;
    const int EndOfData = 257;
    const int FirstFreeCode = 258;

    [Fact]
    public void LzwReadsLiteralCodesAsThemselves()
    {
        var decoded = Filtering.LzwDecode.Decode(
            Packed(ClearTable, 'A', 'B', 'C', EndOfData), new FilterParms(null));

        decoded.Should().Equal(Encoding.ASCII.GetBytes("ABC"));
    }

    [Fact]
    public void LzwDoesNotInsistOnBeingToldToClearTheTableFirst()
    {
        var decoded = Filtering.LzwDecode.Decode(Packed('A', 'B', EndOfData), new FilterParms(null));

        decoded.Should().Equal(Encoding.ASCII.GetBytes("AB"));
    }

    [Fact]
    public void LzwReadsAStreamThatSaysNothingAsNothing()
    {
        Filtering.LzwDecode.Decode(Packed(ClearTable, EndOfData), new FilterParms(null))
            .Should().BeEmpty();
    }

    [Fact]
    public void LzwClearingTheTableInTheMiddleStartsTheCodesOverAgain()
    {
        var decoded = Filtering.LzwDecode.Decode(
            Packed(ClearTable, 'A', 'B', ClearTable, 'C', 'D', EndOfData), new FilterParms(null));

        decoded.Should().Equal(Encoding.ASCII.GetBytes("ABCD"));
    }

    /// <summary>
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   Every LZW decoder has to handle one code that is not in its table yet: the encoder is
    ///   allowed to emit the entry it is in the middle of defining, which happens whenever the
    ///   input repeats a run. The rule is that such a code stands for the previous entry followed
    ///   by that entry's own first byte - the "KwKwK" case, in the usual telling.
    ///   </para>
    ///   <para>
    ///   This decoder writes the previous entry and stops:
    ///   <code>
    ///   str = _stringTable[oldCode];
    ///   outputStream.Write(str, 0, str.Length);   // the repeated first byte never goes out
    ///   AddEntry(str, str[0]);
    ///   </code>
    ///   It adds the right entry to the table, so the stream stays in step and nothing throws -
    ///   the output is simply one byte short, at each occurrence, silently. A run of the same byte
    ///   is exactly what an encoder emits this code for, so a scanned page or a flat-coloured
    ///   image loses bytes wherever it repeats.
    ///   </para>
    /// </remarks>
    [Fact]
    public void LzwLosesAByteWhereverTheStreamRepeatsItself()
    {
        // Code 258 is the entry being defined by this very code, and stands for "AA", so the
        // whole stream is "A" + "AA".
        var decoded = Filtering.LzwDecode.Decode(
            Packed(ClearTable, 'A', FirstFreeCode, EndOfData), new FilterParms(null));

        decoded.Should().Equal(Encoding.ASCII.GetBytes("AA"));
        decoded.Should().NotEqual(Encoding.ASCII.GetBytes("AAA"), "which is what it stands for");
    }

    [Fact]
    public void LzwTreatsAStreamThatRunsOutAsOneThatEnded()
    {
        // Reading past the end is caught and reported as the end-of-data code, so a truncated
        // stream gives back what was read rather than throwing.
        var decoded = Filtering.LzwDecode.Decode(Packed(ClearTable, 'A', 'B'), new FilterParms(null));

        decoded.Should().Equal(Encoding.ASCII.GetBytes("AB"));
    }

    [Fact]
    public void LzwRefusesTheFlavourItCannotRead()
    {
        var act = () => Filtering.LzwDecode.Decode(new byte[] { 0x00, 0x01, 0x02 }, new FilterParms(null));

        act.Should().Throw<Exception>().WithMessage("*flavour*");
    }

    [Fact]
    public void LzwEncodingIsNotSupportedAndSaysSo()
    {
        var act = () => Filtering.LzwDecode.Encode(new byte[] { 1, 2, 3 });

        act.Should().Throw<NotImplementedException>().WithMessage("*LZW*");
    }
}
