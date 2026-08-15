using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.AcroForms;
using PdfSharpCore.Pdf.IO;

namespace PdfSharpCore.Test.Pdfs.AcroForms;

/// <summary>
///   Builds a document with an interactive form in it, because there is no other way to get one.
/// </summary>
/// <remarks>
///   <para>
///   Every field class in <c>PdfSharpCore.Pdf.AcroForms</c> has internal constructors only: a
///   field reaches a caller by being read out of a document, where the field collection looks at
///   the <c>/FT</c> entry and the flags and decides which class the dictionary becomes. So a test
///   that wants a <c>PdfCheckBoxField</c> has to write the dictionary that makes one, save the
///   document, and read it back.
///   </para>
///   <para>
///   That round trip is the point rather than an inconvenience - it is exactly what a caller
///   opening somebody else's form goes through, and it means these tests exercise the type
///   transformation as well as whatever they were written for.
///   </para>
/// </remarks>
internal sealed class AcroFormBuilder
{
    readonly PdfDocument _document = new();
    readonly List<PdfDictionary> _fields = new();
    readonly PdfPage _page;

    internal AcroFormBuilder()
    {
        _page = _document.AddPage();
    }

    /// <summary>The document being built, for tests that need to reach past the form.</summary>
    internal PdfDocument Document => _document;

    /// <summary>
    ///   Adds a field. <paramref name="describe"/> receives the field's dictionary, already
    ///   carrying the entries every widget needs, and adds whatever the field type wants.
    /// </summary>
    internal AcroFormBuilder With(string fieldType, string name,
        System.Action<PdfDictionary> describe = null)
    {
        var field = new PdfDictionary(_document);
        field.Elements.SetName("/Type", "/Annot");
        field.Elements.SetName("/Subtype", "/Widget");
        field.Elements.SetName(PdfAcroField.Keys.FT, fieldType);
        field.Elements.SetString(PdfAcroField.Keys.T, name);
        field.Elements.SetString(PdfAcroField.Keys.DA, "/Helv 10 Tf 0 g");
        field.Elements[PdfAcroField.Keys.Rect] = new PdfRectangle(new XRect(20, 20, 200, 20));
        describe?.Invoke(field);

        _document.Internals.AddObject(field);
        _fields.Add(field);
        return this;
    }

    /// <summary>
    ///   Adds a field with children of its own, so that the parts of the API that walk the tree
    ///   have a tree to walk.
    /// </summary>
    internal AcroFormBuilder WithParent(string name, params (string Type, string Name)[] kids)
    {
        var parent = new PdfDictionary(_document);
        parent.Elements.SetString(PdfAcroField.Keys.T, name);

        var children = new PdfArray(_document);
        foreach (var (type, kidName) in kids)
        {
            var kid = new PdfDictionary(_document);
            kid.Elements.SetName("/Type", "/Annot");
            kid.Elements.SetName("/Subtype", "/Widget");
            kid.Elements.SetName(PdfAcroField.Keys.FT, type);
            kid.Elements.SetString(PdfAcroField.Keys.T, kidName);
            _document.Internals.AddObject(kid);
            children.Elements.Add(kid.Reference);
        }
        parent.Elements[PdfAcroField.Keys.Kids] = children;

        _document.Internals.AddObject(parent);
        _fields.Add(parent);
        return this;
    }

    /// <summary>Writes the document out and reads it back, so the fields become field objects.</summary>
    internal PdfDocument Build()
    {
        var annotations = new PdfArray(_document);
        var fields = new PdfArray(_document);
        foreach (var field in _fields)
        {
            fields.Elements.Add(field.Reference);
            if (field.Elements.ContainsKey(PdfAcroField.Keys.Rect))
                annotations.Elements.Add(field.Reference);
        }
        _page.Elements.SetObject("/Annots", annotations);

        var form = new PdfDictionary(_document);
        form.Elements.SetObject(PdfAcroForm.Keys.Fields, fields);
        _document.Internals.AddObject(form);
        _document.Internals.Catalog.Elements["/AcroForm"] = form.Reference;

        using var saved = new MemoryStream();
        _document.Save(saved, false);
        saved.Position = 0;
        // Fully qualified: this test assembly has a PdfReader of its own.
        return PdfSharpCore.Pdf.IO.PdfReader.Open(saved, PdfDocumentOpenMode.Modify);
    }

    // ----- the entries individual field types want ------------------------------------------------

    /// <summary>An appearance dictionary naming the on and off states of a tickable field.</summary>
    internal static void WithOnAndOffAppearances(PdfDictionary field, string onState = "/Yes")
    {
        var document = field.Owner;
        var normal = new PdfDictionary(document);
        normal.Elements[onState] = new PdfDictionary(document);
        normal.Elements["/Off"] = new PdfDictionary(document);

        var appearance = new PdfDictionary(document);
        appearance.Elements["/N"] = normal;
        field.Elements["/AP"] = appearance;
    }

    /// <summary>
    ///   The <c>/Opt</c> array a choice field or a radio group picks its value out of. Spelled out
    ///   rather than taken from a Keys class, because the two that declare it - PdfChoiceField and
    ///   PdfRadioButtonField - declare it separately and neither is reachable from here.
    /// </summary>
    internal static void WithOptions(PdfDictionary field, params string[] options)
    {
        var document = field.Owner;
        var opt = new PdfArray(document);
        foreach (var option in options)
            opt.Elements.Add(new PdfString(option));
        field.Elements["/Opt"] = opt;
    }

    internal static void WithFlags(PdfDictionary field, PdfAcroFieldFlags flags) =>
        field.Elements.SetInteger(PdfAcroField.Keys.Ff, (int)flags);
}
