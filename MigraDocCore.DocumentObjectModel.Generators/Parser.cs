using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigraDocCore.DocumentObjectModel.Generators.Model;

namespace MigraDocCore.DocumentObjectModel.Generators;

/// <summary>
/// Turns symbols into models. The only Roslyn-aware part of the generator beyond the entry point.
/// </summary>
internal static class Parser
{
    public const string DvAttribute = "MigraDocCore.DocumentObjectModel.Internals.DVAttribute";
    const string DocumentObject = "MigraDocCore.DocumentObjectModel.DocumentObject";
    const string DocumentObjectCollection = "MigraDocCore.DocumentObjectModel.DocumentObjectCollection";
    const string NullableValue = "MigraDocCore.DocumentObjectModel.Internals.INullableValue";

    static readonly SymbolDisplayFormat Fqn = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>
    /// A [DV] member, or null with a diagnostic if it is one the model cannot describe.
    /// </summary>
    /// <remarks>
    /// The diagnostic comes back as a <see cref="DiagnosticInfo"/> rather than a
    /// <see cref="Diagnostic"/> because this return value goes into the pipeline, and a Diagnostic
    /// carries a Location, which carries the SyntaxTree it came from.
    /// </remarks>
    public static (ParsedMember? Member, DiagnosticInfo? Error) Parse(GeneratorAttributeSyntaxContext context, int order)
    {
        ISymbol symbol = context.TargetSymbol;
        INamedTypeSymbol? owner = symbol.ContainingType;
        if (owner is null)
            return (null, null);

        string ownerFqn = owner.ToDisplayString(Fqn);
        LocationInfo? location = LocationInfo.From(symbol.Locations.FirstOrDefault());

        if (symbol.IsStatic)
        {
            return (null, DiagnosticInfo.Create(Diagnostics.NotAnInstanceMember, location,
                owner.Name, symbol.Name, "static"));
        }

        if (symbol.DeclaredAccessibility == Accessibility.Private)
        {
            return (null, DiagnosticInfo.Create(Diagnostics.NotAnInstanceMember, location,
                owner.Name, symbol.Name, "private"));
        }

        if (!DerivesFrom(owner, DocumentObject))
        {
            return (null, DiagnosticInfo.Create(Diagnostics.NotADocumentObject, location,
                owner.Name, symbol.Name));
        }

        if (!IsPartial(owner))
        {
            return (null, DiagnosticInfo.Create(Diagnostics.NotPartial, location, owner.Name));
        }

        ITypeSymbol memberType;
        bool isField;
        bool isWritable;
        switch (symbol)
        {
            case IFieldSymbol field:
                memberType = field.Type;
                isField = true;
                isWritable = !field.IsReadOnly;
                break;
            case IPropertySymbol property:
                memberType = property.Type;
                isField = false;
                isWritable = property.SetMethod is not null;
                break;
            default:
                return (null, null);
        }

        (MemberKind kind, ITypeSymbol valueType)? classified = Classify(memberType);
        if (classified is null)
        {
            return (null, DiagnosticInfo.Create(Diagnostics.UnsupportedMemberType, location,
                owner.Name, symbol.Name, memberType.ToDisplayString()));
        }

        (MemberKind kind, ITypeSymbol valueTypeSymbol) = classified.Value;
        bool isRefOnly = ReadRefOnly(context.Attributes);

        if (isRefOnly && memberType.IsValueType)
        {
            return (null, DiagnosticInfo.Create(Diagnostics.RefOnlyOnValueType, location,
                owner.Name, symbol.Name));
        }

        // A DocumentObject member is only assignable through the model when it is a field. The
        // descriptor class this replaces threw "This value cannot be set." for every property.
        bool settable = kind is MemberKind.DocumentObject or MemberKind.Collection
            ? isField
            : isWritable;

        var member = new DomMemberModel(
            Name: symbol.Name,
            DeclaringTypeFqn: ownerFqn,
            MemberTypeFqn: memberType.ToDisplayString(Fqn),
            ValueTypeFqn: valueTypeSymbol.ToDisplayString(Fqn),
            Kind: kind,
            IsRefOnly: isRefOnly,
            IsField: isField,
            IsWritable: settable,
            CanConstruct: kind is MemberKind.DocumentObject or MemberKind.Collection
                          && HasAccessibleParameterlessConstructor(memberType),
            IsEnum: valueTypeSymbol.TypeKind == TypeKind.Enum,
            BoxedDefaultExpression: kind == MemberKind.Leaf
                ? BoxedDefault(valueTypeSymbol)
                : null);

        return (new ParsedMember(member, ownerFqn, order, location), null);
    }

