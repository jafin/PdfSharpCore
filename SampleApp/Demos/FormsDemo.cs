using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.AcroForms;
using SampleApp.Infrastructure;

namespace SampleApp.Demos;

/// <summary>
///   An interactive form - text boxes, check boxes, radio buttons, a combo box, a list box and a
///   push button - built through the object model, because the typed AcroForm API cannot author one.
/// </summary>
/// <remarks>
///   <para>
///     This demo is the odd one out. Every other demo calls a method the library offers; this one
///     writes dictionaries by hand, because <see cref="PdfAcroForm"/> and every
///     <see cref="PdfAcroField"/> under it have <c>internal</c> constructors. The typed API reads
///     and fills a form somebody else wrote. It cannot make one.
///   </para>
///   <para>
///     So the demo is two things at once: a working interactive form, and the shortest honest
///     answer to "how do I create a form field with PdfSharpCore" - which is that you assemble
///     ISO 32000-1 section 12.7 yourself out of <see cref="PdfDictionary"/>. Page two writes that
///     down rather than leaving it to be rediscovered.
///   </para>
/// </remarks>
internal sealed class FormsDemo : PdfDemo
{
    public FormsDemo() : base() { }

    public override string Name => "Forms";

    public override string Summary => "An interactive AcroForm: text, choice and button fields.";

    public override IReadOnlyList<string> Shows => new[]
    {
        "Text fields - single line, required, multiline, and a password",
        "A check box and a radio group, each with the appearance streams a viewer toggles between",
        "A combo box and a list box, both from an /Opt array, one of them editable",
        "A push button carrying a URI action",
        "That every field here is a hand-built dictionary, because the typed AcroForm API is read-only",
    };

    public override int PageCount => 2;

