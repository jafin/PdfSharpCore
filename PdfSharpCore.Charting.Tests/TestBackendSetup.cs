using System.Runtime.CompilerServices;
using PdfSharpCore.Charting.Tests.Helpers;
using PdfSharpCore.Fonts;

namespace PdfSharpCore.Charting.Tests;

/// <summary>
/// PdfSharpCore carries no font backend of its own, so the test assembly registers one. Skia
/// because it is the default; nothing here is about which backend is in use.
/// </summary>
/// <remarks>
/// A module initializer rather than a fixture: the font resolver may only be set before the first
/// font is created, so it has to be in place before any test runs rather than before the first one
/// that asks for it.
///
/// No image source and no glyph outline provider. A chart draws lines, rectangles, wedges and
/// text, and reaches neither seam - leaving them unset means a renderer that grows a use for one
/// says so with the seam's own exception rather than quietly working here and nowhere else.
/// </remarks>
internal static class TestBackendSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Not the resolver the library ships: that one picks a font off the machine, which measures
        // a tick label differently on each of them. See PinnedFontResolver.
        GlobalFontSettings.FontResolver = new PinnedFontResolver();
    }
}
