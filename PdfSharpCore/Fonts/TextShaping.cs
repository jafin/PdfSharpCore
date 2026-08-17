using System;
using System.Collections.Generic;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts.OpenType;
using PdfSharpCore.Text;

namespace PdfSharpCore.Fonts;

/// <summary>
/// The one place characters become glyphs. Every path in this library that needs to know which
/// glyphs a string draws as - measuring, drawing, outlining - asks here, so that registering an
/// <see cref="ITextShaper"/> changes all of them at once and none of them separately.
/// </summary>
static class TextShaping
{
    /// <summary>
    /// Shapes a whole string: cuts it into runs that are each one direction, one script and one
    /// face, shapes each of them on its own terms, and hands them back in the order they are drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what measuring and drawing ask, rather than <see cref="Shape(System.ReadOnlySpan{char},PdfSharpCore.Drawing.XFont,PdfSharpCore.Fonts.OpenType.OpenTypeDescriptor,PdfSharpCore.Fonts.XTextDirection,string,string)"/> directly, because a
    /// string is not a run. Shaping "Hello <c>&#x0645;&#x0631;&#x062D;&#x0628;&#x0627;</c>" as one
    /// piece asks the face to apply one script's rules to two scripts' characters and draws the
    /// second of them backwards; cutting it first is the whole of what the Unicode Bidirectional
    /// Algorithm and script itemisation are for. A face with no Arabic in it then draws four empty
    /// boxes, which is what <see cref="GlobalFontSettings.FontFallback"/> is for, and which cuts
    /// the runs again in places the first two cuts know nothing about.
    /// </para>
    /// <para>
    /// Text made entirely of characters below <c>U+02B0</c> skips itemisation. Every one of them is
    /// Latin or Common and none of them is right to left, so itemisation can only ever answer the
    /// single run the fast path shapes - and taking it means the commonest string in the library
    /// costs no bidirectional resolution, no string copy and no list. Coverage is not skippable in
    /// the same way, because a Latin face may lack a Latin character; it is skipped only when no
    /// fallback is registered, which is then the whole of what registering none buys.
    /// </para>
    /// </remarks>
    internal static ShapedText ShapeText(ReadOnlySpan<char> text, XFont font, OpenTypeDescriptor descriptor,
        BidiParagraphDirection direction = BidiParagraphDirection.Automatic, string language = null)
    {
        if (font == null)
            throw new ArgumentNullException(nameof(font));
        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));

        bool itemize = NeedsItemizing(text);
        bool fallback = FontFallbackResolution.Enabled;

        if (!itemize && !fallback)
            return ShapedText.Of(Shape(text, font, descriptor, XTextDirection.LeftToRight, null, language),
                font, text.Length);

        // Itemisation reads a string and the bidirectional algorithm allocates several arrays the
        // length of it, so one more copy here is not what this path costs.
        string whole = text.ToString();
        var segments = new List<ShapedSegment>();

        if (itemize)
        {
            foreach (var run in TextItemizer.Itemize(whole, direction))
                AddRun(segments, whole, run.Start, run.Length, run.Direction, run.ScriptCode,
                    font, descriptor, language);
        }
        else
        {
            // One run by construction - see NeedsItemizing - and told nothing about its script,
            // exactly as the fast path above tells the shaper nothing.
            AddRun(segments, whole, 0, whole.Length, XTextDirection.LeftToRight, null,
                font, descriptor, language);
        }

        return new ShapedText(segments);
    }

    /// <summary>
    /// Shapes one itemised run, cutting it again wherever the face has to change, and adds the
    /// pieces to <paramref name="into"/> in the order they are drawn.
    /// </summary>
    static void AddRun(List<ShapedSegment> into, string whole, int start, int length,
        XTextDirection direction, string script, XFont font, OpenTypeDescriptor descriptor,
        string language)
    {
        if (!FontFallbackResolution.Enabled)
        {
            into.Add(Segment(whole, start, length, direction, script, font, descriptor, language));
            return;
        }

        int end = start + length;
        int first = into.Count;
        int pieceStart = start;
        XFont piece = null;

        for (int idx = start; idx < end; idx++)
        {
            // The trailing half of a surrogate pair is not a character and has no face of its own.
            // Asked separately it would be a lone surrogate, which no cmap covers, and a cut here
            // would put the two halves of one character in two fonts. It goes wherever its leading
            // half went.
            //
            // That leading half is asked about as a UTF-16 code unit rather than as the code point
            // the pair spells, which is as much as this library can currently do with a
            // supplementary character: the cmap reader handles format 4 alone, so nothing above the
            // BMP resolves to a glyph in any face and there is no coverage answer to be had. See
            // docs/specs/text-shaping-and-bidi.md.
            if (idx > start && char.IsLowSurrogate(whole[idx]) && char.IsHighSurrogate(whole[idx - 1]))
                continue;

            var wanted = FontFallbackResolution.FontFor(whole[idx], font, descriptor);

            // No opinion, or the same opinion as the piece being built: nothing to cut.
            if (wanted == null || ReferenceEquals(wanted, piece))
                continue;

            if (piece == null)
            {
                // Nothing before this had an opinion, so what has gone before belongs to the first
                // face that does have one - a run opening with a space is not a run of spaces.
                piece = wanted;
                continue;
            }

            into.Add(Segment(whole, pieceStart, idx - pieceStart, direction, script,
                piece, DescriptorFor(piece), language));
            pieceStart = idx;
            piece = wanted;
        }

        var last = piece ?? font;
        into.Add(Segment(whole, pieceStart, end - pieceStart, direction, script,
            last, DescriptorFor(last), language));

        // The pieces of a right-to-left run are drawn right to left: the one written first is the
        // rightmost. Reversing them here is the same thing the bidirectional algorithm does to the
        // runs of a paragraph, one level down and for a reason it knows nothing about.
        if (direction == XTextDirection.RightToLeft)
            into.Reverse(first, into.Count - first);
    }

    static ShapedSegment Segment(string whole, int start, int length, XTextDirection direction,
        string script, XFont font, OpenTypeDescriptor descriptor, string language)
        => new ShapedSegment(
            Shape(whole.AsSpan(start, length), font, descriptor, direction, script, language),
            font, start, length);

    /// <summary>
    /// Whether the text could possibly be more than one run.
    /// </summary>
    /// <remarks>
    /// Below <c>U+02B0</c> - Basic Latin through IPA Extensions - every character is script Latin or
    /// script Common, and no character has a right-to-left or Arabic bidirectional class. So the
    /// paragraph level is 0, no rule can raise anything to an odd level, nothing is reordered, and
    /// there is exactly one script. The first character at or above that bound is where the cheap
    /// answer stops being the true one; combining marks and every non-Latin script are above it.
    /// </remarks>
    static bool NeedsItemizing(ReadOnlySpan<char> text)
    {
        for (int idx = 0; idx < text.Length; idx++)
        {
            if (text[idx] >= '\u02B0')
                return true;
        }

        return false;
    }

    /// <summary>
    /// Shapes a run against a font, through the registered <see cref="ITextShaper"/> if there is
    /// one and through the unshaped path if there is not.
    /// </summary>
    internal static ShapedRun Shape(ReadOnlySpan<char> text, XFont font,
        XTextDirection direction = XTextDirection.LeftToRight,
        string script = null, string language = null)
        => Shape(text, font, DescriptorFor(font), direction, script, language);

    /// <inheritdoc cref="Shape(System.ReadOnlySpan{char},PdfSharpCore.Drawing.XFont,PdfSharpCore.Fonts.XTextDirection,string,string)"/>
    /// <remarks>
    /// The overload for callers that already hold the descriptor - the measuring path measures a
    /// line at a time and would otherwise look it up once per line.
    /// </remarks>
    internal static ShapedRun Shape(ReadOnlySpan<char> text, XFont font, OpenTypeDescriptor descriptor,
        XTextDirection direction = XTextDirection.LeftToRight,
        string script = null, string language = null)
    {
        if (font == null)
            throw new ArgumentNullException(nameof(font));

        var shaper = GlobalFontSettings.TextShaper;
        if (shaper == null)
            return Unshaped(text, descriptor, direction);

        // A shaper that answers nothing is treated as a shaper that declined the run rather than
        // as an error: the unshaped result is always available and always better than throwing
        // from the middle of a page being drawn.
        return shaper.Shape(text, font.ShapingFont, direction, script, language)
            ?? Unshaped(text, descriptor, direction);
    }

    static OpenTypeDescriptor DescriptorFor(XFont font)
    {
        if (font == null)
            throw new ArgumentNullException(nameof(font));

        return FontDescriptorCache.GetOrCreateDescriptorFor(font) as OpenTypeDescriptor
            ?? throw new InvalidOperationException("No OpenTypeDescriptor for the font to shape against.");
    }

    /// <summary>
    /// What this library did before there was a seam, and what it still does when none is
    /// registered: one character, one <c>cmap</c> lookup, one glyph, in the order they were
    /// written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept faithful to the old behaviour down to its limits, because every existing document
    /// depends on them. A surrogate pair is two lookups on the two halves, which finds nothing and
    /// draws <c>.notdef</c> twice; a symbol face has its code points shifted into the range the
    /// face actually encodes. Both are wrong and neither is this method's to fix - the fix is a
    /// real shaper.
    /// </para>
    /// <para>
    /// One thing it does do that no <c>cmap</c> lookup can: a right-to-left run comes back
    /// reversed. <see cref="ShapedRun"/> promises visual order and a renderer relies on it, so this
    /// has to keep the promise however little else it can do. It means a consumer who takes no
    /// shaping dependency at all still gets Arabic and Hebrew in the right order - unjoined, which
    /// is wrong, but no longer also backwards, which was the older and louder complaint.
    /// </para>
    /// </remarks>
    internal static ShapedRun Unshaped(ReadOnlySpan<char> text, XFont font,
        XTextDirection direction = XTextDirection.LeftToRight)
        => Unshaped(text, DescriptorFor(font), direction);

    /// <inheritdoc cref="Unshaped(System.ReadOnlySpan{char},PdfSharpCore.Drawing.XFont,PdfSharpCore.Fonts.XTextDirection)"/>
    internal static ShapedRun Unshaped(ReadOnlySpan<char> text, OpenTypeDescriptor descriptor,
        XTextDirection direction = XTextDirection.LeftToRight)
    {
        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));

        if (text.Length == 0)
            return ShapedRun.Empty(descriptor.UnitsPerEm, direction);

        bool symbol = descriptor.FontFace.cmap.symbol;
        int symbolBase = descriptor.FontFace.os2.usFirstCharIndex & 0xFF00;

        // A joining control is inside the run because a real shaper has to read it - see
        // BidiResult.Runs - and this is not a real shaper. It draws nothing for one: the control is
        // zero width by definition, so whatever glyph the face happens to map it to is not a glyph
        // to put on the page, and .notdef least of all.
        int drawn = 0;
        for (int idx = 0; idx < text.Length; idx++)
        {
            if (!UnicodeProperties.IsJoiningControl(text[idx]))
                drawn++;
        }

        if (drawn == 0)
            return ShapedRun.Empty(descriptor.UnitsPerEm, direction);

        bool rightToLeft = direction == XTextDirection.RightToLeft;
        var glyphs = new ShapedGlyph[drawn];
        for (int idx = 0, position = 0; idx < text.Length; idx++)
        {
            char ch = text[idx];
            if (UnicodeProperties.IsJoiningControl(ch))
                continue;

            // Used | rather than + because of http://PdfSharpCore.codeplex.com/workitem/15954.
            if (symbol)
                ch = (char)(ch | symbolBase);

            int glyphIndex = descriptor.CharCodeToGlyphIndex(ch);

            // The cluster stays the index of the character it came from; only the position in the
            // list changes, so a right-to-left run's clusters descend exactly as a shaper's do.
            int at = rightToLeft ? drawn - 1 - position : position;
            glyphs[at] = new ShapedGlyph((ushort)glyphIndex, idx, descriptor.GlyphIndexToWidth(glyphIndex));
            position++;
        }

        return new ShapedRun(glyphs, descriptor.UnitsPerEm, direction);
    }

    /// <summary>
    /// The glyph identifiers of a run, one <see cref="char"/> per glyph, in drawing order - the
    /// form <see cref="PdfSharpCore.Pdf.Internal.PdfEncoders"/> and the content-stream writer want
    /// a Type 0 glyph run in.
    /// </summary>
    /// <remarks>
    /// A glyph identifier is a <see cref="ushort"/> and a <see cref="char"/> is a UTF-16 code unit,
    /// so this string is glyph indices wearing a string's clothes. It is never text and must never
    /// be decoded as any.
    /// </remarks>
    internal static string GlyphIds(ShapedRun run)
    {
        var glyphs = run.Glyphs;
        var ids = new char[glyphs.Count];
        for (int idx = 0; idx < glyphs.Count; idx++)
            ids[idx] = (char)glyphs[idx].GlyphId;

        return new string(ids);
    }
}
