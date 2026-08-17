using System;
using System.Collections.Generic;

namespace PdfSharpCore.Text;

/// <summary>
/// Splits mixed text into runs of a single script - "this much is Arabic, this much is Latin" -
/// which is the unit a shaper can handle at a time.
/// </summary>
/// <remarks>
/// <para>
/// UAX #24. The whole of the difficulty is that most punctuation, all spaces and every digit have
/// script <see cref="UnicodeScript.Common"/>, and combining marks have
/// <see cref="UnicodeScript.Inherited"/>: neither says anything about which script the text is in,
/// and both have to be swept into whichever run they find themselves next to. A full stop after
/// Arabic belongs to the Arabic run; the same full stop after Latin belongs to the Latin one.
/// </para>
/// <para>
/// <b>What this does not do:</b> the Script_Extensions property. A character can belong to several
/// scripts at once - U+0640 Arabic tatweel is used by Syriac and Adlam too - and <c>scx</c> is the
/// property that says so. This reads <c>sc</c> alone, which puts such a character in the script it
/// is named for rather than in whichever neighbouring script also claims it. The visible cost is a
/// run boundary where there need not be one, never a wrong glyph, and adding <c>scx</c> later is a
/// third generated table and no change to any caller.
/// </para>
/// </remarks>
public static class ScriptItemizer
{
    /// <summary>
    /// The runs of a string, in written order, with indices into the string.
    /// </summary>
    public static IReadOnlyList<ScriptRun> Itemize(string text)
    {
        if (text == null)
            throw new ArgumentNullException(nameof(text));

        var runs = new List<ScriptRun>();
        if (text.Length == 0)
            return runs;

        var script = UnicodeScript.Common;
        int start = 0;

        for (int idx = 0; idx < text.Length;)
        {
            int width = char.IsHighSurrogate(text[idx]) && idx + 1 < text.Length
                        && char.IsLowSurrogate(text[idx + 1])
                ? 2
                : 1;

            int codePoint = width == 2 ? char.ConvertToUtf32(text[idx], text[idx + 1]) : text[idx];
            var here = UnicodeProperties.ScriptOf(codePoint);

            if (here == UnicodeScript.Inherited || here == UnicodeScript.Common)
            {
                // Carried by whatever it is next to. If the run has no script yet, this character
                // does not give it one either - it waits for the first character that does, which
                // then takes the punctuation before it with it.
                idx += width;
                continue;
            }

            if (script == UnicodeScript.Common)
            {
                // The run had nothing but Common and Inherited so far, so it becomes this script
                // retroactively rather than starting a new one here.
                script = here;
                idx += width;
                continue;
            }

            if (here != script)
            {
                runs.Add(new ScriptRun(start, idx - start, script));
                start = idx;
                script = here;
            }

            idx += width;
        }

        runs.Add(new ScriptRun(start, text.Length - start, script));
        return runs;
    }
}

/// <summary>
/// One stretch of text in a single script.
/// </summary>
public readonly struct ScriptRun
{
    internal ScriptRun(int start, int length, UnicodeScript script)
    {
        Start = start;
        Length = length;
        Script = script;
    }

    /// <summary>The index of the first character of the run.</summary>
    public int Start { get; }

    /// <summary>How many characters the run covers.</summary>
    public int Length { get; }

    /// <summary>
    /// The script. <see cref="UnicodeScript.Common"/> for a run that held nothing but punctuation,
    /// spaces and digits, which is what text with no letters in it comes to.
    /// </summary>
    public UnicodeScript Script { get; }

    /// <summary>
    /// The ISO 15924 code of <see cref="Script"/>, lowercased - the form
    /// <see cref="PdfSharpCore.Fonts.ITextShaper"/> is told a run's script in.
    /// </summary>
    public string ScriptCode => UnicodeProperties.ScriptCode(Script);

    /// <inheritdoc/>
    public override string ToString() => $"[{Start}..{Start + Length - 1}] {Script}";
}
