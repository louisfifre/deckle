using Xunit;

namespace Deckle.App.Tests;

// The hand-off must fire only for the current\ folder of a local copy with a
// complete staged build, and never for a checkout, a worktree or an install:
// a false positive would make those builds exit at every start.
[Trait("Category", "integration")]
public sealed class LocalCopyUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Deckle-LocalCopyTests-" + Guid.NewGuid().ToString("N")[..8]);

    public LocalCopyUpdateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_staged_build_beside_current_is_pending()
    {
        string current = Stage(LocalCopyUpdate.CurrentFolderName);
        Stage(LocalCopyUpdate.NextFolderName);
        File.WriteAllText(Path.Combine(_root, LocalCopyUpdate.ApplyScriptName), "");

        Assert.Equal(_root, LocalCopyUpdate.FindPendingRoot(current + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Nothing_is_pending_without_a_staged_build()
    {
        string current = Stage(LocalCopyUpdate.CurrentFolderName);
        File.WriteAllText(Path.Combine(_root, LocalCopyUpdate.ApplyScriptName), "");

        Assert.Null(LocalCopyUpdate.FindPendingRoot(current));
    }

    [Fact]
    public void Nothing_is_pending_without_the_helper()
    {
        string current = Stage(LocalCopyUpdate.CurrentFolderName);
        Stage(LocalCopyUpdate.NextFolderName);

        Assert.Null(LocalCopyUpdate.FindPendingRoot(current));
    }

    [Fact]
    public void A_build_output_folder_is_never_a_local_copy()
    {
        string release = Stage("release");
        Stage(LocalCopyUpdate.NextFolderName);
        File.WriteAllText(Path.Combine(_root, LocalCopyUpdate.ApplyScriptName), "");

        Assert.Null(LocalCopyUpdate.FindPendingRoot(release));
    }

    [Fact]
    public void A_next_folder_still_being_written_is_not_pending()
    {
        string current = Stage(LocalCopyUpdate.CurrentFolderName);
        Directory.CreateDirectory(Path.Combine(_root, LocalCopyUpdate.NextFolderName));
        File.WriteAllText(Path.Combine(_root, LocalCopyUpdate.ApplyScriptName), "");

        Assert.Null(LocalCopyUpdate.FindPendingRoot(current));
    }

    private string Stage(string folder)
    {
        string path = Path.Combine(_root, folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "Deckle.exe"), "");
        return path;
    }
}
