using PdfSharpCore.Drawing;

namespace PdfSharpCore.Pdf.Extraction;

/// <summary>
/// A run of text as it was drawn: what it says, where its baseline starts, and how large it is.
/// </summary>
/// <remarks>
/// One run per show-text operator, not one per glyph. A glyph-level box needs the advance of every
/// individual glyph, and a run needs only the advance of the whole — so this is what can be given
/// exactly rather than approximately. See the remarks on <see cref="PdfTextExtractor"/>.
/// </remarks>
public sealed class PdfTextRun
{
    internal PdfTextRun(string text, XPoint origin, double width, double fontSize, string fontName)
    {
        Text = text;
        Origin = origin;
        Width = width;
        FontSize = fontSize;
        FontName = fontName;
    }

    /// <summary>
    /// What the run says, decoded through the font's <c>/ToUnicode</c> map where it has one.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Where the baseline of the run starts, in PDF user space — so the origin is the bottom-left
    /// of the page and Y grows upwards, which is the opposite of how a page is usually drawn on.
    /// </summary>
    public XPoint Origin { get; }

    /// <summary>
    /// How wide the run is along its baseline, in user space units.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// The size the text was drawn at, after the text matrix has had its say — so a run scaled by
    /// its matrix reports the size it appears at rather than the number passed to <c>Tf</c>.
    /// </summary>
    public double FontSize { get; }

    /// <summary>
    /// The resource name of the font, such as <c>/F0</c>. Not the typeface name: a content stream
    /// names fonts by the key they have in the page's resources and by nothing else.
    /// </summary>
    public string FontName { get; }

    /// <inheritdoc/>
    public override string ToString() => $"\"{Text}\" at {Origin.X:0.##},{Origin.Y:0.##}";
}
