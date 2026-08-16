using System;
using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.AcroForms;
using Xunit;

namespace PdfSharpCore.Test.Pdfs.AcroForms;

/// <summary>
///   What each kind of field adds to <see cref="PdfAcroField"/>: a text field its text, a tick box
///   whether it is ticked, a choice field which option is chosen. Between them these are what a
///   caller filling in somebody else's form actually touches.
///   <para>
///   The values a field holds are not stored the way its property reads them. A tick box holds the
///   <em>name</em> of an appearance state and calls everything that is not <c>/Off</c> ticked; a
///   choice field holds the option text and finds its index by searching <c>/Opt</c>. So the
///   properties are doing real work rather than forwarding, and the round trips below are what
///   say the work is right.
///   </para>
/// </summary>
public class AcroFormFieldKindTests
{
    static PdfDocument FormWith(string fieldType, string name, Action<PdfDictionary> describe = null) =>
        new AcroFormBuilder().With(fieldType, name, describe).Build();

    // ----- text ----------------------------------------------------------------------------------

    [Fact]
    public void ATextFieldHoldsTheTextItIsGiven()
    {
        var document = FormWith("/Tx", "surname");
        var field = (PdfTextField)document.AcroForm.Fields["surname"];

        field.Text = "Bosch";

        field.Text.Should().Be("Bosch");
        field.Value.Should().BeOfType<PdfString>().Which.Value.Should().Be("Bosch");
    }

    [Fact]
    public void ATextFieldStartsEmptyRatherThanNull()
    {
        ((PdfTextField)FormWith("/Tx", "surname").AcroForm.Fields["surname"]).Text.Should().BeEmpty();
    }

    [Fact]
    public void ATextFieldRemembersHowLongItsTextMayBe()
    {
        var field = (PdfTextField)FormWith("/Tx", "surname").AcroForm.Fields["surname"];

        field.MaxLength.Should().Be(0, "no limit is written as no entry");

        field.MaxLength = 12;
        field.MaxLength.Should().Be(12);
    }

    [Fact]
    public void ATextFieldsAppearanceIsItsToChoose()
    {
        var field = (PdfTextField)FormWith("/Tx", "surname").AcroForm.Fields["surname"];

        field.Font.Should().NotBeNull("a field draws its own text, so it needs a font before anyone sets one");
        field.ForeColor.Should().Be(XColors.Black);
        field.BackColor.Should().Be(XColor.Empty, "no background at all rather than a white one");

        field.Font = new XFont("Arial", 14);
        field.ForeColor = XColors.Red;
        field.BackColor = XColors.LightGray;

        field.Font.Size.Should().Be(14);
        field.ForeColor.Should().Be(XColors.Red);
        field.BackColor.Should().Be(XColors.LightGray);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATextFieldCanBeTurnedIntoAMultiLineOneAndBack(bool multiLine)
    {
        var field = (PdfTextField)FormWith("/Tx", "surname").AcroForm.Fields["surname"];

        field.MultiLine = multiLine;

        field.MultiLine.Should().Be(multiLine);
        ((field.Flags & PdfAcroFieldFlags.Multiline) != 0).Should().Be(multiLine);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATextFieldCanBeTurnedIntoAPasswordFieldAndBack(bool password)
    {
        var field = (PdfTextField)FormWith("/Tx", "surname").AcroForm.Fields["surname"];

        field.Password = password;

        field.Password.Should().Be(password);
        ((field.Flags & PdfAcroFieldFlags.Password) != 0).Should().Be(password);
    }

    // ----- tick boxes ----------------------------------------------------------------------------

    [Fact]
    public void ATickBoxIsNotTickedUntilItIs()
    {
        var field = (PdfCheckBoxField)FormWith("/Btn", "agree",
            f => AcroFormBuilder.WithOnAndOffAppearances(f)).AcroForm.Fields["agree"];

        field.Checked.Should().BeFalse("a box with no value set is not ticked");
    }

    [Fact]
    public void TickingABoxSetsItToTheStateItsAppearanceCallsOn()
    {
        // The on state is whatever the appearance dictionary names that is not /Off - it is /Yes
        // by convention and /On, /1 or something in the author's own language in practice, so the
        // field has to look it up rather than assume.
        var field = (PdfCheckBoxField)FormWith("/Btn", "agree",
            f => AcroFormBuilder.WithOnAndOffAppearances(f, "/Ja")).AcroForm.Fields["agree"];

        field.Checked = true;

        field.Checked.Should().BeTrue();
        field.Value.Should().BeOfType<PdfName>().Which.ToString().Should().Be("/Ja");
        field.Elements.GetName("/AS").Should().Be("/Ja", "the appearance has to follow the value");
    }

    [Fact]
    public void UntickingABoxSetsItToOff()
    {
        var field = (PdfCheckBoxField)FormWith("/Btn", "agree",
            f => AcroFormBuilder.WithOnAndOffAppearances(f)).AcroForm.Fields["agree"];

        field.Checked = true;
        field.Checked = false;

        field.Checked.Should().BeFalse();
        field.Value.Should().BeOfType<PdfName>().Which.ToString().Should().Be("/Off");
        field.Elements.GetName("/AS").Should().Be("/Off");
    }

    /// <summary>
    ///   A box with no appearance dictionary names no state at all, so <c>GetNonOffValue</c> used
    ///   to return null and the setter handed that straight to <c>SetName</c>, which refuses it:
    ///   the caller asked to tick a box and got an ArgumentNullException naming a parameter called
    ///   <c>value</c> that they never passed. A form built by hand, or one whose appearances were
    ///   stripped, is exactly this case, so the lookup falls back to the conventional on state.
    /// </summary>
    [Fact]
    public void TickingABoxThatHasNoAppearanceFallsBackToTheConventionalOnState()
    {
        var field = (PdfCheckBoxField)FormWith("/Btn", "agree").AcroForm.Fields["agree"];

        field.Checked = true;

        field.Checked.Should().BeTrue();
        field.Value.Should().BeOfType<PdfName>().Which.ToString().Should().Be("/Yes");

        field.Checked = false;

        field.Checked.Should().BeFalse();
        field.Value.Should().BeOfType<PdfName>().Which.ToString().Should().Be("/Off");
    }

    // ----- radio groups --------------------------------------------------------------------------

    [Fact]
    public void ARadioGroupNamesItsButtonsInAnOptionArray()
    {
        var field = (PdfRadioButtonField)FormWith("/Btn", "size", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Radio);
            AcroFormBuilder.WithOptions(f, "small", "medium", "large");
        }).AcroForm.Fields["size"];

        field.SelectedIndex.Should().Be(-1, "nothing is chosen to begin with");

        field.SelectedIndex = 1;

        // A radio group records its choice as a name - the export value of the button that is on -
        // rather than as the text string the option array holds it as.
        field.Value.Should().BeOfType<PdfName>().Which.ToString().Should().Be("/medium");
        field.SelectedIndex.Should().Be(1, "it finds again what it wrote");
    }

