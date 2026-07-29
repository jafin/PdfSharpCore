#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharp.com
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.Internal;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.AcroForms;
using PdfSharpCore.Pdf.Security;

// ReSharper disable ConvertPropertyToExpressionBody

namespace PdfSharpCore.Pdf;

/// <summary>
/// Represents a PDF document.
/// </summary>
[DebuggerDisplay("(Name={Name})")] // A name makes debugging easier
public sealed class PdfDocument : PdfObject, IDisposable
{
    internal DocumentState _state;
    internal PdfDocumentOpenMode _openMode;

    /// <summary>
    /// Creates a new PDF document in memory.
    /// To open an existing PDF file, use the PdfReader class.
    /// </summary>
    public PdfDocument()
    {
        //PdfDocument.Gob.AttatchDocument(Handle);

        _creation = DateTime.Now;
        _state = DocumentState.Created;
        _version = 14;
        Initialize();
        Info.CreationDate = _creation;
    }

    /// <summary>
    /// Creates a new PDF document with the specified file name. The file is immediately created and keeps
    /// locked until the document is closed, at that time the document is saved automatically.
    /// Do not call Save() for documents created with this constructor, just call Close().
    /// To open an existing PDF file and import it, use the PdfReader class.
    /// </summary>
    public PdfDocument(string filename)
    {
        //PdfDocument.Gob.AttatchDocument(Handle);

        _creation = DateTime.Now;
        _state = DocumentState.Created;
        _version = 14;
        Initialize();
        Info.CreationDate = _creation;

        // TODO 4STLA: encapsulate the whole c'tor with #if !NETFX_CORE?
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates a new PDF document using the specified stream.
    /// The stream won't be used until the document is closed, at that time the document is saved automatically.
    /// Do not call Save() for documents created with this constructor, just call Close().
    /// To open an existing PDF file, use the PdfReader class.
    /// </summary>
    public PdfDocument(Stream outputStream)
    {
        //PdfDocument.Gob.AttatchDocument(Handle);

        _creation = DateTime.Now;
        _state = DocumentState.Created;
        Initialize();
        Info.CreationDate = _creation;

        _outStream = outputStream;
    }

    internal PdfDocument(Lexer lexer)
    {
        //PdfDocument.Gob.AttatchDocument(Handle);

        _creation = DateTime.Now;
        _state = DocumentState.Imported;

        //_info = new PdfInfo(this);
        //_pages = new PdfPages(this);
        //_fontTable = new PdfFontTable();
        //_catalog = new PdfCatalog(this);
        ////_font = new PdfFont();
        //_objects = new PdfObjectTable(this);
        //_trailer = new PdfTrailer(this);
        _irefTable = new PdfCrossReferenceTable(this);
        _lexer = lexer;
    }

    void Initialize()
    {
        //_info = new PdfInfo(this);
        _fontTable = new PdfFontTable(this);
        _imageTable = new PdfImageTable(this);
        _trailer = new PdfTrailer(this);
        _irefTable = new PdfCrossReferenceTable(this);
        _trailer.CreateNewDocumentIDs();
    }

    //~PdfDocument()
    //{
    //  Dispose(false);
    //}

    /// <summary>
    /// Disposes all references to this document stored in other documents. This function should be called
    /// for documents you finished importing pages from. Calling Dispose is technically not necessary but
    /// useful for earlier reclaiming memory of documents you do not need anymore.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        //GC.SuppressFinalize(this);
    }

    void Dispose(bool disposing)
    {
        if (_state != DocumentState.Disposed)
        {
            if (disposing)
            {
                // Dispose managed resources.
            }
            //PdfDocument.Gob.DetatchDocument(Handle);
        }
        _state = DocumentState.Disposed;
    }

    /// <summary>
    /// Gets or sets a user defined object that contains arbitrary information associated with this document.
    /// The tag is not used by PdfSharpCore.
    /// </summary>
    public object Tag
    {
        get { return _tag; }
        set { _tag = value; }
    }
    object _tag;

