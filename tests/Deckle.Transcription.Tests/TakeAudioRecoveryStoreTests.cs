using Xunit;

namespace Deckle.Transcription.Tests;

// The store seals with the real DPAPI and writes to a temporary directory, so
// these are integration tests. The samples are synthetic ramps, never speech.
[Trait("Category", "integration")]
public sealed class TakeAudioRecoveryStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, $"take-audio-store-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_root, "recovery", "take-audio.dat");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A ramp whose values are exact in PCM16, so a roundtrip compares equal.
    private static float[] Ramp(int length, int seed)
    {
        var samples = new float[length];
        for (int i = 0; i < length; i++)
            samples[i] = ((i * 7 + seed * 1000) % 2000 - 1000) / 32768f;
        return samples;
    }

    private TakeAudioRecoveryStore NewStore() => new(FilePath);

    [Fact]
    public void SeveralRecordsReadBackAsOneContinuousTake()
    {
        var store = NewStore();
        float[] first = Ramp(16_000, 1), second = Ramp(16_000, 2), third = Ramp(4_000, 3);

        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(first));
        Assert.True(store.TryAppend(second));
        Assert.True(store.TryAppend(third));
        store.Close();

        TakeAudioRead read = store.TryRead();

        Assert.Equal(TakeAudioReadStatus.Restored, read.Status);
        Assert.Equal(3, read.Records);
        Assert.False(read.TailLost);
        Assert.Equal(first.Concat(second).Concat(third).ToArray(), read.Samples);
        Assert.Equal(36_000 / 16_000.0, read.Seconds);
    }

    [Fact]
    public void TheFileCanBeReadWhileTheTakeIsStillWritingIt()
    {
        var store = NewStore();
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(Ramp(800, 1)));

        // The handle is still open: this is what a crash leaves behind.
        TakeAudioRead read = store.TryRead();
        store.Close();

        Assert.Equal(TakeAudioReadStatus.Restored, read.Status);
        Assert.Equal(800, read.Samples.Length);
    }

    [Fact]
    public void ATruncatedTailIsDroppedAndEveryWholeRecordBeforeItIsKept()
    {
        var store = NewStore();
        float[] first = Ramp(16_000, 1), second = Ramp(16_000, 2);
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(first));
        Assert.True(store.TryAppend(second));
        store.Close();

        // A crash during the third record leaves part of it on disk.
        using (var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write))
            stream.Write(new byte[] { 0x80, 0x01, 0x00, 0x00, 1, 2, 3 });

        TakeAudioRead read = store.TryRead();

        Assert.Equal(TakeAudioReadStatus.Restored, read.Status);
        Assert.Equal(2, read.Records);
        Assert.True(read.TailLost);
        Assert.Equal(first.Concat(second).ToArray(), read.Samples);
    }

    [Fact]
    public void AFileCutInsideTheLengthOfARecordKeepsTheRecordsBeforeIt()
    {
        var store = NewStore();
        float[] first = Ramp(800, 1);
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(first));
        store.Close();
        using (var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write))
            stream.Write(new byte[] { 0x05, 0x00 });

        TakeAudioRead read = store.TryRead();

        Assert.Equal(1, read.Records);
        Assert.True(read.TailLost);
        Assert.Equal(first, read.Samples);
    }

    [Fact]
    public void ADamagedRecordAfterWholeOnesEndsTheReadThere()
    {
        var store = NewStore();
        float[] first = Ramp(800, 1);
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(first));
        Assert.True(store.TryAppend(Ramp(800, 2)));
        store.Close();

        // Flip a byte inside the second blob: DPAPI refuses it.
        byte[] bytes = File.ReadAllBytes(FilePath);
        bytes[^10] ^= 0xFF;
        File.WriteAllBytes(FilePath, bytes);

        TakeAudioRead read = store.TryRead();

        Assert.Equal(TakeAudioReadStatus.Restored, read.Status);
        Assert.Equal(1, read.Records);
        Assert.True(read.TailLost);
        Assert.Equal(first, read.Samples);
    }

    [Fact]
    public void TheFileOnDiskDoesNotContainThePlainAudio()
    {
        var store = NewStore();
        float[] samples = Ramp(16_000, 4);
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(samples));
        store.Close();

        // The PCM16 bytes a plain WAV-like dump would hold, first 64 samples.
        byte[] plain = new byte[128];
        for (int i = 0; i < 64; i++)
            BitConverter.TryWriteBytes(plain.AsSpan(i * 2), (short)Math.Round(samples[i] * 32768f));

        byte[] onDisk = File.ReadAllBytes(FilePath);

        Assert.True(onDisk.AsSpan().IndexOf(plain) < 0);
    }

    [Fact]
    public void BeginCreatesTheMissingDirectoryAndReplacesALeftoverFile()
    {
        var store = NewStore();
        Assert.False(Directory.Exists(Path.GetDirectoryName(FilePath)));
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(Ramp(800, 1)));
        store.Close();

        Assert.True(store.TryBegin());
        store.Close();

        TakeAudioRead read = store.TryRead();
        Assert.Equal(TakeAudioReadStatus.Restored, read.Status);
        Assert.Empty(read.Samples);
        Assert.Equal(0, read.Records);
    }

    [Fact]
    public void AppendWithoutAnOpenFileReturnsFalse()
    {
        var store = NewStore();

        Assert.False(store.TryAppend(Ramp(800, 1)));
        Assert.False(store.Exists);
    }

    [Fact]
    public void DeleteRemovesTheFileAndToleratesAMissingOne()
    {
        var store = NewStore();
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(Ramp(800, 1)));

        Assert.True(store.Delete());
        Assert.False(store.Exists);
        Assert.False(store.TryAppend(Ramp(800, 1)));
        Assert.True(store.Delete());
    }

    [Fact]
    public void ReadOfAMissingFileReportsMissing()
    {
        var store = NewStore();

        TakeAudioRead read = store.TryRead();

        Assert.Equal(TakeAudioReadStatus.Missing, read.Status);
        Assert.Empty(read.Samples);
    }

    [Fact]
    public void ReadOfACorruptedHeaderReportsUnreadableWithoutThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20]);
        var store = NewStore();

        Assert.Equal(TakeAudioReadStatus.Unreadable, store.TryRead().Status);
    }

    [Fact]
    public void ReadOfAFileShorterThanItsHeaderReportsUnreadable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, [(byte)'D', (byte)'K']);
        var store = NewStore();

        Assert.Equal(TakeAudioReadStatus.Unreadable, store.TryRead().Status);
    }

    [Fact]
    public void ARecordDpapiRefusesFromTheStartReportsUnreadable()
    {
        var store = NewStore();
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(Ramp(800, 1)));
        store.Close();
        byte[] bytes = File.ReadAllBytes(FilePath);
        bytes[^10] ^= 0xFF;
        File.WriteAllBytes(FilePath, bytes);

        Assert.Equal(TakeAudioReadStatus.Unreadable, store.TryRead().Status);
    }

    [Fact]
    public void BeginFailureReturnsFalseInsteadOfThrowing()
    {
        // A file where the directory should be makes CreateDirectory fail.
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "recovery"), "not a directory");
        var store = NewStore();

        Assert.False(store.TryBegin());
        Assert.False(store.TryAppend(Ramp(800, 1)));
    }

    [Fact]
    public void MarkAttemptedSetsTheFileAsideAndRestoreBringsItBack()
    {
        var store = NewStore();
        float[] samples = Ramp(800, 1);
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(samples));

        Assert.True(store.MarkAttempted());

        Assert.False(store.Exists);
        Assert.True(store.AttemptedExists);
        Assert.Equal(samples, store.TryReadAttempted().Samples);

        Assert.True(store.RestoreAttempted());

        Assert.True(store.Exists);
        Assert.False(store.AttemptedExists);
        Assert.Equal(samples, store.TryRead().Samples);
    }

    [Fact]
    public void RestoreLeavesANewerTakeAloneAndDropsTheSetAsideOne()
    {
        var store = NewStore();
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(Ramp(800, 1)));
        Assert.True(store.MarkAttempted());
        float[] newer = Ramp(1_600, 2);
        Assert.True(store.TryBegin());
        Assert.True(store.TryAppend(newer));
        store.Close();

        Assert.False(store.RestoreAttempted());

        Assert.False(store.AttemptedExists);
        Assert.Equal(newer, store.TryRead().Samples);
    }

    [Fact]
    public void DeleteAttemptedRemovesTheMarkerAndToleratesAMissingOne()
    {
        var store = NewStore();
        Assert.True(store.TryBegin());
        Assert.True(store.MarkAttempted());

        Assert.True(store.DeleteAttempted());
        Assert.False(store.AttemptedExists);
        Assert.True(store.DeleteAttempted());
    }

    [Fact]
    public void MarkAttemptedWithoutAFileReturnsFalse()
    {
        Assert.False(NewStore().MarkAttempted());
    }
}
