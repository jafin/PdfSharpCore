using System.Collections.Generic;
using PdfSharpCore.Drawing;

namespace PdfSharpCore.Internal;

public class FontFamilyModel
{
    public string Name { get; set; }

    /// <summary>
    /// Maps each style this family ships a file for to the face name that file is known by.
    /// Face names, not paths: the resolver hands these to callers and looks the path back up
    /// from one, so holding a path here would let the two drift apart.
    /// </summary>
    public Dictionary<XFontStyle, string> FontFiles = new();

    public bool IsStyleAvailable(XFontStyle fontStyle)
    {
        return FontFiles.ContainsKey(fontStyle);
    }
}
