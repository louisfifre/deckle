using Xunit;

namespace Deckle.Setup.Tests;

[Trait("Category", "regression")]
public sealed class DataRootTreeRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deckle-relocate-{Guid.NewGuid():N}");

    [Fact]
    public void An_entry_that_vanishes_while_the_tree_is_walked_does_not_stop_the_copy()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(source);
        foreach (string name in new[] { "a.txt", "b.txt", "c.txt" })
            File.WriteAllText(Path.Combine(source, name), name);

        // The live app removes files while the copy runs. The directory listing
        // is already read when a.txt is copied, so b.txt and c.txt are listed
        // but gone by the time their attributes are read.
        var tree = new DataRootTree((from, to) =>
        {
            File.Copy(from, to, overwrite: false);
            if (from.EndsWith("a.txt", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(Path.Combine(source, "b.txt"));
                File.Delete(Path.Combine(source, "c.txt"));
            }
            return true;
        });

        DataRootCopyResult result = tree.Copy(
            source, target, totalBytes: 15, progress: null, CancellationToken.None);

        Assert.Equal(1, result.Files);
        Assert.True(File.Exists(Path.Combine(target, "a.txt")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
