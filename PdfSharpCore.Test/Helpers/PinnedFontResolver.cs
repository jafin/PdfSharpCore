using System;
using System.Collections.Concurrent;
using System.IO;
using PdfSharpCore.Fonts;

namespace PdfSharpCore.Test.Helpers
{
    /// <summary>
    ///   Answers every request with the fonts shipped alongside the tests, rather than with
    ///   whatever the machine happens to have installed.
    /// </summary>
    /// <remarks>
    ///   The resolver the library ships scans the font directories of the platform, so a document
    ///   built by the tests is laid out with Arial here and with whatever stands in for it there.
    ///   That decides more than the shape of the glyphs: their widths decide where a line wraps,
    ///   and the height of a line against the height of its characters decides whether the line
    ///   fits at all. Tests that assert either of those cannot pass on both machines while the
    ///   font is chosen for them. Liberation Sans is used because it carries the metrics of Arial,
    ///   which the assertions and the reference images were written against.
    /// </remarks>
    internal sealed class PinnedFontResolver : IFontResolver
    {
        private static readonly ConcurrentDictionary<string, byte[]> Fonts =
            new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///   The one family here that is not Liberation. Its outlines are PostScript (CFF) rather
        ///   than TrueType, which is a different embedding path entirely, and nothing else shipped
        ///   with the tests exercises it.
        /// </summary>
        public const string CffFamilyName = "Source Code Pro";

        private const string CffFaceName = "SourceCodePro-Regular.otf";

        public string DefaultFontName => "Arial";

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (string.Equals(familyName, CffFamilyName, StringComparison.OrdinalIgnoreCase))
                return new FontResolverInfo(CffFaceName);

            // Every other family is answered, so that a document asking for a font that is not
            // shipped is laid out the same way everywhere instead of falling back to the machine.
            return new FontResolverInfo(FaceNameOf(isBold, isItalic));
        }

        public byte[] GetFont(string faceName)
        {
            return Fonts.GetOrAdd(faceName, name => File.ReadAllBytes(
                name == CffFaceName
                    ? PathHelper.GetInstance().GetAssetPath("Fonts", CffFaceName)
                    : PathHelper.GetInstance().GetAssetPath("Fonts", "LiberationSans-" + name + ".ttf")));
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
    }
}