    protected override PdfDocument Build(DemoContext context)
    {
        #region example
        const string Sans = "Liberation Sans";

        PdfDocument document = new PdfDocument();
        document.Info.Title = "Interactive form";

        PdfPage page = document.AddPage();
        XGraphics gfx = XGraphics.FromPdfPage(page);

        XFont titleFont = new XFont(Sans, 18, XFontStyle.Bold);
        XFont labelFont = new XFont(Sans, 9, XFontStyle.Bold);
        XFont noteFont = new XFont(Sans, 7.5);

        // ---- The plumbing every field needs ------------------------------------------
        //
        // PdfInternals.CreateIndirectObject is not usable - its body never assigns the
        // constructor it looks for, so it returns null. AddObject is the working route: make
        // the dictionary owned by the document, then hand it to the reference table.
        PdfDictionary NewObject()
        {
            PdfDictionary dictionary = new PdfDictionary(document);
            document.Internals.AddObject(dictionary);
            return dictionary;
        }

        // The two standard-14 faces a form needs. Neither is embedded and neither has to be:
        // Helvetica draws the field values, ZapfDingbats draws the tick and the radio dot.
        PdfDictionary StandardFont(string baseFont, bool winAnsi)
        {
            PdfDictionary font = NewObject();
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type1");
            font.Elements.SetName("/BaseFont", baseFont);
            if (winAnsi)
                font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
            return font;
        }

        PdfDictionary fonts = NewObject();
        fonts.Elements.SetReference("/Helv", StandardFont("/Helvetica", winAnsi: true));
        fonts.Elements.SetReference("/ZaDb", StandardFont("/ZapfDingbats", winAnsi: false));

        // The resource dictionary is shared by every appearance stream and by the form's /DR,
        // so it is indirect. A direct dictionary hung off several parents would be written out
        // once per parent.
        PdfDictionary resources = NewObject();
        resources.Elements.SetReference("/Font", fonts);

        // The form itself. /NeedAppearances asks the viewer to build the appearance streams for
        // the text and choice fields, which is what saves this demo from laying out their glyphs
        // by hand. The buttons below still carry their own, because a check box's appearance is
        // the thing being toggled rather than a rendering of its value.
        PdfArray fields = new PdfArray(document);

        PdfDictionary acroForm = NewObject();
        acroForm.Elements["/Fields"] = fields;
        acroForm.Elements.SetBoolean("/NeedAppearances", true);
        acroForm.Elements.SetString("/DA", "/Helv 0 Tf 0 g");
        acroForm.Elements.SetReference("/DR", resources);

        // PdfDocument.Catalog is internal; Internals.Catalog is the public way in. It is a
        // PdfDictionary underneath, so the key can be set even though the typed AcroForm
        // property could never be assigned from out here.
        document.Internals.Catalog.Elements.SetReference("/AcroForm", acroForm);

        // Widgets are annotations, and the page's typed Annotations collection only accepts a
        // PdfAnnotation - whose one general-purpose subclass is internal. So /Annots is built
        // directly too.
        PdfArray annotations = new PdfArray(document);
        page.Elements["/Annots"] = annotations;

        // ---- One field, in the shape a viewer expects --------------------------------
        //
        // Field and widget are merged into a single dictionary. The specification allows that
        // whenever a field has exactly one widget, which is every field here except the radio
        // group, and it halves the number of objects.
        PdfDictionary Field(string fieldType, string name, string tooltip, XRect box)
        {
            PdfDictionary field = NewObject();
            field.Elements.SetName("/Type", "/Annot");
            field.Elements.SetName("/Subtype", "/Widget");
            field.Elements.SetName("/FT", fieldType);
            field.Elements.SetString("/T", name);
            field.Elements.SetString("/TU", tooltip);
            field.Elements.SetInteger("/F", 4);              // bit 3, Print
            field.Elements.SetReference("/P", page);

            // A size of its own, rather than the form's "/Helv 0 Tf". Zero means auto-size, and
            // what a viewer makes of that on a multiline box is its own business: Ghostscript
            // scales the first line to the height of the whole box, which fills the page with
            // one word. Naming the size is the difference between a form that looks the same
            // everywhere and one that does not.
            if (fieldType != "/Btn")
                field.Elements.SetString("/DA", "/Helv 9 Tf 0 g");

            // The drawing above is in world space, measured down from the top left. An
            // annotation is placed in default page space, measured up from the bottom left.
            field.Elements.SetRectangle("/Rect",
                new PdfRectangle(gfx.Transformer.WorldToDefaultPage(box)));

            fields.Elements.Add(field.Reference);
            annotations.Elements.Add(field.Reference);
            return field;
        }

        // /MK is what a viewer paints the field's box and border from.
        void Decorate(PdfDictionary field, double grey)
        {
            PdfDictionary appearance = new PdfDictionary(document);
            appearance.Elements["/BG"] = new PdfArray(document, new PdfReal(grey));
            appearance.Elements["/BC"] = new PdfArray(document, new PdfReal(0.45));
            field.Elements["/MK"] = appearance;
            field.Elements["/BS"] = new PdfLiteral("<</W 1/S/S>>");
        }

        void SetFlags(PdfDictionary field, PdfAcroFieldFlags flags)
        {
            field.Elements.SetInteger("/Ff", (int)flags);
        }

        // A circle as the four Bezier curves a PDF path is limited to. The magic number is the
        // usual one: a control point 0.5523 of the radius along the tangent puts the curve
        // within a thousandth of a true arc.
        string Circle(double centreX, double centreY, double radius)
        {
            double k = radius * 0.5523;

            string Point(double x, double y) =>
                x.ToString("0.###", CultureInfo.InvariantCulture) + " "
                + y.ToString("0.###", CultureInfo.InvariantCulture);

            return $"{Point(centreX + radius, centreY)} m "
                + $"{Point(centreX + radius, centreY + k)} {Point(centreX + k, centreY + radius)} "
                + $"{Point(centreX, centreY + radius)} c "
                + $"{Point(centreX - k, centreY + radius)} {Point(centreX - radius, centreY + k)} "
                + $"{Point(centreX - radius, centreY)} c "
                + $"{Point(centreX - radius, centreY - k)} {Point(centreX - k, centreY - radius)} "
                + $"{Point(centreX, centreY - radius)} c "
                + $"{Point(centreX + k, centreY - radius)} {Point(centreX + radius, centreY - k)} "
                + $"{Point(centreX + radius, centreY)} c h";
        }

        // An appearance stream is a form XObject: a BBox, some resources, and a content stream
        // in exactly the operators XGraphics would have emitted.
        PdfDictionary Appearance(XRect box, string content)
        {
            PdfDictionary form = NewObject();
            form.Elements.SetName("/Type", "/XObject");
            form.Elements.SetName("/Subtype", "/Form");
            form.Elements["/BBox"] = new PdfArray(document,
                new PdfReal(0), new PdfReal(0), new PdfReal(box.Width), new PdfReal(box.Height));
            form.Elements.SetReference("/Resources", resources);
            form.CreateStream(Encoding.ASCII.GetBytes(content));
            return form;
        }

        gfx.DrawString("Interactive form", titleFont, XBrushes.Black, new XPoint(56, 68));
        gfx.DrawLine(new XPen(XColors.SteelBlue, 1.5), 56, 78, 539, 78);
        gfx.DrawString("Open this in a reader that supports forms - the fields below are fillable.",
            noteFont, XBrushes.DimGray, new XPoint(56, 92));

        const double FieldX = 210;
        const double FieldW = 300;

        // One cursor down the page, rather than a measured coordinate per row. The note under
        // each field is what forces it: the notes are wider than the label column, so anything
        // that placed them by hand would sooner or later run one of them under a field box.
        double cursor = 116;

        XRect Row(string label, double height)
        {
            gfx.DrawString(label, labelFont, XBrushes.Black, new XPoint(56, cursor + 12));
            return new XRect(FieldX, cursor, FieldW, height);
        }

        void EndRow(XRect box, string note)
        {
            gfx.DrawString(note, noteFont, XBrushes.DimGray,
                new XPoint(FieldX, box.Bottom + 12));
            cursor = box.Bottom + 28;
        }

        // ---- Text fields -------------------------------------------------------------
        XRect fullNameBox = Row("Full name", 20);
        PdfDictionary fullName = Field("/Tx", "name.full", "Your name as it appears on your passport",
            fullNameBox);
        fullName.Elements.SetString("/V", "Ada Lovelace");
        fullName.Elements.SetInteger("/Q", 0);              // 0 left, 1 centre, 2 right
        Decorate(fullName, 0.96);
        EndRow(fullNameBox, "A value in /V, a tooltip in /TU, and /Q for the alignment.");

        XRect emailBox = Row("Email", 20);
        PdfDictionary email = Field("/Tx", "name.email", "Required - we will not use it for anything",
            emailBox);
        SetFlags(email, PdfAcroFieldFlags.Required);
        Decorate(email, 0.96);
        EndRow(emailBox, "Required, so a reader marks it when the form is submitted empty.");

        XRect secretBox = Row("Passphrase", 20);
        PdfDictionary secret = Field("/Tx", "name.secret", "Typed back as bullets", secretBox);
        SetFlags(secret, PdfAcroFieldFlags.Password);
        Decorate(secret, 0.96);
        // The flag masks what is typed and nothing more. ISO 32000-1 Table 228 adds only a note
        // that a reader "should never store the value", which is advice to the reader rather than
        // a guarantee to the author - so a form field is not somewhere to keep a secret.
        EndRow(secretBox, "Password: echoed as bullets. Advisory only - not secret storage.");

        XRect notesBox = Row("Notes", 56);
        PdfDictionary notes = Field("/Tx", "name.notes", "Anything else we should know", notesBox);
        SetFlags(notes, PdfAcroFieldFlags.Multiline);
        notes.Elements.SetString("/V", "Multiline: this box wraps and scrolls.\nA newline is a newline.");
        Decorate(notes, 0.96);
        EndRow(notesBox, "Multiline, and pre-filled with a value containing a line break.");

        // ---- A check box -------------------------------------------------------------
        //
        // Both states are drawn here rather than left to /NeedAppearances. What a check box
        // shows IS its value, so the two streams are the field rather than a rendering of it.
        XRect tickBox = Row("Subscribe", 16);
        tickBox = new XRect(tickBox.X, tickBox.Y, 16, 16);
        PdfDictionary subscribe = Field("/Btn", "prefs.subscribe", "Send me the newsletter", tickBox);

        const string BoxOutline = "0.45 G 1 w 0.5 0.5 15 15 re S\n";
        PdfDictionary ticked = Appearance(tickBox,
            "q 1 1 1 rg 0 0 16 16 re f Q\n" + BoxOutline
            + "q 0 g BT /ZaDb 12 Tf 2.5 3.5 Td (4) Tj ET Q\n");   // ZapfDingbats '4' is a tick
        PdfDictionary unticked = Appearance(tickBox, "q 1 1 1 rg 0 0 16 16 re f Q\n" + BoxOutline);

        PdfDictionary tickStates = new PdfDictionary(document);
        tickStates.Elements.SetReference("/Yes", ticked);
        tickStates.Elements.SetReference("/Off", unticked);
        PdfDictionary tickAppearance = new PdfDictionary(document);
        tickAppearance.Elements["/N"] = tickStates;
        subscribe.Elements["/AP"] = tickAppearance;

        // /AS names which of the two streams is showing; /V is the field's value. They agree
        // here, and a viewer keeps them in step as the box is clicked.
        subscribe.Elements.SetName("/AS", "/Yes");
        subscribe.Elements.SetName("/V", "/Yes");
        subscribe.Elements.SetName("/DV", "/Yes");
        EndRow(tickBox, "/AP /N holds one stream per state; /AS names the one on show.");

        // ---- A radio group -----------------------------------------------------------
        //
        // The one field here that cannot be merged with its widget: three widgets share a
        // parent, and the parent is what holds the name and the value.
        XRect deliveryRow = Row("Delivery", 14);

        PdfDictionary delivery = NewObject();
        delivery.Elements.SetName("/FT", "/Btn");
        delivery.Elements.SetString("/T", "order.delivery");
        delivery.Elements.SetString("/TU", "How soon do you want it");
        delivery.Elements.SetInteger("/Ff",
            (int)(PdfAcroFieldFlags.Radio | PdfAcroFieldFlags.NoToggleToOff));
        delivery.Elements.SetName("/V", "/Standard");
        PdfArray kids = new PdfArray(document);
        delivery.Elements["/Kids"] = kids;
        fields.Elements.Add(delivery.Reference);

        string[] choices = { "Standard", "Express", "Collect" };
        for (int index = 0; index < choices.Length; index++)
        {
            XRect dot = new XRect(FieldX + index * 100, deliveryRow.Y, 14, 14);

            PdfDictionary widget = NewObject();
            widget.Elements.SetName("/Type", "/Annot");
            widget.Elements.SetName("/Subtype", "/Widget");
            widget.Elements.SetInteger("/F", 4);
            widget.Elements.SetReference("/P", page);
            widget.Elements.SetReference("/Parent", delivery);
            widget.Elements.SetRectangle("/Rect",
                new PdfRectangle(gfx.Transformer.WorldToDefaultPage(dot)));

            // Each widget's "on" state is named after the choice it stands for. That name is
            // what the parent's /V is compared against, so the names have to match exactly.
            //
            // The ring is four Beziers. A content stream has no arc operator - that is
            // PostScript - and a viewer handed one draws nothing at all rather than complaining,
            // which is how the first draft of this demo ended up with two invisible radio
            // buttons and one that only showed because its dot is a ZapfDingbats glyph.
            string ring = Circle(7, 7, 6.5);
            string blank = $"q 1 1 1 rg {ring} f Q\n0.45 G 1 w {ring} S\n";

            PdfDictionary on = Appearance(dot,
                blank + $"q 0 g {Circle(7, 7, 3.2)} f Q\n");
            PdfDictionary off = Appearance(dot, blank);

            PdfDictionary states = new PdfDictionary(document);
            states.Elements.SetReference("/" + choices[index], on);
            states.Elements.SetReference("/Off", off);
            PdfDictionary appearance = new PdfDictionary(document);
            appearance.Elements["/N"] = states;
            widget.Elements["/AP"] = appearance;
            widget.Elements.SetName("/AS", index == 0 ? "/" + choices[index] : "/Off");

            kids.Elements.Add(widget.Reference);
            annotations.Elements.Add(widget.Reference);

            gfx.DrawString(choices[index], noteFont, XBrushes.Black,
                new XPoint(FieldX + index * 100 + 19, deliveryRow.Y + 11));
        }
        EndRow(deliveryRow,
            "One field, three widgets under /Kids. The parent holds the name and the value.");

        // ---- Choice fields -----------------------------------------------------------
        XRect countryBox = Row("Country", 20);
        PdfDictionary country = Field("/Ch", "address.country", "Pick one, or type your own",
            countryBox);
        SetFlags(country, PdfAcroFieldFlags.Combo | PdfAcroFieldFlags.Edit | PdfAcroFieldFlags.Sort);
        country.Elements["/Opt"] = new PdfArray(document,
            new PdfString("Australia"), new PdfString("Canada"), new PdfString("Ireland"),
            new PdfString("New Zealand"), new PdfString("United Kingdom"));
        country.Elements.SetString("/V", "United Kingdom");
        Decorate(country, 0.96);
        EndRow(countryBox,
            "Combo + Edit, so the list can also be typed into. Sort orders it for display.");

        XRect interestsBox = Row("Interests", 56);
        PdfDictionary interests = Field("/Ch", "prefs.interests", "Choose as many as you like",
            interestsBox);
        SetFlags(interests, PdfAcroFieldFlags.MultiSelect);
        interests.Elements["/Opt"] = new PdfArray(document,
            new PdfString("Typography"), new PdfString("Colour management"),
            new PdfString("Page imposition"), new PdfString("Tagged PDF"));
        interests.Elements["/V"] = new PdfArray(document,
            new PdfString("Typography"), new PdfString("Tagged PDF"));
        interests.Elements["/I"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(3));
        Decorate(interests, 0.96);
        EndRow(interestsBox,
            "A list box is a choice field without the Combo flag. /I carries the selected rows.");

        // ---- A push button -----------------------------------------------------------
        //
        // A push button has no value at all - it exists for its action. This one opens a URL,
        // which is the only action type this library writes without help.
        XRect buttonBox = Row("Then", 24);
        buttonBox = new XRect(buttonBox.X, buttonBox.Y, 140, 24);
        PdfDictionary button = Field("/Btn", "actions.help", "Opens the PdfSharpCore repository",
            buttonBox);
        SetFlags(button, PdfAcroFieldFlags.Pushbutton);
        button.Elements["/A"] = new PdfLiteral(
            "<</S/URI/URI(https://github.com/ststeiger/PdfSharpCore)>>");

        PdfDictionary caption = new PdfDictionary(document);
        caption.Elements.SetString("/CA", "Read the manual");
        button.Elements["/MK"] = caption;

        // Unlike the text fields, a push button gets no help from /NeedAppearances, so its face
        // is drawn here in the same operators a content stream uses.
        button.Elements["/AP"] = NormalAppearance(Appearance(buttonBox,
            "q 0.85 0.89 0.94 rg 0 0 140 24 re f Q\n"
            + "0.35 0.45 0.6 RG 1 w 0.5 0.5 139 23 re S\n"
            + "q 0.1 0.2 0.35 rg BT /Helv 10 Tf 22 8.5 Td (Read the manual) Tj ET Q\n"));
        EndRow(buttonBox,
            "A push button carries an action instead of a value: /A here is a URI action.");

        PdfDictionary NormalAppearance(PdfDictionary normal)
        {
            PdfDictionary appearance = new PdfDictionary(document);
            appearance.Elements.SetReference("/N", normal);
            return appearance;
        }

        // ---- Page two: what the typed API can and cannot do ---------------------------
        PdfPage notesPage = document.AddPage();
        XGraphics notesGfx = XGraphics.FromPdfPage(notesPage);

        notesGfx.DrawString("Why this demo writes dictionaries", titleFont, XBrushes.Black,
            new XPoint(56, 68));
        notesGfx.DrawLine(new XPen(XColors.SteelBlue, 1.5), 56, 78, 539, 78);

        XFont body = new XFont(Sans, 9.5);
        XFont mono = new XFont("Source Code Pro", 8.5);

        string[] paragraphs =
        {
            "PdfSharpCore ships a typed AcroForm API - PdfAcroForm, PdfTextField, PdfCheckBoxField,",
            "PdfRadioButtonField, PdfComboBoxField, PdfListBoxField, PdfPushButtonField and",
            "PdfSignatureField. Every one of them has an internal constructor, and so does",
            "PdfWidgetAnnotation. They are reached by opening a document that already has a form:",
            "PdfReader.Open(path, PdfDocumentOpenMode.Modify).AcroForm.Fields[\"name.full\"].",
            "",
            "That API fills a form. It does not create one, and there is no public seam that would",
            "let it - PdfAcroFieldCollection has no Add, and PdfDocument.Catalog is internal.",
            "So page one is built the only way it can be from outside the assembly: as the",
            "dictionaries of ISO 32000-1 section 12.7, hung off /AcroForm and off the page's /Annots.",
        };

        double lineY = 104;
        foreach (string paragraph in paragraphs)
        {
            notesGfx.DrawString(paragraph, body, XBrushes.Black, new XPoint(56, lineY));
            lineY += 14;
        }

        (string Capability, string State)[] table =
        {
            ("Create a field of any type", "not offered - hand-built here"),
            ("Read a field's name, type and flags", "PdfAcroField.Name, .Flags"),
            ("Fill a field's value", "PdfAcroField.Value"),
            ("Make every field read-only", "PdfDocument.MakeAcroFormsReadOnly()"),
            ("Walk a hierarchy of fields", "PdfAcroField.Fields, .GetDescendantNames()"),
            ("Sign a document", "PdfSignatureField exists; no signing"),
            ("Flatten a form into page content", "not offered"),
        };

        lineY += 16;
        notesGfx.DrawString("What the typed API does with a form it did not write", labelFont,
            XBrushes.Black, new XPoint(56, lineY));
        lineY += 8;
        notesGfx.DrawLine(XPens.LightGray, 56, lineY, 539, lineY);
        lineY += 16;

        foreach ((string Capability, string State) row in table)
        {
            notesGfx.DrawString(row.Capability, body, XBrushes.Black, new XPoint(56, lineY));
            notesGfx.DrawString(row.State, mono, XBrushes.DimGray, new XPoint(290, lineY));
            lineY += 16;
        }

        lineY += 14;
        notesGfx.DrawString(
            "One trap worth knowing: PdfDocument.AcroForm casts whatever /AcroForm holds to",
            body, XBrushes.Black, new XPoint(56, lineY));
        lineY += 14;
        notesGfx.DrawString(
            "PdfAcroForm. A form built as a plain dictionary - as this one is - is only typed on the",
            body, XBrushes.Black, new XPoint(56, lineY));
        lineY += 14;
        notesGfx.DrawString(
            "way back in, when PdfReader transforms it. Reading it from the live document does not.",
            body, XBrushes.Black, new XPoint(56, lineY));
        #endregion

        return document;
    }
}
