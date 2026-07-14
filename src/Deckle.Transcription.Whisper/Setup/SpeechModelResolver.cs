using Deckle.Transcription;

namespace Deckle.Transcription.Whisper;

// Central policy for choosing a catalog model. Callers supply filesystem
// existence so the precedence can be tested without touching process state.
public static class SpeechModelResolver
{
    public static ModelEntry ResolveSelection(string? requestedId, string? configuredFileName)
        => SpeechModels.WhisperModels.FirstOrDefault(m => m.Id == requestedId)
            ?? SpeechModels.WhisperModels.FirstOrDefault(m => m.FileName == configuredFileName)
            ?? SpeechModels.DefaultWhisperModel;

    internal static SpeechModelResolution ResolvePath(
        string? configuredFileName,
        string modelsDirectory,
        string? environmentPath,
        Func<string, bool> fileExists)
    {
        string modelFile = string.IsNullOrWhiteSpace(configuredFileName)
            ? SpeechModels.DefaultModelFileName
            : configuredFileName;
        string configuredPath = Path.Combine(modelsDirectory, modelFile);

        if (!string.IsNullOrWhiteSpace(environmentPath)
            && Path.IsPathRooted(environmentPath)
            && fileExists(environmentPath))
        {
            return new SpeechModelResolution(
                environmentPath, modelFile, InstalledFallbackFileName: null,
                IgnoredEnvironmentPath: null);
        }

        string? installedFile = null;
        if (!fileExists(configuredPath))
            installedFile = SpeechModels.BestInstalledFileName(modelsDirectory, fileExists);

        string fallbackPath = installedFile is null
            ? configuredPath
            : Path.Combine(modelsDirectory, installedFile);

        if (string.IsNullOrWhiteSpace(environmentPath))
            return new SpeechModelResolution(fallbackPath, modelFile, installedFile, null);

        return new SpeechModelResolution(fallbackPath, modelFile, installedFile, environmentPath);
    }

    public static bool TryPersistSelection(
        ModelEntry? selectedModel,
        bool transcriptionPresent,
        Func<ModelEntry, bool> isInstalled,
        TranscriptionSettings settings,
        Action save)
    {
        if (!transcriptionPresent
            || selectedModel is null
            || !isInstalled(selectedModel)
            || settings.Engine.Model == selectedModel.FileName)
        {
            return false;
        }

        settings.Engine.Model = selectedModel.FileName;
        save();
        return true;
    }
}

internal readonly record struct SpeechModelResolution(
    string Path,
    string ConfiguredFileName,
    string? InstalledFallbackFileName,
    string? IgnoredEnvironmentPath);
