using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace MigraDocCore.DocumentObjectModel.Generators.Tests;

/// <summary>
/// MDG001 to MDG006 each replaced a condition that used to fail at run time or not at all. Until
/// now none had a test proving it fires, which made them decorative - MDG002 in particular, since
/// it replaced a Debug.Assert(false) that is a no-op in Release.
/// </summary>
public class DiagnosticTests
{
    const string Ns = "using MigraDocCore.DocumentObjectModel;\nusing MigraDocCore.DocumentObjectModel.Internals;\nnamespace Probe;\n";

    [Fact]
    public void AValidTypeProducesATableAndNoDiagnostics()
    {
        var result = GeneratorHarness.Run(Ns + """
            public partial class Widget : DocumentObject
            {
                [DV] internal bool? visible;
                [DV] internal string caption;
                [DV] internal Unit width;
            }
            """);

        result.Ids.Should().BeEmpty();
        result.AllGenerated.Should().Contain("partial class Widget");
        result.AllGenerated.Should().Contain("\"visible\"").And.Contain("\"caption\"").And.Contain("\"width\"");
        result.AllGenerated.Should().Contain("\"parent\"", "inherited [DV] members are included");
    }

    [Fact]
    public void TheGeneratedTableCompiles()
    {
        var result = GeneratorHarness.Run(Ns + """
            public partial class Widget : DocumentObject
            {
                [DV] internal bool? visible;
            }
            """);

        result.CompilationErrors.Should().BeEmpty(
            "the emitted source must bind, not merely be produced");
    }

    [Fact]
    public void EachKindIsClassifiedAsTheModelExpects()
    {
        var result = GeneratorHarness.Run(Ns + """
            public partial class Child : DocumentObject { }
            public partial class Bag : DocumentObjectCollection { }
            public enum Shade { None, Some }

            public partial class Widget : DocumentObject
            {
                [DV] internal bool? nullableValueType;
                [DV] internal string text;
                [DV] internal Shade? nullableEnum;
                [DV] internal Unit tracksItsOwnNull;
                [DV] public bool PlainBool { get; set; }
                [DV] internal Child child;
                [DV] internal Bag bag;
            }
            """);

        result.Ids.Should().BeEmpty();
        string generated = result.AllGenerated;

        generated.Should().Contain("valueName: \"nullableValueType\"").And.Contain("ValueKind.Leaf");
        generated.Should().Contain("valueName: \"tracksItsOwnNull\"");
        generated.Should().Contain("ValueKind.NullableValue", "Unit implements INullableValue");
        generated.Should().Contain("ValueKind.PlainValue", "a plain bool has no null of its own");
        generated.Should().Contain("ValueKind.DocumentObject");
        generated.Should().Contain("ValueKind.Collection");
    }

    [Fact]
    public void MDG001_TypeWithDvMembersMustBePartial()
    {
        var result = GeneratorHarness.Run(Ns + """
            public class Widget : DocumentObject
            {
                [DV] internal bool? visible;
            }
            """);

        result.Ids.Should().Contain("MDG001");
    }

    [Fact]
    public void MDG002_MemberTypeTheModelCannotDescribe()
    {
        // Replaces Debug.Assert(false, type.FullName) followed by a null descriptor - nothing at
        // all in Release, and a NullReferenceException in the DDL parser much later.
        var result = GeneratorHarness.Run(Ns + """
            public partial class Widget : DocumentObject
            {
                [DV] internal System.Collections.Generic.List<int> unsupported;
            }
            """);

        result.Ids.Should().Contain("MDG002");
    }

    [Fact]
    public void MDG003_StaticMember()
    {
        var result = GeneratorHarness.Run(Ns + """
            public partial class Widget : DocumentObject
            {
                [DV] internal static bool? visible;
            }
            """);

        result.Ids.Should().Contain("MDG003");
    }

    [Fact]
    public void MDG003_PrivateMember()
    {
        var result = GeneratorHarness.Run(Ns + """
            public partial class Widget : DocumentObject
            {
                [DV] private bool? visible;
            }
            """);

        result.Ids.Should().Contain("MDG003");
    }

    [Fact]
    public void MDG004_TwoMembersCollidingUnderCaseInsensitiveLookup()
    {
        // The name table is case-insensitive. Before the generator this threw from Hashtable.Add
        // at first use of the type, far from the declarations that caused it.
        var result = GeneratorHarness.Run(Ns + """
            public partial class Widget : DocumentObject
            {
                [DV] internal bool? caption;
                [DV] public bool? Caption { get; set; }
            }
            """);

        result.Ids.Should().Contain("MDG004");
    }

    [Fact]
    public void MDG005_DvOutsideADocumentObject()
    {
        var result = GeneratorHarness.Run(Ns + """
            public partial class NotADomType
            {
                [DV] internal bool? visible;
            }
            """);

        result.Ids.Should().Contain("MDG005");
    }

    [Fact]
    public void MDG006_RefOnlyOnAValueMember()
    {
        var result = GeneratorHarness.Run(Ns + """
            public partial class Widget : DocumentObject
            {
                [DV(RefOnly = true)] internal bool? visible;
            }
            """);

        result.Ids.Should().Contain("MDG006");
    }

    /// <summary>
    ///   A type declaring no [DV] member of its own still needs a table - it inherits parent, and
    ///   without one it fails to implement DocumentObject.Meta. Fifteen real DOM types are in this
    ///   position, and an attribute-driven provider alone cannot see them.
    /// </summary>
    [Fact]
    public void ATypeWithNoDvMembersOfItsOwnStillGetsATable()
    {
        var result = GeneratorHarness.Run(Ns + """
            public partial class Empty : DocumentObject { }
            """);

        result.Ids.Should().BeEmpty();
        result.AllGenerated.Should().Contain("partial class Empty");
        result.AllGenerated.Should().Contain("\"parent\"");
        result.CompilationErrors.Should().BeEmpty();
    }

    /// <summary>
    ///   An abstract class needs no table: it cannot be instantiated, and its members are picked up
    ///   by every concrete type below it.
    /// </summary>
    [Fact]
    public void AnAbstractTypeGetsNoTableButItsMembersAreInherited()
    {
        var result = GeneratorHarness.Run(Ns + """
            public abstract partial class Shape : DocumentObject
            {
                [DV] internal Unit width;
            }

            public partial class Box : Shape
            {
                [DV] internal bool? visible;
            }
            """);

        result.Ids.Should().BeEmpty();
        result.GeneratedSources.Should().NotContain(s => s.Contains("partial class Shape"));
        result.AllGenerated.Should().Contain("partial class Box");
        result.AllGenerated.Should().Contain("\"width\"", "Box inherits Shape's [DV] members");
        result.AllGenerated.Should().Contain("\"visible\"");
    }
}
