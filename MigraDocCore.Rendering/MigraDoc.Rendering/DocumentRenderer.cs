#region MigraDoc - Creating Documents on the Fly
//
// Authors:
//   Klaus Potzesny (mailto:Klaus.Potzesny@PdfSharpCore.com)
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
using System.Collections;
using MigraDocCore.DocumentObjectModel;
using PdfSharpCore.Pdf;
using PdfSharpCore.Drawing;
using MigraDocCore.DocumentObjectModel.Visitors;
using MigraDocCore.DocumentObjectModel.Shapes;
using MigraDocCore.DocumentObjectModel.Tables;
using MigraDocCore.Rendering.MigraDoc.Rendering.Resources;

namespace MigraDocCore.Rendering;

/// <summary>
/// Provides methods to render the document or single parts of it to a XGraphics object.
/// </summary>
/// <remarks>
/// One prepared instance of this class can serve to render several output formats.
/// </remarks>
public class DocumentRenderer
{
    /// <summary>
    /// Initializes a new instance of the DocumentRenderer class.
    /// </summary>
    /// <param name="document">The migradoc document to render.</param>
    public DocumentRenderer(Document document)
    {
        this.document = document;
    }

    /// <summary>
    /// Prepares this instance for rendering.
    /// </summary>
    public void PrepareDocument()
    {
        PdfFlattenVisitor visitor = new PdfFlattenVisitor();
        visitor.Visit(document);
        previousListNumbers = new Hashtable(3);
        previousListNumbers[ListType.NumberList1] = 0;
        previousListNumbers[ListType.NumberList2] = 0;
        previousListNumbers[ListType.NumberList3] = 0;
        formattedDocument = new FormattedDocument(document, this);
        //REM: Size should not be necessary in this case.
        XGraphics gfx = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
        //      this.previousListNumber = int.MinValue;
        //gfx.MUH = this.unicode;
        //gfx.MFEH = this.fontEmbedding;

        previousListInfo = null;
        formattedDocument.Format(gfx);
    }

    /// <summary>
    /// Occurs while the document is being prepared (can be used to show a progress bar).
    /// </summary>
    public event PrepareDocumentProgressEventHandler PrepareDocumentProgress;

    /// <summary>
    /// Allows applications to display a progress indicator while PrepareDocument() is being executed.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="maximum"></param>
    internal virtual void OnPrepareDocumentProgress(int value, int maximum)
    {
        if (PrepareDocumentProgress != null)
        {
            // Invokes the delegates. 
            PrepareDocumentProgressEventArgs e = new PrepareDocumentProgressEventArgs(value, maximum);
            PrepareDocumentProgress(this, e);
        }
    }

    /// <summary>
    /// Gets a value indicating whether this instance supports PrepareDocumentProgress.
    /// </summary>
    public bool HasPrepareDocumentProgress => PrepareDocumentProgress != null;

    /// <summary>
    /// Occurs when an image is replaced by a placeholder because it could not be measured or drawn.
    /// </summary>
    /// <remarks>
    /// Rendering carries on regardless, so that one unreadable image does not cost a whole
    /// document. This is where the reason it failed can be seen: without a handler the exception
    /// that stopped the image is dropped, and the only thing left of it is a grey box on the page.
    /// </remarks>
    public event EventHandler<ImageFailedEventArgs> ImageFailed;

#nullable enable
    /// <summary>
    /// Reports an image that was replaced by a placeholder.
    /// </summary>
    internal virtual void OnImageFailed(Image image, ImageFailure failure, Exception? exception)
    {
        ImageFailed?.Invoke(this, new ImageFailedEventArgs(image, failure, exception));
    }
#nullable restore

    /// <summary>
    /// Gets the formatted document of this instance.
    /// </summary>
    public FormattedDocument FormattedDocument => formattedDocument;

    internal FormattedDocument formattedDocument;

    /// <summary>
    /// Renders a MigraDoc document to the specified graphics object.
    /// </summary>
    public void RenderPage(XGraphics gfx, int page)
    {
        RenderPage(gfx, page, PageRenderOptions.All);
    }

