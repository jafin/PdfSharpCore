using System.Linq;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.AcroForms;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace PdfSharpCore.Test.Pdfs.AcroForms;

/// <summary>
///   A tick box that is drawn in two places at once. The field is one field with one value, and
///   its two children are the two widgets that draw it - the shape the setter's own comment calls
///   "fields that exist twice with the same name", and which it records as having taken two
///   working days to work out.
///   <para>
///   The scheme is not what it looks like. Both children are always given a value, and the state
///   of the box is which of the two carries the on state: ticked means child 0 is on and child 1
///   is off, unticked means the reverse. The getter reads child 0 alone. It is worth pinning
///   precisely, because half the setter is unreachable from the ordinary one-widget case and no
///   test covered any of it.
///   </para>
/// </summary>
public class AcroFormTwinCheckBoxTests
{
    /// <summary>The tick box, with two children each offering the same on state and /Off.</summary>
    static PdfCheckBoxField ATwinTickBox(string onState = "/Yes")
    {
        var document = new AcroFormBuilder()
            .WithTypedParent("/Btn", "agree",
                kid => AcroFormBuilder.WithOnAndOffAppearances(kid, onState),
                kid => AcroFormBuilder.WithOnAndOffAppearances(kid, onState))
            .Build();

        return (PdfCheckBoxField)document.AcroForm.Fields["agree"];
    }

    /// <summary>The value and appearance state of one of the field's children.</summary>
    static (string Value, string Appearance) ChildState(PdfAcroField field, int index)
    {
        var kid = (PdfDictionary)((PdfReference)field.Fields.Elements.Items[index]).Value;
        return (kid.Elements.GetName("/V"), kid.Elements.GetName("/AS"));
    }

    [Fact]
    public void AFieldDrawnTwiceIsOneFieldWithChildren()
    {
        var field = ATwinTickBox();

        field.HasKids.Should().BeTrue();
        field.Fields.Elements.Items.Length.Should().Be(2);
    }

    [Fact]
    public void ATwinTickBoxIsNotTickedUntilItIs()
    {
        ATwinTickBox().Checked.Should().BeFalse("neither child has a value yet");
    }

    [Fact]
    public void TickingATwinBoxPutsTheOnStateOnTheFirstChildAndOffOnTheSecond()
    {
        var field = ATwinTickBox("/Ja");

        field.Checked = true;

        field.Checked.Should().BeTrue();
        ChildState(field, 0).Should().Be(("/Ja", "/Ja"), "the first child carries the tick");
        ChildState(field, 1).Should().Be(("/Off", "/Off"), "and the second is turned off");
    }

    [Fact]
    public void UntickingATwinBoxPutsTheOnStateOnTheSecondChildInstead()
    {
        // Not what "unticked" suggests: the second child is set to its on state rather than to
        // /Off. Which of the two is on is how the pair records the answer, and the getter reads
        // the first alone - so a first child of /Off is an unticked box.
        var field = ATwinTickBox("/Ja");

        field.Checked = true;
        field.Checked = false;

        field.Checked.Should().BeFalse();
        ChildState(field, 0).Should().Be(("/Off", "/Off"));
        ChildState(field, 1).Should().Be(("/Ja", "/Ja"));
    }

    [Fact]
    public void ATwinBoxCanBeTickedAndUntickedRepeatedlyWithoutDrifting()
    {
        var field = ATwinTickBox();

        for (var round = 0; round < 3; round++)
        {
            field.Checked = true;
            field.Checked.Should().BeTrue("round {0}", round);
            field.Checked = false;
            field.Checked.Should().BeFalse("round {0}", round);
        }
    }

    [Fact]
    public void WhatWasSetSurvivesBeingWrittenOutAndReadBack()
    {
        // The answer lives in the children's dictionaries rather than the field's own, so it is
        // worth saying that it is really written and not merely held in memory.
        var document = new AcroFormBuilder()
            .WithTypedParent("/Btn", "agree",
                kid => AcroFormBuilder.WithOnAndOffAppearances(kid, "/Ja"),
                kid => AcroFormBuilder.WithOnAndOffAppearances(kid, "/Ja"))
            .Build();
        ((PdfCheckBoxField)document.AcroForm.Fields["agree"]).Checked = true;

        using var saved = new System.IO.MemoryStream();
        document.Save(saved, false);
        saved.Position = 0;
        var reread = PdfSharpCore.Pdf.IO.PdfReader.Open(saved, PdfDocumentOpenMode.Modify);

        var field = (PdfCheckBoxField)reread.AcroForm.Fields["agree"];
        field.Checked.Should().BeTrue();
        ChildState(field, 0).Should().Be(("/Ja", "/Ja"));
        ChildState(field, 1).Should().Be(("/Off", "/Off"));
    }

    /// <summary>
    ///   A field with children but not exactly two of them is left alone entirely - the setter
    ///   does nothing and the getter answers false whatever the children say. Recorded rather
    ///   than argued with: three widgets drawing one tick box is unusual, and doing nothing is at
    ///   least not doing the wrong thing.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void AFieldWithAnyNumberOfChildrenButTwoIsLeftAlone(int childCount)
    {
        var builder = new AcroFormBuilder();
        var describers = Enumerable.Range(0, childCount)
            .Select(_ => new System.Action<PdfDictionary>(
                kid => AcroFormBuilder.WithOnAndOffAppearances(kid)))
            .ToArray();
        var field = (PdfCheckBoxField)builder
            .WithTypedParent("/Btn", "agree", describers).Build()
            .AcroForm.Fields["agree"];

        field.Checked = true;

        field.Checked.Should().BeFalse("only a pair is handled");
        for (var index = 0; index < childCount; index++)
            ChildState(field, index).Should().Be(("", ""), "child {0} was not touched", index);
    }

    /// <summary>
    ///   A child with no appearance dictionary names no states, so there is nothing to set it to
    ///   and it is left as it was. The other child is still dealt with.
    /// </summary>
    [Fact]
    public void AChildWithNoAppearanceIsLeftAsItWas()
    {
        var field = (PdfCheckBoxField)new AcroFormBuilder()
            .WithTypedParent("/Btn", "agree",
                kid => AcroFormBuilder.WithOnAndOffAppearances(kid, "/Ja"),
                kid => { })
            .Build().AcroForm.Fields["agree"];

        field.Checked = true;

        ChildState(field, 0).Should().Be(("/Ja", "/Ja"));
        ChildState(field, 1).Should().Be(("", ""), "there was no state to give it");
    }
}
