using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using PdfSharpCore.Fonts;

namespace MigraDocCore.Rendering.Tests.Helpers;

/// <summary>
///   Answers every request with the fonts shipped alongside these tests, rather than with
///   whatever the machine happens to have installed.
/// </summary>
/// <remarks>
///   The resolver the library ships scans the font directories of the platform, so a document
///   laid out by the tests uses Arial here and whatever stands in for it there. That decides more
///   than the shape of the glyphs: their widths decide where a line wraps, and every assertion
///   below about what landed on which page rests on the wrap being the same everywhere.
///   Liberation Sans is used because it carries the metrics of Arial.
///
///   The main test project has a resolver of the same name and for the same reason. This one is
///   deliberately the smaller of the two - no registration hook, no PostScript face - because the
///   renderer is what is under test here and the fonts only have to hold still.
/// </remarks>
internal sealed class PinnedFontResolver : IFontResolver
{
    static readonly ConcurrentDictionary<string, byte[]> Fonts =
        new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

    public string DefaultFontName => "Arial";

    /// <summary>
    ///   Every family is answered, so that a document asking for a font that is not shipped is
    ///   laid out the same way everywhere instead of falling back to the machine.
    /// </summary>
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        return new FontResolverInfo(FaceNameOf(isBold, isItalic));
    }

    public byte[] GetFont(string faceName)
    {
        return Fonts.GetOrAdd(faceName, name => File.ReadAllBytes(FontPath("LiberationSans-" + name + ".ttf")));
    }

    static string FaceNameOf(bool isBold, bool isItalic)
    {
        if (isBold && isItalic)
            return "BoldItalic";
        if (isBold)
            return "Bold";
        if (isItalic)
            return "Italic";
        return "Regular";
    }

    static string FontPath(string fileName)
    {
        var directory = Path.GetDirectoryName(typeof(PinnedFontResolver).GetTypeInfo().Assembly.Location);
        return Path.Combine(directory, "Assets", "Fonts", fileName);
    }
}
