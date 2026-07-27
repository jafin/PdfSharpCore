using System.Runtime.CompilerServices;
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PdfSharpCore.Fonts;
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
            GlobalFontSettings.FontResolver = new SkiaFontResolver();
        }
    }
}
