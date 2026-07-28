
using System.Linq;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;

using PdfSharpCore.Internal;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;


namespace PdfSharpCore.Utils
{

    /// <summary>
    /// Family name and style read from a font file.
    /// </summary>
    public readonly struct FontMetadata
    {
        public FontMetadata(string familyName, XFontStyle style)
        {
            FamilyName = familyName;
            Style = style;
        }

        public string FamilyName { get; }

        public XFontStyle Style { get; }
    }


    /// <summary>
    /// Locates the fonts installed on the current platform and resolves typefaces against them.
    /// Reading the family name and style out of a font file is left to a derived class, so that
    /// PdfSharpCore itself does not depend on any particular font library.
    /// Use the resolver from PdfSharpCore.Skia or PdfSharpCore.ImageSharp, or derive your own.
    /// </summary>
    public abstract class FontResolverBase
        : IFontResolver
    {
        public virtual string DefaultFontName => "Arial";

        // Per instance rather than static: two backends in one process must not inherit each
        // other's font mappings, and reading metadata is the derived class's job.
        private readonly object _initLock = new object();

        // Volatile because EnsureInitialized reads this outside the lock. Without it a thread can
        // see the reference before the writes that filled the dictionary, on any architecture with
        // a weaker memory model than x86.
        private volatile Dictionary<string, FontFamilyModel> _installedFonts;

        /// <summary>
        /// Maps the face name handed out by <see cref="ResolveTypeface"/> to the file it was read
        /// from. Published before <see cref="_installedFonts"/>, whose volatile write orders it.
        /// </summary>
        private Dictionary<string, string> _facePaths;


        /// <summary>
        /// Reads the family name and style out of the given font file.
        /// </summary>
        protected abstract FontMetadata ReadFontMetadata(string fontFilePath);


        /// <summary>
        /// Reports a font file that could not be used. Compiled out of release builds, which also
        /// keeps the caught exception "used" as far as the compiler is concerned.
        /// </summary>
        [Conditional("DEBUG")]
        private static void LogError(string message)
        {
            System.Console.Error.WriteLine(message);
        }


        /// <summary>
        /// Scans the platform font directories on first use. Deferred rather than done in the
        /// constructor so that <see cref="ReadFontMetadata"/> is never called on a half-built
        /// derived instance.
        /// </summary>
        private void EnsureInitialized()
        {
            if (_installedFonts != null)
                return;

            lock (_initLock)
            {
                if (_installedFonts == null)
                    SetupFontsFiles(GetPlatformFontFiles());
            }
        }


        private static string[] GetPlatformFontFiles()
        {
            string fontDir;

            bool isOSX = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
            if (isOSX)
            {
                fontDir = "/Library/Fonts/";
                return System.IO.Directory.GetFiles(fontDir, "*.ttf", System.IO.SearchOption.AllDirectories);
            }

            bool isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
            if (isLinux)
            {
                return LinuxSystemFontResolver.Resolve();
            }

            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            if (isWindows)
            {
                fontDir = System.Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Fonts");
                var fontPaths = new List<string>();

                var systemFontPaths = System.IO.Directory.GetFiles(fontDir, "*.ttf", System.IO.SearchOption.AllDirectories);
                fontPaths.AddRange(systemFontPaths);

                var appdataFontDir = System.Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Fonts");
                if (System.IO.Directory.Exists(appdataFontDir))
                {
                    var appdataFontPaths = System.IO.Directory.GetFiles(appdataFontDir, "*.ttf", System.IO.SearchOption.AllDirectories);
                    fontPaths.AddRange(appdataFontPaths);
                }

                return fontPaths.ToArray();
            }

            throw new System.NotImplementedException("FontResolver not implemented for this platform (PdfSharpCore.Utils.FontResolverBase.cs).");
        }


        private readonly struct FontFileInfo
        {
            public FontFileInfo(string faceName, FontMetadata metadata)
            {
                this.FaceName = faceName;
                this.Metadata = metadata;
            }

            /// <summary>
            /// The name this face is handed out under, and the key into the face-to-path map.
            /// </summary>
            public string FaceName { get; }

            public FontMetadata Metadata { get; }

            public string FamilyName => this.Metadata.FamilyName;

            public XFontStyle GuessFontStyle() => this.Metadata.Style;
        }


        /// <summary>
        /// Builds the family lookup from an explicit set of font files, replacing anything
        /// discovered previously.
        /// </summary>
        public void SetupFontsFiles(string[] sSupportedFonts)
        {
            Dictionary<string, string> facePaths = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            List<FontFileInfo> tempFontInfoList = new List<FontFileInfo>();

            foreach (string fontPathFile in sSupportedFonts)
            {
                string faceName = System.IO.Path.GetFileName(fontPathFile);

                // Two font directories habitually hold a file of the same name - on Windows the
                // system one and the per-user one. The first found wins, so that the face name a
                // family points at and the file it is read from cannot drift apart. Checked before
                // the metadata is read, so that a file the derived class cannot parse does not
                // reserve a name that a readable one could have used.
                if (facePaths.ContainsKey(faceName))
                    continue;

                FontMetadata metadata;
                try
                {
                    metadata = ReadFontMetadata(fontPathFile);
                }
                catch (System.Exception e)
                {
                    LogError(e.ToString());
                    continue;
                }

                Debug.WriteLine(fontPathFile);
                facePaths.Add(faceName, fontPathFile);
                tempFontInfoList.Add(new FontFileInfo(faceName, metadata));
            }

            Dictionary<string, FontFamilyModel> installedFonts = new Dictionary<string, FontFamilyModel>();

            // Deserialize all font families
            foreach (IGrouping<string, FontFileInfo> familyGroup in tempFontInfoList.GroupBy(info => info.FamilyName))
                try
                {
                    string familyName = familyGroup.Key;
                    FontFamilyModel family = DeserializeFontFamily(familyName, familyGroup);
                    installedFonts.Add(familyName.ToLower(), family);
                }
                catch (System.Exception e)
                {
                    LogError(e.ToString());
                }

            lock (_initLock)
            {
                _facePaths = facePaths;
                // Written last: its volatile write publishes _facePaths to EnsureInitialized,
                // which reads that field to decide whether both are ready.
                _installedFonts = installedFonts;
            }
        }


        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        private static FontFamilyModel DeserializeFontFamily(string fontFamilyName, IEnumerable<FontFileInfo> fontList)
        {
            FontFamilyModel font = new FontFamilyModel { Name = fontFamilyName };

            // there is only one font
            if (fontList.Count() == 1)
                font.FontFiles.Add(XFontStyle.Regular, fontList.First().FaceName);
            else
            {
                foreach (FontFileInfo info in fontList)
                {
                    XFontStyle style = info.GuessFontStyle();
                    if (!font.FontFiles.ContainsKey(style))
                        font.FontFiles.Add(style, info.FaceName);
                }
            }

            return font;
        }

        /// <param name="faceName">A face name handed out by <see cref="ResolveTypeface"/>.</param>
        public virtual byte[] GetFont(string faceName)
        {
            EnsureInitialized();

            if (!_facePaths.TryGetValue(faceName, out string fontPathFile))
                throw new System.IO.FileNotFoundException(
                    "No font file was discovered for the face name '" + faceName + "'.", faceName);

            return System.IO.File.ReadAllBytes(fontPathFile);
        }

        public bool NullIfFontNotFound { get; set; } = false;

        public virtual FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            EnsureInitialized();

            if (_installedFonts.Count == 0)
                throw new System.IO.FileNotFoundException("No Fonts installed on this device!");

            if (_installedFonts.TryGetValue(familyName.ToLower(), out FontFamilyModel family))
            {
                if (isBold && isItalic)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.BoldItalic, out string boldItalicFace))
                        return new FontResolverInfo(boldItalicFace);
                }
                else if (isBold)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Bold, out string boldFace))
                        return new FontResolverInfo(boldFace);
                }
                else if (isItalic)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Italic, out string italicFace))
                        return new FontResolverInfo(italicFace);
                }

                if (family.FontFiles.TryGetValue(XFontStyle.Regular, out string regularFace))
                    return new FontResolverInfo(regularFace);

                return new FontResolverInfo(family.FontFiles.First().Value);
            }

            if (NullIfFontNotFound)
                return null;

            return new FontResolverInfo(_installedFonts.First().Value.FontFiles.First().Value);
        }
    }
}
