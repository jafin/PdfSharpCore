using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.IO;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Dom;

/// <summary>
///   Border.Clear marks a border as cleared, which is not the same as leaving it unset: a cleared
///   border writes "Top = null" into the DDL so that it overrides whatever it would otherwise
///   inherit, while an unset one writes nothing at all.
///
///   The flag behind it, Border.fClear, is a plain bool rather than one of the DOM's nullable
///   values - it carries no [DV] attribute, so the reflection layer never sees it, and the only
///   assignment anywhere sets it true. These tests pin that two-state behaviour.
/// </summary>
[Collection(DomSerializationCollection.Name)]
public class BorderClearedTests
{
    static Borders ABordersObject() =>
        new Document().AddSection().AddParagraph("Hello").Format.Borders;

    [Fact]
    public void ABorderStartsOutNotCleared()
    {
        ABordersObject().Top.BorderCleared.Should().BeFalse();
    }

    [Fact]
    public void ClearingABorderMarksIt()
    {
        var borders = ABordersObject();

        borders.Top.Clear();

        borders.Top.BorderCleared.Should().BeTrue();
    }

    [Fact]
    public void ClearingOneBorderLeavesTheOthersAlone()
    {
        var borders = ABordersObject();

        borders.Top.Clear();

        borders.Bottom.BorderCleared.Should().BeFalse();
        borders.Left.BorderCleared.Should().BeFalse();
        borders.Right.BorderCleared.Should().BeFalse();
    }

    [Fact]
    public void AClearedBorderIsWrittenAsNullWhenItAlsoCarriesAValue()
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph("Hello");

        paragraph.Format.Borders.Top.Width = 1;
        paragraph.Format.Borders.Top.Clear();

        DdlWriter.WriteToString(document).Should().Contain("Top = null");
    }

    /// <summary>
    ///   A border that has only been cleared is not written at all, so Clear() does nothing unless
    ///   the border also carries a value.
    ///
    ///   Borders.Serialize asks !IsNull("Top") before serializing each border (Borders.cs:426), and
    ///   that question goes through the reflection layer, which answers from the border's value
    ///   descriptors - Visible, Style, Width and Color. fClear carries no [DV] attribute, so it is
    ///   not among them and cannot make the border look non-null.
    ///
    ///   This is a defect rather than a decision: the documented purpose of Clear() is to write
    ///   'Border = null' into the DDL, and on its own it does not. Pinned here as it stands;
    ///   docs/specs/dom-thread-safety.md item 8 covers the fix.
    /// </summary>
    [Fact]
    public void AClearedBorderCarryingNothingElseIsNotWrittenAtAll()
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph("Hello");

        paragraph.Format.Borders.Top.Clear();

        paragraph.Format.Borders.Top.BorderCleared.Should().BeTrue("the flag itself is set");
        DdlWriter.WriteToString(document).Should().NotContain("Top = null", "but nothing writes it");
    }

    [Fact]
    public void ABorderThatWasNeverClearedIsNotWrittenAsNull()
    {
        var document = new Document();
        document.AddSection().AddParagraph("Hello");

        DdlWriter.WriteToString(document).Should().NotContain("Top = null");
    }

    [Fact]
    public void ClearingSurvivesACloneOfTheBorder()
    {
        var borders = ABordersObject();
        borders.Top.Clear();

        var clone = borders.Top.Clone();

        clone.BorderCleared.Should().BeTrue("Clone is a MemberwiseClone, which copies the flag");
    }
}
