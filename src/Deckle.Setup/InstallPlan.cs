using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Anytype;
using Deckle.Autocorrect.Mlm;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Modules;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;
using Deckle.Vad;

namespace Deckle.Setup;

// ── InstallPlan ───────────────────────────────────────────────────────────────
//
// Maps the wizard's module selection to the install items those modules need:
// Dictation brings the whisper.cpp native runtime, the chosen speech model and
// the Silero VAD model; Autocorrect brings the CamemBERT reranker (checked in
// with the module by decision — the contextual correction is part of what
// "installing Autocorrect" means, not an option); Anytype brings the pinned
// anytype-cli binary (the bot account and API key are a later, interactive
// provisioning act). The provisioning primitives stay in the modules they
// serve — this plan only composes them, the same posture as the rest of the
// wizard.
internal static class InstallPlan
{
    public const string NativeRuntimeItemId = "native-runtime";
    public const string SileroItemId        = "silero-vad";
    public const string CamembertItemId     = "camembert-base";
    public const string AnytypeItemId       = "anytype-cli";

    public static IReadOnlyList<InstallItem> Build(SetupContext context)
    {
        var items = new List<InstallItem>();

        if (context.SelectedModules.Contains(ModuleIds.Transcription))
        {
            items.Add(NativeRuntimeItem());
            // SelectedModel is initialized by SetupWindow construction; the
            // guard only protects a wizard host that bypassed it.
            if (context.SelectedModel is { } model) items.Add(WhisperModelItem(model));
            items.Add(SileroItem());
        }

        if (context.SelectedModules.Contains(ModuleIds.Autocorrect))
            items.Add(CamembertItem());

        if (context.SelectedModules.Contains(ModuleIds.Anytype))
            items.Add(AnytypeItem());

        return items;
    }

    // ── Dictation ─────────────────────────────────────────────────────────────

    private static InstallItem NativeRuntimeItem() => new()
    {
        Id          = NativeRuntimeItemId,
        DisplayName = NativeRuntime.CurrentBundle.DisplayName,
        SizeBytes   = NativeRuntime.CurrentBundle.SizeBytes,
        IsInstalled = NativeRuntime.IsInstalled,
        RunAsync    = async (progress, ct) =>
        {
            // Placeholder URL (build artifact: the hosting release is not
            // published). ChoicesPage gates Next on IsInstalled in that case,
            // so reaching this is a bug — surfaced as an explicit item
            // failure rather than a 404 crash.
            if (NativeRuntime.BundleUrlIsPlaceholder)
                return InstallItemOutcome.Fail("auto-download URL is a placeholder; use Browse... on the previous step");

            var bundle = NativeRuntime.CurrentBundle;

            // Stage beside the final location, so the per-file move during
            // InstallFromZipAsync is rename-only, not copy-then-delete.
            Directory.CreateDirectory(AppPaths.NativeDirectory);
            string zipPath = Path.Combine(AppPaths.NativeDirectory, "_bundle.zip");

            try
            {
                var dl = await Downloader.DownloadAsync(bundle.Url, zipPath, bundle.Sha256, progress, ct);
                if (!dl.Success) return InstallItemOutcome.Fail(dl.ErrorMessage ?? "download failed");

                int extracted = await NativeRuntime.InstallFromZipAsync(zipPath, ct);
                if (extracted < NativeRuntime.RequiredDllNames.Count)
                    return InstallItemOutcome.Fail(
                        $"bundle is incomplete (extracted {extracted}/{NativeRuntime.RequiredDllNames.Count} DLLs)");

                return InstallItemOutcome.Ok(bundle.SizeBytes, dl.ActualSha256);
            }
            finally
            {
                TryDelete(zipPath);
            }
        },
    };

    private static InstallItem WhisperModelItem(ModelEntry model) => new()
    {
        Id          = model.Id,
        DisplayName = model.DisplayName,
        SizeBytes   = model.SizeBytes,
        IsInstalled = () => SpeechModels.IsInstalled(model),
        RunAsync    = async (progress, ct) =>
        {
            string dest = Path.Combine(AppPaths.ModelsDirectory, model.FileName);
            var dl = await Downloader.DownloadAsync(model.Url, dest, model.Sha256, progress, ct);
            return dl.Success
                ? InstallItemOutcome.Ok(new FileInfo(dest).Length, dl.ActualSha256)
                : InstallItemOutcome.Fail(dl.ErrorMessage ?? "download failed");
        },
    };

