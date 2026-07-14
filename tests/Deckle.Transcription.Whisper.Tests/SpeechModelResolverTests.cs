using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

[Trait("Category", "unit")]
public sealed class SpeechModelResolverTests
{
    [Theory]
    [InlineData("whisper-large-v3", "ggml-base.bin", "whisper-large-v3")]
    [InlineData("unknown", "ggml-large-v3.bin", "whisper-large-v3")]
    [InlineData(null, "missing.bin", "whisper-base")]
    public void SelectionUsesRequestThenConfigurationThenDefault(
        string? requestedId,
        string? configuredFile,
        string expectedId)
    {
        ModelEntry result = SpeechModelResolver.ResolveSelection(requestedId, configuredFile);

        Assert.Equal(expectedId, result.Id);
    }

    public static TheoryData<string?, string?, string[], string, string?, string?> PathCases => new()
    {
        { "configured.bin", null, ["configured.bin"], "configured.bin", null, null },
        { null, null, [SpeechModels.DefaultModelFileName], SpeechModels.DefaultModelFileName, null, null },
        { "missing.bin", null, ["ggml-base.bin", "ggml-large-v3.bin"], "ggml-large-v3.bin", "ggml-large-v3.bin", null },
        { "missing.bin", null, [], "missing.bin", null, null },
        { "missing.bin", @"C:\override\model.bin", [@"C:\override\model.bin", "ggml-large-v3.bin"], @"C:\override\model.bin", null, null },
        { "configured.bin", @"relative\model.bin", ["configured.bin"], "configured.bin", null, @"relative\model.bin" },
        { "configured.bin", @"C:\missing\model.bin", ["configured.bin"], "configured.bin", null, @"C:\missing\model.bin" },
    };

    [Theory]
    [MemberData(nameof(PathCases))]
    public void PathUsesEnvironmentThenConfigurationThenBestInstalledModel(
        string? configuredFile,
        string? environmentPath,
        string[] existingPaths,
        string expectedPath,
        string? expectedInstalledFallback,
        string? expectedIgnoredEnvironment)
    {
        const string modelsDirectory = @"C:\models";
        var existing = existingPaths
            .Select(path => Path.IsPathRooted(path) ? path : Path.Combine(modelsDirectory, path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        SpeechModelResolution result = SpeechModelResolver.ResolvePath(
            configuredFile,
            modelsDirectory,
            environmentPath,
            existing.Contains);

        string resolvedExpected = Path.IsPathRooted(expectedPath)
            ? expectedPath
            : Path.Combine(modelsDirectory, expectedPath);
        Assert.Equal(resolvedExpected, result.Path);
        Assert.Equal(
            string.IsNullOrWhiteSpace(configuredFile)
                ? SpeechModels.DefaultModelFileName
                : configuredFile,
            result.ConfiguredFileName);
        Assert.Equal(expectedInstalledFallback, result.InstalledFallbackFileName);
        Assert.Equal(expectedIgnoredEnvironment, result.IgnoredEnvironmentPath);
    }

    public static TheoryData<bool, bool, bool, bool> PersistenceCases => new()
    {
        // transcription present, selection supplied, model installed, expected write
        { false, true,  true,  false },
        { true,  false, true,  false },
        { true,  true,  false, false },
        { true,  true,  true,  true  },
    };

    [Theory]
    [MemberData(nameof(PersistenceCases))]
    public void PersistenceWritesOnlyAUsableChangedSelection(
        bool transcriptionPresent,
        bool selectionSupplied,
        bool installed,
        bool expectedWrite)
    {
        var settings = new TranscriptionSettings();
        ModelEntry selected = SpeechModels.WhisperModels.Single(m => m.Id == "whisper-large-v3");
        int saves = 0;

        bool changed = SpeechModelResolver.TryPersistSelection(
            selectionSupplied ? selected : null,
            transcriptionPresent,
            _ => installed,
            settings,
            () => saves++);

        Assert.Equal(expectedWrite, changed);
        Assert.Equal(expectedWrite ? selected.FileName : SpeechModels.DefaultModelFileName, settings.Engine.Model);
        Assert.Equal(expectedWrite ? 1 : 0, saves);
    }

    [Fact]
    public void PersistenceDoesNotRewriteAnUnchangedSelection()
    {
        ModelEntry selected = SpeechModels.DefaultWhisperModel;
        var settings = new TranscriptionSettings();
        int saves = 0;

        bool changed = SpeechModelResolver.TryPersistSelection(
            selected, true, _ => true, settings, () => saves++);

        Assert.False(changed);
        Assert.Equal(0, saves);
    }

}
