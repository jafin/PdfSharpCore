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

using System.Diagnostics;
using MigraDocCore.DocumentObjectModel.Internals;

namespace MigraDocCore.DocumentObjectModel;

/// <summary>
/// Represents the page setup of a section.
/// </summary>
public partial class PageSetup : DocumentObject
{
  /// <summary>
  /// Initializes a new instance of the PageSetup class.
  /// </summary>
  public PageSetup()
  {
  }

  /// <summary>
  /// Initializes a new instance of the PageSetup class with the specified parent.
  /// </summary>
  internal PageSetup(DocumentObject parent) : base(parent) { }

  #region Methods
  /// <summary>
  /// Creates a deep copy of this object.
  /// </summary>
  public new PageSetup Clone()
  {
    return (PageSetup)DeepCopy();
  }

  /// <summary>
  /// Gets the page's size and height for the given PageFormat.
  /// </summary>
  public static void GetPageSize(PageFormat pageFormat, out Unit pageWidth, out Unit pageHeight)
  {
    //Sizes in mm:
    pageWidth = 0;
    pageHeight = 0;
    int A0Height = 1189;
    int A0Width = 841;
    int height = 0;
    int width = 0;
    switch (pageFormat)
    {
      case PageFormat.A0:
        height = A0Height;
        width = A0Width;
        break;
      case PageFormat.A1:
        height = A0Width;
        width = A0Height / 2;
        break;
      case PageFormat.A2:
        height = A0Height / 2;
        width = A0Width / 2;
        break;
      case PageFormat.A3:
        height = A0Width / 2;
        width = A0Height / 4;
        break;
      case PageFormat.A4:
        height = A0Height / 4;
        width = A0Width / 4;
        break;
      case PageFormat.A5:
        height = A0Width / 4;
        width = A0Height / 8;
        break;
      case PageFormat.A6:
        height = A0Height / 8;
        width = A0Width / 8;
        break;
      case PageFormat.B5:
        height = 257;
        width = 182;
        break;
      case PageFormat.Letter:
        pageWidth = Unit.FromPoint(612);
        pageHeight = Unit.FromPoint(792);
        break;
      case PageFormat.Legal:
        pageWidth = Unit.FromPoint(612);
        pageHeight = Unit.FromPoint(1008);
        break;
      case PageFormat.Ledger:
        pageWidth = Unit.FromPoint(1224);
        pageHeight = Unit.FromPoint(792);
        break;
      case PageFormat.P11x17:
        pageWidth = Unit.FromPoint(792);
        pageHeight = Unit.FromPoint(1224);
        break;
    }
    if (height > 0)
      pageHeight = Unit.FromMillimeter(height);
    if (width > 0)
      pageWidth = Unit.FromMillimeter(width);
  }
  #endregion

  #region Properties
  /// <summary>
  /// Gets or sets a value which defines whether the section starts on next, odd or even page.
  /// </summary>
  public BreakType SectionStart
  {
    get => sectionStart ?? default;
    set => sectionStart = EnumGuard.Checked(value);
  }
  [DV]
  internal BreakType? sectionStart;

  /// <summary>
  /// Gets or sets the page orientation of the section.
  /// </summary>
  public Orientation Orientation
  {
    get => orientation ?? default;
    set => orientation = EnumGuard.Checked(value);
  }
  [DV]
  internal Orientation? orientation;

  /// <summary>
  /// Gets or sets the page width.
  /// </summary>
  public Unit PageWidth
  {
    get => pageWidth;
    set => pageWidth = value;
  }
  [DV]
  internal Unit pageWidth = Unit.NullValue;

  /// <summary>
  /// Gets or sets the starting number for the first section page.
  /// </summary>
  public int StartingNumber
  {
    get => startingNumber ?? 0;
    set => startingNumber = value;
  }
  [DV]
  internal int? startingNumber;

  /// <summary>
  /// Gets or sets the page height.
  /// </summary>
  public Unit PageHeight
  {
    get => pageHeight;
    set => pageHeight = value;
  }
  [DV]
  internal Unit pageHeight = Unit.NullValue;

  /// <summary>
  /// Gets or sets the top margin of the pages in the section.
  /// </summary>
  public Unit TopMargin
  {
    get => topMargin;
    set => topMargin = value;
  }
  [DV]
  internal Unit topMargin = Unit.NullValue;

  /// <summary>
  /// Gets or sets the bottom margin of the pages in the section.
  /// </summary>
  public Unit BottomMargin
  {
    get => bottomMargin;
    set => bottomMargin = value;
  }
  [DV]
  internal Unit bottomMargin = Unit.NullValue;

