# Spec — gaps in the interactive layer

Tied to no upstream issue. This is the inventory of what turned up while writing the `Forms`,
`Annotations` and `Outline` demos for `SampleApp` — the three that cover the part of PDF a reader
*does* something with, rather than the part it draws.

Everything below was found by building something against the public API and then reading the bytes
it produced. Nothing here came from reading the source looking for problems, which is why it is
worth writing down: each item is a thing somebody trying to use the library would hit.

| # | area | gap | status |
|---|---|---|---|
| 1 | forms | the typed AcroForm API cannot author a form | open, worked around |
| 2 | forms | `PdfAcroFieldFlags` has no `Comb` | open |
| 3 | annotations | four PDFKit annotation types have no subtype here | open |
| 4 | annotations | no public way to add an annotation of an arbitrary subtype | open |
| 5 | annotations | no public way to build an appearance stream | open, worked around |
| 6 | annotations | `PdfFileAttachmentAnnotation.Icon`'s getter always throws | open |
| 7 | outlines | `PdfOutline.Opened` was never written | **fixed** |
| 8 | outlines | reading a document lost every expanded branch | **fixed** |
| 9 | general | `PdfInternals.CreateIndirectObject<T>()` always returns null | open |
| 10 | general | bookmarks and links do not survive page import | open, known |

Items 7 and 8 are done, under `docs/specs/bookmarks-and-outlines.md` item 5. The rest are recorded
rather than fixed: each wants a change of its own, and several are API-surface decisions rather
than defects.

Two more entries at the end are not library gaps at all — they are traps that caught this app's own
first draft, kept here because both fail silently and both will catch the next person.

---

## Forms

### 1 — the typed AcroForm API cannot author a form

The largest gap of the three areas, and the reason `Forms` is the only demo in the app that writes
dictionaries by hand.

`PdfSharpCore/Pdf.AcroForms/` ships nine public types. Every one of them can be *read*; none can be
*constructed*:

| type | constructors |
|---|---|
| `PdfAcroForm` | `internal PdfAcroForm(PdfDocument)`, `internal PdfAcroForm(PdfDictionary)` |
| `PdfAcroField` | `internal PdfAcroField(PdfDocument)`, `protected PdfAcroField(PdfDictionary)` |
| `PdfTextField`, `PdfCheckBoxField`, `PdfRadioButtonField`, `PdfComboBoxField`, `PdfListBoxField`, `PdfPushButtonField`, `PdfSignatureField` | all `internal` |
| `PdfWidgetAnnotation` | the class itself is `internal` |

There is no other seam either. `PdfAcroField.PdfAcroFieldCollection` derives from `PdfArray` and
adds no `Add`; `PdfDocument.AcroForm` is get-only and returns `Catalog.AcroForm`;
`PdfDocument.Catalog` is `internal`.

So the API's whole purpose is filling in a form somebody else produced:

```csharp
using var document = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
document.AcroForm.Fields["name.full"].Value = new PdfString("Ada Lovelace");
```

That is a real and useful thing. It is not the thing anybody asking *"how do I add a text box"*
wants, and nothing says so.

**What works instead.** ISO 32000-1 §12.7 can be assembled directly, from outside the assembly,
because two public members are enough to get at the pieces:

```csharp
PdfDictionary field = new PdfDictionary(document);
document.Internals.AddObject(field);                       // makes it indirect
…
document.Internals.Catalog.Elements.SetReference("/AcroForm", acroForm);
```

`PdfInternals.Catalog` is public even though `PdfDocument.Catalog` is not, and `PdfCatalog` is a
`PdfDictionary` underneath, so the key can be set even though the typed `AcroForm` property could
never be assigned. Widgets go into a `PdfArray` set as the page's `/Annots`, because
`PdfPage.Annotations` only accepts a `PdfAnnotation` (see item 4).

**It round-trips.** The nine fields `Forms` builds this way come back from `PdfReader` fully typed —
`PdfTextField`, `PdfCheckBoxField`, `PdfRadioButtonField`, `PdfComboBoxField`, `PdfListBoxField`,
`PdfPushButtonField` — with the right `Flags` and `Value` on each. The dictionaries are correct;
only the way in is missing.

**What closing it would take.** Public constructors taking a `PdfDocument`, an `Add` on the field
collection, a `PdfDocument.CreateAcroForm()` or similar, and `PdfWidgetAnnotation` made public.
Appearance streams would still be the caller's problem, or `/NeedAppearances` theirs to set.

Related capabilities that also do not exist: **flattening** a form into page content, and
**signing** — `PdfSignatureField` is a field type, not a signature implementation.

### 2 — `PdfAcroFieldFlags` has no `Comb`

