using Xunit;

namespace PdfSharpCore.Test.Helpers;

/// <summary>
/// The collection every test that writes or reads DDL belongs to.
/// </summary>
/// <remarks>
/// Serializing a document reaches Color.ToString, which builds its table of standard colour names
/// once into a static and assigns that static while the table is still empty:
///
///     if (stdColors == null)
///     {
///       ...
///       stdColors = new Hashtable(count);   // published empty
///       for (int index = 0; index &lt; count; index++)
///         if (!stdColors.ContainsKey(d))
///           stdColors.Add(d, c);            // then filled
///     }
///
/// A second thread arriving after the assignment and before the loop finishes reads a table that
/// is still filling, and one arriving before the assignment races the ContainsKey/Add pair. Calling
/// Colors.Black.ToString() from 64 threads at once returns four different answers: "Black",
/// "RGB(0,0,0)", an empty string, and ArgumentException "Item has already been added".
///
/// This is a defect in the DOM rather than in the tests. It predates them, it is reachable from any
/// two threads that serialize documents at once, and Color.ToString is public. Until it is fixed,
/// tests in one collection do not run alongside one another, which keeps the suite honest about
/// everything else. docs/specs/dom-thread-safety.md covers the fix.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DomSerializationCollection
{
    public const string Name = "DomSerialization";
}
