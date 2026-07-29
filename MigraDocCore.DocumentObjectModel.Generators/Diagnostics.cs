using Microsoft.CodeAnalysis;

namespace MigraDocCore.DocumentObjectModel.Generators;

/// <summary>
/// Every one of these is a condition that, before the generator, either failed at run time or
/// failed silently.
/// </summary>
internal static class Diagnostics
{
    const string Category = "DomValueModel";

    /// <summary>The generated table is emitted into the declaring type, which must be partial.</summary>
    public static readonly DiagnosticDescriptor NotPartial = new(
        "MDG001",
        "Type with [DV] members must be partial",
        "'{0}' has [DV] members, so its value model is generated into it and it must be declared partial",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Replaces Debug.Assert(false, type.FullName) followed by a null descriptor - a no-op in
    /// Release, then a NullReferenceException somewhere in the DDL parser.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedMemberType = new(
        "MDG002",
        "[DV] member has a type the value model cannot describe",
        "'{0}.{1}' is of type '{2}', which is not a Nullable<T>, string, value type, DocumentObject or DocumentObjectCollection",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>Reflection scanned static members too; none carried [DV], and none should.</summary>
    public static readonly DiagnosticDescriptor NotAnInstanceMember = new(
        "MDG003",
        "[DV] is only meaningful on an instance member",
        "'{0}.{1}' is {2}, so it cannot be part of the value model",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>Replaces Hashtable.Add throwing on first use of the type, far from the cause.</summary>
    public static readonly DiagnosticDescriptor DuplicateValueName = new(
        "MDG004",
        "Two [DV] members share a name",
        "'{0}' has more than one [DV] member named '{1}' (lookup is case-insensitive), counting inherited members",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>Previously the member was simply never seen.</summary>
    public static readonly DiagnosticDescriptor NotADocumentObject = new(
        "MDG005",
        "[DV] is only meaningful inside a DocumentObject",
        "'{0}' does not derive from DocumentObject, so the [DV] on '{1}' has no effect",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>RefOnly excludes a member from recursive walks; on a value type it means nothing.</summary>
    public static readonly DiagnosticDescriptor RefOnlyOnValueType = new(
        "MDG006",
        "RefOnly has no meaning on a value member",
        "'{0}.{1}' is a value type, so [DV(RefOnly = true)] does nothing",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
