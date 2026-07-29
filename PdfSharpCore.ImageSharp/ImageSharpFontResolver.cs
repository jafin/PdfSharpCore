
using PdfSharpCore.Drawing;
using SixLabors.Fonts;


namespace PdfSharpCore.Utils;

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
        return ToMetadata(FontDescription.LoadDescription(fontFilePath));
    }


    protected override FontMetadata ReadFontMetadata(string fontFilePath, int faceIndex)
    {
        if (faceIndex < 0)
            return ReadFontMetadata(fontFilePath);

        // LoadDescription reads one font and cannot say which; a collection has to go through
        // the collection reader even for its first face.
        FontDescription[] descriptions = FontDescription.LoadFontCollectionDescriptions(fontFilePath);
        if (faceIndex >= descriptions.Length)
            throw new System.InvalidOperationException(
                "Font collection holds " + descriptions.Length + " faces; face " + faceIndex + " was asked for.");

        return ToMetadata(descriptions[faceIndex]);
    }


    protected override FontMetadata[] ReadCollectionMetadata(string fontFilePath, int faceCount)
    {
        FontDescription[] descriptions = FontDescription.LoadFontCollectionDescriptions(fontFilePath);
        if (descriptions.Length < faceCount)
            throw new System.InvalidOperationException(
                "Font collection holds " + descriptions.Length + " faces; " + faceCount + " were expected.");

        FontMetadata[] metadata = new FontMetadata[faceCount];
        for (int face = 0; face < faceCount; face++)
            metadata[face] = ToMetadata(descriptions[face]);

        return metadata;
    }


    private static FontMetadata ToMetadata(FontDescription fontDescription)
    {
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
