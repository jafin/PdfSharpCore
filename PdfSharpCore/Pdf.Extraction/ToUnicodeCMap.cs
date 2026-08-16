using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PdfSharpCore.Pdf.Internal;

namespace PdfSharpCore.Pdf.Extraction;

/// <summary>
/// Reads a font's <c>/ToUnicode</c> CMap: the table that says which characters the glyph codes in a
/// content stream stand for.
/// </summary>
/// <remarks>
/// <para>
/// Without it, extracted text is glyph numbers. A font embedded as Identity-H writes the index of
/// the glyph in the font file, which has nothing to do with the character — the same word in two
/// documents using two subsets of the same face is two different runs of numbers.
/// </para>
/// <para>
/// This is the other half of <see cref="Advanced.PdfToUnicodeMap"/>, which writes these. That one
/// emits <c>bfrange</c> entries, so anything this library produced round-trips; <c>bfchar</c> is
/// read too, because other producers use it.
/// </para>
/// </remarks>
internal sealed class ToUnicodeCMap
{
    readonly Dictionary<int, string> _map = new();

    /// <summary>
    /// How many bytes make one code. Two for Identity-H and anything else with a two-byte
    /// codespace, one for a simple font.
    /// </summary>
    public int CodeLength { get; private set; } = 1;

    /// <summary>
    /// Parses a CMap, or answers null when there is nothing usable in it.
    /// </summary>
    public static ToUnicodeCMap Parse(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return null;

        var text = PdfEncoders.RawEncoding.GetString(bytes, 0, bytes.Length);
        var cmap = new ToUnicodeCMap();

        cmap.ReadCodespace(text);
        cmap.ReadCharMappings(text);
        cmap.ReadRangeMappings(text);

        return cmap._map.Count > 0 ? cmap : null;
    }

    /// <summary>
    /// What a code stands for, or null when the map does not say.
    /// </summary>
    public string this[int code] => _map.TryGetValue(code, out var value) ? value : null;

    /// <summary>
    /// The codespace range says how wide a code is. A two-byte codespace is written
    /// <c>&lt;0000&gt;&lt;FFFF&gt;</c>, so the count of hexadecimal digits gives the width.
    /// </summary>
    void ReadCodespace(string text)
    {
        var at = text.IndexOf("begincodespacerange", StringComparison.Ordinal);
        if (at < 0)
            return;

        var tokens = HexTokens(text, at, text.IndexOf("endcodespacerange", at, StringComparison.Ordinal));
        if (tokens.Count > 0)
            CodeLength = Math.Max(1, tokens[0].Length / 2);
    }

    /// <summary>
    /// <c>&lt;src&gt; &lt;dst&gt;</c> pairs between <c>beginbfchar</c> and <c>endbfchar</c>.
    /// </summary>
    void ReadCharMappings(string text)
    {
        var at = 0;
        while ((at = text.IndexOf("beginbfchar", at, StringComparison.Ordinal)) >= 0)
        {
            var end = text.IndexOf("endbfchar", at, StringComparison.Ordinal);
            if (end < 0)
                return;

            var tokens = HexTokens(text, at, end);
            for (var index = 0; index + 1 < tokens.Count; index += 2)
                _map[ToCode(tokens[index])] = ToText(tokens[index + 1]);

            at = end;
        }
    }

    /// <summary>
    /// <c>&lt;lo&gt; &lt;hi&gt; &lt;dst&gt;</c> triples between <c>beginbfrange</c> and
    /// <c>endbfrange</c>, where every code from lo to hi maps to consecutive characters from dst.
    /// </summary>
    /// <remarks>
    /// The array form — <c>&lt;lo&gt; &lt;hi&gt; [&lt;d1&gt; &lt;d2&gt; …]</c>, one destination per
    /// code rather than a run — is deliberately not read. It exists for fonts whose codes map to
    /// characters in no arithmetic order, which no producer of the documents this library is used
    /// with emits, and reading it wrongly would be worse than not reading it: the codes it covers
    /// simply have no mapping and come back as the replacement below.
    /// </remarks>
    void ReadRangeMappings(string text)
    {
        var at = 0;
        while ((at = text.IndexOf("beginbfrange", at, StringComparison.Ordinal)) >= 0)
        {
            var end = text.IndexOf("endbfrange", at, StringComparison.Ordinal);
            if (end < 0)
                return;

            var tokens = HexTokens(text, at, end);
            for (var index = 0; index + 2 < tokens.Count; index += 3)
            {
                var low = ToCode(tokens[index]);
                var high = ToCode(tokens[index + 1]);
                var target = ToCode(tokens[index + 2]);

                // A range covering the whole codespace is a producer saying "identity", and
                // expanding it would be 65536 entries of nothing.
                if (high - low > 0xFFFF)
                    continue;

                for (var code = low; code <= high; code++)
                    _map[code] = char.ConvertFromUtf32(target + (code - low));
            }

            at = end;
        }
    }

    /// <summary>
    /// The hexadecimal strings between two positions, each without its angle brackets.
    /// </summary>
    static List<string> HexTokens(string text, int from, int to)
    {
        var tokens = new List<string>();
        if (to < 0)
            to = text.Length;

        var at = from;
        while (true)
        {
            var open = text.IndexOf('<', at);
            if (open < 0 || open >= to)
                break;

            var close = text.IndexOf('>', open);
            if (close < 0 || close >= to)
                break;

            tokens.Add(text.Substring(open + 1, close - open - 1).Trim());
            at = close + 1;
        }

        return tokens;
    }

    static int ToCode(string hex) =>
        hex.Length == 0 ? 0 : int.Parse(hex.Length > 8 ? hex.Substring(0, 8) : hex,
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>
    /// A destination is a run of UTF-16BE code units, so a single mapping may be more than one
    /// character — which is how a ligature says it stands for the letters it joined.
    /// </summary>
    static string ToText(string hex)
    {
        var text = new StringBuilder();
        for (var at = 0; at + 4 <= hex.Length; at += 4)
        {
            text.Append((char)int.Parse(hex.Substring(at, 4), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture));
        }

        return text.Length > 0 ? text.ToString() : hex;
    }
}
