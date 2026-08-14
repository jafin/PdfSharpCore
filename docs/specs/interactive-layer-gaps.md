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
| 3 | annotations | four PDFKit annotation types have no subtype here | partly done |
| 4 | annotations | no public way to add an annotation of an arbitrary subtype | **fixed** |
| 5 | annotations | no public way to give an annotation an appearance | **fixed** |
| 6 | annotations | `PdfFileAttachmentAnnotation.Icon`'s getter always throws | **fixed** |
| 7 | outlines | `PdfOutline.Opened` was never written | **fixed** |
| 8 | outlines | reading a document lost every expanded branch | **fixed** |
| 9 | general | `PdfInternals.CreateIndirectObject<T>()` always returns null | open |
| 10 | general | bookmarks and links do not survive page import | open, known |

Items 7 and 8 are done, under `docs/specs/bookmarks-and-outlines.md` item 5. Items 4, 5 and 6 are
done under this one. The rest are recorded rather than fixed: each wants a change of its own, and several are
API-surface decisions rather than defects.

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
`PdfPushButtonField` — each with the right `Flags`, and the right `Value` where the field type has
one at all: a push button carries an action rather than a value. The dictionaries are correct;
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
fair yardstick because it is a list somebody else wrote. That page documents eleven helper methods;
nine have a counterpart here and two do not.

### 3 — four subtypes had no counterpart — two done, two left

| PDFKit | here |
|---|---|
| `note` | `PdfTextAnnotation` |
| `link` | `PdfLinkAnnotation`, or `XGraphics.AddWebLink` |
| `goTo` | `XGraphics.AddNamedLink` / `AddDocumentLink` |
| `highlight` | `PdfHighlightAnnotation` |
| `underline` | `PdfUnderlineAnnotation` |
| `strike` | `PdfStrikeOutAnnotation` |
| `fileAnnotation` | `PdfFileAttachmentAnnotation` |
| `lineAnnotation` | **missing** — no `/Line` |
| `rectAnnotation` | `PdfSquareAnnotation` |
| `ellipseAnnotation` | `PdfCircleAnnotation` |
| `textAnnotation` | **missing** — no `/FreeText` |

All four were appearance-bearing: a viewer will not draw a `/Square` from its `/Rect` alone, so
adding them meant writing appearance streams the way `PdfTextMarkupAnnotation` already does for its
four subtypes. That is the work, not the subtype wrapper — the same lesson
`text-markup-annotations.md` records for `/Highlight`.

**All four are reachable; two are wrapped.** Items 4 and 5 were what made this a wall: with
`PdfGenericAnnotation` public and `PdfAnnotation.SetAppearance` taking an `XForm`, a caller writes
the subtype and draws its appearance without leaving the public API, and a reader paints it. There
is a test that does exactly that and counts the pixels, beside one that shows the same annotation
without an appearance rasterizing to nothing at all.

`PdfSquareAnnotation` and `PdfCircleAnnotation` have classes of their own. ISO 32000-1 puts both in
one section, and so does this: `PdfSquareCircleAnnotation` carries `/IC`, `/BS` and `/RD` and builds
the appearance itself — through `XForm` and `XGraphics` rather than by writing operators — rebuilding
it whenever the rectangle, the colour, the interior, the border width or the opacity changes. The two
subclasses are a subtype name and one `DrawShape` override each. Asked for neither a border nor a
fill, either draws nothing and **removes** the appearance it had, so a border set back to zero does
not leave the last one on the page.

`/Circle` is a circle only when its rectangle is square — the specification names the subtype and
then describes an ellipse inscribed in `/Rect`. A test paints one in a 200 × 100 rectangle and
checks the four corners are *not* painted, which is the one thing a square would fail.

`/Line` and `/FreeText` are what item 3 has left. Both carry geometry or text of their own rather
than filling their rectangle, so neither fits the base above, and they are ordinary work now rather
than blocked work.

Going the other way, this library has what PDFKit does not: `PdfSquigglyAnnotation`,
`PdfRubberStampAnnotation` with fifteen standard names, `PdfAnnotation.Opacity` (`/CA`),
`PdfAnnotation.Flags`, and many quadrilaterals under one markup annotation via `AddQuad`.

There is also no `/Popup` annotation type. `PdfAnnotation.Keys.Popup` is defined and no class uses
it, so a note's popup is positioned entirely by the reader.

### 4 — no public way to add an arbitrary subtype — **fixed**

`PdfAnnotations.Add` takes a `PdfAnnotation`. `PdfAnnotation` is `abstract` with no public way to
set `/Subtype`, and `PdfGenericAnnotation` — the one general-purpose subclass — was `internal`. So
an annotation type the library has no class for could not be added through the typed collection at
all, and the only route was `page.Elements["/Annots"]` built by hand.

This is also recorded in `text-markup-annotations.md` as the reason the StackOverflow workaround for
issue #342 could not be made to work. It is what made item 3 a hard wall rather than an
inconvenience: without it, a caller could not add a `/Square` even knowing exactly what one is.

