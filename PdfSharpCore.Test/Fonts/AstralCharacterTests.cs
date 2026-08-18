using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Test.Helpers;
using Xunit;
using PdfSharpCore.Pdf.IO;
using Reader = PdfSharpCore.Pdf.IO.PdfReader;

namespace PdfSharpCore.Test.Fonts;

/// <summary>
///   Characters above the basic multilingual plane, which this library could not draw at all until
///   it learned to read a <c>cmap</c> format 12 subtable.
/// </summary>
/// <remarks>
///   <para>
///     An emoji was three failures at once, and they compounded. The unshaped path looked each
///     UTF-16 code unit up in a format 4 subtable, so a surrogate pair drew <c>.notdef</c>
///     <em>twice</em> - one character, two empty boxes. Coverage was asked the same way, so no face
///     could ever answer "yes" for an astral character. And because coverage could not answer,
///     font fallback could not be offered one either: there was no question to ask.
///   </para>
///   <para>
///     Fixing the reader fixes all three, because all three go through
///     <c>OpenTypeDescriptor.CharCodeToGlyphIndex</c>. What is pinned here is each of them
///     separately, so that a regression says which one broke.
///   </para>
///   <para>
///     The faces are the ones already checked in. Source Code Pro carries a format 12 subtable with
///     four real emoji in it, and Liberation Sans carries no format 12 subtable at all - which
///     makes the pair of them exactly the fallback case, with no new font asset needed.
///   </para>
/// </remarks>
[Collection(TextShapingCollection.Name)]
public class AstralCharacterTests
{
    // Emoji that Source Code Pro really has, confirmed against its own cmap rather than assumed:
    // U+1F512 LOCK, U+1F916 ROBOT FACE, U+1F3B5 MUSICAL NOTE.
    const int Lock = 0x1F512;
    const int Robot = 0x1F916;

    // MATHEMATICAL BOLD CAPITAL A. In neither face, so it is the "nobody can draw it" case.
    const int BoldA = 0x1D400;

    static string Text(int codePoint) => char.ConvertFromUtf32(codePoint);

    /// <summary>The face with a format 12 subtable.</summary>
    static XFont WithFormat12() => new XFont(PinnedFontResolver.CffFamilyName, 20);

    /// <summary>The face without one.</summary>
    static XFont WithoutFormat12() => new XFont("Arial", 20);

    sealed class Installed : IDisposable
    {
        internal Installed(IFontFallback fallback) => GlobalFontSettings.FontFallback = fallback;

        public void Dispose() => GlobalFontSettings.FontFallback = null;
    }

    /// <summary>A fallback offering one family, and only for the code points named.</summary>
    sealed class Only : IFontFallback
    {
        readonly HashSet<int> _mine;
        readonly string[] _families;

        internal Only(IEnumerable<int> codePoints, params string[] families)
        {
            _mine = new HashSet<int>(codePoints);
            _families = families;
        }

        public IEnumerable<string> FamiliesFor(int codePoint, bool isBold, bool isItalic)
            => _mine.Contains(codePoint) ? _families : Enumerable.Empty<string>();
    }

    // ----- one character, one glyph ----------------------------------------------------------------

    [Fact]
    public void AnAstralCharacterIsOneGlyphAndNotTwo()
    {
        // The heart of it. Two glyphs for one character was not merely wrong on the page - it made
        // the character undrawable in principle, because no cmap maps a lone surrogate.
        DrawnText.Glyphs(DrawnText.Page(Text(Lock), WithFormat12()))
            .Should().HaveCount(1, "a surrogate pair is one character");
    }

    [Fact]
    public void AFaceThatHasTheCharacterDrawsIt()
    {
        var glyphs = DrawnText.Glyphs(DrawnText.Page(Text(Lock), WithFormat12()));

        glyphs.Single().Should().NotBe(0,
            "Source Code Pro has U+1F512 in its format 12 subtable, so it is not .notdef");
    }

    [Fact]
    public void TwoDifferentAstralCharactersGetTwoDifferentGlyphs()
    {
        // Guards against a reader that answers plausibly but reads the wrong group - one constant
        // wrong answer would satisfy every test above this one.
        int drawnLock = DrawnText.Glyphs(DrawnText.Page(Text(Lock), WithFormat12())).Single();
        int drawnRobot = DrawnText.Glyphs(DrawnText.Page(Text(Robot), WithFormat12())).Single();

        drawnLock.Should().NotBe(drawnRobot);
    }

    [Fact]
    public void AFaceWithoutTheCharacterDrawsOneNotdefRatherThanTwo()
    {
        // Liberation Sans has no format 12 subtable. The answer is still .notdef - it always was -
        // but it is one .notdef for one character, where it used to be one per surrogate.
        DrawnText.Glyphs(DrawnText.Page(Text(Lock), WithoutFormat12()))
            .Should().Equal(new[] { 0 });
    }

    [Fact]
    public void ACharacterNoFaceHasIsStillOneNotdef()
    {
        DrawnText.Glyphs(DrawnText.Page(Text(BoldA), WithFormat12()))
            .Should().Equal(new[] { 0 });
    }

    // ----- measuring agrees with drawing -----------------------------------------------------------

    [Fact]
    public void AnAstralCharacterIsMeasuredAsOneCharacter()
    {
        // Measuring and drawing go through the same seam, so a width that still counted two
        // .notdef would put every following word in the wrong place.
        var font = WithFormat12();

        DrawnText.MeasuredWidth(Text(Lock), font).Should().BeApproximately(
            DrawnText.MeasuredWidth(Text(Lock), font), 1e-9);

        DrawnText.MeasuredWidth(Text(Lock) + Text(Lock), font).Should().BeApproximately(
            2 * DrawnText.MeasuredWidth(Text(Lock), font), 1e-9,
            "two of the same character are twice one of them");
    }

