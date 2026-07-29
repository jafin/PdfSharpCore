#region MigraDoc - Creating Documents on the Fly
//
// Authors:
//   Stefan Lange (mailto:Stefan.Lange@PdfSharpCore.com)
//   Klaus Potzesny (mailto:Klaus.Potzesny@PdfSharpCore.com)
//   David Stephensen (mailto:David.Stephensen@PdfSharpCore.com)
//
// Copyright (c) 2001-2009 empira Software GmbH, Cologne (Germany)
//
// http://www.PdfSharpCore.com
// http://www.migradoc.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Diagnostics;
using System.IO;
using MigraDocCore.DocumentObjectModel.Internals;
using static MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes.ImageSource;

namespace MigraDocCore.DocumentObjectModel.Shapes;

/// <summary>
/// Represents an image in the document or paragraph.
/// </summary>
public partial class Image : Shape
{
    /// <summary>
    /// Initializes a new instance of the Image class.
    /// </summary>
    public Image()
    {
    }

    /// <summary>
    /// Initializes a new instance of the Image class with the specified parent.
    /// </summary>
    internal Image(DocumentObject parent) : base(parent) { }

    //#region Methods
    /// <summary>
    /// Creates a deep copy of this object.
    /// </summary>
    public new Image Clone()
    {
        return (Image)DeepCopy();
    }

    /// <summary>
    /// Implements the deep copy of the object.
    /// </summary>
    protected override object DeepCopy()
    {
        Image image = (Image)base.DeepCopy();
        if (image.pictureFormat != null)
        {
            image.pictureFormat = image.pictureFormat.Clone();
            image.pictureFormat.parent = image;
        }
        return image;
    }
    //#endregion
    [DV]
    // Written only through the reflection layer, so it needs an initializer to count as assigned.
    internal string name = null;

    public IImageSource Source { get; set; }

    /// <summary>
    /// Gets or sets the ScaleWidth of the image.
    /// If the Width is set to, the resulting image width is ScaleWidth * Width.
    /// </summary>
    public double ScaleWidth
    {
        get => this.scaleWidth ?? 0;
        set => this.scaleWidth = value;
    }
    [DV]
    internal double? scaleWidth;

    /// <summary>
    /// Gets or sets the ScaleHeight of the image.
    /// If the Height is set too, the resulting image height is ScaleHeight * Height.
    /// </summary>
    public double ScaleHeight
    {
        get => this.scaleHeight ?? 0;
        set => this.scaleHeight = value;
    }
    [DV]
    internal double? scaleHeight;

    /// <summary>
    /// Gets or sets whether the AspectRatio of the image is kept unchanged.
    /// If both Width and Height are set, this property is ignored.
    /// </summary>
    public bool LockAspectRatio
    {
        get => this.lockAspectRatio ?? false;
        set => this.lockAspectRatio = value;
    }
    [DV]
    internal bool? lockAspectRatio;

    /// <summary>
    /// Gets or sets the PictureFormat for the image
    /// </summary>
    public PictureFormat PictureFormat
    {
        get
        {
            if (this.pictureFormat == null)
                this.pictureFormat = new PictureFormat(this);
            return this.pictureFormat;
        }
        set
        {
            SetParent(value);
            this.pictureFormat = value;
        }
    }
    [DV]
    internal PictureFormat pictureFormat;

    /// <summary>
    /// Gets or sets a user defined resolution for the image in dots per inch.
    /// </summary>
    public double Resolution
    {
        get => this.resolution ?? 0;
        set => this.resolution = value;
    }
    [DV]
    internal double? resolution;
    //#endregion

    #region Internal
    /// <summary>
    /// Converts Image into DDL.
    /// </summary>
    internal override void Serialize(Serializer serializer)
    {
        serializer.WriteLine("\\image(\"" + (this.name ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")");

        int pos = serializer.BeginAttributes();

        base.Serialize(serializer);
        if (this.scaleWidth != null)
            serializer.WriteSimpleAttribute("ScaleWidth", this.ScaleWidth);
        if (this.scaleHeight != null)
            serializer.WriteSimpleAttribute("ScaleHeight", this.ScaleHeight);
        if (this.lockAspectRatio != null)
            serializer.WriteSimpleAttribute("LockAspectRatio", this.LockAspectRatio);
        if (this.resolution != null)
            serializer.WriteSimpleAttribute("Resolution", this.Resolution);
        if (!this.IsNull("PictureFormat"))
            this.pictureFormat.Serialize(serializer);

        serializer.EndAttributes(pos);
    }

    /// <summary>
    /// Gets the concrete image path, taking into account the DOM document's DdlFile and
    /// ImagePath properties as well as the given working directory (which can be null).
    /// </summary>
    public string GetFilePath(string workingDir)
    {
        string filePath = "";

        try
        {
            if (!String.IsNullOrEmpty(workingDir))
                filePath = workingDir;
            else
                filePath = Directory.GetCurrentDirectory() + "\\";

            if (!Document.IsNull("ImagePath"))
            {
                string foundfile = ImageHelper.GetImageName(filePath, Source.Name, Document.ImagePath);
                if (foundfile != null)
                    filePath = foundfile;
                else
                    filePath = Path.Combine(filePath, Source.Name);
            }
            else
                filePath = Path.Combine(filePath, Source.Name);
        }
        catch (Exception ex)
        {
            Debug.Assert(false, "Should never occur with properly formatted Wiki texts. " + ex);
            return null;
            //throw;
        }

        return filePath;
    }

    #endregion
}