    /// <summary>
    /// A DocumentObject class declaration, or null if the syntax node is not one.
    /// </summary>
    public static ParsedType? ParseType(GeneratorSyntaxContext context)
    {
        if (context.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)context.Node) is not INamedTypeSymbol symbol)
            return null;
        if (!DerivesFrom(symbol, DocumentObject))
            return null;

        return new ParsedType(
            Fqn: symbol.ToDisplayString(Fqn),
            Namespace: symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
            Name: symbol.Name,
            BaseFqn: symbol.BaseType?.ToDisplayString(Fqn),
            IsAbstract: symbol.IsAbstract,
            IsPartial: IsPartial(symbol));
    }

    /// <summary>
    /// The same order of tests the reflection-built model applied, with its single value-type
    /// branch split in two: a struct that implements INullableValue tracks its own null, a plain
    /// bool or enum does not.
    /// </summary>
    static (MemberKind, ITypeSymbol)? Classify(ITypeSymbol type)
    {
        // Nullable<T> first - it is a struct, so the value-type test below would swallow it.
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return (MemberKind.Leaf, nullable.TypeArguments[0]);

        if (type.SpecialType == SpecialType.System_String)
            return (MemberKind.Leaf, type);

        if (type.IsValueType)
        {
            return type.AllInterfaces.Any(i => i.ToDisplayString(Fqn) == "global::" + NullableValue)
                ? (MemberKind.NullableValue, type)
                : (MemberKind.PlainValue, type);
        }

        if (DerivesFrom(type, DocumentObjectCollection))
            return (MemberKind.Collection, type);

        if (DerivesFrom(type, DocumentObject))
            return (MemberKind.DocumentObject, type);

        return null;
    }

    /// <summary>
    /// What GetValue hands back for a Leaf that was never set. The model has always answered with
    /// the type's default rather than with null unless GV.GetNull was asked for, and with "" rather
    /// than null for a string.
    /// </summary>
    static string BoxedDefault(ITypeSymbol valueType) =>
        valueType.SpecialType == SpecialType.System_String
            ? "\"\""
            : $"default({valueType.ToDisplayString(Fqn)})";

    static bool ReadRefOnly(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (AttributeData attribute in attributes)
        {
            foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
            {
                if (named.Key == "RefOnly" && named.Value.Value is bool value)
                    return value;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether the member's type can be constructed on demand, for GV.ReadWrite auto-creation.
    /// DocumentObject.parent is the case that matters: it is typed as the abstract base, so it has
    /// constructors but none that can be called.
    /// </summary>
    static bool HasAccessibleParameterlessConstructor(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsAbstract: false } named
        && named.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0
            && c.DeclaredAccessibility is Accessibility.Public
                or Accessibility.Internal
                or Accessibility.ProtectedOrInternal);

    static bool DerivesFrom(ITypeSymbol type, string baseFqn)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString(Fqn) == "global::" + baseFqn)
                return true;
        }
        return false;
    }

    static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(d => d.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));

    /// <summary>
    /// Groups members by declaring type and closes the inheritance chain: a type's value model
    /// contains its own [DV] members and every one its bases declare.
    /// </summary>
    /// <remarks>
    /// This is why the pipeline has a Collect() barrier. A member cannot know what derives from the
    /// type that declares it, so the base chain can only be closed once every member is in hand.
    /// The models are value-equatable, so an edit that changes no [DV] member still produces
    /// identical output and the emit stage's cache holds.
    /// </remarks>
    public static IEnumerable<DomTypeModel> GroupByTypeClosingInheritance(
        IEnumerable<ParsedType> types,
        IEnumerable<ParsedMember> members,
        SourceProductionContext context)
    {
        var byType = members
            .GroupBy(m => m.TypeFqn)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.DeclarationOrder).ToList());

        var declarations = new Dictionary<string, ParsedType>(System.StringComparer.Ordinal);
        foreach (ParsedType type in types)
            declarations[type.Fqn] = type;

        foreach (ParsedType type in declarations.Values.OrderBy(t => t.Fqn, System.StringComparer.Ordinal))
        {
            // An abstract class needs no table of its own - it cannot be instantiated, and its
            // members are picked up by every concrete type below it.
            if (type.IsAbstract)
                continue;

            // Base first, so the order is deterministic. Reflection's own order never was.
            var chain = new List<string>();
            for (string? t = type.Fqn; t is not null; )
            {
                chain.Insert(0, t);
                t = declarations.TryGetValue(t, out ParsedType? found) ? found.BaseFqn : null;
            }

            var collected = new List<DomMemberModel>();
            var seen = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string owner in chain)
            {
                if (!byType.TryGetValue(owner, out List<ParsedMember>? declared))
                    continue;
                foreach (ParsedMember member in declared)
                {
                    if (seen.ContainsKey(member.Member.Name))
                    {
                        // Points at the later of the two declarations - the base chain is walked
                        // base first, so that is the one the collision is attributable to. Raising
                        // a real Diagnostic here rather than a DiagnosticInfo is fine: grouping
                        // runs inside the source-output stage, downstream of every cache.
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.DuplicateValueName, member.Location?.ToLocation(),
                            type.Name, member.Member.Name));
                        continue;
                    }
                    seen.Add(member.Member.Name, owner);
                    collected.Add(member.Member);
                }
            }

            yield return new DomTypeModel(
                Namespace: type.Namespace,
                Name: type.Name,
                HintName: type.Fqn.Replace("global::", "").Replace('.', '_'),
                Members: new EquatableArray<DomMemberModel>(collected));
        }
    }
}
