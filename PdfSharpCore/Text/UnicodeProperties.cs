using System;

namespace PdfSharpCore.Text;

/// <summary>
/// The character properties the bidirectional algorithm and script itemisation are defined in
/// terms of, read out of tables generated from the Unicode Character Database.
/// </summary>
/// <remarks>
/// <para>
/// .NET exposes General_Category and little else: neither Bidi_Class nor Script is reachable
/// through <see cref="System.Globalization.CharUnicodeInfo"/>, and neither can be derived from
/// what is. So the data is carried - see <c>tools/UnicodeTableGenerator</c> for where it comes
/// from and how to bump it.
/// </para>
/// <para>
/// Both tables are a complete partition of U+0000..U+10FFFF with the database's own defaults
/// materialised into them, so a lookup is one binary search and there is no defaulting left to get
/// wrong at run time. That matters more than it sounds: unassigned code points in the Hebrew and
/// Arabic blocks default to <see cref="BidiClass.R"/> and <see cref="BidiClass.AL"/> rather than to
/// <see cref="BidiClass.L"/>, and an implementation that missed it would be wrong for exactly the
/// scripts the algorithm exists for.
/// </para>
/// </remarks>
public static class UnicodeProperties
{
    /// <summary>
    /// The version of the Unicode Character Database the tables were generated from.
    /// </summary>
    public static string UnicodeVersion => UnicodeTables.UnicodeVersion;

    /// <summary>
    /// The Bidi_Class of a code point.
    /// </summary>
    /// <param name="codePoint">
    /// A Unicode code point, not a UTF-16 code unit. A surrogate on its own answers
    /// <see cref="BidiClass.L"/>, which is what the database gives it.
    /// </param>
    public static BidiClass BidiClassOf(int codePoint)
        => (BidiClass)UnicodeTables.BidiClassRangeValue[
            IndexOf(UnicodeTables.BidiClassRangeStart, codePoint)];

    /// <summary>
    /// The Script of a code point.
    /// </summary>
    public static UnicodeScript ScriptOf(int codePoint)
        => (UnicodeScript)UnicodeTables.UnicodeScriptRangeValue[
            IndexOf(UnicodeTables.UnicodeScriptRangeStart, codePoint)];

    /// <summary>
    /// The ISO 15924 code of a script, lowercased - <c>"arab"</c>, <c>"latn"</c>, <c>"deva"</c>.
    /// This is the form <see cref="PdfSharpCore.Fonts.ITextShaper"/> is told a run's script in.
    /// </summary>
    public static string ScriptCode(UnicodeScript script)
    {
        var codes = UnicodeTables.UnicodeScriptCode;
        var index = (int)script;
        return index >= 0 && index < codes.Length ? codes[index] : "zzzz";
    }

    /// <summary>
    /// The index of the run <paramref name="codePoint"/> falls in. The table is a complete
    /// partition, so there is always one.
    /// </summary>
    static int IndexOf(int[] starts, int codePoint)
    {
        if (codePoint < 0 || codePoint > 0x10FFFF)
            throw new ArgumentOutOfRangeException(nameof(codePoint),
                "A Unicode code point is between U+0000 and U+10FFFF.");

        int index = Array.BinarySearch(starts, codePoint);

        // Landing between two starts means the run began at the one before.
        return index >= 0 ? index : ~index - 1;
    }
}
