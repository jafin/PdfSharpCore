#region MigraDoc - Creating Documents on the Fly
//
// Authors:
//   Stefan Lange
//   Klaus Potzesny
//   David Stephensen
//
// Copyright (c) 2001-2019 empira Software GmbH, Cologne Area (Germany)
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
using System.IO;
using System.Text;

namespace MigraDocCore.DocumentObjectModel.IO
{
    /// <summary>
    /// Represents a reader that provides access to DDL data.
    /// </summary>
    public class DdlReader : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the DdlReader class with the specified Stream.
        /// </summary>
        public DdlReader(Stream stream)
            : this(stream, null)
        { }

        /// <summary>
        /// Initializes a new instance of the DdlReader class with the specified Stream and ErrorManager2.
        /// </summary>
        public DdlReader(Stream stream, DdlReaderErrors errors)
        {
            _errorManager = errors;
            _reader = new StreamReader(stream);
        }

        /// <summary>
        /// Initializes a new instance of the DdlReader class with the specified filename.
        /// </summary>
        public DdlReader(string filename)
            : this(filename, null)
        { }

        /// <summary>
        /// Initializes a new instance of the DdlReader class with the specified filename and ErrorManager2.
        /// </summary>
        public DdlReader(string filename, DdlReaderErrors errors)
        {
            _fileName = filename;
            _errorManager = errors;
            _reader = new StreamReader(File.OpenRead(filename), Encoding.UTF8, false, 1028, false);
            //_reader = new StreamReader(null, Encoding.UTF8);
        }

        /// <summary>
        /// Initializes a new instance of the DdlReader class with the specified TextReader.
        /// </summary>
        public DdlReader(TextReader reader)
            : this(reader, null)
        { }

        /// <summary>
        /// Initializes a new instance of the DdlReader class with the specified TextReader and ErrorManager2.
        /// </summary>
        public DdlReader(TextReader reader, DdlReaderErrors errors)
        {
            _doClose = false;
            _errorManager = errors;
            _reader = reader;
        }

        /// <summary>
        /// Closes the underlying stream or text writer.
        /// </summary>
        public void Close()
        {
            if (_doClose && _reader != null)
            {
                _reader.Close();
                _reader = null;
            }
        }

        /// <summary>
        /// Reads and returns a Document from a file or a DDL string.
        /// </summary>
        public Document ReadDocument()
        {
            string ddl = _reader.ReadToEnd();

            Document document;
            if (!String.IsNullOrEmpty(_fileName))
            {
                DdlParser parser = new DdlParser(_fileName, ddl, _errorManager);
                document = parser.ParseDocument(null);
                document._ddlFile = _fileName;
            }
            else
            {
                DdlParser parser = new DdlParser(ddl, _errorManager);
                document = parser.ParseDocument(null);
            }

            return document;
        }

        /// <summary>
        /// Reads and returns a DocumentObject from a file or a DDL string.
        /// </summary>
        public DocumentObject ReadObject()
        {
            string ddl = _reader.ReadToEnd();
            DdlParser parser = !String.IsNullOrEmpty(_fileName) ? 
                new DdlParser(_fileName, ddl, _errorManager) : 
                new DdlParser(ddl, _errorManager);
            return parser.ParseDocumentObject();
        }

        /// <summary>
        /// Reads and returns a Document from the specified file.
        /// </summary>
        public static Document DocumentFromFile(string documentFileName) //, ErrorManager2 _errorManager)
        {
            using (var reader = new DdlReader(documentFileName))
                return reader.ReadDocument();
        }

        /// <summary>
        /// Reads and returns a Document from the specified DDL string.
        /// </summary>
        public static Document DocumentFromString(string ddl)
        {
            using (var stringReader = new StringReader(ddl))
            {
                using (var reader = new DdlReader(stringReader))
                {
                    return reader.ReadDocument();
                }
            }
        }

        /// <summary>
        /// Reads and returns a domain object from the specified file.
        /// </summary>
        public static DocumentObject ObjectFromFile(string documentFileName, DdlReaderErrors errors)
        {
            using (var reader = new DdlReader(documentFileName, errors))
                return reader.ReadObject();
        }

        /// <summary>
        /// Reads and returns a domain object from the specified file.
        /// </summary>
        public static DocumentObject ObjectFromFile(string documentFileName)
        {
            return ObjectFromFile(documentFileName, null);
        }

        /// <summary>
        /// Reads and returns a domain object from the specified DDL string.
        /// </summary>
        public static DocumentObject ObjectFromString(string ddl, DdlReaderErrors errors)
        {
            using (var stringReader = new StringReader(ddl))
            {

                using (var reader = new DdlReader(stringReader))
                    return reader.ReadObject();
            }
        }

        /// <summary>
        /// Reads and returns a domain object from the specified DDL string.
        /// </summary>
        public static DocumentObject ObjectFromString(string ddl)
        {
            return ObjectFromString(ddl, null);
        }

        public void Dispose()
        {
            // Dispose of unmanaged resources.
            Dispose(true);
            // Suppress finalization.
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }
        }

        readonly bool _doClose = true;
        TextReader _reader;
        DdlReaderErrors _errorManager;
        string _fileName;
    }
}