    /// <summary>
    /// Gets or sets a value used to distinguish PdfDocument objects.
    /// The name is not used by PdfSharpCore.
    /// </summary>
    string Name
    {
        get { return _name; }
        set { _name = value; }
    }
    string _name = NewName();

    /// <summary>
    /// Get a new default name for a new document.
    /// </summary>
    static string NewName()
    {
        return "Document " + _nameCount++;
    }
    static int _nameCount;

    internal bool CanModify
    {
        //get {return _state == DocumentState.Created || _state == DocumentState.Modifyable;}
        get { return true; }
    }

    /// <summary>
    /// Closes this instance.
    /// </summary>
    public void Close()
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);

        if (_outStream != null)
        {
            // Get security handler if document gets encrypted
            PdfStandardSecurityHandler securityHandler = null;
            if (SecuritySettings.DocumentSecurityLevel != PdfDocumentSecurityLevel.None)
                securityHandler = SecuritySettings.SecurityHandler;

            PdfWriter writer = new PdfWriter(_outStream, securityHandler);
            try
            {
                DoSave(writer);
            }
            finally
            {
                writer.Close();
            }
        }
    }

    /// <summary>
    /// Saves the document to the specified path. If a file already exists, it will be overwritten.
    /// </summary>
    public void Save(string path)
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);


        using (Stream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Save(stream);
        }
    }

    /// <summary>
    /// Saves the document to the specified stream.
    /// </summary>
    public void Save(Stream stream, bool closeStream)
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);

        // TODO: more diagnostic checks
        string message = "";
        if (!CanSave(ref message))
            throw new PdfSharpException(message);

        // Saving back into the stream the document was read from is a common way to modify a
        // document in place. Reading has left the position near the end of the stream, so writing
        // would start there and keep the entire original file as a prefix. The result still opens,
        // because readers locate the last startxref, but the file has roughly doubled in size for
        // no reason. All objects were read into memory when the document was opened, so the stream
        // is no longer needed and can be rewound and truncated.
        // The new file is written into a buffer first, and the stream is not touched until that
        // has succeeded. Truncating it up front would leave a save that fails part way through
        // with neither the document it started from nor the one it was asked for.
        MemoryStream buffer = null;
        if (_lexer != null && ReferenceEquals(_lexer.PdfStream, stream) && stream.CanSeek && stream.CanWrite)
            buffer = new MemoryStream();

        // Get security handler if document gets encrypted.
        PdfStandardSecurityHandler securityHandler = null;
        if (SecuritySettings.DocumentSecurityLevel != PdfDocumentSecurityLevel.None)
            securityHandler = SecuritySettings.SecurityHandler;

        PdfWriter writer = null;
        try
        {
            writer = new PdfWriter(buffer ?? stream, securityHandler);
            DoSave(writer);

            if (buffer != null)
            {
                stream.Position = 0;
                stream.SetLength(0);
                buffer.WriteTo(stream);
                stream.Flush();
            }
        }
        finally
        {
            if (buffer != null)
                buffer.Dispose();
            if (stream != null)
            {
                if (closeStream)
                    stream.Dispose();
                else
                    stream.Position = 0; // Reset the stream position if the stream is kept open.
            }
            if (writer != null)
                writer.Close(closeStream);
        }
    }

    /// <summary>
    /// Saves the document to the specified stream.
    /// The stream is not closed by this function.
    /// (Older versions of PDFsharp closes the stream. That was not very useful.)
    /// </summary>
    public void Save(Stream stream)
    {
        Save(stream, false);
    }

    /// <summary>
    /// Implements saving a PDF file.
    /// </summary>
    void DoSave(PdfWriter writer)
    {
        if (_pages == null || _pages.Count == 0)
        {
            if (_outStream != null)
            {
                // Give feedback if the wrong constructor was used.
                throw new InvalidOperationException("Cannot save a PDF document with no pages. Do not use \"public PdfDocument(string filename)\" or \"public PdfDocument(Stream outputStream)\" if you want to open an existing PDF document from a file or stream; use PdfReader.Open() for that purpose.");
            }
            throw new InvalidOperationException("Cannot save a PDF document with no pages.");
        }

        try
        {
            // HACK: Remove XRefTrailer
            if (_trailer is PdfCrossReferenceStream)
            {
                // HACK^2: Preserve the SecurityHandler.
                PdfStandardSecurityHandler securityHandler = _securitySettings.SecurityHandler;
                _trailer = new PdfTrailer((PdfCrossReferenceStream)_trailer);
                _trailer._securityHandler = securityHandler;
            }

            bool encrypt = _securitySettings.DocumentSecurityLevel != PdfDocumentSecurityLevel.None;
            if (encrypt)
            {
                PdfStandardSecurityHandler securityHandler = _securitySettings.SecurityHandler;
                if (securityHandler.Reference == null)
                    _irefTable.Add(securityHandler);
                else
                    Debug.Assert(_irefTable.Contains(securityHandler.ObjectID));
                _trailer.Elements[PdfTrailer.Keys.Encrypt] = _securitySettings.SecurityHandler.Reference;
            }
            else
                _trailer.Elements.Remove(PdfTrailer.Keys.Encrypt);

            PrepareForSave();

            if (encrypt)
                _securitySettings.SecurityHandler.PrepareEncryption();

            writer.WriteFileHeader(this);
            PdfReference[] irefs = _irefTable.AllReferences;
            int count = irefs.Length;
            for (int idx = 0; idx < count; idx++)
            {
                PdfReference iref = irefs[idx];
                iref.Position = writer.Position;
                iref.Value.WriteObject(writer);
            }
            var startxref = writer.Position;
            _irefTable.WriteObject(writer);
            writer.WriteRaw("trailer\n");
            _trailer.Elements.SetInteger("/Size", count + 1);
            _trailer.WriteObject(writer);
            writer.WriteEof(this, startxref);

            //if (encrypt)
            //{
            //  state &= ~DocumentState.SavingEncrypted;
            //  //_securitySettings.SecurityHandler.EncryptDocument();
            //}
        }
        finally
        {
            if (writer != null)
            {
                writer.Stream.Flush();
                // DO NOT CLOSE WRITER HERE
                //writer.Close();
            }
        }
    }

    /// <summary>
    /// Dispatches PrepareForSave to the objects that need it.
    /// </summary>
    internal override void PrepareForSave()
    {
        PdfDocumentInformation info = Info;

        // Add patch level to producer if it is not '0'.
        string pdfSharpProducer = VersionInfo.Producer;
        if (!ProductVersionInfo.VersionPatch.Equals("0"))
            pdfSharpProducer = ProductVersionInfo.Producer2;

        // Set Creator if value is undefined.
        if (info.Elements[PdfDocumentInformation.Keys.Creator] == null)
            info.Creator = pdfSharpProducer;

        // Keep original producer if file was imported.
        string producer = info.Producer;
        if (producer.Length == 0)
            producer = pdfSharpProducer;
        else
        {
            // Prevent endless concatenation if file is edited with PDFsharp more than once.
            if (!producer.StartsWith(VersionInfo.Title))
                producer = pdfSharpProducer + " (Original: " + producer + ")";
        }
        info.Elements.SetString(PdfDocumentInformation.Keys.Producer, producer);

        // Stamp a document opened for modification with the time it was written. This used to be
        // done when the document was opened, which meant that reading one to look at its dates
        // changed the very date being looked at, whether or not anything was ever written. Set the
        // element rather than the property, so that saving twice stamps twice and neither save is
        // taken for a date the caller chose.
        // IsImported tells a document PdfReader opened from a newly created one, which shares the
        // default open mode of Modify and is dated by its creation date alone, as it always was.
        if (IsImported && _openMode == PdfDocumentOpenMode.Modify && !info.ModificationDateIsTheCallersOwn)
            info.Elements.SetDateTime(PdfDocumentInformation.Keys.ModDate, DateTime.Now);

        // Prepare used fonts.
        if (_fontTable != null)
            _fontTable.PrepareForSave();

        // Let catalog do the rest.
        Catalog.PrepareForSave();

        // Remove all unreachable objects (e.g. from deleted pages)
        int removed = _irefTable.Compact();
        if (removed != 0)
            Debug.WriteLine("PrepareForSave: Number of deleted unreachable objects: " + removed);
        _irefTable.Renumber();
    }

    /// <summary>
    /// Determines whether the document can be saved.
    /// </summary>
    public bool CanSave(ref string message)
    {
        if (!SecuritySettings.CanSave(ref message))
            return false;

        return true;
    }

    internal bool HasVersion(string version)
    {
        return String.Compare(Catalog.Version, version) >= 0;
    }

    /// <summary>
    /// Gets the document options used for saving the document.
    /// </summary>
    public PdfDocumentOptions Options
    {
        get
        {
            if (_options == null)
                _options = new PdfDocumentOptions(this);
            return _options;
        }
    }
    PdfDocumentOptions _options;

    /// <summary>
    /// Gets PDF specific document settings.
    /// </summary>
    public PdfDocumentSettings Settings
    {
        get
        {
            if (_settings == null)
                _settings = new PdfDocumentSettings(this);
            return _settings;
        }
    }
    PdfDocumentSettings _settings;

    /// <summary>
    /// NYI Indicates whether large objects are written immediately to the output stream to relieve
    /// memory consumption.
    /// </summary>
    internal bool EarlyWrite
    {
        get { return false; }
    }

    /// <summary>
    /// Gets or sets the PDF version number. Return value 14 e.g. means PDF 1.4 / Acrobat 5 etc.
    /// Return value 20 means PDF 2.0.
    /// </summary>
    public int Version
    {
        get { return _version; }
        set
        {
            if (!CanModify)
                throw new InvalidOperationException(PSSR.CannotModify);
            if ((value < 12 || value > 17) && value != 20) // TODO not really implemented
                throw new ArgumentException(PSSR.InvalidVersionNumber, nameof(value));
            _version = value;
        }
    }
    internal int _version;

    /// <summary>
    /// Gets the number of pages in the document.
    /// </summary>
    public int PageCount
    {
        get
        {
            if (CanModify)
                return Pages.Count;
            // PdfOpenMode is InformationOnly
            PdfDictionary pageTreeRoot = (PdfDictionary)Catalog.Elements.GetObject(PdfCatalog.Keys.Pages);
            return pageTreeRoot.Elements.GetInteger(PdfPages.Keys.Count);
        }
    }

    /// <summary>
    /// Gets the file size of the document.
    /// </summary>
    public long FileSize
    {
        get { return _fileSize; }
    }
    internal long _fileSize; // TODO: make private

    /// <summary>
    /// Gets the full qualified file name if the document was read form a file, or an empty string otherwise.
    /// </summary>
    public string FullPath
    {
        get { return _fullPath; }
    }
    internal string _fullPath = String.Empty; // TODO: make private

    /// <summary>
    /// Gets a Guid that uniquely identifies this instance of PdfDocument.
    /// </summary>
    public Guid Guid
    {
        get { return _guid; }
    }
    Guid _guid = Guid.NewGuid();

    internal DocumentHandle Handle
    {
        get
        {
            if (_handle == null)
                _handle = new DocumentHandle(this);
            return _handle;
        }
    }
    DocumentHandle _handle;

    /// <summary>
    /// Returns a value indicating whether the document was newly created or opened from an existing document.
    /// Returns true if the document was opened with the PdfReader.Open function, false otherwise.
    /// </summary>
    public bool IsImported
    {
        get { return (_state & DocumentState.Imported) != 0; }
    }

    /// <summary>
    /// Returns a value indicating whether the document is read only or can be modified.
    /// </summary>
    public bool IsReadOnly
    {
        get { return (_openMode != PdfDocumentOpenMode.Modify); }
    }

    internal Exception DocumentNotImported()
    {
        return new InvalidOperationException("Document not imported.");
    }

    /// <summary>
    /// Gets information about the document.
    /// </summary>
    public PdfDocumentInformation Info
    {
        get
        {
            if (_info == null)
                _info = _trailer.Info;
            return _info;
        }
    }
    PdfDocumentInformation _info;  // never changes if once created

    /// <summary>
    /// This function is intended to be undocumented.
    /// </summary>
    public PdfCustomValues CustomValues
    {
        get
        {
            if (_customValues == null)
                _customValues = PdfCustomValues.Get(Catalog.Elements);
            return _customValues;
        }
        set
        {
            if (value != null)
                throw new ArgumentException("Only null is allowed to clear all custom values.");
            PdfCustomValues.Remove(Catalog.Elements);
            _customValues = null;
        }
    }
    PdfCustomValues _customValues;

    /// <summary>
    /// Get the pages dictionary.
    /// </summary>
    public PdfPages Pages
    {
        get
        {
            if (_pages == null)
                _pages = Catalog.Pages;
            return _pages;
        }
    }
    PdfPages _pages;  // never changes if once created

    /// <summary>
    /// Gets or sets a value specifying the page layout to be used when the document is opened.
    /// </summary>
    public PdfPageLayout PageLayout
    {
        get { return Catalog.PageLayout; }
        set
        {
            if (!CanModify)
                throw new InvalidOperationException(PSSR.CannotModify);
            Catalog.PageLayout = value;
        }
    }

    /// <summary>
    /// Gets or sets a value specifying how the document should be displayed when opened.
    /// </summary>
    public PdfPageMode PageMode
    {
        get { return Catalog.PageMode; }
        set
        {
            if (!CanModify)
                throw new InvalidOperationException(PSSR.CannotModify);
            Catalog.PageMode = value;
        }
    }

    /// <summary>
    /// Gets the viewer preferences of this document.
    /// </summary>
    public PdfViewerPreferences ViewerPreferences
    {
        get { return Catalog.ViewerPreferences; }
    }

    /// <summary>
    /// Gets the root of the outline (or bookmark) tree.
    /// </summary>
    public PdfOutlineCollection Outlines
    {
        get { return Catalog.Outlines; }
    }

    /// <summary>
    /// Gets the page labels of the document: what a reader shows for a page instead of its
    /// position, so that front matter can be numbered i, ii, iii while the body starts again
    /// at 1. A document with no labels reads as having none rather than being given any.
    /// </summary>
    public PdfPageLabels PageLabels
    {
        get { return _pageLabels ?? (_pageLabels = new PdfPageLabels(this)); }
    }
    PdfPageLabels _pageLabels;

    /// <summary>
    /// Get the AcroForm dictionary.
    /// </summary>
    public PdfAcroForm AcroForm
    {
        get { return Catalog.AcroForm; }
    }

    /// <summary>
    /// Gets or sets the default language of the document.
    /// </summary>
    public string Language
    {
        get { return Catalog.Language; }
        set { Catalog.Language = value; }
        //get { return Catalog.Elements.GetString(PdfCatalog.Keys.Lang); }
        //set { Catalog.Elements.SetString(PdfCatalog.Keys.Lang, value); }
    }

    /// <summary>
    /// Gets the security settings of this document.
    /// </summary>
    public PdfSecuritySettings SecuritySettings
    {
        get { return _securitySettings ?? (_securitySettings = new PdfSecuritySettings(this)); }
    }
    internal PdfSecuritySettings _securitySettings;

    /// <summary>
    /// Gets the document font table that holds all fonts used in the current document.
    /// </summary>
    internal PdfFontTable FontTable
    {
        get { return _fontTable ?? (_fontTable = new PdfFontTable(this)); }
    }
    PdfFontTable _fontTable;

    /// <summary>
    /// Gets the document image table that holds all images used in the current document.
    /// </summary>
    internal PdfImageTable ImageTable
    {
        get
        {
            if (_imageTable == null)
                _imageTable = new PdfImageTable(this);
            return _imageTable;
        }
    }
    PdfImageTable _imageTable;

    /// <summary>
    /// Gets the document form table that holds all form external objects used in the current document.
    /// </summary>
    internal PdfFormXObjectTable FormTable  // TODO: Rename to ExternalDocumentTable.
    {
        get { return _formTable ?? (_formTable = new PdfFormXObjectTable(this)); }
    }
    PdfFormXObjectTable _formTable;

    /// <summary>
    /// Gets the document ExtGState table that holds all form state objects used in the current document.
    /// </summary>
    internal PdfExtGStateTable ExtGStateTable
    {
        get { return _extGStateTable ?? (_extGStateTable = new PdfExtGStateTable(this)); }
    }
    PdfExtGStateTable _extGStateTable;

    /// <summary>
    /// Gets the PdfCatalog of the current document.
    /// </summary>
    internal PdfCatalog Catalog
    {
        get { return _catalog ?? (_catalog = _trailer.Root); }
    }
    PdfCatalog _catalog;  // never changes if once created

    /// <summary>
    /// Gets the PdfInternals object of this document, that grants access to some internal structures
    /// which are not part of the public interface of PdfDocument.
    /// </summary>
    public new PdfInternals Internals
    {
        get { return _internals ?? (_internals = new PdfInternals(this)); }
    }
    PdfInternals _internals;

    /// <summary>
    /// Creates a new page, <b>appends it to this document</b>, and returns it.
    /// Depending of the IsMetric property of the current region the page size is set to
    /// A4 or Letter respectively. If this size is not appropriate it should be changed before
    /// any drawing operations are performed on the page.
    /// <para>
    /// The page returned is already part of this document. Do not pass it to
    /// <see cref="AddPage(PdfPage, AnnotationCopyingType)"/> or
    /// <see cref="InsertPage(int, PdfPage, AnnotationCopyingType)"/> afterwards - that places
    /// the same page twice and throws. Draw on the page returned here instead.
    /// </para>
    /// <para>
    /// To build a page before deciding where it goes, use <c>new PdfPage(document)</c>, which
    /// creates a drawable page without adding it to the page tree, and place it later with
    /// <see cref="PlacePage"/>.
    /// </para>
    /// </summary>
    [MustUseReturnValue]
    public PdfPage AddPage()
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);
        return Catalog.Pages.Add();
    }

    /// <summary>
    /// Adds the specified page to this document. If the page is from an external document,
    /// it is imported to this document. In this case the returned page is not the same
    /// object as the specified one.
    /// <para>
    /// <b>Always use the value returned</b> rather than the page passed in: whether the two are
    /// the same object depends on which document owned the page, which is not visible at the
    /// call site. Prefer <see cref="ImportPage"/> or <see cref="PlacePage"/>, which each do one
    /// of those two things and say which in their name.
    /// </para>
    /// </summary>
    [MustUseReturnValue]
    public PdfPage AddPage(PdfPage page, AnnotationCopyingType annotationCopying = AnnotationCopyingType.ShallowCopy)
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);
        return Catalog.Pages.Add(page, annotationCopying);
    }

    /// <summary>
    /// Creates a new page, <b>inserts it in this document</b> at the specified position, and
    /// returns it. The page returned is already part of this document - draw on it rather than
    /// passing it to another Add or Insert call.
    /// </summary>
    [MustUseReturnValue]
    public PdfPage InsertPage(int index)
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);
        return Catalog.Pages.Insert(index);
    }

    /// <summary>
    /// Inserts the specified page in this document. If the page is from an external document,
    /// it is imported to this document. In this case the returned page is not the same
    /// object as the specified one.
    /// <para>
    /// <b>Always use the value returned</b> rather than the page passed in: whether the two are
    /// the same object depends on which document owned the page, which is not visible at the
    /// call site. Prefer <see cref="ImportPage"/> or <see cref="PlacePage"/>, which each do one
    /// of those two things and say which in their name.
    /// </para>
    /// </summary>
    [MustUseReturnValue]
    public PdfPage InsertPage(int index, PdfPage page, AnnotationCopyingType annotationCopying = AnnotationCopyingType.ShallowCopy)
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);
        return Catalog.Pages.Insert(index, page, annotationCopying);
    }

    /// <summary>
    /// Places a page this document already owns but has not yet added to the page tree, and
    /// returns that same page object.
    /// <para>
    /// This is the counterpart of <c>new PdfPage(document)</c>: create a page, draw on it, then
    /// place it where you want it. Unlike <see cref="AddPage(PdfPage, AnnotationCopyingType)"/>
    /// the page returned is always the page passed in, and a page from another document is
    /// rejected rather than silently imported.
    /// </para>
    /// </summary>
    /// <param name="index">The position to place the page at.</param>
    /// <param name="page">A page owned by this document that is not yet placed.</param>
    public PdfPage PlacePage(int index, PdfPage page)
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);
        return Catalog.Pages.Place(index, page);
    }

    /// <summary>
    /// Imports a page from another document and returns the imported copy, which is always a
    /// different object from the page passed in.
    /// <para>
    /// Unlike <see cref="AddPage(PdfPage, AnnotationCopyingType)"/> this always copies, so the
    /// return value never aliases the argument. A page this document already owns is rejected.
    /// The source document must be opened with <see cref="PdfDocumentOpenMode.Import"/>.
    /// </para>
    /// </summary>
    /// <param name="index">The position to place the imported copy at.</param>
    /// <param name="page">A page belonging to another document.</param>
    /// <param name="annotationCopying">Annotation copying action, by default annotations are copied shallowly.</param>
    [MustUseReturnValue]
    public PdfPage ImportPage(int index, PdfPage page, AnnotationCopyingType annotationCopying = AnnotationCopyingType.ShallowCopy)
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);
        return Catalog.Pages.Import(index, page, annotationCopying);
    }

    /// <summary>
    /// Adds a second page showing the content of an existing page of this document, and
    /// returns the new page.
    /// <para>
    /// The duplicate shares the content stream of the source rather than copying its bytes, so
    /// the file does not grow by the size of the page, but it gets a resource dictionary of its
    /// own so that later drawing on either page does not affect the other. Annotations are not
    /// carried over, because an annotation names the page that owns it.
    /// </para>
    /// </summary>
    /// <param name="sourceIndex">The index of the page to duplicate.</param>
    /// <param name="index">The index to place the duplicate at.</param>
    [MustUseReturnValue]
    public PdfPage DuplicatePage(int sourceIndex, int index)
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);
        return Catalog.Pages.Duplicate(sourceIndex, index);
    }

    /// <summary>
    /// Moves a page within the page sequence of this document.
    /// </summary>
    /// <param name="oldIndex">The page index before this operation.</param>
    /// <param name="newIndex">The page index after this operation.</param>
    public void MovePage(int oldIndex, int newIndex)
    {
        if (!CanModify)
            throw new InvalidOperationException(PSSR.CannotModify);
        Catalog.Pages.MovePage(oldIndex, newIndex);
    }

    /// <summary>
    /// Marks the acroform fields readonly 
    /// </summary>
    public void MakeAcroFormsReadOnly()
    {
        for (var i = 0; i < AcroForm?.Fields.Count(); i++)
        {
            AcroForm.Fields[i].ReadOnly = true;
        }
    }

    /// <summary>
    /// Drops from each page the resources it does not draw with.
    /// <para>
    /// Pages of a document commonly share one resource dictionary naming every font and image in
    /// it, whether or not a given page draws with them. Importing such a page brings all of them
    /// along, which is why splitting a document into one file per page can give every file the
    /// weight of the whole document. Call this on the document that is about to be saved.
    /// </para>
    /// <para>
    /// A page whose content cannot be read in full is left as it stands, so this never drops a
    /// resource a page turns out to need.
    /// </para>
    /// </summary>
    public void PruneUnusedResources()
    {
        foreach (PdfPage page in Pages)
            PdfResourcePruner.Prune(page);
    }

    public void ConsolidateImages()
    {
        var images = ImageInfo.FindAll(this);

        var mapHashcodeToMd5 = new Dictionary<int, string>();
        var mapMd5ToPdfItem = new Dictionary<string, PdfItem>();

        // Calculate MD5 for each image XObject and build lookups for all images.
        foreach (ImageInfo img in images)
        {
            mapHashcodeToMd5[img.XObject.GetHashCode()] = img.XObjectMD5;
            mapMd5ToPdfItem[img.XObjectMD5] = img.Item.Value;
        }

        // Set the PdfItem for each image to the one chosen for the MD5.
        foreach (ImageInfo img in images)
        {
            string md5 = mapHashcodeToMd5[img.XObject.GetHashCode()];
            img.XObjects.Elements[img.Item.Key] = mapMd5ToPdfItem[md5];
        }
    }
        
    internal class ImageInfo
    {
        public PdfDictionary XObjects { get; }
        public KeyValuePair<string, PdfItem> Item  { get; }
        public PdfDictionary XObject { get; }
        public string XObjectMD5 { get; }

        private static readonly MD5Managed Hasher = new();
            
        public ImageInfo(PdfDictionary xObjects, KeyValuePair<string, PdfItem> item, PdfDictionary xObject)
        {
            XObjects = xObjects;
            Item = item;
            XObject = xObject;
            XObjectMD5 = ComputeMD5(xObject.Stream.Value);
        }
            
        /// <summary>
        /// Get info for each image in the document.
        /// </summary>
        internal static List<ImageInfo> FindAll(PdfDocument doc) =>
            doc.Pages.Cast<PdfPage>()
                .Select(page => page.Elements.GetDictionary("/Resources"))
                .Select(resources => resources?.Elements?.GetDictionary("/XObject"))
                .Where(xObjects => xObjects?.Elements != null)
                .SelectMany(xObjects =>
                    from item in xObjects.Elements
                    let xObject = (item.Value as PdfReference)?.Value as PdfDictionary
                    where xObject?.Elements?.GetString("/Subtype") == "/Image"
                    select new ImageInfo(xObjects, item, xObject)
                )
                .ToList();
            
        /// <summary>
        /// Compute and return the MD5 hash of the input data.
        /// </summary>
        internal static string ComputeMD5(byte[] input)
        {
            byte[] hashBytes;
            lock (Hasher)
            {
                hashBytes = Hasher.ComputeHash(input);
                Hasher.Initialize();
            }
                
            var sb = new StringBuilder();
            foreach (var x in hashBytes)
            {
                sb.Append(x.ToString("x2"));
            }
        
            return sb.ToString();
        }
    }

    /// <summary>
    /// Gets the security handler.
    /// </summary>
    public PdfStandardSecurityHandler SecurityHandler
    {
        get { return _trailer.SecurityHandler; }
    }

    internal PdfTrailer _trailer;
    internal PdfCrossReferenceTable _irefTable;
    internal Stream _outStream;

    // Imported Document
    internal Lexer _lexer;

    internal DateTime _creation;

    /// <summary>
    /// Occurs when the specified document is not used anymore for importing content.
    /// </summary>
    internal void OnExternalDocumentFinalized(DocumentHandle handle)
    {
        if (tls != null)
        {
            //PdfDocument[] documents = tls.Documents;
            tls.DetachDocument(handle);
        }

        if (_formTable != null)
            _formTable.DetachDocument(handle);
    }

    //internal static GlobalObjectTable Gob = new GlobalObjectTable();

    /// <summary>
    /// Gets the ThreadLocalStorage object. It is used for caching objects that should created
    /// only once.
    /// </summary>
    internal static ThreadLocalStorage Tls
    {
        get { return tls ?? (tls = new ThreadLocalStorage()); }
    }
    [ThreadStatic]
    static ThreadLocalStorage tls;

    [DebuggerDisplay("(ID={ID}, alive={IsAlive})")]
    internal class DocumentHandle
    {
        public DocumentHandle(PdfDocument document)
        {
            _weakRef = new WeakReference(document);
            ID = document._guid.ToString("B").ToUpper();
        }

        public bool IsAlive
        {
            get { return _weakRef.IsAlive; }
        }

        public PdfDocument Target
        {
            get { return _weakRef.Target as PdfDocument; }
        }
        readonly WeakReference _weakRef;

        public string ID;

        public override bool Equals(object obj)
        {
            DocumentHandle handle = obj as DocumentHandle;
            if (!ReferenceEquals(handle, null))
                return ID == handle.ID;
            return false;
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }

        public static bool operator ==(DocumentHandle left, DocumentHandle right)
        {
            if (ReferenceEquals(left, null))
                return ReferenceEquals(right, null);
            return left.Equals(right);
        }

        public static bool operator !=(DocumentHandle left, DocumentHandle right)
        {
            return !(left == right);
        }
    }
}
