using System.Collections.Generic;

namespace PdfSharpCore.Test.IO;

/// <summary>
///   Pages shaped to exercise the four PDF/A rules that need a page-resource walk rather than a
///   look at what the document was configured with: transparency, a JPEG 2000 image, an
///   interpolated image, and a device colour the output intent does not describe. Built by hand,
///   the way <see cref="SharedResourceFixtures"/> is, because none of this library's own drawing
///   API writes a raw <c>/ca</c>, a raw <c>/Filter /JPXDecode</c>, or a bare <c>k</c> operator.
/// </summary>
internal static class ResourceConformanceFixtures
{
    /// <summary>A page painting through a graphics state with less than full alpha.</summary>
    internal static byte[] PageWithATranslucentGraphicsState()
    {
        return OnePageDocument(
            "/ExtGState<</GS0 5 0 R>>",
            "/GS0 gs 0 0 1 rg 0 0 100 100 re f",
            "<</Type/ExtGState/ca 0.5>>");
    }

    /// <summary>The same page, opaque — an explicit full alpha rather than none at all.</summary>
    internal static byte[] PageWithAnOpaqueGraphicsState()
    {
        return OnePageDocument(
            "/ExtGState<</GS0 5 0 R>>",
            "/GS0 gs 0 0 1 rg 0 0 100 100 re f",
            "<</Type/ExtGState/ca 1>>");
    }

    /// <summary>
    ///   Transparency reachable only by following a form XObject the page draws — not visible from
    ///   the page's own resource dictionary without reading into the form.
    /// </summary>
    internal static byte[] PageWithTransparencyThroughANestedForm()
    {
        return OnePageDocument(
            "/XObject<</Fm0 5 0 R>>",
            "/Fm0 Do",
            Form("/Group<</S/Transparency/CS/DeviceGray>>", "0 0 1 rg 0 0 100 100 re f"));
    }

    /// <summary>
    ///   Transparency reachable only by following the soft mask hung off a graphics state — not an
    ///   image, not a form the page names directly, and not the graphics state's own alpha.
    /// </summary>
    internal static byte[] PageWithTransparencyThroughASoftMask()
    {
        return OnePageDocument(
            "/ExtGState<</GS0 5 0 R>>",
            "/GS0 gs 0 0 1 rg 0 0 100 100 re f",
            "<</Type/ExtGState/SMask<</Type/Mask/S/Luminosity/G 6 0 R>>>>",
            Form("", "1 g 0 0 100 100 re f"));
    }

    /// <summary>A page drawing an image filtered with JPXDecode.</summary>
    internal static byte[] PageWithAJpeg2000Image()
    {
        return OnePageDocument("/XObject<</Im0 5 0 R>>", "/Im0 Do", Image("/Filter/JPXDecode"));
    }

    /// <summary>The same page, with an ordinary image in the same place.</summary>
    internal static byte[] PageWithAnOrdinaryImage()
    {
        return OnePageDocument("/XObject<</Im0 5 0 R>>", "/Im0 Do", Image(""));
    }

    /// <summary>A page drawing an image set to interpolate.</summary>
    internal static byte[] PageWithAnInterpolatedImage()
    {
        return OnePageDocument("/XObject<</Im0 5 0 R>>", "/Im0 Do", Image("/Interpolate true"));
    }

    /// <summary>A page painting with both DeviceRGB and DeviceCMYK directly.</summary>
    internal static byte[] PageMixingDeviceColourSpaces()
    {
        return OnePageDocument("", "1 0 0 rg 0 0 50 50 re f 0 0 0 1 k 50 0 50 50 re f");
    }

    /// <summary>The same shape, painting with RGB alone — what an sRGB output intent describes.</summary>
    internal static byte[] PageDrawingOnlyRgb()
    {
        return OnePageDocument("", "1 0 0 rg 0 0 100 100 re f");
    }

    /// <summary>
    ///   A single page document whose page names the resources given and draws the content given.
    ///   The objects that follow the page are numbered from five.
    /// </summary>
    private static byte[] OnePageDocument(string resources, string content, params string[] rest)
    {
        var objects = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            Page("/Resources<<" + resources + ">>/Contents 4 0 R"),
            Draw(content),
        };
        objects.AddRange(rest);

        return RawPdf.Build(objects);
    }

    private static string Page(string entries)
    {
        return "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]" + entries + ">>";
    }

    private static string Draw(string content)
    {
        return RawPdf.Stream("", content);
    }

    private static string Form(string entries, string content)
    {
        return RawPdf.Stream("/Type/XObject/Subtype/Form/BBox[0 0 200 200]" + entries, content);
    }

    private static string Image(string extra)
    {
        return RawPdf.Stream(
            "/Type/XObject/Subtype/Image/Width 4/Height 4/ColorSpace/DeviceGray/BitsPerComponent 8"
            + extra,
            new string('A', 16));
    }
}