The enum covers fifteen of the sixteen field flags. Bit 25, `Comb` — a text field divided into
`/MaxLen` equal cells, which is how a form draws boxes for a postcode or a card number — is absent,
so a caller wanting it writes `field.Elements.SetInteger("/Ff", 1 << 24)` and loses the enum.

Two smaller things in the same file: `DoNotSpellCheckTextField` and `DoNotSpellCheckChoiseField` are
both `1 << 22`, which is *correct* — bit 23 means the same for both field types — but the enum is
`[Flags]`, so `ToString()` picks one of the two names arbitrarily. And `Choise` is a typo that
cannot now be corrected without a breaking change.

---

## Annotations

Measured against [PDFKit's annotation API](https://pdfkit.org/docs/annotations.html), which is a
fair yardstick because it is a list somebody else wrote.

### 3 — four subtypes have no counterpart

| PDFKit | here |
|---|---|
| `note` | `PdfTextAnnotation` |
| `link` | `PdfLinkAnnotation`, or `XGraphics.AddWebLink` |
| `goTo` | `XGraphics.AddNamedLink` / `AddDocumentLink` |
| `highlight` | `PdfHighlightAnnotation` |
| `underline` | `PdfUnderlineAnnotation` |
| `strike` | `PdfStrikeOutAnnotation` |
| `fileAnnotation` | `PdfFileAttachmentAnnotation` |
| `annotate` | subclass `PdfAnnotation` — but see item 4 |
| `lineAnnotation` | **missing** — no `/Line` |
| `rectAnnotation` | **missing** — no `/Square` |
| `ellipseAnnotation` | **missing** — no `/Circle` |
| `textAnnotation` | **missing** — no `/FreeText` |

The four missing ones are all appearance-bearing: a viewer will not draw a `/Square` from its
`/Rect` alone, so adding them means writing appearance streams the way `PdfTextMarkupAnnotation`
already does for its four subtypes. That is the work, not the subtype wrapper — the same lesson
`text-markup-annotations.md` records for `/Highlight`.

Until then the honest answer is to draw the shape with `XGraphics`, which is page content rather
than an annotation and therefore not editable, hideable or printable-separately.

Going the other way, this library has what PDFKit does not: `PdfSquigglyAnnotation`,
`PdfRubberStampAnnotation` with fifteen standard names, `PdfAnnotation.Opacity` (`/CA`),
`PdfAnnotation.Flags`, and many quadrilaterals under one markup annotation via `AddQuad`.

There is also no `/Popup` annotation type. `PdfAnnotation.Keys.Popup` is defined and no class uses
it, so a note's popup is positioned entirely by the reader.

### 4 — no public way to add an arbitrary subtype

`PdfAnnotations.Add` takes a `PdfAnnotation`. `PdfAnnotation` is `abstract` with no public way to
set `/Subtype`, and `PdfGenericAnnotation` — the one general-purpose subclass — is `internal`. So
an annotation type the library has no class for cannot be added through the typed collection at
all, and the only route is `page.Elements["/Annots"]` built by hand.

This is already recorded in `text-markup-annotations.md` as the reason the StackOverflow workaround
for issue #342 could not be made to work. It is repeated here because it is what makes item 3 a
hard wall rather than an inconvenience: without it, a caller could add a `/Square` themselves.

`PdfAnnotations.Insert` is commented out, so annotations can only be appended.

### 5 — no public way to build an appearance stream

Every constructor of `PdfFormXObject` is `internal` — all four of them. An appearance stream is a
form XObject, so nothing in the typed API can produce one.

The raw route works and is what `Forms` uses for its check box, radio group and push button:

```csharp
PdfDictionary form = new PdfDictionary(document);
document.Internals.AddObject(form);
form.Elements.SetName("/Type", "/XObject");
form.Elements.SetName("/Subtype", "/Form");
form.Elements["/BBox"] = new PdfArray(document, …);
form.CreateStream(Encoding.ASCII.GetBytes(content));
```

`PdfDictionary.CreateStream(byte[])` being public is what makes this possible at all. The content
has to be written as PDF operators by hand — see the second trap at the end.

### 6 — `PdfFileAttachmentAnnotation.Icon`'s getter always throws

A plain defect, and the only one in this document that is not a missing feature.

```csharp
public IconType Icon
{
    get
    {
        var iconName = Elements.GetName(Keys.Name);
        if (iconName == null)
            return IconType.PushPin;
        return (IconType)(Enum.Parse(typeof(IconType), iconName));
    }
    set => Elements.SetName(Keys.Name, value.ToString());
}
```

Two faults, and together they mean the property can never be read successfully:

- **`GetName` returns the name with its slash.** `PdfName.Value` includes it, and `SetName` adds one
  if the caller left it off. So the getter parses `"/Paperclip"`, and
  `Enum.Parse(typeof(IconType), "/Paperclip")` throws `ArgumentException: Requested value
  '/Paperclip' was not found.`
- **`GetName` returns `String.Empty`, never `null`**, so the guard above it never fires. An
  attachment with no `/Name` at all reaches `Enum.Parse(typeof(IconType), "")`, which throws
  `ArgumentException: Must specify valid information for parsing in the string.`

Confirmed by direct probe: both paths throw. The setter is fine, which is why the `Annotations` demo
works — it only ever sets.

The same property exists three times over, and the other two are right.
`PdfTextAnnotation.Icon` does `value.Substring(1)` and checks `Enum.IsDefined` before parsing;
`PdfRubberStampAnnotation.Icon` does the same. Both were probed and both return what was set. So the
fix is to make the third copy match the two beside it — or better, to have one helper the three
share.

---

## Outlines

### 7 and 8 — fixed

`PdfOutline.Opened` was accepted by four constructors and four `Outlines.Add` overloads and never
written, so every bookmark tree arrived collapsed however it had been built; and `Initialize` never
read the state back, so opening a document and saving it again silently lost every expanded branch.

Both are fixed, with 7 tests. The full account — including why the `OpenCount` bookkeeping that was
supposed to do this could not have worked, and why the quantity it computed was the wrong one — is
`bookmarks-and-outlines.md` item 5.

### 10 — bookmarks and links do not survive page import

Not new, and not this work's to fix: an outline entry resolves to a page and a position rather than
to a named destination in the catalog's `/Names` tree, so importing a page into another document
leaves any bookmark pointing at it behind. `import-size-and-annotations.md` lists the same gap for
page import, and `bookmarks-and-outlines.md` lists it as out of scope.

Worth restating here only because the `Outline` demo makes it look solved: its *contents page* links
are named destinations, and would survive. Its *bookmarks* are direct page destinations, and would
not. The two views of the same structure differ in exactly this.

---

## General

### 9 — `PdfInternals.CreateIndirectObject<T>()` always returns null

```csharp
public T CreateIndirectObject<T>() where T : PdfObject
{
    T result = null;
    ConstructorInfo ctorInfo = null; // TODO
    if (ctorInfo != null)
    {
        result = (T)ctorInfo.Invoke(new object[] { _document });
        AddObject(result);
    }

    Debug.Assert(result != null, "CreateIndirectObject failed with type " + typeof(T).FullName);
    return result;
}
```

`ctorInfo` is assigned `null` and never assigned again, so the body is unreachable and the method
returns `null` for every type. The `Debug.Assert` fires in a debug build and says nothing in a
release one, so a caller who tries it gets a `NullReferenceException` somewhere else entirely.

This is the obvious-looking method for the job item 1 needs doing, which is how it was found.
`PdfInternals.AddObject` is the working route:

```csharp
PdfDictionary dictionary = new PdfDictionary(document);
document.Internals.AddObject(dictionary);
```

The method should either be implemented — `typeof(T).GetConstructor` with a `PdfDocument`
parameter, non-public, which is what the TODO intends — or deleted. Left as it is, it is a public
API that cannot work.

---

## Two traps that are not the library's fault

Both cost time while building the demos. Neither produces an error; both produce a page that is
quietly wrong, which is the expensive kind.

### `/DA` with a zero font size is not portable

Zero means auto-size. On a single-line text field every viewer does something sensible. On a
**multiline** one, Ghostscript scales the first line to the height of the whole box — a two-line
value filled an entire A4 page and overprinted six other fields.

The form-level default `/DA` is a natural place to write `/Helv 0 Tf 0 g`, and it is what most
examples show. Name a real size on every field instead, and the form looks the same everywhere.

### a PDF content stream has no `arc` operator

`arc` is PostScript. A PDF path is built from `m`, `l`, `c`, `v`, `y`, `re` and `h`, and nothing
else — a circle is four Bézier curves with control points 0.5523 of the radius along the tangent.

The first draft of the `Forms` radio group drew its rings with `arc`. A viewer handed an operator it
does not know **draws nothing and reports nothing**, so the result was two invisible radio buttons
and a third that appeared only because its selected dot happens to be a ZapfDingbats glyph rather
than a path. Nothing in the test suite would have caught it; it was found by rasterizing the page
and looking at it.

---

## How these were found

Worth recording, because it is the argument for keeping the demos: **eight of the ten gaps above
were found by writing an ordinary caller and reading what came out.** None needed a fuzzer, a
specification review or a source audit. Items 6, 7, 8 and 9 are all defects in code that compiles,
has no warnings, and whose properties read back exactly as they were set.

The pattern is the one `demonstration-app.md` already names: a feature that silently does nothing
looks identical, from the calling side, to a feature that works. The only thing that tells them
apart is somebody drawing a page and looking at it.
