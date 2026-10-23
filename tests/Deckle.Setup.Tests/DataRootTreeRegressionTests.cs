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

    // The caller retries once and then throws; the cause of the refusal used to
    // be known only to a Verbose line, so the Error that stops the move said
    // which file but never why. The message now names the error type and code
    // (not the OS text, which repeats an absolute path).
    [Fact]
    public void A_required_file_that_cannot_be_copied_names_the_cause_in_the_error()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(source);
        string locked = Path.Combine(source, "settings.json");
        File.WriteAllText(locked, "settings");

        // Held open without sharing: File.Copy cannot read it.
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var tree = new DataRootTree();

        IOException error = Assert.Throws<IOException>(() => tree.Copy(
            source, target, totalBytes: 8, progress: null, CancellationToken.None));

        Assert.Contains("settings.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IOException), error.Message, StringComparison.Ordinal);
        Assert.Contains("0x", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