    /// <summary>
    /// Renders a MigraDoc document to the specified graphics object.
    /// </summary>
    public void RenderPage(XGraphics gfx, int page, PageRenderOptions options)
    {
        if (formattedDocument.IsEmptyPage(page))
            return;

        FieldInfos fieldInfos = formattedDocument.GetFieldInfos(page);

        if (printDate != DateTime.MinValue)
            fieldInfos.date = printDate;
        else
            fieldInfos.date = DateTime.Now;

        if ((options & PageRenderOptions.RenderHeader) == PageRenderOptions.RenderHeader)
            RenderHeader(gfx, page);
        if ((options & PageRenderOptions.RenderFooter) == PageRenderOptions.RenderFooter)
            RenderFooter(gfx, page);

        if ((options & PageRenderOptions.RenderContent) == PageRenderOptions.RenderContent)
        {
            RenderInfo[] renderInfos = formattedDocument.GetRenderInfos(page);
            //foreach (RenderInfo renderInfo in renderInfos)
            int count = renderInfos.Length;
            for (int idx = 0; idx < count; idx++)
            {
                RenderInfo renderInfo = renderInfos[idx];
                Renderer renderer = Renderer.Create(gfx, this, renderInfo, fieldInfos);
                renderer.Render();
            }
        }
    }

    /// <summary>
    /// Gets the document objects that get rendered on the specified page.
    /// </summary>
    public DocumentObject[] GetDocumentObjectsFromPage(int page)
    {
        RenderInfo[] renderInfos = formattedDocument.GetRenderInfos(page);
        int count = renderInfos != null ? renderInfos.Length : 0;
        DocumentObject[] documentObjects = new DocumentObject[count];
        for (int idx = 0; idx < count; idx++)
            documentObjects[idx] = renderInfos[idx].DocumentObject;
        return documentObjects;
    }

    /// <summary>
    /// Gets the render information for document objects that get rendered on the specified page.
    /// </summary>
    public RenderInfo[] GetRenderInfoFromPage(int page)
    {
        return formattedDocument.GetRenderInfos(page);
    }

    /// <summary>
    /// Renders a single object to the specified graphics object at the given point.
    /// </summary>
    /// <param name="graphics">The graphics object to render on.</param>
    /// <param name="xPosition">The left position of the rendered object.</param>
    /// <param name="yPosition">The top position of the rendered object.</param>
    /// <param name="width">The width.</param>
    /// <param name="documentObject">The document object to render. Can be paragraph, table, or shape.</param>
    /// <remarks>This function is still in an experimental state.</remarks>
    public void RenderObject(XGraphics graphics, XUnit xPosition, XUnit yPosition, XUnit width, DocumentObject documentObject)
    {
        if (graphics == null)
            throw new ArgumentNullException("graphics");

        if (documentObject == null)
            throw new ArgumentNullException("documentObject");

        if (!(documentObject is Shape) && !(documentObject is Table) &&
            !(documentObject is Paragraph))
            throw new ArgumentException(AppResources.ObjectNotRenderable, "documentObject");

        Renderer renderer = Renderer.Create(graphics, this, documentObject, null);
        renderer.Format(new Rectangle(xPosition, yPosition, width, double.MaxValue), null);

        RenderInfo renderInfo = renderer.RenderInfo;
        renderInfo.LayoutInfo.ContentArea.X = xPosition;
        renderInfo.LayoutInfo.ContentArea.Y = yPosition;

        renderer = Renderer.Create(graphics, this, renderer.RenderInfo, null);
        renderer.Render();
    }

    /// <summary>
    /// Gets or sets the working directory for rendering.
    /// </summary>
    public string WorkingDirectory
    {
        get => workingDirectory;
        set => workingDirectory = value;
    }
    string workingDirectory;

    private void RenderHeader(XGraphics graphics, int page)
    {
        FormattedHeaderFooter formattedHeader = formattedDocument.GetFormattedHeader(page);
        if (formattedHeader == null)
            return;

        Rectangle headerArea = formattedDocument.GetHeaderArea(page);
        RenderInfo[] renderInfos = formattedHeader.GetRenderInfos();
        FieldInfos fieldInfos = formattedDocument.GetFieldInfos(page);
        foreach (RenderInfo renderInfo in renderInfos)
        {
            Renderer renderer = Renderer.Create(graphics, this, renderInfo, fieldInfos);
            renderer.Render();
        }
    }

    private void RenderFooter(XGraphics graphics, int page)
    {
        FormattedHeaderFooter formattedFooter = formattedDocument.GetFormattedFooter(page);
        if (formattedFooter == null)
            return;

        Rectangle footerArea = formattedDocument.GetFooterArea(page);
        RenderInfo[] renderInfos = formattedFooter.GetRenderInfos();
        if (renderInfos.Length == 0)
            return;

        // A footer sits at the bottom of its area rather than the top, so the content has to
        // come down by however much of the area it leaves empty. That is one distance for the
        // whole of it: moving each element to the same place instead lays them all on top of
        // one another. See https://github.com/ststeiger/PdfSharpCore/issues/414.
        LayoutInfo firstLayoutInfo = renderInfos[0].LayoutInfo;
        XUnit formattedTop = firstLayoutInfo.ContentArea.Y - firstLayoutInfo.MarginTop;
        XUnit renderedTop = footerArea.Y + footerArea.Height - RenderInfo.GetTotalHeight(renderInfos);
        XUnit distance = renderedTop - formattedTop;

        FieldInfos fieldInfos = formattedDocument.GetFieldInfos(page);
        foreach (RenderInfo renderInfo in renderInfos)
        {
            Renderer renderer = Renderer.Create(graphics, this, renderInfo, fieldInfos);
            XUnit savedY = renderer.RenderInfo.LayoutInfo.ContentArea.Y;
            renderer.RenderInfo.LayoutInfo.ContentArea.Y = savedY + distance;
            try
            {
                renderer.Render();
            }
            finally
            {
                // The pages of a section share the one formatted footer, so a move left in
                // place would be there still the next time the footer was drawn.
                renderer.RenderInfo.LayoutInfo.ContentArea.Y = savedY;
            }
        }
    }

