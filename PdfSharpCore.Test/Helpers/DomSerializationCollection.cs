using Xunit;

namespace PdfSharpCore.Test.Helpers;

/// <summary>
/// The collection every test that writes or reads DDL belongs to.
/// </summary>
/// <remarks>
/// Each DocumentObject caches its reflection metadata in a static field, built the first time
/// anything asks for it:
///
///     internal override Meta Meta
///     {
///       get
///       {
///         if (meta == null)
///           meta = new Meta(typeof(Document));
///         return meta;
///       }
///     }
///
/// The assignment publishes the Meta before its value descriptors have finished being collected,
/// so a second thread arriving mid-construction can read one that is still filling and find a
/// value it should have found. Serializing a document then silently leaves that attribute out.
///
/// This is a defect in the DOM rather than in the tests - the same race is reachable from any two
/// threads that serialize different documents at once, and it predates these tests. Until it is
/// fixed, tests in one collection do not run alongside one another, which keeps the suite honest
/// about everything else.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DomSerializationCollection
{
    public const string Name = "DomSerialization";
}
