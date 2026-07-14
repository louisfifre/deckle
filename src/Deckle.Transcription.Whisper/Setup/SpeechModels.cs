using System.Collections.Generic;
using System.IO;
using System.Linq;

using Deckle.Core;
using Deckle.Transcription;

namespace Deckle.Transcription.Whisper;

// ── SpeechModels ─────────────────────────────────────────────────────────────
//
// **Single source of truth** for every speech model the app understands.
// TranscriptionEngine, the wizard, Settings — they all read filenames, default
// IDs, and download URLs from this catalog instead of hard-coding them.
//
// V1 catalog: two Whisper models (base, large-v3). Sha256 and SizeBytes come
// from the HuggingFace LFS pointers of ggerganov/whisper.cpp (the `oid sha256:`
// and `size` lines of https://huggingface.co/ggerganov/whisper.cpp/raw/main/<file>).
// Upstream republishes the .bin files occasionally — re-read both together
// when a download starts failing on checksum.
public static class SpeechModels
{
    // Default Whisper model the engine targets when no override is set.
    // Single source of truth — TranscriptionEngine reads this rather than its
    // own copy of the filename. Swap it here when bumping the default.
    // base, not large-v3: the neophyte install path must not open on a 3 GB
    // download. An install that already carries large-v3 keeps it through
    // the installed-model fallback in WhisperBackend.ResolveModelPath.
    public const string DefaultModelFileName = "ggml-base.bin";

    public static IReadOnlyList<ModelEntry> WhisperModels { get; } = new[]
    {
        new ModelEntry(
            Id:          "whisper-base",
            FileName:    DefaultModelFileName,
            DisplayName: "Whisper base — multilingual, fast (~150 MB)",
            Url:         "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
            SizeBytes:   147_951_465L,
            Sha256:      "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe"),
        new ModelEntry(
            Id:          "whisper-large-v3",
            FileName:    "ggml-large-v3.bin",
            DisplayName: "Whisper large-v3 — multilingual, best accuracy (~3 GB)",
            Url:         "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin",
            SizeBytes:   3_095_033_483L,
            Sha256:      "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2"),
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

    public static async Task<ProvisioningResult> ProvisionAsync(
        ModelEntry model,
        IProgress<Downloader.DownloadProgress> progress,
        CancellationToken ct)
    {
        string destination = Path.Combine(AppPaths.ModelsDirectory, model.FileName);
        Downloader.DownloadResult download = await Downloader.DownloadAsync(
            model.Url, destination, model.Sha256, progress, ct);
        return download.Success
            ? ProvisioningResult.Ok(new FileInfo(destination).Length, download.ActualSha256)
            : ProvisioningResult.Fail(download.ErrorMessage ?? "download failed");
    }

    // Provisioning/boot gates ask for a usable model, not specifically the
    // default: the engine's ResolveModelPath falls back to the best installed
    // model, so an install carrying only large-v3 is fully provisioned.
    public static bool IsAnyModelInstalled() => WhisperModels.Any(IsInstalled);

    // Best catalog model actually on disk in the given directory, largest
    // first — quality tracks size in the Whisper family. Feeds the engine's
    // installed-model fallback: an install whose settings target an absent
    // file (a pre-ggml-base-default install that never wrote Engine.Model,
    // a fresh install whose only download was the non-default choice) keeps
    // transcribing with what it has instead of failing on a missing file.
    public static string? BestInstalledFileName(string modelsDirectory)
    {
        try
        {
            return BestInstalledFileName(modelsDirectory, IsUsableModelFile);
        }
        catch
        {
            return null;
        }
    }

    internal static string? BestInstalledFileName(
        string modelsDirectory,
        Func<string, bool> isUsable)
        => WhisperModels
            .OrderByDescending(m => m.SizeBytes)
            .Select(m => m.FileName)
            .FirstOrDefault(file => isUsable(Path.Combine(modelsDirectory, file)));

    internal static bool IsUsableModelFile(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch { return false; }
    }
}
