using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// Write() against a real temporary file system — the pure name resolution is
// covered by TranscriptFileWriterTests; these pin the disk-facing contract the
// engine relies on: the transcript lands beside its source, the content round-
// trips as UTF-8 without BOM, and a re-run of the same audio lands next to the
// earlier transcript instead of overwriting it.
[Trait("Category", "integration")]
public sealed class TranscriptFileWriterDiskTests
{
    private static string NewTempDir() =>
        Path.Combine(AppContext.BaseDirectory, $"transcript-writer-{Guid.NewGuid():N}");

    [Fact]
    public void WriteLandsBesideTheSourceAudio()
    {
        string root = NewTempDir();
        string sourceDirectory = Path.Combine(root, "recordings");
        string source = Path.Combine(sourceDirectory, "meeting.m4a");
        try
        {
            Directory.CreateDirectory(sourceDirectory);
            string written = TranscriptFileWriter.Write("bonjour", source);

            Assert.Equal(Path.Combine(sourceDirectory, "meeting.txt"), written);
            Assert.True(File.Exists(written));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteRoundTripsUtf8WithoutBom()
    {
        string sourceDirectory = NewTempDir();
        string source = Path.Combine(sourceDirectory, "réunion.mp3");
        const string text = "Décodé, réécrit — « fidèle » à l'audio.";
        try
        {
            Directory.CreateDirectory(sourceDirectory);
            string written = TranscriptFileWriter.Write(text, source);

            byte[] bytes = File.ReadAllBytes(written);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Equal(text, File.ReadAllText(written));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
                Directory.Delete(sourceDirectory, recursive: true);
        }
    }

    [Fact]
    public void RerunOfTheSameAudioLandsNextToTheEarlierTranscript()
    {
        string sourceDirectory = NewTempDir();
        string source = Path.Combine(sourceDirectory, "meeting.wav");
        try
        {
            Directory.CreateDirectory(sourceDirectory);
            string first = TranscriptFileWriter.Write("first run", source);
            string second = TranscriptFileWriter.Write("second run", source);

            Assert.Equal(Path.Combine(sourceDirectory, "meeting.txt"), first);
            Assert.Equal(Path.Combine(sourceDirectory, "meeting (2).txt"), second);
            Assert.Equal("first run", File.ReadAllText(first));
            Assert.Equal("second run", File.ReadAllText(second));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
                Directory.Delete(sourceDirectory, recursive: true);
        }
    }
}
