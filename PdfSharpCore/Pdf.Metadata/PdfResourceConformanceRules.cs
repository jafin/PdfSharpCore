using System.Collections.Generic;
using System.Linq;
using PdfSharpCore.Pdf.Advanced;

namespace PdfSharpCore.Pdf.Metadata;

/// <summary>
/// The questions the four unenforced PDF/A rules ask of a page-resource walk. The walk itself —
/// <see cref="PdfPageResourceUsage"/> — only reports what a page reaches; every judgment about what
/// that means for a claimed profile lives here, beside the rest of the conformance rules, rather
/// than inside the walk. A walk that knew about PDF/A would have to change for every future rule.
/// </summary>
internal static class PdfResourceConformanceRules
{
    /// <summary>
    /// Whether the page paints with transparency, reached through an image's soft mask, a graphics
    /// state's alpha, blend mode or soft mask, or a form declaring a transparency group of its own —
    /// however deep the form or soft mask that carries it is nested.
    /// </summary>
    internal static bool UsesTransparency(PdfPageResourceUsage usage)
    {
        if (usage.Images.Any(PdfTransparencyDetector.ImagePaints))
            return true;

        if (usage.GraphicsStates.Any(PdfTransparencyDetector.StatePaints))
            return true;

        return usage.Forms.Any(DeclaresTransparencyGroup);
    }

    static bool DeclaresTransparencyGroup(PdfDictionary form)
    {
        var group = form.Elements.GetDictionary(PdfPage.Keys.Group);
        return group != null && group.Elements.GetName("/S") == "/Transparency";
    }

    /// <summary>Whether any image the page reaches is filtered with <c>/JPXDecode</c>.</summary>
    internal static bool UsesJpxImage(PdfPageResourceUsage usage) => usage.Images.Any(HasJpxFilter);

    static bool HasJpxFilter(PdfDictionary image)
    {
        var filter = Resolve(image.Elements["/Filter"]);

        if (filter is PdfName single)
            return single.Value == "/JPXDecode";

        if (filter is PdfArray array)
        {
            foreach (var item in array.Elements)
            {
                if (Resolve(item) is PdfName name && name.Value == "/JPXDecode")
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether any image the page reaches is set to interpolate.</summary>
    internal static bool UsesInterpolatedImage(PdfPageResourceUsage usage) =>
        usage.Images.Any(image => image.Elements.GetBoolean("/Interpolate"));

    /// <summary>
    /// Adds the component count of every device colour family the page paints with — 1 for grey, 3
    /// for RGB, 4 for CMYK, and whatever an ICC-based, indexed, separation or device-N colour space
    /// resolves to underneath — to <paramref name="families"/>.
    /// </summary>
    /// <remarks>
    /// Reported as component counts rather than as colour spaces, because that is the one fact
    /// <see cref="PdfConformanceWriter"/> can compare against an output intent's own <c>/N</c> — the
    /// two do not have to be the same kind of colour space to agree, only the same size.
    /// </remarks>
    internal static void CollectDeviceColorFamilies(PdfPageResourceUsage usage, HashSet<int> families)
    {
        if (usage.UsesDeviceGray)
            families.Add(1);
        if (usage.UsesDeviceRgb)
            families.Add(3);
        if (usage.UsesDeviceCmyk)
            families.Add(4);

        foreach (var image in usage.Images)
        {
            var components = ComponentsOf(image.Elements["/ColorSpace"], 0);
            if (components != null)
                families.Add(components.Value);
        }

        foreach (var colorSpace in usage.NamedColorSpaces)
        {
            var components = ComponentsOf(colorSpace, 0);
            if (components != null)
                families.Add(components.Value);
        }
    }

    /// <summary>
    /// How many components a colour space has, resolving an indexed, separation, device-N or
    /// pattern space to the components of the space underneath. Null for a space this cannot read —
    /// an unrecognised name, an ICC profile with no <c>/N</c>, or nesting deep enough that this has
    /// given up understanding it, which is the same defensive limit the walk itself uses.
    /// </summary>
    static int? ComponentsOf(PdfItem colorSpace, int depth)
    {
        if (depth > 8)
            return null;

        var item = Resolve(colorSpace);

        switch (item)
        {
            case PdfName name:
                return DeviceFamilyOf(name.Value);

            case PdfDictionary stream when stream.Elements.ContainsKey("/N"):
                // An ICC-based profile referenced directly, rather than through the usual
                // [/ICCBased ref] array — unusual, but the array form below reaches the same
                // dictionary through its second element.
                return stream.Elements.GetInteger("/N");

            case PdfArray array when array.Elements.Count > 0:
                return ComponentsOfArray(array, depth);

            default:
                return null;
        }
    }

    static int? ComponentsOfArray(PdfArray array, int depth)
    {
        var head = Resolve(array.Elements[0]) as PdfName;
        switch (head?.Value)
        {
            case "/ICCBased":
                return array.Elements.Count > 1
                    && Resolve(array.Elements[1]) is PdfDictionary profile
                    && profile.Elements.ContainsKey("/N")
                    ? profile.Elements.GetInteger("/N")
                    : null;

            case "/Indexed":
                return array.Elements.Count > 1 ? ComponentsOf(array.Elements[1], depth + 1) : null;

            case "/Separation":
            case "/DeviceN":
                // [/Separation name alternateSpace tintTransform] and
                // [/DeviceN names alternateSpace tintTransform ...] agree on where the space
                // a reader without the separation ink actually paints in sits.
                return array.Elements.Count > 2 ? ComponentsOf(array.Elements[2], depth + 1) : null;

            case "/CalGray":
                return 1;

            case "/CalRGB":
            case "/Lab":
                return 3;

            case "/Pattern":
                // An uncoloured tiling pattern names the space its colour operands are given in;
                // a coloured one and a shading pattern carry no colour of their own to ask about.
                return array.Elements.Count > 1 ? ComponentsOf(array.Elements[1], depth + 1) : null;

            default:
                return null;
        }
    }

    static int? DeviceFamilyOf(string name) => name switch
    {
        "/DeviceGray" or "/CalGray" or "/G" => 1,
        "/DeviceRGB" or "/CalRGB" or "/RGB" or "/Lab" => 3,
        "/DeviceCMYK" or "/CMYK" => 4,
        _ => null,
    };

    static PdfItem Resolve(PdfItem item) => item is PdfReference reference ? reference.Value : item;
}
