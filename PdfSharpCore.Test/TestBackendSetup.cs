using System.Runtime.CompilerServices;
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PdfSharpCore.Fonts;
using PdfSharpCore.Test.Helpers;
using PdfSharpCore.Utils;

namespace PdfSharpCore.Test
{
    /// <summary>
    /// PdfSharpCore no longer ships an imaging or font backend of its own, so the test assembly
    /// registers one. Skia is used because it is the default backend; the ImageSharp backend is
    /// exercised explicitly by the tests that construct its image source directly.
    /// </summary>
    internal static class TestBackendSetup
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            ImageSource.ImageSourceImpl = new SkiaImageSource();
            // Not the resolver the library ships: that one picks a font off the machine, which
            // lays a document out differently on each of them. See PinnedFontResolver.
            GlobalFontSettings.FontResolver = new PinnedFontResolver();
            GhostscriptSetup.Configure();
        }
    }
}