  /// <summary>
  /// Gets or sets the left margin of the pages in the section.
  /// </summary>
  public Unit LeftMargin
  {
    get => leftMargin;
    set => leftMargin = value;
  }
  [DV]
  internal Unit leftMargin = Unit.NullValue;

  /// <summary>
  /// Gets or sets the right margin of the pages in the section.
  /// </summary>
  public Unit RightMargin
  {
    get => rightMargin;
    set => rightMargin = value;
  }
  [DV]
  internal Unit rightMargin = Unit.NullValue;

  /// <summary>
  /// Gets or sets a value which defines whether the odd and even pages
  /// of the section have different header and footer.
  /// </summary>
  public bool OddAndEvenPagesHeaderFooter
  {
    get => oddAndEvenPagesHeaderFooter ?? false;
    set => oddAndEvenPagesHeaderFooter = value;
  }
  [DV]
  internal bool? oddAndEvenPagesHeaderFooter;

  /// <summary>
  /// Gets or sets a value which define whether the section has a different
  /// first page header and footer.
  /// </summary>
  public bool DifferentFirstPageHeaderFooter
  {
    get => differentFirstPageHeaderFooter ?? false;
    set => differentFirstPageHeaderFooter = value;
  }
  [DV]
  internal bool? differentFirstPageHeaderFooter;

  /// <summary>
  /// Gets or sets the distance between the header and the page top
  /// of the pages in the section.
  /// </summary>
  public Unit HeaderDistance
  {
    get => headerDistance;
    set => headerDistance = value;
  }
  [DV]
  internal Unit headerDistance = Unit.NullValue;

  /// <summary>
  /// Gets or sets the distance between the footer and the page bottom
  /// of the pages in the section.
  /// </summary>
  public Unit FooterDistance
  {
    get => footerDistance;
    set => footerDistance = value;
  }
  [DV]
  internal Unit footerDistance = Unit.NullValue;

  /// <summary>
  /// Gets or sets a value which defines whether the odd and even pages
  /// of the section should change left and right margin.
  /// </summary>
  public bool MirrorMargins
  {
    get => mirrorMargins ?? false;
    set => mirrorMargins = value;
  }
  [DV]
  internal bool? mirrorMargins;

  /// <summary>
  /// Gets or sets a value which defines whether a page should break horizontally.
  /// Currently only tables are supported.
  /// </summary>
  public bool HorizontalPageBreak
  {
    get => horizontalPageBreak ?? false;
    set => horizontalPageBreak = value;
  }
  [DV]
  internal bool? horizontalPageBreak;

  /// <summary>
  /// Gets or sets the page format of the section.
  /// </summary>
  public PageFormat PageFormat
  {
    get => pageFormat ?? default;
    set => pageFormat = EnumGuard.Checked(value);
  }
  [DV]
  internal PageFormat? pageFormat;

  /// <summary>
  /// Gets or sets a comment associated with this object.
  /// </summary>
  public string Comment
  {
    get => comment ?? "";
    set => comment = value;
  }
  [DV]
  internal string comment;
  #endregion

  /// <summary>
  /// Gets the PageSetup of the previous section, or null, if the page setup belongs 
  /// to the first section.
  /// </summary>
  public PageSetup PreviousPageSetup()
  {
    Section section = Parent as Section;
    if (section != null)
    {
      section = section.PreviousSection();
      if (section != null)
        return section.PageSetup;
    }
    return null;
  }

  /// <summary>
  /// Gets a PageSetup object with default values for all properties.
  /// </summary>
  internal static PageSetup DefaultPageSetup
  {
    get
    {
      AssertDefaultPageSetupUnmodified();
      return defaultPageSetup;
    }
  }

  /// <summary>
  /// The page setup every section starts out from. Built by the type initializer rather than on
  /// first use: filling one in afterwards would let a second thread find the field already set
  /// and carry off a page setup that is still only half written.
  /// </summary>
  private static readonly PageSetup defaultPageSetup = CreateDefaultPageSetup();
#if DEBUG
  private static readonly PageSetup defaultPageSetupClone = defaultPageSetup.Clone();
#endif

