using System.Runtime.InteropServices;
using Xunit;

namespace PdfSharpCore.Test.Helpers
{
    /// <summary>
    /// A fact that compares rendered output against a checked-in reference image.
    /// </summary>
    /// <remarks>
    /// The reference images were rendered on Linux. Text rasterizes differently on other
    /// platforms because a different set of system fonts is installed - "Arial" resolves to
    /// Arial on Windows and to a substitute on a typical Linux image - so the comparison is only
    /// meaningful there. Rather than failing everywhere else, these tests report as skipped with
    /// the reason, and CI remains the authority on rendering.
    /// </remarks>
    public sealed class GoldenImageFactAttribute : FactAttribute
    {
        public GoldenImageFactAttribute()
        {
            if (!GhostscriptSetup.IsAvailable)
            {
                Skip = "Ghostscript is not available to rasterize PDFs on this platform.";
                return;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Skip = "Reference images are rendered on Linux; " + RuntimeInformation.OSDescription
                       + " resolves different system fonts, so the comparison would not be meaningful.";
        }
    }
}
