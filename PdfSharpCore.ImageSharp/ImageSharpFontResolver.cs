
using PdfSharpCore.Drawing;
using SixLabors.Fonts;


namespace PdfSharpCore.Utils
{

    /// <summary>
    /// Resolves the fonts installed on the current platform, reading family name and style with SixLabors.Fonts.
    /// Register it once before any font operation:
    /// <c>GlobalFontSettings.FontResolver = new ImageSharpFontResolver();</c>
    /// </summary>
    public class ImageSharpFontResolver
        : FontResolverBase
    {

        protected override FontMetadata ReadFontMetadata(string fontFilePath)
        {
            FontDescription fontDescription = FontDescription.LoadDescription(fontFilePath);

            XFontStyle style;
            switch (fontDescription.Style)
            {
                case FontStyle.Bold:
                    style = XFontStyle.Bold;
                    break;
                case FontStyle.Italic:
                    style = XFontStyle.Italic;
                    break;
                case FontStyle.BoldItalic:
                    style = XFontStyle.BoldItalic;
                    break;
                default:
                    style = XFontStyle.Regular;
                    break;
            }

            return new FontMetadata(fontDescription.FontFamilyInvariantCulture, style);
        }
    }
}
