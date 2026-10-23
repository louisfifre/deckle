using System.Collections.Concurrent;
using System.Text.Json;
using Xunit;

namespace Deckle.Core.Tests;

// The store follows its file: a write made by another process or by hand
// replaces the in-memory snapshot and raises Changed, while the store's own
// writes and half-written files leave it alone. The file system is the seam,
// so each test owns a temporary folder with one settings file in it.
public sealed class JsonSettingsStoreTests
{
    public sealed class Sample
    {
        public int Value { get; set; }
    }

    private static JsonSerializerOptions Options() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Generous: the watcher coalesces notifications, and a loaded machine
    // can delay the file system's own delivery well past that window.
    private static readonly TimeSpan FollowTimeout = TimeSpan.FromSeconds(5);

    // Long enough for a reload the store should NOT perform to have happened
    // if it were going to, measured against the store's own settle window.
    private static readonly TimeSpan QuietWindow =
        JsonSettingsStore<Sample>.ExternalWriteSettle * 4;

    private sealed class TempStore : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(), $"deckle-core-test-{Guid.NewGuid():N}");

        public string FilePath => Path.Combine(_folder, "settings.json");
        public JsonSettingsStore<Sample> Store { get; }
        public ManualResetEventSlim Changed { get; } = new();
        public ConcurrentQueue<string> Warnings { get; } = new();

        // initialText seeds the settings file before the store loads it, which
        // is how a hand-edited or damaged file reaches the store at startup.
        public TempStore(string? initialText = null)
        {
            Directory.CreateDirectory(_folder);
            if (initialText is not null) File.WriteAllText(FilePath, initialText);
            Store = new JsonSettingsStore<Sample>(
                FilePath, $"Deckle-Core-Test-{Guid.NewGuid():N}", Options(),
                logWarning: Warnings.Enqueue);
            Store.Changed += () => Changed.Set();
        }

        public void Dispose()
        {
            Store.Dispose();
            Changed.Dispose();
            try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    [Trait("Category", "integration")]
    public void AWriteFromOutsideReplacesCurrentAndRaisesChanged()
    {
        using var temp = new TempStore();

        File.WriteAllText(temp.FilePath, "{ \"value\": 7 }");

        Assert.True(
            temp.Changed.Wait(FollowTimeout, TestContext.Current.CancellationToken),
            "Changed was not raised after an external write");
        Assert.Equal(7, temp.Store.Current.Value);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void ItsOwnWriteKeepsTheInMemoryInstance()
    {
        using var temp = new TempStore();
        var before = temp.Store.Current;
        before.Value = 3;

        temp.Store.Flush();
        Thread.Sleep(QuietWindow);

        // A reload would have replaced the instance; the store's own write
        // must be recognised as such and leave it in place.
        Assert.Same(before, temp.Store.Current);
        Assert.Equal(3, temp.Store.Current.Value);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void AHalfWrittenFileIsIgnoredUntilAValidOneLands()
    {
        using var temp = new TempStore();
        var before = temp.Store.Current;

        File.WriteAllText(temp.FilePath, "{ \"value\": ");
        Thread.Sleep(QuietWindow);

        Assert.False(temp.Changed.IsSet, "Changed was raised on an invalid file");
        Assert.Same(before, temp.Store.Current);

        File.WriteAllText(temp.FilePath, "{ \"value\": 9 }");

        Assert.True(
            temp.Changed.Wait(FollowTimeout, TestContext.Current.CancellationToken),
            "Changed was not raised once the file became valid");
        Assert.Equal(9, temp.Store.Current.Value);
    }

    // A damaged file falls back to defaults, and the first save writes those
    // defaults over it. The person's text must survive that overwrite.
    [Fact]
    [Trait("Category", "regression")]
    public void AnUnreadableFileKeepsItsTextInABackupBeforeDefaultsOverwriteIt()
    {
        const string damaged = "{ not json";
        using var temp = new TempStore(damaged);

        Assert.Equal(0, temp.Store.Current.Value);
        temp.Store.Flush();

        Assert.Equal(damaged, File.ReadAllText(
            temp.FilePath + JsonSettingsStore<Sample>.BackupExtension));
        Assert.Equal(0, JsonSerializer.Deserialize<Sample>(
            File.ReadAllText(temp.FilePath), Options())!.Value);
        Assert.Contains(temp.Warnings, w => w.Contains("reason=parse_failed"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AReadableFileLeavesNoBackup()
    {
        using var temp = new TempStore("{ \"value\": 2 }");

        temp.Store.Flush();

        Assert.False(File.Exists(
            temp.FilePath + JsonSettingsStore<Sample>.BackupExtension));
        Assert.Equal(2, temp.Store.Current.Value);
    }

    // The debounce window is 300 ms; a change made just before the app closes
    // must reach the disk when the store is disposed.
    [Fact]
    [Trait("Category", "regression")]
    public void DisposingTheStoreWritesAPendingDebouncedSave()
    {
        using var temp = new TempStore();

        temp.Store.Replace(new Sample { Value = 42 });
        temp.Store.Dispose();

        Assert.Equal(42, JsonSerializer.Deserialize<Sample>(
            File.ReadAllText(temp.FilePath), Options())!.Value);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void ASaveAfterDisposeIsStillWritten()
    {
        using var temp = new TempStore();
        temp.Store.Dispose();

        temp.Store.Replace(new Sample { Value = 8 });

        Assert.Equal(8, JsonSerializer.Deserialize<Sample>(
            File.ReadAllText(temp.FilePath), Options())!.Value);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void DisposingWithNothingPendingLeavesTheFileAlone()
    {
        using var temp = new TempStore("{ \"value\": 5 }");
        string before = File.ReadAllText(temp.FilePath);

        temp.Store.Dispose();
        temp.Store.Dispose();

        Assert.Equal(before, File.ReadAllText(temp.FilePath));
    }

    // Changed is raised from the follow timer's thread, where a subscriber's
    // exception used to escape and end the process; the store must survive it
    // and keep following.
    [Fact]
    [Trait("Category", "integration")]
    [Trait("Category", "regression")]
    public void AThrowingChangedHandlerDoesNotStopTheStoreFollowing()
    {
        using var temp = new TempStore();
        int calls = 0;
        temp.Store.Changed += () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("subscriber failure");
        };

        File.WriteAllText(temp.FilePath, "{ \"value\": 4 }");
        Assert.True(
            temp.Changed.Wait(FollowTimeout, TestContext.Current.CancellationToken),
            "Changed was not raised after the first external write");
        Assert.Equal(4, temp.Store.Current.Value);

        temp.Changed.Reset();
        File.WriteAllText(temp.FilePath, "{ \"value\": 5 }");

        Assert.True(
            temp.Changed.Wait(FollowTimeout, TestContext.Current.CancellationToken),
            "Changed was not raised after a subscriber threw");
        Assert.Equal(5, temp.Store.Current.Value);
    }
}
