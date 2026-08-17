using System;
using System.Collections.Generic;
using PdfSharpCore.Fonts;

namespace PdfSharpCore.Text;

/// <summary>
/// Cuts a paragraph into the runs a shaper can be handed one at a time: each one a single
/// direction and a single script, in the order they are drawn.
/// </summary>
/// <remarks>
/// This is where the bidirectional algorithm and script itemisation meet. Neither is enough on its
/// own - <see cref="BidiAlgorithm"/> says which way each stretch runs but not what script it is,
/// <see cref="ScriptItemizer"/> the other way about - and
/// <see cref="PdfSharpCore.Fonts.ITextShaper.Shape"/> wants both at once, for a stretch of text
/// that has exactly one of each.
/// </remarks>
public static class TextItemizer
{
    /// <summary>
    /// The runs of a paragraph, in visual order - leftmost first - so that a renderer can draw
    /// them one after another without doing any reordering of its own.
    /// </summary>
    /// <param name="text">The paragraph.</param>
    /// <param name="direction">Which way it runs, or Automatic to read it off the text.</param>
    public static IReadOnlyList<TextRun> Itemize(
        string text, BidiParagraphDirection direction = BidiParagraphDirection.Automatic)
    {
        if (text == null)
            throw new ArgumentNullException(nameof(text));

        var runs = new List<TextRun>();
        if (text.Length == 0)
            return runs;

        var bidi = BidiAlgorithm.Resolve(text, direction);

        // Script per character rather than per run, so that a bidi run can be cut wherever the
        // script changes inside it without having to intersect two lists of ranges.
        var scripts = new UnicodeScript[text.Length];
        foreach (var run in ScriptItemizer.Itemize(text))
        {
            for (int idx = run.Start; idx < run.Start + run.Length; idx++)
                scripts[idx] = run.Script;
        }

        foreach (var level in bidi.Runs())
        {
            int start = level.Start;
            var script = ScriptOf(scripts, level, start);

            for (int idx = start + 1; idx < level.Start + level.Length; idx++)
            {
                var here = scripts[idx];
                if (here == script || here == UnicodeScript.Common || here == UnicodeScript.Inherited)
                    continue;

                runs.Add(new TextRun(start, idx - start, level.Level, script));
                start = idx;
                script = here;
            }

            runs.Add(new TextRun(start, level.Start + level.Length - start, level.Level, script));
        }

        return runs;
    }

    /// <summary>
    /// The script a run starts in: the first one in it that is not Common or Inherited, because a
    /// run beginning with a space or a bracket is not a run of punctuation.
    /// </summary>
    static UnicodeScript ScriptOf(UnicodeScript[] scripts, BidiRun run, int from)
    {
        for (int idx = from; idx < run.Start + run.Length; idx++)
        {
            if (scripts[idx] != UnicodeScript.Common && scripts[idx] != UnicodeScript.Inherited)
                return scripts[idx];
        }

        return UnicodeScript.Common;
    }
}

/// <summary>
/// One stretch of a paragraph in a single direction and a single script - a unit of shaping.
/// </summary>
public readonly struct TextRun
{
    internal TextRun(int start, int length, byte level, UnicodeScript script)
    {
        Start = start;
        Length = length;
        Level = level;
        Script = script;
    }

    /// <summary>The index of the first character, in the order the text was written.</summary>
    public int Start { get; }

    /// <summary>How many characters the run covers.</summary>
    public int Length { get; }

    /// <summary>The bidirectional embedding level: even runs left to right, odd right to left.</summary>
    public byte Level { get; }

    /// <summary>The script of the run.</summary>
    public UnicodeScript Script { get; }

    /// <summary>Which way the run is written, which is the parity of its level.</summary>
    public XTextDirection Direction
        => (Level & 1) == 0 ? XTextDirection.LeftToRight : XTextDirection.RightToLeft;

    /// <summary>
    /// The ISO 15924 code of <see cref="Script"/>, lowercased - what
    /// <see cref="PdfSharpCore.Fonts.ITextShaper.Shape"/> takes.
    /// </summary>
    public string ScriptCode => UnicodeProperties.ScriptCode(Script);

    /// <inheritdoc/>
    public override string ToString()
        => $"[{Start}..{Start + Length - 1}] {Script} {Direction}";
}
