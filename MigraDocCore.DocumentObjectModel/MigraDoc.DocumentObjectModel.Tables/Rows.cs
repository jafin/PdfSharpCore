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
using MigraDocCore.DocumentObjectModel.Internals;
using MigraDocCore.DocumentObjectModel.Visitors;

namespace MigraDocCore.DocumentObjectModel.Tables;

/// <summary>
/// Represents the collection of all rows of a table.
/// </summary>
public class Rows : DocumentObjectCollection, IVisitable
{
  /// <summary>
  /// Initializes a new instance of the Rows class.
  /// </summary>
  public Rows()
  {
  }

  /// <summary>
  /// Initializes a new instance of the Rows class with the specified parent.
  /// </summary>
  internal Rows(DocumentObject parent) : base(parent) { }

  #region Methods
  /// <summary>
  /// Creates a deep copy of this object.
  /// </summary>
  public new Rows Clone()
  {
    return (Rows)base.DeepCopy();
  }

  /// <summary>
  /// Adds a new row to the rows collection. Allowed only if at least one column exists.
  /// </summary>
  public Row AddRow()
  {
    if (Table.Columns.Count == 0)
      throw new InvalidOperationException("Cannot add row, because no columns exists.");

    Row row = new Row();
    Add(row);
    return row;
  }
  #endregion

  #region Properties
  /// <summary>
  /// Gets the table the rows collection belongs to.
  /// </summary>
  public Table Table => parent as Table;

  /// <summary>
  /// Gets a row by its index.
  /// </summary>
  public new Row this[int index] => base[index] as Row;

  /// <summary>
  /// Gets or sets the row alignment of the table.
  /// </summary>
  public RowAlignment Alignment
  {
    get => (RowAlignment)alignment.Value;
    set => alignment.Value = (int)value;
  }
  [DV(Type = typeof(RowAlignment))]
  internal NEnum alignment = NEnum.NullValue(typeof(RowAlignment));

  /// <summary>
  /// Gets or sets the left indent of the table. If row alignment is not Left,
  /// the value is ignored.
  /// </summary>
  public Unit LeftIndent
  {
    get => leftIndent;
    set => leftIndent = value;
  }
  [DV]
  internal Unit leftIndent = Unit.NullValue;

  /// <summary>
  /// Gets or sets the default vertical alignment for all rows.
  /// </summary>
  public VerticalAlignment VerticalAlignment
  {
    get => (VerticalAlignment)verticalAlignment.Value;
    set => verticalAlignment.Value = (int)value;
  }
  [DV(Type = typeof(VerticalAlignment))]
  internal NEnum verticalAlignment = NEnum.NullValue(typeof(VerticalAlignment));

  /// <summary>
  /// Gets or sets the height of the rows.
  /// </summary>
  public Unit Height
  {
    get => height;
    set => height = value;
  }
  [DV]
  internal Unit height = Unit.NullValue;

  /// <summary>
  /// Gets or sets the rule which is used to determine the height of the rows.
  /// </summary>
  public RowHeightRule HeightRule
  {
    get => (RowHeightRule)heightRule.Value;
    set => heightRule.Value = (int)value;
  }
  [DV(Type = typeof(RowHeightRule))]
  internal NEnum heightRule = NEnum.NullValue(typeof(RowHeightRule));

  /// <summary>
  /// Gets or sets a comment associated with this object.
  /// </summary>
  public string Comment
  {
    get => comment.Value;
    set => comment.Value = value;
  }
  [DV]
  internal NString comment = NString.NullValue;
  #endregion

  #region Internal
  /// <summary>
  /// Converts Rows into DDL.
  /// </summary>
  internal override void Serialize(Serializer serializer)
  {
    serializer.WriteComment(comment.Value);
    serializer.WriteLine("\\rows");

    int pos = serializer.BeginAttributes();

    if (!alignment.IsNull)
      serializer.WriteSimpleAttribute("Alignment", Alignment);

    if (!height.IsNull)
      serializer.WriteSimpleAttribute("Height", Height);

    if (!heightRule.IsNull)
      serializer.WriteSimpleAttribute("HeightRule", HeightRule);

    if (!leftIndent.IsNull)
      serializer.WriteSimpleAttribute("LeftIndent", LeftIndent);

    if (!verticalAlignment.IsNull)
      serializer.WriteSimpleAttribute("VerticalAlignment", VerticalAlignment);

    serializer.EndAttributes(pos);

    serializer.BeginContent();
    int rows = Count;
    if (rows > 0)
    {
      for (int row = 0; row < rows; row++)
        this[row].Serialize(serializer);
    }
    else
      serializer.WriteComment("Invalid - no rows defined. Table will not render.");
    serializer.EndContent();
  }

  /// <summary>
  /// Allows the visitor object to visit the document object and it's child objects.
  /// </summary>
  void IVisitable.AcceptVisitor(DocumentObjectVisitor visitor, bool visitChildren)
  {
    visitor.VisitRows(this);

    foreach (Row row in this)
      ((IVisitable)row).AcceptVisitor(visitor, visitChildren);
  }

  /// <summary>
  /// Returns the meta object of this instance.
  /// </summary>
  internal override Meta Meta
  {
    get
    {
      if (meta == null)
        meta = new Meta(typeof(Rows));
      return meta;
    }
  }
  static Meta meta;
  #endregion
}