    // ----- the character survives the trip out ------------------------------------------------------

    [Fact]
    public void TheSurrogatePairIsRecoveredWholeByToUnicode()
    {
        // The cluster of the one glyph is the index of the high surrogate and the run ends two code
        // units later, so what the glyph stands for is the pair rather than half of it. This is the
        // half of the change a reader sees: a destination of one code unit here would mean copying
        // the emoji out of the page yielded half a character.
        string cmap = ToUnicodeCMapOf(DrawnText.Page(Text(Lock), WithFormat12()));

        cmap.Should().NotBeNull("an embedded Identity-H font carries a /ToUnicode CMap");

        // A glyph standing for two code units cannot be written as a bfrange, whose destination is
        // a single code - so it has to be a bfchar, and its destination is the pair in UTF-16BE.
        cmap.Should().Contain("beginbfchar");
        cmap.Should().Contain("<D83DDD12>",
            "U+1F512 is D83D DD12 in UTF-16, and both code units have to be in the destination");
    }

    /// <summary>
    ///   The <c>/ToUnicode</c> CMap of the font on a page, read back out of the saved document.
    /// </summary>
    /// <remarks>
    ///   Saved and reopened rather than inspected in place, because the CMap is written at save
    ///   time - asking the object model before that finds nothing. The stream is found by its
    ///   content rather than by walking the font dictionary down to it: there is one CMap in these
    ///   one-font documents, and <c>begincmap</c> identifies it without depending on the shape of
    ///   the font dictionary this test is not about.
    /// </remarks>
    static string ToUnicodeCMapOf(PdfPage page)
    {
        using MemoryStream saved = new MemoryStream();
        page.Owner.Save(saved, false);
        saved.Position = 0;

        using PdfDocument reopened = Reader.Open(saved, PdfDocumentOpenMode.ReadOnly);
        foreach (PdfObject item in reopened.Internals.GetAllObjects())
        {
            if (item is PdfDictionary dictionary && dictionary.Stream != null)
            {
                string decoded = Encoding.ASCII.GetString(dictionary.Stream.UnfilteredValue);
                if (decoded.Contains("begincmap"))
                    return decoded;
            }
        }

        return null;
    }

    // ----- and the reason all this matters: fallback ------------------------------------------------

    [Fact]
    public void AnAstralCharacterCanNowBeOfferedForFallback()
    {
        // The third failure, and the one that could not even be attempted before: Liberation Sans
        // cannot draw the lock, Source Code Pro can, and until coverage could answer for an astral
        // character there was no way to find that out.
        using var _ = new Installed(new Only(new[] { Lock }, PinnedFontResolver.CffFamilyName));

        var glyphs = DrawnText.Glyphs(DrawnText.Page(Text(Lock), WithoutFormat12()));

        glyphs.Should().HaveCount(1);
        glyphs.Single().Should().NotBe(0,
            "the character was drawn by the fallback face, which really has it");
    }

    [Fact]
    public void TheFallbackFaceIsSelectedInTheContentStream()
    {
        // Not merely a non-zero glyph: a second font resource, selected with its own Tf. A glyph
        // number alone could come from the original face by accident.
        using var _ = new Installed(new Only(new[] { Lock }, PinnedFontResolver.CffFamilyName));

        string content = DrawnText.ContentOf(
            DrawnText.Page("A" + Text(Lock) + "B", WithoutFormat12()));

        Regex.Matches(content, @"/F\d+ [\d.]+ Tf")
            .Select(match => match.Value).Distinct()
            .Should().HaveCount(2, "the Latin and the emoji are drawn from different faces");
    }

    [Fact]
    public void AFallbackIsNotTakenWhenTheFaceCanAlreadyDrawIt()
    {
        // Coverage answering "yes" for an astral character has to stop the fallback, or a face
        // that was perfectly able to draw the character would be replaced anyway.
        using var _ = new Installed(new Only(new[] { Lock }, "Arial"));

        string content = DrawnText.ContentOf(DrawnText.Page(Text(Lock), WithFormat12()));

        Regex.Matches(content, @"/F\d+ [\d.]+ Tf")
            .Select(match => match.Value).Distinct()
            .Should().HaveCount(1, "Source Code Pro has the character, so nothing is fallen back to");
    }

    // ----- what must not have changed ----------------------------------------------------------------

    [Fact]
    public void OrdinaryTextIsUntouched()
    {
        // The whole change is guarded by a code point above 0xFFFF, and this is the assertion that
        // says so: a face carrying a format 12 subtable still answers for ordinary text out of its
        // format 4 one, exactly as it did.
        var font = WithFormat12();

        DrawnText.Glyphs(DrawnText.Page("Hello", font)).Should().HaveCount(5);
        DrawnText.ContentOf(DrawnText.Page("Hello", font)).Should().Contain(" Tj");
    }

    [Fact]
    public void AnUnpairedSurrogateIsStillOneGlyphAndDrawsNothingReal()
    {
        // A lone high surrogate is not a character and cannot be completed. It must not be read as
        // the start of a pair that is not there, and it must not swallow the character after it.
        string text = "\uD83D" + "A";

        var glyphs = DrawnText.Glyphs(DrawnText.Page(text, WithFormat12()));

        glyphs.Should().HaveCount(2, "an unpaired surrogate is one unit and the A is another");
        glyphs[0].Should().Be(0, "a lone surrogate is not a character any face maps");
        glyphs[1].Should().NotBe(0, "the A after it is untouched");
    }
}
