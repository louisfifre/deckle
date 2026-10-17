using System.Text;
using Xunit;

namespace Deckle.Transcription.Tests;

// The store seals with the real DPAPI and writes to a temporary directory, so
// these are integration tests. Sample sentences are neutral on purpose.
[Trait("Category", "integration")]
public sealed class DictationRecoveryStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, $"recovery-store-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_root, "recovery", "dictation.dat");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void WriteThenReadReturnsTheSameText()
    {
        var store = new DictationRecoveryStore(FilePath);
        const string text = "The quick brown fox jumps over the lazy dog. Été à Paris.";

        Assert.True(store.TryWrite(text));
        var status = store.TryRead(out string read);

        Assert.Equal(DictationRecoveryReadStatus.Restored, status);
        Assert.Equal(text, read);
    }

    [Fact]
    public void WriteCreatesTheMissingDirectory()
    {
        var store = new DictationRecoveryStore(FilePath);

        Assert.False(Directory.Exists(Path.GetDirectoryName(FilePath)));
        Assert.True(store.TryWrite("Sample sentence."));
        Assert.True(store.Exists);
    }

    [Fact]
    public void TheFileOnDiskDoesNotContainThePlainText()
    {
        var store = new DictationRecoveryStore(FilePath);
        const string text = "Plain sample sentence for the seal check.";

        Assert.True(store.TryWrite(text));

        byte[] bytes = File.ReadAllBytes(FilePath);
        Assert.DoesNotContain(text, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void SecondWriteReplacesTheFirstWithLongerText()
    {
        var store = new DictationRecoveryStore(FilePath);
        const string first = "First sample sentence.";
        const string longer = first + " Second sample sentence, which makes the take longer.";

        Assert.True(store.TryWrite(first));
        Assert.True(store.TryWrite(longer));

        Assert.Equal(DictationRecoveryReadStatus.Restored, store.TryRead(out string read));
        Assert.Equal(longer, read);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void DeleteRemovesTheFileAndToleratesAMissingOne()
    {
        var store = new DictationRecoveryStore(FilePath);
        Assert.True(store.TryWrite("Sample sentence."));

        Assert.True(store.Delete());
        Assert.False(store.Exists);
        Assert.True(store.Delete());
    }

    [Fact]
    public void ReadOfAMissingFileReportsMissing()
    {
        var store = new DictationRecoveryStore(FilePath);

        Assert.Equal(DictationRecoveryReadStatus.Missing, store.TryRead(out string read));
        Assert.Equal(string.Empty, read);
    }

    [Fact]
    public void ReadOfACorruptedFileReportsUnreadableWithoutThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, [1, 2, 3, 4, 5, 6, 7, 8]);
        var store = new DictationRecoveryStore(FilePath);

        Assert.Equal(DictationRecoveryReadStatus.Unreadable, store.TryRead(out string read));
        Assert.Equal(string.Empty, read);
    }

    [Fact]
    public void WriteFailureReturnsFalseInsteadOfThrowing()
    {
        // A file where the directory should be makes CreateDirectory fail.
        Directory.CreateDirectory(_root);
        string blocker = Path.Combine(_root, "recovery");
        File.WriteAllText(blocker, "not a directory");
        var store = new DictationRecoveryStore(FilePath);

        Assert.False(store.TryWrite("Sample sentence."));
    }
}
