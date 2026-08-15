using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using PdfSharpCore.Fonts;

namespace PdfSharpCore.Charting.Tests.Helpers;

/// <summary>
///   Answers every request with the fonts shipped alongside these tests, rather than with
///   whatever the machine happens to have installed.
/// </summary>
/// <remarks>
///   The charting defaults ask for Arial, and the resolver the library ships would answer with
///   whatever the machine holds under that name. That decides more than the shape of the glyphs.
///   Every axis renderer here measures its tick labels before it decides how much of the frame to
///   keep for itself, and what is left over is the plot area - so the width of a digit in the
///   resolved face reaches all the way to where the columns are drawn. Liberation Sans is used
///   because it carries the metrics of Arial.
///
///   The other two test projects have a resolver of the same name and for the same reason. This
///   one is the smallest of the three: no registration hook and no PostScript face, because
///   nothing here draws in a font of its own choosing.
/// </remarks>
internal sealed class PinnedFontResolver : IFontResolver
{
    private static readonly ConcurrentDictionary<string, byte[]> Fonts =
        new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

    public string DefaultFontName => "Arial";

    /// <summary>
    ///   Every family is answered, so that a chart asking for a font that is not shipped is laid
    ///   out the same way everywhere instead of falling back to the machine.
    /// </summary>
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        return new FontResolverInfo(FaceNameOf(isBold, isItalic));
    }

    public byte[] GetFont(string faceName)
    {
        return Fonts.GetOrAdd(faceName, name => File.ReadAllBytes(FontPath("LiberationSans-" + name + ".ttf")));
    }

    private static string FaceNameOf(bool isBold, bool isItalic)
    {
        if (isBold && isItalic)
            return "BoldItalic";
        if (isBold)
            return "Bold";
        if (isItalic)
            return "Italic";
        return "Regular";
    }

    private static string FontPath(string fileName)
    {
        var directory = Path.GetDirectoryName(typeof(PinnedFontResolver).GetTypeInfo().Assembly.Location);
        return Path.Combine(directory, "Assets", "Fonts", fileName);
    }
}
