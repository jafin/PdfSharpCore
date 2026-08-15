using System.Collections.Generic;
using PdfSharpCore.Pdf;
using PdfSharpCore.Test.Helpers;

namespace MigraDocCore.Rendering.Tests.Helpers;

/// <summary>
///   What a page actually shows, as the sequence of glyphs it draws.
/// </summary>
/// <remarks>
///   The text on a page cannot be read back as text. MigraDoc embeds its fonts as Identity-H, so a
///   show-text operator carries glyph identifiers rather than characters - two bytes each - and
///   turning them back into characters would mean reading the embedded font's own tables.
///
///   Comparing sequences sidesteps that. The identifiers are the face's own, so the same
///   characters in the same font always produce the same numbers, and a test can say what a page
///   should read by rendering that text and comparing. It is why the assertions below say
///   <c>Of(page).Should().Equal(For("Page: I"))</c> rather than naming the string outright.
/// </remarks>
internal static class Glyphs
{
    /// <summary>The glyphs the page draws, in the order it draws them.</summary>
    internal static IReadOnlyList<int> On(PdfPage page)
    {
        var glyphs = new List<int>();

        foreach (var run in TextOperators.ShownStrings(page))
        {
            // Two bytes per glyph. Reading a run a byte at a time shifts everything by half a
            // glyph and produces a sequence that differs everywhere.
            for (var idx = 0; idx + 1 < run.Length; idx += 2)
                glyphs.Add((run[idx] << 8) | run[idx + 1]);
        }

        return glyphs;
    }

    /// <summary>
    ///   The glyphs that text draws when it is laid out as plain paragraphs, one per line given.
    ///   Nothing separates the lines in the result: MigraDoc draws one run per word and puts the
    ///   spaces between them in the positioning, so no whitespace glyph is ever shown.
    /// </summary>
    internal static IReadOnlyList<int> For(params string[] lines)
    {
        var document = new MigraDocCore.DocumentObjectModel.Document();
        var paragraph = document.AddSection().AddParagraph();

        for (var idx = 0; idx < lines.Length; idx++)
        {
            if (idx > 0)
                paragraph.AddLineBreak();
            paragraph.AddText(lines[idx]);
        }

        return On(Rendered.FirstPageOf(document));
    }
}
