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

using MigraDocCore.DocumentObjectModel.Internals;

namespace MigraDocCore.DocumentObjectModel.Shapes;

/// <summary>
/// Defines the background filling of the shape.
/// </summary>
public class FillFormat : DocumentObject
{
  /// <summary>
  /// Initializes a new instance of the FillFormat class.
  /// </summary>
  public FillFormat()
  {
  }

  /// <summary>
  /// Initializes a new instance of the FillFormat class with the specified parent.
  /// </summary>
  internal FillFormat(DocumentObject parent) : base(parent) { }

  #region Methods
  /// <summary>
  /// Creates a deep copy of this object.
  /// </summary>
  public new FillFormat Clone()
  {
    return (FillFormat)DeepCopy();
  }
  #endregion

  #region Properties
  /// <summary>
  /// Gets or sets the color of the filling.
  /// </summary>
  public Color Color
  {
    get => color;
    set => color = value;
  }
  [DV]
  internal Color color = Color.Empty;

  /// <summary>
  /// Gets or sets a value indicating whether the background color should be visible.
  /// </summary>
  public bool Visible
  {
    get => visible ?? false;
    set => visible = value;
  }
  [DV]
  internal bool? visible;
  #endregion

  #region Internal
  /// <summary>
  /// Converts FillFormat into DDL.
  /// </summary>
  internal override void Serialize(Serializer serializer)
  {
    int pos = serializer.BeginContent("FillFormat");
    if (visible != null)
      serializer.WriteSimpleAttribute("Visible", Visible);
    if (!color.IsNull)
      serializer.WriteSimpleAttribute("Color", Color);
    serializer.EndContent();
  }

  /// <summary>
  /// Returns the meta object of this instance.
  /// </summary>
  internal override Meta Meta => meta;

  /// <summary>
  /// Built once by the CLR, which finishes a static initializer before any thread
  /// can read the field it initializes. The lazy version this replaces had every
  /// thread that arrived first build its own and throw all but one away.
  /// </summary>
  static readonly Meta meta = new Meta(typeof(FillFormat));
  #endregion
}
