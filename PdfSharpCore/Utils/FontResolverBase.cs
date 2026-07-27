
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

        private Dictionary<string, FontFamilyModel> _installedFonts;

        private string[] _supportedFonts;


        /// <summary>
        /// Reads the family name and style out of the given font file.
        /// </summary>
        protected abstract FontMetadata ReadFontMetadata(string fontFilePath);


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
            public FontFileInfo(string path, FontMetadata metadata)
            {
                this.Path = path;
                this.Metadata = metadata;
            }

            public string Path { get; }

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
            Dictionary<string, FontFamilyModel> installedFonts = new Dictionary<string, FontFamilyModel>();

            List<FontFileInfo> tempFontInfoList = new List<FontFileInfo>();
            foreach (string fontPathFile in sSupportedFonts)
            {
                try
                {
                    FontFileInfo fontInfo = new FontFileInfo(fontPathFile, ReadFontMetadata(fontPathFile));
                    Debug.WriteLine(fontPathFile);
                    tempFontInfoList.Add(fontInfo);
                }
                catch (System.Exception e)
                {
#if DEBUG
                    System.Console.Error.WriteLine(e);
#endif
                }
            }

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
#if DEBUG
                    System.Console.Error.WriteLine(e);
#endif
                }

            lock (_initLock)
            {
                _supportedFonts = sSupportedFonts;
                _installedFonts = installedFonts;
            }
        }


        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        private static FontFamilyModel DeserializeFontFamily(string fontFamilyName, IEnumerable<FontFileInfo> fontList)
        {
            FontFamilyModel font = new FontFamilyModel { Name = fontFamilyName };

            // there is only one font
            if (fontList.Count() == 1)
                font.FontFiles.Add(XFontStyle.Regular, fontList.First().Path);
            else
            {
                foreach (FontFileInfo info in fontList)
                {
                    XFontStyle style = info.GuessFontStyle();
                    if (!font.FontFiles.ContainsKey(style))
                        font.FontFiles.Add(style, info.Path);
                }
            }

            return font;
        }

        public virtual byte[] GetFont(string faceFileName)
        {
            EnsureInitialized();

            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                string ttfPathFile = "";
                try
                {
                    ttfPathFile = _supportedFonts.ToList().First(x => x.ToLower().Contains(
                        System.IO.Path.GetFileName(faceFileName).ToLower())
                    );

                    using (System.IO.Stream ttf = System.IO.File.OpenRead(ttfPathFile))
                    {
                        ttf.CopyTo(ms);
                        ms.Position = 0;
                        return ms.ToArray();
                    }
                }
                catch (System.Exception e)
                {
                    System.Console.WriteLine(e);
                    throw new System.Exception("No Font File Found - " + faceFileName + " - " + ttfPathFile);
                }
            }
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
                    if (family.FontFiles.TryGetValue(XFontStyle.BoldItalic, out string boldItalicFile))
                        return new FontResolverInfo(System.IO.Path.GetFileName(boldItalicFile));
                }
                else if (isBold)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Bold, out string boldFile))
                        return new FontResolverInfo(System.IO.Path.GetFileName(boldFile));
                }
                else if (isItalic)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Italic, out string italicFile))
                        return new FontResolverInfo(System.IO.Path.GetFileName(italicFile));
                }

                if (family.FontFiles.TryGetValue(XFontStyle.Regular, out string regularFile))
                    return new FontResolverInfo(System.IO.Path.GetFileName(regularFile));

                return new FontResolverInfo(System.IO.Path.GetFileName(family.FontFiles.First().Value));
            }

            if (NullIfFontNotFound)
                return null;

            string ttfFile = _installedFonts.First().Value.FontFiles.First().Value;
            return new FontResolverInfo(System.IO.Path.GetFileName(ttfFile));
        }
    }
}
