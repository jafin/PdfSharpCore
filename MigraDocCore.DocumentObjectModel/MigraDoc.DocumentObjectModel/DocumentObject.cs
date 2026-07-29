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
using MigraDocCore.DocumentObjectModel.Internals;
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Resources;

namespace MigraDocCore.DocumentObjectModel;

/// <summary>
/// Base class of all objects of the MigraDoc Document Object Model.
/// </summary>
public abstract partial class DocumentObject
{
  /// <summary>
  /// Initializes a new instance of the DocumentObject class.
  /// </summary>
  internal DocumentObject()
  {
  }

  /// <summary>
  /// Initializes a new instance of the DocumentObject class with the specified parent.
  /// </summary>
  internal DocumentObject(DocumentObject parent)
  {
    Debug.Assert(parent != null, "Parent must not be null.");
    this.parent = parent;
  }

  /// <summary>
  /// Creates a deep copy of the DocumentObject. The parent of the new object is null.
  /// </summary>
  public object Clone()
  {
    return DeepCopy();
  }

  /// <summary>
  /// Implements the deep copy of the object.
  /// </summary>
  protected virtual object DeepCopy()
  {
    DocumentObject value = (DocumentObject)MemberwiseClone();
    value.parent = null;
    return value;
  }

  /// <summary>
  /// Creates an object using the default constructor.
  /// </summary>
  public object CreateValue(string name)
  {
    ValueDescriptor vd = Meta[name];
    if (vd != null)
      return vd.CreateValue();
    return null;
  }

  /// <summary>
  /// Gets the parent object.
  /// </summary>
  internal DocumentObject Parent => parent;

  [DV(RefOnly = true)]
  protected internal DocumentObject parent;

  /// <summary>
  /// Throws if this object belongs to a style that is read-only.
  /// </summary>
  /// <remarks>
  /// The built-in DefaultParagraphFont style is read-only, and used to enforce that by handing
  /// back a clone of its ParagraphFormat on every read. That is not enforcement: an assignment
  /// landed on the clone, the clone was discarded when the expression ended, and the caller had no
  /// way to tell success from silence. The clone is still handed out - reading a built-in style is
  /// legitimate - but it now carries its Style as its parent, so a write can find it and refuse.
  /// </remarks>
  internal void ThrowIfReadOnly()
  {
    for (DocumentObject owner = this; owner != null; owner = owner.parent)
    {
      if (owner is Style { IsReadOnly: true } style)
      {
        throw new InvalidOperationException(
          $"The style '{style.Name}' is read-only and cannot be modified. It is one of the "
          + "built-in styles. Add a style of your own with Styles.AddStyle, basing it on this one "
          + "if you want to start from its formatting.");
      }
    }
  }

  /// <summary>
  /// Gets the document of the object, or null, if the object is not associated with a document.
  /// </summary>
  public Document Document
  {
    get
    {
      DocumentObject doc = Parent;
      while (doc != null)
      {
        Document document = doc as Document;
        if (document != null)
          return document;
        doc = doc.parent;
      }
      return null;
    }
  }

  /// <summary>
  /// Gets the section of the object, or null, if the object is not associated with a section.
  /// </summary>
  public Section Section
  {
    get
    {
      DocumentObject doc = Parent;
      while (doc != null)
      {
        Section section = doc as Section;
        if (section != null)
          return section;
        doc = doc.parent;
      }
      return null;
    }
  }

  /// <summary>
  /// Converts DocumentObject into DDL.
  /// </summary>
  internal abstract void Serialize(Serializer serializer);

  /// <summary>
  /// Returns the value with the specified name.
  /// </summary>
  public virtual object GetValue(string name)
  {
    return GetValue(name, GV.ReadWrite);
  }

  /// <summary>
  /// Returns the value with the specified name and value flags.
  /// </summary>
  public virtual object GetValue(string name, GV flags)
  {
    return Meta.GetValue(this, name, flags);
  }

  /// <summary>
  /// Sets the given value and sets its parent afterwards.
  /// </summary>
  public virtual void SetValue(string name, object val)
  {
    Meta.SetValue(this, name, val);
    if (val is DocumentObject)
      ((DocumentObject)val).parent = this;
  }

  /// <summary>
  /// Determines whether this instance has a value of the given name.
  /// </summary>
  public virtual bool HasValue(string name)
  {
    return Meta.HasValue(name);
  }

  /// <summary>
  /// Determines whether the value of the given name is null.
  /// </summary>
  public virtual bool IsNull(string name)
  {
    return Meta.IsNull(this, name);
  }

  /// <summary>
  /// Resets the value of the given name, i.e. IsNull(name) will return true afterwards.
  /// </summary>
  public virtual void SetNull(string name)
  {
    Meta.SetNull(this, name);
  }

  /// <summary>
  /// Determines whether this instance is null (not set).
  /// </summary>
  public virtual bool IsNull()
  {
    return Meta.IsNull(this);
  }

  /// <summary>
  /// Resets this instance, i.e. IsNull() will return true afterwards.
  /// </summary>
  public virtual void SetNull()
  {
    Meta.SetNull(this);
  }

  /// <summary>
  /// Gets or sets a value that contains arbitrary information about this object.
  /// </summary>
  public object Tag
  {
    get => tag;
    set => tag = value;
  }
  object tag;

  /// <summary>
  /// Returns the meta object of this instance.
  /// </summary>
  internal abstract Meta Meta
  {
    get;
  }

  /// <summary>
  /// Sets the parent of the specified value.
  /// If a parent is already set, an ArgumentException will be thrown.
  /// </summary>
  protected void SetParent(DocumentObject val)
  {
    if (val != null)
    {
      if (val.Parent != null)
        throw new ArgumentException(DomSR.ParentAlreadySet(val, this));

      val.parent = this;
    }
  }

  /// <summary>
  /// When overridden in a derived class resets cached values
  /// (like column index).
  /// </summary>
  internal virtual void ResetCachedValues()
  {
  }
}
