namespace Deckle.Atelier.Tests;

// A temporary folder of the test's own, removed when the test ends: the
// "temporary file system" the integration category stands on.
internal sealed class ScratchFolder : IDisposable
{
    public string Path { get; } = Directory.CreateDirectory(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deckle-atelier-tests", Guid.NewGuid().ToString("N"))).FullName;

    public string Folder(params string[] parts)
    {
        string path = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    public string File(string content, params string[] parts)
    {
        string path = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