    // The Silero model is also lazily fetched by VadService at first use —
    // that path stays as the safety net; installing it here makes the first
    // dictation complete instead of untrimmed.
    private static InstallItem SileroItem() => new()
    {
        Id          = SileroItemId,
        DisplayName = Loc.Get("Setup_Item_SileroVad"),
        SizeBytes   = SileroVadModel.SizeBytes,
        IsInstalled = () => File.Exists(Path.Combine(AppPaths.ModelsDirectory, SileroVadModel.FileName)),
        RunAsync    = async (progress, ct) =>
        {
            string dest = Path.Combine(AppPaths.ModelsDirectory, SileroVadModel.FileName);
            var dl = await Downloader.DownloadAsync(SileroVadModel.Url, dest, SileroVadModel.Sha256, progress, ct);
            return dl.Success
                ? InstallItemOutcome.Ok(new FileInfo(dest).Length, dl.ActualSha256)
                : InstallItemOutcome.Fail(dl.ErrorMessage ?? "download failed");
        },
    };

    // ── Autocorrect ───────────────────────────────────────────────────────────

    private static InstallItem CamembertItem() => new()
    {
        Id          = CamembertItemId,
        DisplayName = Loc.Get("Setup_Item_Camembert"),
        SizeBytes   = CamembertAssets.TotalSizeBytes,
        IsInstalled = () => CamembertAssets.IsInstalled(CamembertDirectory),
        RunAsync    = async (progress, ct) =>
        {
            // Several files, one item: progress is reported cumulatively over
            // the catalog's total, so the row's bar walks the whole ~440 MB
            // once instead of restarting per file.
            long total = CamembertAssets.TotalSizeBytes;
            long doneBytes = 0;

            foreach (var file in CamembertAssets.Files)
            {
                long baseBytes = doneBytes;
                var offset = new Progress<Downloader.DownloadProgress>(p =>
                    progress.Report(new Downloader.DownloadProgress(baseBytes + p.BytesDownloaded, total)));

                string dest = Path.Combine(CamembertDirectory, file.FileName);
                var dl = await Downloader.DownloadAsync(file.Url, dest, file.Sha256, offset, ct);
                if (!dl.Success)
                    return InstallItemOutcome.Fail($"{file.FileName}: {dl.ErrorMessage ?? "download failed"}");

                doneBytes += file.SizeBytes;
            }

            return InstallItemOutcome.Ok(total);
        },
    };

    private static string CamembertDirectory =>
        Path.Combine(AppPaths.ModelsDirectory, CamembertAssets.DirectoryName);

    // ── Anytype ───────────────────────────────────────────────────────────────

    private static InstallItem AnytypeItem() => new()
    {
        Id          = AnytypeItemId,
        DisplayName = BackendInstallation.CurrentBundle.DisplayName,
        SizeBytes   = BackendInstallation.CurrentBundle.SizeBytes,
        IsInstalled = BackendInstallation.IsInstalled,
        RunAsync    = async (progress, ct) =>
        {
            var bundle = BackendInstallation.CurrentBundle;
            Directory.CreateDirectory(BackendInstallation.InstallDirectory);
            string zipPath = Path.Combine(BackendInstallation.InstallDirectory, "_bundle.zip");

            try
            {
                var dl = await Downloader.DownloadAsync(bundle.Url, zipPath, bundle.Sha256, progress, ct);
                if (!dl.Success) return InstallItemOutcome.Fail(dl.ErrorMessage ?? "download failed");

                bool ok = await BackendInstallation.InstallFromZipAsync(zipPath, ct);
                return ok
                    ? InstallItemOutcome.Ok(bundle.SizeBytes, dl.ActualSha256)
                    : InstallItemOutcome.Fail("bundle did not contain anytype.exe");
            }
            finally
            {
                TryDelete(zipPath);
            }
        },
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
