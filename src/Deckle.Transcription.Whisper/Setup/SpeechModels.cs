using System.Collections.Generic;
using System.IO;
using System.Linq;

using Deckle.Core;
using Deckle.Transcription.Setup;

namespace Deckle.Transcription.Whisper.Setup;

// ── SpeechModels ─────────────────────────────────────────────────────────────
//
// **Single source of truth** for every speech model the app understands.
// TranscriptionEngine, the wizard, Settings — they all read filenames, default
// IDs, and download URLs from this catalog instead of hard-coding them.
//
// V1 catalog: two Whisper models (base, large-v3). SHA-256 fields are
// placeholders — HuggingFace doesn't publish a canonical hash format
// compatible with our verifier yet. They'll be filled when the redist
// pipeline computes them.
public static class SpeechModels
{
    // Default Whisper model the engine targets when no override is set.
    // Single source of truth — TranscriptionEngine reads this rather than its
    // own copy of the filename. Swap it here when bumping the default.
    public const string DefaultModelFileName = "ggml-large-v3.bin";

    public static IReadOnlyList<ModelEntry> WhisperModels { get; } = new[]
    {
        new ModelEntry(
            Id:          "whisper-base",
            FileName:    "ggml-base.bin",
            DisplayName: "Whisper base — multilingual, fast (~150 MB)",
            Url:         "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
            SizeBytes:   147_964_211L),
        new ModelEntry(
            Id:          "whisper-large-v3",
            FileName:    DefaultModelFileName,
            DisplayName: "Whisper large-v3 — multilingual, best accuracy (~3 GB)",
            Url:         "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin",
            SizeBytes:   3_094_623_691L),
    };

    // Catalog handle for the engine's default model. The wizard surfaces
    // this as the pre-selected radio in the Choices page.
    public static ModelEntry DefaultWhisperModel =>
        WhisperModels.First(m => m.FileName == DefaultModelFileName);

    public static bool IsInstalled(ModelEntry entry)
    {
        try
        {
            string path = Path.Combine(AppPaths.ModelsDirectory, entry.FileName);
            if (!File.Exists(path)) return false;
            return new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsDefaultInstalled() => IsInstalled(DefaultWhisperModel);
}
