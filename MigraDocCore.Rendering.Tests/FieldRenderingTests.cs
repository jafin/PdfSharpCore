using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.Rendering.Tests.Helpers;
using Xunit;

namespace MigraDocCore.Rendering.Tests;

/// <summary>
///   Fields are written into the document as placeholders and are only worth anything once the
///   renderer has replaced them with what they stand for, in the format each of them names.
/// </summary>
/// <remarks>
///   Promoted from MigraDoc 1.32's TestParagraphRenderer.Fields, which put one of every field on a
///   page and saved it. A field that renders as nothing at all, or as its own name, looks like a
///   blank on that page and like a passing run to anything that only asks whether it threw - so
///   each assertion here says what the field is expected to read.
/// </remarks>
public class FieldRenderingTests
{
    [Fact]
    public void APageFieldAskedForRomanNumeralsRendersTheNumberAsOne()
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.AddText("Page: ");
        paragraph.AddPageField().Format = "ROMAN";
        paragraph.AddLineBreak();
        paragraph.AddText("NumPages: ");
        paragraph.AddNumPagesField();

        // A single page, so the page number is one and the count of them is one - which in roman
        // numerals and in arabic respectively is "I" and "1". Both come from the same field
        // machinery and only the format tells them apart.
        Glyphs.On(Rendered.FirstPageOf(document))
            .Should().Equal(Glyphs.For("Page: I", "NumPages: 1"));
    }

    [Fact]
    public void ASectionFieldAskedForLettersRendersTheSectionAsA()
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.AddText("Section: ");
        paragraph.AddSectionField().Format = "ALPHABETIC";

        Glyphs.On(Rendered.FirstPageOf(document))
            .Should().Equal(Glyphs.For("Section: A"));
    }

    [Fact]
    public void ABookmarkAndTheReferenceToItRenderThePageItIsOn()
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.AddText("Bookmark: ");
        paragraph.AddBookmark("Egal");
        paragraph.AddLineBreak();
        paragraph.AddText("PageRef: ");
        paragraph.AddPageRefField("Egal");

        // The bookmark itself draws nothing - it marks a place - and the reference to it resolves
        // to the page that place is on, which here is the first. A reference that never resolves
        // renders as a blank and takes the whole line's meaning with it.
        Glyphs.On(Rendered.FirstPageOf(document))
            .Should().Equal(Glyphs.For("Bookmark: ", "PageRef: 1"));
    }
}
