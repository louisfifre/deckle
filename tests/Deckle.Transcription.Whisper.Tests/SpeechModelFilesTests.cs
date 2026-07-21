using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

[Trait("Category", "integration")]
public sealed class SpeechModelFilesTests
{
    [Fact]
    public void InstalledFallbackIgnoresAnEmptyLargerModel()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"deckle-models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            ModelEntry largest = SpeechModels.WhisperModels.MaxBy(model => model.SizeBytes)!;
            File.WriteAllBytes(Path.Combine(directory, largest.FileName), []);
            File.WriteAllText(Path.Combine(directory, SpeechModels.DefaultModelFileName), "model");

            string? selected = SpeechModels.BestInstalledFileName(directory);

            Assert.Equal(SpeechModels.DefaultModelFileName, selected);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}
