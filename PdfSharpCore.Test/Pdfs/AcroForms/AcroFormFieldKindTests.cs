using System;
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
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   A box with no appearance dictionary has no name for its ticked state, so
    ///   <c>GetNonOffValue</c> returns null and the setter hands that straight to
    ///   <c>SetName</c>, which refuses it. The caller asked to tick a box and gets an
    ///   ArgumentNullException naming a parameter called <c>value</c> that they never passed. A
    ///   form built by hand, or one whose appearances were stripped, is exactly this case.
    /// </remarks>
    [Fact]
    public void TickingABoxThatHasNoAppearanceThrowsAboutTheWrongThing()
    {
        var field = (PdfCheckBoxField)FormWith("/Btn", "agree").AcroForm.Fields["agree"];

        var act = () => field.Checked = true;

        act.Should().Throw<ArgumentNullException>().WithParameterName("value");
        field.Checked.Should().BeFalse();
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

        // What it writes is the defect below.
        field.Value.Should().BeOfType<PdfName>();
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
        field.Elements.GetInteger("/I").Should().Be(1, "a viewer reads /I rather than searching /Opt");
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

    [Fact]
    public void AChoiceFieldWithNoOptionsFindsNothing()
    {
        var field = (PdfListBoxField)FormWith("/Ch", "county").AcroForm.Fields["county"];

        field.SelectedIndex.Should().Be(-1);
    }

    // ----- known defects: option text carries its delimiters -------------------------------------

    /// <summary>
    ///   A known defect, pinned so that fixing it is visible rather than silent.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The choice and radio fields compare and copy their options with <c>ToString()</c> rather
    ///   than with the string's value. <c>PdfString.ToString()</c> writes the string as it appears
    ///   in the file - in parentheses - so "Sussex" is handled throughout as "(Sussex)".
    ///   </para>
    ///   <para>
    ///   Two consequences, in <c>PdfChoiceField.ValueInOptArray</c>,
    ///   <c>PdfChoiceField.IndexInOptArray</c> and
    ///   <c>PdfRadioButtonField.SelectedIndex</c>'s setter:
    ///   </para>
    ///   <list type="bullet">
    ///     <item>Choosing an option writes the parentheses into <c>/V</c>, so the file says the
    ///     user picked a value no viewer will match against the option list. A radio group gets it
    ///     worse: its value is written as a <em>name</em>, so <c>/V</c> becomes the malformed
    ///     <c>/(medium)</c>.</item>
    ///     <item>Reading a form somebody else wrote gives -1 for every choice field, because their
    ///     <c>/V</c> holds "Sussex" and this compares it against "(Sussex)".</item>
    ///   </list>
    ///   <para>
    ///   Setting and then reading in the same session agrees with itself, which is why the tests
    ///   above pass and this went unnoticed.
    ///   </para>
    /// </remarks>
    [Fact]
    public void ChoosingAnOptionWritesItsDelimitersIntoTheValue()
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

        listBox.Value.Should().BeOfType<PdfString>().Which.Value.Should().Be("(Sussex)");
        radio.Value.Should().BeOfType<PdfName>().Which.ToString().Should().Be("/(medium)");
    }

    [Fact]
    public void AChoiceFieldCannotFindWhatAnotherProducerChose()
    {
        // The same form as above, except that /V was already set - as every real form that has
        // been filled in has - to the option text without delimiters.
        var document = new AcroFormBuilder().With("/Ch", "county", field =>
        {
            AcroFormBuilder.WithOptions(field, "Kent", "Sussex");
            field.Elements.SetString(PdfAcroField.Keys.V, "Sussex");
        }).Build();

        var field = (PdfListBoxField)document.AcroForm.Fields["county"];

        field.SelectedIndex.Should().Be(-1, "which is what 'nothing is selected' looks like");
    }

    [Fact]
    public void AnOptionGivenAsAnExportAndDisplayPairCannotBeFoundEither()
    {
        // /Opt may hold [exportValue displayText] pairs rather than plain strings, and the export
        // value is what /V is meant to match. It is compared with its delimiters on, so it never
        // does.
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

        listBox.SelectedIndex.Should().Be(-1);
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
