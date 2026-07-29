using System.Reflection;
namespace MigraDocCore.Rendering.UnitTest
{
  /// <summary>
  /// Summary description for ValueDumper.
  /// </summary>
  internal class ValueDumper
  {
    // The trimmer cannot know which fields survive for the runtime type of obj, and the
    // requirement cannot be declared on the parameter because DynamicallyAccessedMembers only
    // applies to Type and String. This is a debug-only dump helper with no live callers, so it
    // never runs on a trimmed path; if it is ever put back into use, the types it dumps need
    // their non-public fields preserved. The attribute only exists on .NET 5+, and netstandard2.1
    // is not trim analysed, so it is omitted there.
#if NET5_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075",
      Justification = "Debug-only dump helper with no live callers; not reachable on a trimmed path.")]
#endif
    internal static string DumpValues(object obj)
    {
      string dumpString = "[" + obj.GetType() + "]\r\n";
      foreach (FieldInfo fieldInfo in obj.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
      {
        if (fieldInfo.FieldType.GetTypeInfo().IsValueType)
        {
          dumpString += "  " + fieldInfo.Name + " = " + fieldInfo.GetValue(obj) + "\r\n";
        }
      }
      return dumpString;

    }
  }
}
