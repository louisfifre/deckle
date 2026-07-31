using System.Globalization;
using System.Text;
using Deckle.Audio;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

[Trait("Category", "system")]
public sealed class WhisperReferenceAudioSystemTests
{
    [Fact]
    public async Task InstalledWhisperTranscribesTheReferenceSpeech()
    {
        Assert.SkipUnless(
            string.Equals(
                Environment.GetEnvironmentVariable("DECKLE_WHISPER_RUN_SYSTEM"),
                "1",
                StringComparison.Ordinal),
            "Set DECKLE_WHISPER_RUN_SYSTEM=1 to run the installed Whisper system test.");

        string expectedTranscript = Environment.GetEnvironmentVariable(
            "DECKLE_WHISPER_REFERENCE_EXPECTED") ?? "";
        Assert.False(
            string.IsNullOrWhiteSpace(expectedTranscript),
            "DECKLE_WHISPER_REFERENCE_EXPECTED must contain the verified speech.wav transcript.");

        Assert.True(
            NativeRuntime.IsInstalled(),
            "The installed Whisper native runtime is required for this opt-in system test.");

        string audioPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Sounds",
            "speech.wav");
        AudioFileDecodeResult decoded = await Task.Run(
            () => AudioFileDecoder.Decode(audioPath),
            TestContext.Current.CancellationToken);
        Assert.Equal(AudioFileDecodeStatus.Decoded, decoded.Status);
        Assert.False(decoded.Pcm.IsEmpty);

        var host = new SystemHost();
        using var backend = new WhisperBackend(host);
        ModelLoadResult loaded = await backend.LoadModelAsync(
            TestContext.Current.CancellationToken);
        Assert.True(loaded.Success, loaded.ErrorReason);

        TranscriptionResult result = await backend.TranscribeAsync(
            decoded.Pcm,
            segmentSink: null,
            ct: TestContext.Current.CancellationToken,
            context: new TranscriptionContext(PrimingText: string.Empty));

        Assert.False(result.Aborted);
        Assert.Equal(0, result.ResultCode);
        Assert.NotEmpty(result.Segments);
        double wordErrorRate = WordErrorRate(
            Normalize(expectedTranscript),
            Normalize(result.FullText));
        Assert.True(
            wordErrorRate <= 0.35,
            $"Reference transcription WER {wordErrorRate:P1} exceeded 35%. Actual: {result.FullText}");
    }

    private static string Normalize(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        foreach (char value in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(value);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            normalized.Append(char.IsLetterOrDigit(value)
                ? char.ToLowerInvariant(value)
                : ' ');
        }
        return string.Join(
            ' ',
            normalized.ToString().Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static double WordErrorRate(string expected, string actual)
    {
        string[] reference = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] hypothesis = actual.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (reference.Length == 0)
            return hypothesis.Length == 0 ? 0 : 1;

        var previous = new int[hypothesis.Length + 1];
        var current = new int[hypothesis.Length + 1];
        for (int column = 0; column <= hypothesis.Length; column++)
            previous[column] = column;

        for (int row = 1; row <= reference.Length; row++)
        {
            current[0] = row;
            for (int column = 1; column <= hypothesis.Length; column++)
            {
                int substitution = previous[column - 1]
                    + (reference[row - 1] == hypothesis[column - 1] ? 0 : 1);
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    substitution);
            }
            (previous, current) = (current, previous);
        }

        return (double)previous[hypothesis.Length] / reference.Length;
    }

    private sealed class SystemHost : ITranscriptionEngineHost
    {
        public SystemHost()
        {
            string? modelDirectory =
                Environment.GetEnvironmentVariable("DECKLE_WHISPER_MODEL_DIR");
            _modelDirectory = string.IsNullOrWhiteSpace(modelDirectory)
                ? AppPaths.ModelsDirectory
                : modelDirectory;
            Llm.Enabled = false;
        }

        private readonly string _modelDirectory;
        public TranscriptionSettings Transcription { get; } = new();
        public CaptureSettings Audio { get; } = new();
        public TelemetrySettings Telemetry { get; } = new();
        public LlmSettings Llm { get; } = new();
        public string ResolveModelsDirectory() => _modelDirectory;
        public void SaveSettings() { }
        public void ApplyLevelWindow(LevelWindowSettings lw) { }
    }
}
