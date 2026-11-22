using Xunit;

namespace Deckle.Install.Tests;

public sealed class FolderSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deckle-foldersafety-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [Trait("Category", "regression")]
    [InlineData(@"C:\Tools", @"C:\Tools\Deckle")]
    [InlineData(@"C:\Tools\", @"C:\Tools\Deckle")]
    [InlineData(@"C:\Tools\Deckle", @"C:\Tools\Deckle")]
    [InlineData(@"C:\Tools\deckle\", @"C:\Tools\deckle")]
    [InlineData(@"C:\", @"C:\Deckle")]
    public void Picking_a_shared_folder_installs_into_its_own_Deckle_folder(string picked, string expected)
    {
        Assert.Equal(expected, FolderSafety.OwnFolderUnder(picked));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void A_writable_folder_passes_the_probe_and_leaves_nothing_behind()
    {
        string folder = Path.Combine(_root, "new");

        Assert.True(FolderSafety.CanWrite(folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void A_folder_that_cannot_be_created_fails_the_probe()
    {
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, "file.txt");
        File.WriteAllText(file, "x");

        Assert.False(FolderSafety.CanWrite(Path.Combine(file, "below")));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void Removing_a_shared_data_root_deletes_only_what_Deckle_owns()
    {
        string root = Path.Combine(_root, "Tools");
        Directory.CreateDirectory(Path.Combine(root, "models"));
        File.WriteAllText(Path.Combine(root, "models", "m.bin"), "m");
        File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "other"));
        File.WriteAllText(Path.Combine(root, "other", "keep.txt"), "k");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "k");

        bool gone = FolderSafety.RemoveDataRoot(root);

        Assert.False(gone);
        Assert.False(Directory.Exists(Path.Combine(root, "models")));
        Assert.False(File.Exists(Path.Combine(root, "settings.json")));
        Assert.True(File.Exists(Path.Combine(root, "other", "keep.txt")));
        Assert.True(File.Exists(Path.Combine(root, "notes.txt")));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void Removing_a_data_root_holding_only_Deckle_files_removes_the_root_too()
    {
        string root = Path.Combine(_root, "Tools");
        Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
        File.WriteAllText(Path.Combine(root, "secrets.dat"), "s");

        Assert.True(FolderSafety.RemoveDataRoot(root));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void A_data_root_named_Deckle_is_removed_whole()
    {
        string root = Path.Combine(_root, "Deckle");
        Directory.CreateDirectory(Path.Combine(root, "unknown"));
        File.WriteAllText(Path.Combine(root, "unknown", "f.bin"), "f");

        Assert.True(FolderSafety.RemoveDataRoot(root));
        Assert.False(Directory.Exists(root));
    }
}
