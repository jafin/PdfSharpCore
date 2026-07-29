namespace PdfSharpCore.Pdf.Advanced;

/// <summary>
/// Represent a file stream embedded in the PDF document
/// </summary>
public class PdfFileSpecification : PdfDictionary
{
    private readonly PdfDictionary embeddedFileDictionary;

    public PdfFileSpecification(PdfDocument document)
        : base(document)
    {
        embeddedFileDictionary = new PdfDictionary();

        Elements.SetName(Keys.Type, "/Filespec");
        Elements.SetObject(Keys.EF, embeddedFileDictionary);
    }

    public PdfFileSpecification(PdfDocument document, string fileName, PdfEmbeddedFile embeddedFile) 
        : this(document)
    {
        FileName = fileName;
        EmbeddedFile = embeddedFile;
    }

    public string FileName
    {
        // /UF says what the name really is, so it is the one to believe. Documents written
        // before it was set, and those written by other libraries, have only /F.
        get
        {
            string unicodeName;
            if (Elements.TryGetString(Keys.UF, out unicodeName))
                return unicodeName;
            return Elements.GetString(Keys.F);
        }
        // A file specification string is not a text string: it stays one byte per character,
        // and it is /UF that holds the name for readers that want more than ASCII.
        set
        {
            Elements.SetString(Keys.F, value, PdfStringEncoding.RawEncoding);
            Elements.SetString(Keys.UF, value);
        }
    }

    public PdfEmbeddedFile EmbeddedFile
    {
        get
        {
            var reference = embeddedFileDictionary.Elements.GetReference(Keys.F);

            return reference?.Value as PdfEmbeddedFile;
        }
        set
        {
            if (value == null)
            {
                embeddedFileDictionary.Elements.Remove(Keys.F);
            }
            else
            {
                if (!value.IsIndirect)
                    Owner._irefTable.Add(value);

                embeddedFileDictionary.Elements.SetReference(Keys.F, value);
            }
        }
    }
        
    /// <summary>
    /// Predefined keys of this embedded file.
    /// </summary>
    public class Keys : KeysBase
    {
        /// <summary>
        /// (Required if an EF or RF entry is present; recommended always) 
        /// The type of PDF object that this dictionary describes; must be Filespec 
        /// for a file specification dictionary (see implementation note 45 in Appendix H).
        /// </summary>
        [KeyInfo(KeyType.Name | KeyType.Optional, FixedValue = "Filespec")]
        public const string Type = "/Type";

        /// <summary>
        /// (Required if the DOS, Mac, and Unix entries are all absent; amended with the UF 
        /// entry for PDF 1.7) A file specification string of the form described in Section 
        /// 3.10.1, “File Specification Strings,” or (if the file system is URL) a uniform 
        /// resource locator, as described in Section 3.10.4, “URL Specifications.”
        /// 
        /// Note: It is recommended that the UF entry be used in addition to the F entry.
        /// The UF entry provides cross-platform and cross-language compatibility and the F 
        /// entry provides backwards compatibility
        /// </summary>
        [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
        public const string F = "/F";

        /// <summary>
        /// (Optional, but recommended if the F entry exists; PDF 1.7) A Unicode text string
        /// that provides file specification of the form described in Section 3.10.1,
        /// “File Specification Strings.” This is a text string encoded using PDFDocEncoding
        /// or UTF-16BE with a leading byte-order marker, and so can name a file that the
        /// one byte per character F entry cannot spell.
        /// </summary>
        [KeyInfo("1.7", KeyType.TextString | KeyType.Optional)]
        public const string UF = "/UF";

        /// <summary>
        /// (Required if RF is present; PDF 1.3; amended to include the UF key in PDF 1.7) 
        /// A dictionary containing a subset of the keys F, UF, DOS, Mac, and Unix, 
        /// corresponding to the entries by those names in the file specification dictionary. 
        /// The value of each such key is an embedded file stream (see Section 3.10.3, 
        /// “Embedded File Streams”) containing the corresponding file. If this entry is 
        /// present, the Type entry is required and the file specification dictionary must 
        /// be indirectly referenced. (See implementation note 46in Appendix H.)
        /// 
        /// Note: It is recommended that the F and UF entries be used in place of the DOS, 
        /// Mac, or Unix entries.
        /// </summary>
        [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
        public const string EF = "/EF";

        /// <summary>
        /// Gets the KeysMeta for these keys.
        /// </summary>
        internal static DictionaryMeta Meta
        {
            get
            {
                if (meta == null)
                    meta = CreateMeta(typeof(Keys));
                return meta;
            }
        }
        static DictionaryMeta meta;
    }

    /// <summary>
    /// Gets the KeysMeta of this dictionary type.
    /// </summary>
    internal override DictionaryMeta Meta => Keys.Meta;
}