`PdfGenericAnnotation` is now public, with constructors that take the subtype:

```csharp
var square = new PdfGenericAnnotation("/Square");   // or "Square"; the solidus is added
page.Annotations.Add(square);
square.Rectangle = new PdfRectangle(gfx.Transformer.WorldToDefaultPage(box));
```

It keeps its `PdfDictionary` constructor, which is what `PdfAnnotations` already used to give a
type to annotations read out of a document it has no class for — so the same class is now both
what an unknown subtype is read into and how one is written.

An empty subtype is refused at the call rather than written, because a dictionary with no
`/Subtype` is not something a reader can do anything with, and the failure would otherwise surface
as a silently ignored annotation.

`PdfAnnotations.Insert` is still commented out, so annotations can only be appended.

### 5 — no public way to give an annotation an appearance — **fixed**

Recorded originally as "no public way to build an appearance stream", which was not quite right and
worth correcting: the drawing could always be *built*, just never *handed over*.

`XForm` has public constructors and `XGraphics.FromForm` draws onto one, so an appearance stream was
always reachable as a drawing. What was not reachable was the form XObject underneath it —
`XForm.PdfForm` is `internal`, and every constructor of `PdfFormXObject` is too — so there was no
way to put the result in an annotation's `/AP`.

`PdfAnnotation` now takes it directly:

```csharp
var form = new XForm(document, new XSize(120, 60));
using (var gfx = XGraphics.FromForm(form))
    gfx.DrawRectangle(XBrushes.RoyalBlue, 0, 0, 120, 60);

square.SetAppearance(form);                 // /AP /N
square.SetAppearance("/Off", blankForm);    // one of a named set, and /AS names it
```

The named overload accumulates rather than replaces, because that is what a set of states is for: a
check box needs both of its states in the file at once and `/AS` picks between them. The unnamed one
replaces the set and clears `/AS`, since a single appearance is not one of a set and a state naming
something no longer there leaves a reader with nothing to draw.

`PdfFormXObject` stays internal. Nothing needs it now, and keeping it in means the appearance is
described by the same drawing API as the rest of the library rather than by a second one.

**A defect turned up under this.** `XForm.Finish` did `Gfx.Dispose()` unconditionally, so finishing
a form that had never been drawn on threw `NullReferenceException` — reachable before this change
through the public `DrawingFinished()`, and unavoidable after it, because *an empty appearance is a
real thing*: the "off" state of a check box or a radio button is an empty content stream. Now
`Gfx?.Dispose()`.

The raw dictionary route still works, and is what the `Forms` demo uses for its check box, radio
group and push button — it needs one appearance per state on many fields and writes its own
operators. `PdfDictionary.CreateStream(byte[])` being public is what makes that possible.

### 6 — `PdfFileAttachmentAnnotation.Icon`'s getter always threw — **fixed**

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

The same property existed three times over, and the other two were right.
`PdfTextAnnotation.Icon` did `value.Substring(1)` and checked `Enum.IsDefined` before parsing;
`PdfRubberStampAnnotation.Icon` did the same, character for character, differing only in the
enumeration named. Both were probed and both returned what was set.

#### The change

Three copies that drifted is the defect, so the fix is one implementation rather than a corrected
third copy: `PdfAnnotation.IconFromName<T>` — `private protected`, so it adds nothing to the public
surface — and all three properties now read through it.

```csharp
private protected static T IconFromName<T>(string name, T fallback) where T : struct
{
    if (string.IsNullOrEmpty(name))
        return fallback;

    string member = name[0] == '/' ? name.Substring(1) : name;

    return Enum.IsDefined(typeof(T), member)
        ? (T)Enum.Parse(typeof(T), member, false)
        : fallback;
}
```

The fallback is what differs between the three, and it is the only thing that does.
`PdfFileAttachmentAnnotation.IconType` has no `NoIcon` member, and does not need one: Table 184
gives `/Name` a default of `PushPin`, so an attachment without the entry is a push pin. The other
two fall back to `NoIcon`.

`Enum.IsDefined` rather than `Enum.TryParse`, which the two working copies also used and which is
worth keeping deliberately: handed a string of digits `TryParse` succeeds and returns that number as
the enumeration value, so a document naming its icon `/1` would read back as whichever member
happens to be 1.

The attachment's **setter** changed too, in the one way it also differed: it wrote
`value.ToString()` for any value, so a cast from an out-of-range integer put something like `/42`
into the file. It now removes the entry instead, which is what the other two do and what leaves a
reader on its documented default rather than on a name it cannot know.

Covered by `PdfSharpCore.Test/Annotations/AnnotationIconTests.cs`, 31 tests: every icon of all three
enumerations round-tripping, the absent and unrecognised cases falling back rather than throwing,
the digit-named icon that `TryParse` would have accepted, an out-of-range value not being written,
and every name being written with its solidus.

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
