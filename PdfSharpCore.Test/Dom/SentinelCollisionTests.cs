using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Internals;
using MigraDocCore.DocumentObjectModel.IO;
using MigraDocCore.DocumentObjectModel.Shapes;
using PdfSharpCore.Test.Helpers;
using Xunit;

namespace PdfSharpCore.Test.Dom;

/// <summary>
///   NInt marked "not set" by storing int.MinValue and NDouble marks it by storing double.NaN, so
///   those values could not be stored as data - assigning one silently meant "unset" instead, and
///   the value read back was zero.
///
///   int? has a separate bit for "has a value" and so hands the whole range of the type back to the
///   caller. The integer tests below are the inverted form, pinning the fix. The double test still
///   pins the defect, and inverts when NDouble moves.
/// </summary>
[Collection(DomSerializationCollection.Name)]
public class SentinelCollisionTests
{
    static PageSetup APageSetup() => new Document().AddSection().PageSetup;

    static Image AnImage() => new();

    [Fact]
    public void AnIntegerSetToTheFormerSentinelIsKept()
    {
        var pageSetup = APageSetup();

        pageSetup.StartingNumber = int.MinValue;

        pageSetup.IsNull("StartingNumber").Should().BeFalse("int? tracks null separately from the value");
        pageSetup.StartingNumber.Should().Be(int.MinValue, "the assigned value is no longer lost");
        pageSetup.GetValue("StartingNumber", GV.GetNull).Should().Be(int.MinValue);
    }

    [Fact]
    public void AnIntegerSetToTheFormerSentinelSurvivesTheDdlRoundTrip()
    {
        var document = new Document();
        document.AddSection().PageSetup.StartingNumber = int.MinValue;

        var reread = DdlReader.DocumentFromString(DdlWriter.WriteToString(document));

        reread.LastSection.PageSetup.StartingNumber.Should().Be(int.MinValue);
    }

    [Fact]
    public void ADoubleSetToTheSentinelIsMistakenForUnset()
    {
        var image = AnImage();

        image.ScaleWidth = double.NaN;

        image.IsNull("ScaleWidth").Should().BeTrue("double.NaN is NDouble's marker for unset");
        image.ScaleWidth.Should().Be(0, "the assigned value is lost");
        image.GetValue("ScaleWidth", GV.GetNull).Should().BeNull();
    }

    [Fact]
    public void EveryOtherExtremeOfTheRangeSurvives()
    {
        var pageSetup = APageSetup();
        var image = AnImage();

        pageSetup.StartingNumber = int.MinValue + 1;
        image.ScaleWidth = double.MinValue;

        pageSetup.StartingNumber.Should().Be(int.MinValue + 1);
        image.ScaleWidth.Should().Be(double.MinValue);
    }
}