    internal void AddOutline(int level, string title, PdfPage destinationPage)
    {
        AddOutline(level, title, destinationPage, double.NaN);
    }

    /// <summary>
    /// Adds an outline entry pointing at a place on a page.
    /// </summary>
    /// <param name="destinationTop">
    /// How far up the destination page the heading sits, in the coordinates a PDF page is
    /// measured in. NaN points the entry at the page without saying where on it, which leaves
    /// the reader wherever the page is already scrolled to.
    /// </param>
    internal void AddOutline(int level, string title, PdfPage destinationPage, double destinationTop)
    {
        if (level < 1 || destinationPage == null)
            return;

        PdfDocument document = destinationPage.Owner;

        if (document == null)
            return;

        PdfOutlineCollection outlines = document.Outlines;
        while (--level > 0)
        {
            int count = outlines.Count;
            if (count == 0)
            {
                // You cannot add empty bookmarks to PDF. So we use blank here.
                PdfOutline outline = outlines.Add(" ", destinationPage, true);
                outline.Top = destinationTop;
                outlines = outline.Outlines;
            }
            else
                outlines = outlines[count - 1].Outlines;
        }
        PdfOutline added = outlines.Add(title, destinationPage, true);
        added.Top = destinationTop;
    }

    internal int NextListNumber(ListInfo listInfo)
    {
        ListType listType = listInfo.ListType;
        bool isNumberList = listType == ListType.NumberList1 ||
                            listType == ListType.NumberList2 ||
                            listType == ListType.NumberList3;

        int listNumber = int.MinValue;
        if (listInfo == previousListInfo)
        {
            if (isNumberList)
                return (int)previousListNumbers[listType];
            return listNumber;
        }

        //bool listTypeChanged = this.previousListInfo == null || this.previousListInfo.ListType != listType;

        if (isNumberList)
        {
            listNumber = 1;
            if (/*!listTypeChanged &&*/ (listInfo.IsNull("ContinuePreviousList") || listInfo.ContinuePreviousList))
                listNumber = (int)previousListNumbers[listType] + 1;

            previousListNumbers[listType] = listNumber;
        }
        //      else
        //        listNumber = int.MinValue;

        previousListInfo = listInfo;
        return listNumber;
    }
    ListInfo previousListInfo;
    Hashtable previousListNumbers;
    private Document document;
    internal DateTime printDate = DateTime.MinValue;

    /// <summary>
    /// Arguments for the PrepareDocumentProgressEvent which is called while a document is being prepared (you can use this to display a progress bar).
    /// </summary>
    public class PrepareDocumentProgressEventArgs : EventArgs
    {
        /// <summary>
        /// Indicates the current step reached in document preparation.
        /// </summary>
        public int Value;
        /// <summary>
        /// Indicates the final step in document preparation. The quitient of Value and Maximum can be used to calculate a percentage (e. g. for use in a progress bar).
        /// </summary>
        public int Maximum;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrepareDocumentProgressEventArgs"/> class.
        /// </summary>
        /// <param name="value">The current step in document preparation.</param>
        /// <param name="maximum">The latest step in document preparation.</param>
        public PrepareDocumentProgressEventArgs(int value, int maximum)
        {
            Value = value;
            Maximum = maximum;
        }
    }

    /// <summary>
    /// The event handler that is being called for the PrepareDocumentProgressEvent event.
    /// </summary>
    public delegate void PrepareDocumentProgressEventHandler(object sender, PrepareDocumentProgressEventArgs e);

    internal int ProgressMaximum;
    internal int ProgressCompleted;

    /// <summary>
    /// Gets or sets the private fonts of the document.
    /// </summary>
    public XPrivateFontCollection PrivateFonts
    {
        get => privateFonts;
        set => privateFonts = value;
    }
    //[DV]
    internal XPrivateFontCollection privateFonts;
}
