using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// Write() against a real temporary file system — the pure name resolution is
// covered by TranscriptFileWriterTests; these pin the disk-facing contract the
// engine relies on: the output directory is created on demand, the content
// round-trips as UTF-8 without BOM, and a re-run of the same audio lands next to
// the earlier transcript instead of overwriting it.
[Trait("Category", "integration")]
public sealed class TranscriptFileWriterDiskTests
{
    private static string NewTempDir() =>
        Path.Combine(AppContext.BaseDirectory, $"transcript-writer-{Guid.NewGuid():N}");

    [Fact]
    public void WriteCreatesTheMissingOutputDirectory()
    {
        string root = NewTempDir();
        string output = Path.Combine(root, "nested", "transcripts");
        try
        {
            string written = TranscriptFileWriter.Write("bonjour", @"C:\audio\meeting.m4a", output);

            Assert.Equal(Path.Combine(output, "meeting.txt"), written);
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
        string output = NewTempDir();
        const string text = "Décodé, réécrit — « fidèle » à l'audio.";
        try
        {
            string written = TranscriptFileWriter.Write(text, @"C:\audio\réunion.mp3", output);

            byte[] bytes = File.ReadAllBytes(written);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Equal(text, File.ReadAllText(written));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void RerunOfTheSameAudioLandsNextToTheEarlierTranscript()
    {
        string output = NewTempDir();
        try
        {
            string first = TranscriptFileWriter.Write("first run", @"C:\audio\meeting.wav", output);
            string second = TranscriptFileWriter.Write("second run", @"C:\audio\meeting.wav", output);

            Assert.Equal(Path.Combine(output, "meeting.txt"), first);
            Assert.Equal(Path.Combine(output, "meeting (2).txt"), second);
            Assert.Equal("first run", File.ReadAllText(first));
            Assert.Equal("second run", File.ReadAllText(second));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }
}