    [Fact]
    public void ARadioGroupRefusesAButtonItDoesNotHave()
    {
        var field = (PdfRadioButtonField)FormWith("/Btn", "size", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Radio);
            AcroFormBuilder.WithOptions(f, "small", "large");
        }).AcroForm.Fields["size"];

        var tooHigh = () => field.SelectedIndex = 2;
        var negative = () => field.SelectedIndex = -1;

        tooHigh.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ARadioGroupWithNoOptionsAtAllQuietlyIgnoresBeingSet()
    {
        var field = (PdfRadioButtonField)FormWith("/Btn", "size",
            f => AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Radio)).AcroForm.Fields["size"];

        var act = () => field.SelectedIndex = 0;

        act.Should().NotThrow("there is no array to bound-check against");
        field.SelectedIndex.Should().Be(-1);
    }

    // ----- choice fields -------------------------------------------------------------------------

    [Fact]
    public void AListBoxChoosesByIndexAndRemembersTheTextOfWhatWasChosen()
    {
        var field = (PdfListBoxField)FormWith("/Ch", "county",
            f => AcroFormBuilder.WithOptions(f, "Kent", "Sussex", "Surrey")).AcroForm.Fields["county"];

        field.SelectedIndex.Should().Be(-1);

        field.SelectedIndex = 2;

        field.SelectedIndex.Should().Be(2, "it finds again what it wrote");
    }

    [Fact]
    public void AComboBoxAlsoRecordsTheIndexSeparatelySoTheViewerFollowsIt()
    {
        var field = (PdfComboBoxField)FormWith("/Ch", "county", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo);
            AcroFormBuilder.WithOptions(f, "Kent", "Sussex", "Surrey");
        }).AcroForm.Fields["county"];

        field.SelectedIndex = 1;

        field.SelectedIndex.Should().Be(1);
        SelectedIndicesOf(field).Should().Equal(new[] { 1 },
            "a viewer reads /I rather than searching /Opt");
    }

    [Fact]
    public void AChoiceFieldRefusesAnIndexItHasNoOptionFor()
    {
        var field = (PdfListBoxField)FormWith("/Ch", "county",
            f => AcroFormBuilder.WithOptions(f, "Kent", "Sussex")).AcroForm.Fields["county"];

        var tooHigh = () => field.SelectedIndex = 5;
        var negative = () => field.SelectedIndex = -1;

        tooHigh.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///   <c>/I</c> was written as a bare integer, which the specification does not allow and which
    ///   <see cref="PdfChoiceField.Keys.I"/> does not claim either - its key metadata declares
    ///   <c>KeyType.Array</c>, so the code and the declaration beside it disagreed. A reader that
    ///   takes the key at its word gets an array and finds a number.
    /// </summary>
    [Fact]
    public void TheIndexOfTheChosenOptionIsWrittenAsAnArray()
    {
        var field = (PdfComboBoxField)FormWith("/Ch", "county", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo);
            AcroFormBuilder.WithOptions(f, "Kent", "Sussex", "Surrey");
        }).AcroForm.Fields["county"];

        field.SelectedIndex = 2;

        field.Elements[PdfChoiceField.Keys.I].Should().BeOfType<PdfArray>()
            .Which.Elements.Count.Should().Be(1, "a combo box offers a single choice");
        SelectedIndicesOf(field).Should().Equal(new[] { 2 });
    }

    /// <summary>
    ///   The shape in the file is what matters, an integer and an array of one being different
    ///   bytes, so this one goes all the way out to a document and back rather than reading the
    ///   dictionary it was just written into.
    /// </summary>
    [Fact]
    public void TheIndexIsStillAnArrayAfterARoundTrip()
    {
        var document = FormWith("/Ch", "county", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo);
            AcroFormBuilder.WithOptions(f, "Kent", "Sussex", "Surrey");
        });
        ((PdfComboBoxField)document.AcroForm.Fields["county"]).SelectedIndex = 2;

        using var written = new MemoryStream();
        document.Save(written, false);
        written.Position = 0;
        // Fully qualified: this test assembly has a PdfReader of its own.
        var reopened = PdfSharpCore.Pdf.IO.PdfReader.Open(written, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Modify);

        var field = (PdfComboBoxField)reopened.AcroForm.Fields["county"];
        field.Elements[PdfChoiceField.Keys.I].Should().BeOfType<PdfArray>();
        SelectedIndicesOf(field).Should().Equal(new[] { 2 });
        field.SelectedIndex.Should().Be(2, "/V and /I still agree");
    }

    /// <summary>
    ///   Choosing twice replaces the entry rather than growing it, so a field cannot end up
    ///   claiming two options are chosen at once.
    /// </summary>
    [Fact]
    public void ChoosingASecondOptionReplacesTheIndexRatherThanAddingToIt()
    {
        var field = (PdfComboBoxField)FormWith("/Ch", "county", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo);
            AcroFormBuilder.WithOptions(f, "Kent", "Sussex", "Surrey");
        }).AcroForm.Fields["county"];

        field.SelectedIndex = 0;
        field.SelectedIndex = 2;

        SelectedIndicesOf(field).Should().Equal(new[] { 2 });
    }

    [Fact]
    public void AComboBoxIgnoresBeingSetToNothingChosen()
    {
        // Minus one means nothing chosen, and the setter has an explicit arm to leave the field
        // alone rather than search for an option at index -1.
        var field = (PdfComboBoxField)FormWith("/Ch", "county", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo);
            AcroFormBuilder.WithOptions(f, "Kent", "Sussex");
        }).AcroForm.Fields["county"];

        field.SelectedIndex = 0;
        var act = () => field.SelectedIndex = -1;

        act.Should().NotThrow();
        field.SelectedIndex.Should().Be(0, "the field keeps what it had");
    }

    // ----- a combo box taking text it does not offer ----------------------------------------------

    /// <summary>
    ///   An editable combo box accepts text that is none of its options, so the field has to record
    ///   that text <em>as</em> an option: otherwise <c>/V</c> names nothing in <c>/Opt</c> and
    ///   <c>/I</c> has no index to point at.
    ///   <para>
    ///   The append used to reach into the field dictionary by position - the third value in it,
    ///   whatever that happened to be - and cast it to an array. Here the third entry is
    ///   <c>/FT</c>, a name, so the cast threw and an empty catch swallowed it: the text never
    ///   reached <c>/Opt</c> and <c>/I</c> stayed where it was. Worse, in a dictionary whose third
    ///   entry <em>was</em> an array the text was appended to that array instead, silently.
    ///   </para>
    /// </summary>
    [Fact]
    public void AComboBoxGivenTextItDoesNotOfferAddsItToTheOptions()
    {
        var field = (PdfComboBoxField)FormWith("/Ch", "county", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo | PdfAcroFieldFlags.Edit);
            AcroFormBuilder.WithOptions(f, "Kent", "Sussex");
        }).AcroForm.Fields["county"];

        field.Value = new PdfString("Middlesex");

        OptionsOf(field).Should().Equal("Kent", "Sussex", "Middlesex");
        field.SelectedIndex.Should().Be(2);
        SelectedIndicesOf(field).Should().Equal(new[] { 2 },
            "a viewer reads /I rather than searching /Opt");
    }

    [Fact]
    public void AComboBoxGivenAValueItAlreadyOffersLeavesTheOptionsAlone()
    {
        var field = (PdfComboBoxField)FormWith("/Ch", "county", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo);
            AcroFormBuilder.WithOptions(f, "Kent", "Sussex");
        }).AcroForm.Fields["county"];

        field.Value = new PdfString("Sussex");

        OptionsOf(field).Should().Equal(new[] { "Kent", "Sussex" },
            "the option was already on offer, so nothing needed adding");
        field.SelectedIndex.Should().Be(1);
    }

    /// <summary>
    ///   <c>/Opt</c> is optional, so a combo box may have none at all. The value still has to land
    ///   somewhere, which means making the array rather than assuming one.
    /// </summary>
    [Fact]
    public void AComboBoxWithNoOptionsAtAllIsGivenAnOptionArray()
    {
        var field = (PdfComboBoxField)FormWith("/Ch", "county",
            f => AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo | PdfAcroFieldFlags.Edit))
            .AcroForm.Fields["county"];

        field.Value = new PdfString("Middlesex");

        OptionsOf(field).Should().Equal("Middlesex");
        field.SelectedIndex.Should().Be(0);
    }

    /// <summary>
    ///   A choice field's value is a text string, and so is every entry of its <c>/Opt</c> array.
    ///   A caller may still hand it a name, which is taken to mean the text the name stands for -
    ///   the same reading <see cref="PdfRadioButtonField"/> gives its own <c>/V</c>, where the
    ///   slash that makes a name a name is not part of the value. Stored as a name it would be
    ///   invisible to the search through <c>/Opt</c>, so <c>/I</c> would never be pointed at it.
    /// </summary>
    [Fact]
    public void AComboBoxGivenANameStoresTheTextItStandsFor()
    {
        var field = (PdfComboBoxField)FormWith("/Ch", "county", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo | PdfAcroFieldFlags.Edit);
            AcroFormBuilder.WithOptions(f, "Kent", "Sussex");
        }).AcroForm.Fields["county"];

        field.Value = new PdfName("/Middlesex");

        OptionsOf(field).Should().Equal(new[] { "Kent", "Sussex", "Middlesex" });
        field.Value.Should().BeOfType<PdfString>().Which.Value.Should().Be("Middlesex");
        field.SelectedIndex.Should().Be(2);
        SelectedIndicesOf(field).Should().Equal(new[] { 2 });
    }

    [Fact]
    public void AComboBoxGivenANameForAnOptionItAlreadyOffersChoosesThatOption()
    {
        var field = (PdfComboBoxField)FormWith("/Ch", "county", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo);
            AcroFormBuilder.WithOptions(f, "Kent", "Sussex");
        }).AcroForm.Fields["county"];

        field.Value = new PdfName("/Sussex");

        OptionsOf(field).Should().Equal(new[] { "Kent", "Sussex" },
            "the option was already on offer, so nothing needed adding");
        field.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void AComboBoxRefusesAValueThatIsNotText()
    {
        var field = (PdfComboBoxField)FormWith("/Ch", "county",
            f => AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Combo)).AcroForm.Fields["county"];

        // Fully qualified: this test assembly has a PdfInteger of its own.
        var act = () => field.Value = new PdfSharpCore.Pdf.PdfInteger(3);

        act.Should().Throw<NotImplementedException>();
    }

    /// <summary>
    ///   The indices in <c>/I</c>. It is an array - of the options selected, sorted ascending -
    ///   which is what both the specification and this field's own key metadata say, so reading it
    ///   as one is what says it was written as one.
    /// </summary>
    static List<int> SelectedIndicesOf(PdfAcroField field)
    {
        var indices = new List<int>();
        var entry = field.Elements.GetArray(PdfChoiceField.Keys.I);
        if (entry != null)
            foreach (var item in entry.Elements)
                indices.Add(((PdfSharpCore.Pdf.PdfInteger)item).Value);
        return indices;
    }

    static List<string> OptionsOf(PdfAcroField field)
    {
        var options = new List<string>();
        var opt = field.Elements.GetArray("/Opt");
        if (opt != null)
            foreach (var item in opt.Elements)
                options.Add(((PdfString)item).Value);
        return options;
    }

    [Fact]
    public void AChoiceFieldWithNoOptionsFindsNothing()
    {
        var field = (PdfListBoxField)FormWith("/Ch", "county").AcroForm.Fields["county"];

        field.SelectedIndex.Should().Be(-1);
    }

    // ----- option text against the value that names it -------------------------------------------

    /// <summary>
    ///   The choice and radio fields used to compare and copy their options with
    ///   <c>ToString()</c> rather than with the string's value. <c>PdfString.ToString()</c> writes
    ///   the string as it appears in the file - in parentheses - so "Sussex" was handled
    ///   throughout as "(Sussex)": choosing an option wrote the parentheses into <c>/V</c>, and a
    ///   radio group had it worse still, its value being written as a <em>name</em> so that
    ///   <c>/V</c> became the malformed <c>/(medium)</c>. Setting and then reading in the same
    ///   session agreed with itself, which is why it went unnoticed.
    /// </summary>
    [Fact]
    public void ChoosingAnOptionWritesTheOptionTextAndNothingElse()
    {
        var listBox = (PdfListBoxField)FormWith("/Ch", "county",
            f => AcroFormBuilder.WithOptions(f, "Kent", "Sussex")).AcroForm.Fields["county"];
        var radio = (PdfRadioButtonField)FormWith("/Btn", "size", f =>
        {
            AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Radio);
            AcroFormBuilder.WithOptions(f, "small", "medium");
        }).AcroForm.Fields["size"];

        listBox.SelectedIndex = 1;
        radio.SelectedIndex = 1;

        listBox.Value.Should().BeOfType<PdfString>().Which.Value.Should().Be("Sussex");
        radio.Value.Should().BeOfType<PdfName>().Which.ToString().Should().Be("/medium");
    }

    [Fact]
    public void AChoiceFieldFindsWhatAnotherProducerChose()
    {
        // The same form as above, except that /V was already set - as every real form that has
        // been filled in has - to the option text without delimiters.
        var document = new AcroFormBuilder().With("/Ch", "county", field =>
        {
            AcroFormBuilder.WithOptions(field, "Kent", "Sussex");
            field.Elements.SetString(PdfAcroField.Keys.V, "Sussex");
        }).Build();

        var field = (PdfListBoxField)document.AcroForm.Fields["county"];

        field.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void AnOptionGivenAsAnExportAndDisplayPairIsFoundByItsExportValue()
    {
        // /Opt may hold [exportValue displayText] pairs rather than plain strings, and the export
        // value is what /V is meant to match.
        var document = new AcroFormBuilder().With("/Ch", "county", field =>
        {
            var opt = new PdfArray(field.Owner);
            var pair = new PdfArray(field.Owner);
            pair.Elements.Add(new PdfString("KEN"));
            pair.Elements.Add(new PdfString("Kent"));
            opt.Elements.Add(pair);
            field.Elements["/Opt"] = opt;
        }).Build();
        var listBox = (PdfListBoxField)document.AcroForm.Fields["county"];

        listBox.Value = new PdfString("KEN");

        listBox.SelectedIndex.Should().Be(0);
    }

    // ----- the field types that hold nothing of their own ----------------------------------------

    [Fact]
    public void APushButtonAndASignatureAndAnUnknownFieldAreStillFields()
    {
        // None of the three adds a value of its own - a push button does something rather than
        // holding something, a signature's value is a dictionary PDFsharp does not model, and a
        // generic field is whatever PDFsharp could not identify. All three still carry a name and
        // flags, which is what the tree-walking code needs of them.
        var document = new AcroFormBuilder()
            .With("/Btn", "print", f => AcroFormBuilder.WithFlags(f, PdfAcroFieldFlags.Pushbutton))
            .With("/Sig", "signature")
            .With("/Nonsense", "mystery")
            .Build();

        foreach (var name in new[] { "print", "signature", "mystery" })
        {
            var field = document.AcroForm.Fields[name];
            field.Name.Should().Be(name);
            field.HasKids.Should().BeFalse();
            field.ReadOnly.Should().BeFalse();
        }

        document.AcroForm.Fields.DescendantNames
            .Should().BeEquivalentTo(new[] { "print", "signature", "mystery" });
    }
}
