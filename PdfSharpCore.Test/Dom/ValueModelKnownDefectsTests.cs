using System;
using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Internals;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Dom;

/// <summary>
///   Two defects in the value descriptor layer, found while building the parity harness for the
///   move to a generated value model. Both are pre-existing, both are reachable through public API,
///   and both are pinned here as-is so that whichever way they are resolved is a deliberate choice
///   with a failing test to mark it, rather than a silent change during the migration.
/// </summary>
public class ValueModelKnownDefectsTests
{
    /// <summary>
    ///   FormattedText carries [DV] on nine delegating properties, four of which are plain bool
    ///   (Bold, Italic, Superscript, Subscript) and one a plain enum (Underline). A non-nullable
    ///   value type is routed to ValueTypeDescriptor, whose SetNull casts to INullableValue without
    ///   checking - so SetNull throws for a member that does not implement it.
    ///
    ///   This is item 3 of docs/specs/dom-thread-safety.md, which described the unguarded cast as
    ///   reachable only by "the next value type that does not implement the interface". It is
    ///   reachable now.
    /// </summary>
    [Fact]
    public void FormattedTextSetNullNoLongerThrows()
    {
        var formattedText = new FormattedText { Bold = true };

        var setNull = () => formattedText.SetNull();

        setNull.Should().NotThrow(
            "ValueKind.PlainValue does nothing for a member with no null, instead of casting it to "
            + "INullableValue and throwing");

        // Bold is not reset by its own descriptor - a plain bool has no null to write - but the
        // font descriptor next to it resets the Font the property reads through, so it clears
        // anyway. That is why the no-op costs nothing here.
        formattedText.Bold.Should().BeFalse("the Font this property delegates to was reset");
    }

    /// <summary>
    ///   The same members are fine to read - only SetNull is broken - which is why nothing has
    ///   noticed. Serialization never calls SetNull on a whole object.
    /// </summary>
    [Fact]
    public void FormattedTextIsStillReadable()
    {
        var formattedText = new FormattedText { Bold = true };

        formattedText.IsNull().Should().BeFalse();
        formattedText.IsNull("Bold").Should().BeFalse();
        formattedText.GetValue("Bold").Should().Be(true);
    }

    /// <summary>
    ///   DocumentObjectDescriptor.IsNull calls val.IsNull() on its property branch, discards the
    ///   result, and returns true unconditionally. Style.Font is the only [DV] property in the DOM
    ///   whose type is a DocumentObject, so it is the only member the branch applies to.
    ///
    ///   The blast radius is smaller than it looks, which is why nothing has noticed: Meta.IsNull
    ///   (dom, name) does not use it - for a DocumentObject member it calls GetValue and asks the
    ///   object itself - so style.IsNull("Font") answers correctly. Only the whole-object
    ///   Meta.IsNull(dom) sweep calls the descriptor, where Style's separately tracked
    ///   paragraphFormat field masks the wrong answer.
    ///
    ///   So this is a latent defect rather than a live one. Pinned at the descriptor, which is the
    ///   only place it is observable.
    /// </summary>
    [Fact]
    public void ADocumentObjectPropertyDescriptorAlwaysReportsNull()
    {
        // A user-defined style, not Styles[0] - the built-in styles are read-only, and their
        // ParagraphFormat getter hands back a clone, so assignments to them go nowhere.
        var document = new Document();
        Style style = document.Styles.AddStyle("Probe", "Normal");
        style.Font.Bold = true;
        style.Font.Name = "Times New Roman";

        ValueDescriptor font = Meta.GetMeta(style)["Font"];

        font.IsNull(style).Should().BeTrue(
            "the property branch discards the answer it computes and returns true");
        style.Font.IsNull().Should().BeFalse("though the font plainly has values");

        // The route callers actually take is unaffected, which is what keeps this latent.
        style.IsNull("Font").Should().BeFalse("Meta.IsNull(dom, name) asks the object, not the descriptor");
    }
}
