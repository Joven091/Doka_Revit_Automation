using System.Runtime.CompilerServices;

// The reader harness links the production adapter and supplies test-only Revit
// API stand-ins. Production assemblies do not include this test visibility.
[assembly: InternalsVisibleTo("Revit.Reader.Tests")]
