using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Internals;
using MigraDocCore.DocumentObjectModel.Shapes;
using Xunit;

namespace PdfSharpCore.Test.Dom;

/// <summary>
///   NInt marks "not set" by storing int.MinValue and NDouble by storing double.NaN, so those two
///   values cannot be stored as data - assigning either one silently means "unset" instead, and
///   the value read back is zero.
///
///   These tests pin that defect as it stands. They are expected to be inverted when the two types
///   move to int? and double?, which have a separate bit for "has a value" and so give the whole
///   range of each type back to the caller.
/// </summary>
public class SentinelCollisionTests
{
    static PageSetup APageSetup() => new Document().AddSection().PageSetup;

    static Image AnImage() => new();

    [Fact]
    public void AnIntegerSetToTheSentinelIsMistakenForUnset()
    {
        var pageSetup = APageSetup();

        pageSetup.StartingNumber = int.MinValue;

        pageSetup.IsNull("StartingNumber").Should().BeTrue("int.MinValue is NInt's marker for unset");
        pageSetup.StartingNumber.Should().Be(0, "the assigned value is lost");
        pageSetup.GetValue("StartingNumber", GV.GetNull).Should().BeNull();
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
