using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// Integration: the store writes tray-snapshot.json in a temporary folder of
// its own, deleted after each test.
[Trait("Category", "integration")]
public sealed class TraySnapshotStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "Deckle.Shell.Taskbar.Tests", "snapshot-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "tray-snapshot.json");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private static Dictionary<string, uint?> Icons(params (string Subkey, uint? Value)[] icons) =>
        icons.ToDictionary(icon => icon.Subkey, icon => icon.Value);

    [Fact]
    public void WhatIsRecordedIsReadBackByTheNextProcess()
    {
        new TraySnapshotStore(FilePath).Record(Icons(("1234", 0), ("5678", null), ("9", 1)));

        var reopened = new TraySnapshotStore(FilePath);

        Assert.False(reopened.IsEmpty);
        Assert.Equal(Icons(("1234", 0), ("5678", null), ("9", 1)), reopened.Entries().ToDictionary(icon => icon.Key, icon => icon.Value));
    }

    // An icon keyed by a subkey name is the same icon whatever the case the
    // registry hands back.
    [Fact]
    public void SubkeysMatchWhateverTheirCase()
    {
        var store = new TraySnapshotStore(FilePath);
        store.Record(Icons(("Icon", 0)));

        Assert.True(store.Entries().ContainsKey("ICON"));
        Assert.True(new TraySnapshotStore(FilePath).Entries().ContainsKey("icon"));
    }

    [Fact]
    public void RecordingNothingWritesNoFile()
    {
        var store = new TraySnapshotStore(FilePath);

        store.Record(Icons());

        Assert.True(store.IsEmpty);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ClearForgetsTheRecordAndDeletesTheFile()
    {
        var store = new TraySnapshotStore(FilePath);
        store.Record(Icons(("1", 0)));

        store.Clear();

        Assert.True(store.IsEmpty);
        Assert.False(File.Exists(FilePath));
        Assert.True(new TraySnapshotStore(FilePath).IsEmpty);
    }

    [Fact]
    public void ClearingAnEmptyStoreIsHarmless()
    {
        new TraySnapshotStore(FilePath).Clear();
    }

    // A crash between the temporary file and the move leaves the previous file
    // whole; the temporary file never stands in for it.
    [Fact]
    public void ASaveLeavesNoTemporaryFileBehind()
    {
        new TraySnapshotStore(FilePath).Record(Icons(("1", 0)));

        Assert.Equal([FilePath], Directory.GetFiles(_folder));
    }

    [Fact]
    public void AMissingOrUnreadableFileIsAnEmptySnapshot()
    {
        Assert.True(new TraySnapshotStore(FilePath).IsEmpty);

        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");

        Assert.True(new TraySnapshotStore(FilePath).IsEmpty);
    }

    // The first record after an unreadable load replaced the file, and with it
    // the only trace of what Windows held before Deckle.
    [Fact]
    [Trait("Category", "regression")]
    public void AnUnreadableFileIsKeptAsideBeforeTheNextSaveReplacesIt()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");

        var store = new TraySnapshotStore(FilePath);
        store.Record(Icons(("1", 0)));

        Assert.Equal("{ not json", File.ReadAllText(TraySnapshotStore.BackupPathOf(FilePath)));
        Assert.False(new TraySnapshotStore(FilePath).IsEmpty);
    }
}