  private static PageSetup CreateDefaultPageSetup()
  {
    PageSetup pageSetup = new PageSetup();
    pageSetup.PageFormat = PageFormat.A4;
    pageSetup.SectionStart = BreakType.BreakNextPage;
    pageSetup.Orientation = Orientation.Portrait;
    pageSetup.PageWidth = "21cm";
    pageSetup.PageHeight = "29.7cm";
    pageSetup.TopMargin = "2.5cm";
    pageSetup.BottomMargin = "2cm";
    pageSetup.LeftMargin = "2.5cm";
    pageSetup.RightMargin = "2.5cm";
    pageSetup.HeaderDistance = "1.25cm";
    pageSetup.FooterDistance = "1.25cm";
    pageSetup.OddAndEvenPagesHeaderFooter = false;
    pageSetup.DifferentFirstPageHeaderFooter = false;
    pageSetup.MirrorMargins = false;
    pageSetup.HorizontalPageBreak = false;
    return pageSetup;
  }

  /// <summary>
  /// Checks that nobody has written to the shared default, which Document.DefaultPageSetup hands
  /// straight out to whoever asks for it.
  /// </summary>
  [Conditional("DEBUG")]
  private static void AssertDefaultPageSetupUnmodified()
  {
#if DEBUG
    Debug.Assert(defaultPageSetup.PageFormat == defaultPageSetupClone.PageFormat, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.SectionStart == defaultPageSetupClone.SectionStart, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.Orientation == defaultPageSetupClone.Orientation, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.PageWidth == defaultPageSetupClone.PageWidth, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.PageHeight == defaultPageSetupClone.PageHeight, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.TopMargin == defaultPageSetupClone.TopMargin, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.BottomMargin == defaultPageSetupClone.BottomMargin, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.LeftMargin == defaultPageSetupClone.LeftMargin, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.RightMargin == defaultPageSetupClone.RightMargin, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.HeaderDistance == defaultPageSetupClone.HeaderDistance, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.FooterDistance == defaultPageSetupClone.FooterDistance, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.OddAndEvenPagesHeaderFooter == defaultPageSetupClone.OddAndEvenPagesHeaderFooter, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.DifferentFirstPageHeaderFooter == defaultPageSetupClone.DifferentFirstPageHeaderFooter, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.MirrorMargins == defaultPageSetupClone.MirrorMargins, "DefaultPageSetup must not be modified");
    Debug.Assert(defaultPageSetup.HorizontalPageBreak == defaultPageSetupClone.HorizontalPageBreak, "DefaultPageSetup must not be modified");
#endif
  }

  #region Internal
  /// <summary>
  /// Converts PageSetup into DDL.
  /// </summary>
  internal override void Serialize(Serializer serializer)
  {
    serializer.WriteComment((comment ?? ""));
    int pos = serializer.BeginContent("PageSetup");

    if (!pageHeight.IsNull)
      serializer.WriteSimpleAttribute("PageHeight", PageHeight);

    if (!pageWidth.IsNull)
      serializer.WriteSimpleAttribute("PageWidth", PageWidth);

    if (orientation != null)
      serializer.WriteSimpleAttribute("Orientation", Orientation);

    if (!leftMargin.IsNull)
      serializer.WriteSimpleAttribute("LeftMargin", LeftMargin);

    if (!rightMargin.IsNull)
      serializer.WriteSimpleAttribute("RightMargin", RightMargin);

    if (!topMargin.IsNull)
      serializer.WriteSimpleAttribute("TopMargin", TopMargin);

    if (!bottomMargin.IsNull)
      serializer.WriteSimpleAttribute("BottomMargin", BottomMargin);

    if (!footerDistance.IsNull)
      serializer.WriteSimpleAttribute("FooterDistance", FooterDistance);

    if (!headerDistance.IsNull)
      serializer.WriteSimpleAttribute("HeaderDistance", HeaderDistance);

    if (oddAndEvenPagesHeaderFooter != null)
      serializer.WriteSimpleAttribute("OddAndEvenPagesHeaderFooter", OddAndEvenPagesHeaderFooter);

    if (differentFirstPageHeaderFooter != null)
      serializer.WriteSimpleAttribute("DifferentFirstPageHeaderFooter", DifferentFirstPageHeaderFooter);

    if (sectionStart != null)
      serializer.WriteSimpleAttribute("SectionStart", SectionStart);

    if (pageFormat != null)
      serializer.WriteSimpleAttribute("PageFormat", PageFormat);

    if (mirrorMargins != null)
      serializer.WriteSimpleAttribute("MirrorMargins", MirrorMargins);

    if (horizontalPageBreak != null)
      serializer.WriteSimpleAttribute("HorizontalPageBreak", HorizontalPageBreak);

    if (startingNumber != null)
      serializer.WriteSimpleAttribute("StartingNumber", StartingNumber);

    serializer.EndContent(pos);
  }

  #endregion
}
