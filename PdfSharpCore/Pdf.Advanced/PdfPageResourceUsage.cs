using System.Collections.Generic;
using PdfSharpCore.Pdf.Content.Objects;

namespace PdfSharpCore.Pdf.Advanced;

/// <summary>
/// What a page's resources actually contain, found by walking its content the way
/// <see cref="PdfResourcePruner"/> always has: every image and form the page reaches, however deep
/// the forms and soft masks nest, every graphics state it sets, every colour space its content names
/// or paints with directly.
/// </summary>
/// <remarks>
/// This is the walk's other caller — built once in <see cref="PdfPageWalk"/> and asked two different
/// questions rather than written twice. It only reports what a page uses; the PDF/A rules that need
/// this are asked in <c>Metadata.PdfResourceConformanceRules</c> and
/// <c>Metadata.PdfConformanceWriter</c>, not here — a walk that knew about PDF/A would have to change
/// for every future rule.
/// </remarks>
internal sealed class PdfPageResourceUsage : PdfPageWalk
{
    /// <summary>Every image XObject the page reaches.</summary>
    internal List<PdfDictionary> Images { get; } = new();

    /// <summary>Every form XObject the page reaches.</summary>
    internal List<PdfDictionary> Forms { get; } = new();

    /// <summary>Every graphics state the page sets.</summary>
    internal List<PdfDictionary> GraphicsStates { get; } = new();

    /// <summary>
    /// Every colour space the page names through a resource dictionary — a stream (an ICCBased
    /// profile), an array (<c>Indexed</c>, <c>Separation</c>, <c>DeviceN</c>, <c>CalRGB</c> and
    /// their kin) or a bare name, whatever the resource dictionary held.
    /// </summary>
    internal List<PdfItem> NamedColorSpaces { get; } = new();

    /// <summary>Whether the page's content sets grey directly, with <c>g</c> or <c>G</c>.</summary>
    internal bool UsesDeviceGray { get; private set; }

    /// <summary>Whether the page's content sets RGB directly, with <c>rg</c> or <c>RG</c>.</summary>
    internal bool UsesDeviceRgb { get; private set; }

    /// <summary>Whether the page's content sets CMYK directly, with <c>k</c> or <c>K</c>.</summary>
    internal bool UsesDeviceCmyk { get; private set; }

    /// <summary>
    /// Walks the page. <see cref="PdfPageWalk.Understood"/> says whether the whole of it was —
    /// a page whose content defeats the walk answers with none of its resources found, and the
    /// caller is expected to leave it unchecked rather than judge it on a guess.
    /// </summary>
    internal static PdfPageResourceUsage Walk(PdfPage page)
    {
        var resources = page.Elements.GetDictionary(PdfPage.Keys.Resources);
        var usage = new PdfPageResourceUsage(resources);

        // A page with no resource dictionary at all cannot name anything to draw with, so there is
        // nothing to walk and nothing it could have failed to understand.
        if (resources != null)
            usage.ReadPage(page);

        return usage;
    }

    PdfPageResourceUsage(PdfDictionary resources) : base(resources)
    {
    }

    protected override void Observe(COperator op, PdfDictionary scope, int depth)
    {
        switch (op.OpCode.OpCodeName)
        {
            case OpCodeName.g:
            case OpCodeName.G:
                UsesDeviceGray = true;
                break;

            case OpCodeName.rg:
            case OpCodeName.RG:
                UsesDeviceRgb = true;
                break;

            case OpCodeName.k:
            case OpCodeName.K:
                UsesDeviceCmyk = true;
                break;
        }
    }

    protected override void RecordResolved(string category, string name, PdfItem resolved)
    {
        if (resolved == null)
            return;

        switch (category)
        {
            case "/XObject":
                if (resolved is PdfDictionary xObject)
                {
                    var subtype = xObject.Elements.GetName("/Subtype");
                    if (subtype == "/Image")
                        Images.Add(xObject);
                    else if (subtype == "/Form")
                        Forms.Add(xObject);
                }
                break;

            case "/ExtGState":
                if (resolved is PdfDictionary state)
                    GraphicsStates.Add(state);
                break;

            case "/ColorSpace":
                NamedColorSpaces.Add(resolved);
                break;
        }
    }
}
