using AwesomeAssertions;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Drawing;

/// <summary>
///   Gradients with alpha are drawn under a soft mask. Gradients without alpha carry none of that
///   machinery, and nothing about how they are written may change except the one thing that had
///   to: an RGB ramp used to be given a fourth value, the alpha, which is not a colour component.
/// </summary>
/// <remarks>
///   The expected text below was captured from the library before the soft mask was added, with
///   that fourth value struck off each ramp. It covers all four linear gradient modes, the
///   two-point linear constructor and a radial gradient, so a change to the shared geometry shows
///   up here whichever shape it breaks.
/// </remarks>
public class OpaqueGradientOutputTests
{
    [Fact]
    public void AnOpaqueGradientIsWrittenAsItWasBesidesItsRamp()
    {
        GradientOutput.Of(GradientOutput.OpaqueGradients()).Should().Be(Expected);
    }

    [Fact]
    public void AnRgbRampCarriesOneValuePerColourComponent()
    {
        var written = GradientOutput.Of(GradientOutput.OpaqueGradients());

        // Three for DeviceRGB. A fourth makes the function wider than the space it feeds, and a
        // conformant reader answers that by painting nothing at all.
        written.Should().NotContain("/C0 [ 1 0 0 1 ]");
        written.Should().Contain("/C0 [ 1 0 0 ]");
    }

    [Fact]
    public void NoTransparencyMachineryIsAddedForAnOpaqueGradient()
    {
        var written = GradientOutput.Of(GradientOutput.OpaqueGradients());

        written.Should().NotContain("/SMask");
        written.Should().NotContain("/Transparency");
        written.Should().NotContain(" gs\n");
    }

    const string Expected =
        """
        --- content ---
        q
        q
        /Pattern cs
        /Pa0 scn
        20 722 200 100 re
        f
        /Pattern cs
        /Pa1 scn
        20 602 200 100 re
        f
        /Pattern cs
        /Pa2 scn
        20 482 200 100 re
        f
        /Pattern cs
        /Pa3 scn
        20 362 200 100 re
        f
        /Pattern cs
        /Pa4 scn
        220 242 m
        220 186.7715 175.2285 142 120 142 c
        64.7715 142 20 186.7715 20 242 c
        20 297.2285 64.7715 342 120 342 c
        175.2285 342 220 297.2285 220 242 c
        h f
        Q
        Q
        --- /Pattern ---
        /Pa0 << /Matrix [ 1 0 0 1 0 0 ] /PatternType 2 /Shading << /ColorSpace /DeviceRGB /Coords [ 20 822 220 822 ] /Function << /C0 [ 1 0 0 ] /C1 [ 0 0 1 ] /Domain [ 0 1 ] /FunctionType 2 /N 1 >> /ShadingType 2 >> /Type /Pattern >>
        /Pa0/Shading << /ColorSpace /DeviceRGB /Coords [ 20 822 220 822 ] /Function << /C0 [ 1 0 0 ] /C1 [ 0 0 1 ] /Domain [ 0 1 ] /FunctionType 2 /N 1 >> /ShadingType 2 >>
        /Pa1 << /Matrix [ 1 0 0 1 0 0 ] /PatternType 2 /Shading << /ColorSpace /DeviceRGB /Coords [ 20 702 20 602 ] /Function << /C0 [ 0 0.502 0 ] /C1 [ 1 1 0 ] /Domain [ 0 1 ] /FunctionType 2 /N 1 >> /ShadingType 2 >> /Type /Pattern >>
        /Pa1/Shading << /ColorSpace /DeviceRGB /Coords [ 20 702 20 602 ] /Function << /C0 [ 0 0.502 0 ] /C1 [ 1 1 0 ] /Domain [ 0 1 ] /FunctionType 2 /N 1 >> /ShadingType 2 >>
        /Pa2 << /Matrix [ 1 0 0 1 0 0 ] /PatternType 2 /Shading << /ColorSpace /DeviceRGB /Coords [ 20 582 220 482 ] /Function << /C0 [ 0 0 0 ] /C1 [ 1 1 1 ] /Domain [ 0 1 ] /FunctionType 2 /N 1 >> /ShadingType 2 >> /Type /Pattern >>
        /Pa2/Shading << /ColorSpace /DeviceRGB /Coords [ 20 582 220 482 ] /Function << /C0 [ 0 0 0 ] /C1 [ 1 1 1 ] /Domain [ 0 1 ] /FunctionType 2 /N 1 >> /ShadingType 2 >>
        /Pa3 << /Matrix [ 1 0 0 1 0 0 ] /PatternType 2 /Shading << /ColorSpace /DeviceRGB /Coords [ 20 462 220 362 ] /Function << /C0 [ 0 1 1 ] /C1 [ 1 0 1 ] /Domain [ 0 1 ] /FunctionType 2 /N 1 >> /ShadingType 2 >> /Type /Pattern >>
        /Pa3/Shading << /ColorSpace /DeviceRGB /Coords [ 20 462 220 362 ] /Function << /C0 [ 0 1 1 ] /C1 [ 1 0 1 ] /Domain [ 0 1 ] /FunctionType 2 /N 1 >> /ShadingType 2 >>
        /Pa4 << /Matrix [ 1 0 0 1 0 0 ] /PatternType 2 /Shading << /ColorSpace /DeviceRGB /Coords [ 120 242 0 120 242 100 ] /Function << /C0 [ 1 1 1 ] /C1 [ 0 0 0.545 ] /Domain [ 0 1 ] /FunctionType 2 /N 1 >> /ShadingType 3 >> /Type /Pattern >>
        /Pa4/Shading << /ColorSpace /DeviceRGB /Coords [ 120 242 0 120 242 100 ] /Function << /C0 [ 1 1 1 ] /C1 [ 0 0 0.545 ] /Domain [ 0 1 ] /FunctionType 2 /N 1 >> /ShadingType 3 >>
        --- /Shading ---
        --- /ExtGState ---

        """;
}
