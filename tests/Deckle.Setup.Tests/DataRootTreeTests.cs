using Deckle.Setup;
using Xunit;

namespace Deckle.Setup.Tests;

[Trait("Category", "component")]
public sealed class DataRootTreeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deckle-relocate-{Guid.NewGuid():N}");

    [Fact]
    public void CopiesARepresentativeTreeAndShedsOnlyDiagnosticsFailures()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        Write(source, "settings.json", "settings");
        Write(source, Path.Combine("models", "model.bin"), "model");
        Write(source, Path.Combine("diagnostics", "app.jsonl"), "live log");

        var tree = new DataRootTree((from, to) =>
        {
            if (from.EndsWith("app.jsonl", StringComparison.OrdinalIgnoreCase)) return false;
            File.Copy(from, to, overwrite: false);
            return true;
        });

        DataRootCopyResult result = tree.Copy(
            source, target, totalBytes: 13, progress: null, CancellationToken.None);

        Assert.Equal(2, result.Files);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal("settings", File.ReadAllText(Path.Combine(target, "settings.json")));
        Assert.Equal("model", File.ReadAllText(Path.Combine(target, "models", "model.bin")));
        Assert.False(File.Exists(Path.Combine(target, "diagnostics", "app.jsonl")));
    }

    [Fact]
    public void RequiredFileFailureStopsTheCopy()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        Write(source, "settings.json", "settings");
        var tree = new DataRootTree((_, _) => false);

        IOException error = Assert.Throws<IOException>(() => tree.Copy(
            source, target, totalBytes: 8, progress: null, CancellationToken.None));

        Assert.Contains("settings.json", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"diagnostics\app.jsonl", true)]
    [InlineData(@"Diagnostics\live\app.jsonl", true)]
    [InlineData(@"models\diagnostics.bin", false)]
    [InlineData("diagnostics.jsonl", false)]
    public void OnlyTheDiagnosticsTreeIsSheddable(string relativePath, bool expected)
    {
        Assert.Equal(expected, DataRootTree.IsSheddable(relativePath));
    }

    [Fact]
    public void RollbackRemovesOwnedArtifactsButSparesConcurrentContent()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        Write(source, Path.Combine("models", "model.bin"), "model");
        var tree = new DataRootTree();
        tree.Copy(source, target, totalBytes: 5, progress: null, CancellationToken.None);
        Write(target, "foreign.txt", "foreign");

        tree.RollBack(target, source);

        Assert.False(File.Exists(Path.Combine(target, "models", "model.bin")));
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(target, "foreign.txt")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
