using System.Reflection;
using Xunit;

namespace Deckle.Orchestration.Tests;

// Reads a file of Fixtures\ embedded in this assembly, by file name.
internal static class Fixture
{
    public static string Read(string fileName)
    {
        Assembly assembly = typeof(Fixture).Assembly;
        string resource = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith("." + fileName, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
