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

// Maps the wizard's module selection to the install items those modules need.
// Autocorrect brings the CamemBERT reranker by decision: the contextual
// correction is part of what installing Autocorrect means, not an option.
// Anytype brings the pinned anytype-cli binary; the bot account and API key are
// a later, interactive provisioning step. Each module owns its provisioning
// gesture; this plan only composes them.
public static class InstallPlan
{
    public const string NativeRuntimeItemId = "native-runtime";
    public const string SileroItemId        = "silero-vad";
    public const string CamembertItemId     = "camembert-base";
    public const string AnytypeItemId       = "anytype-cli";

    // The one question the App asks before opening the wizard on the
    // provisioning step. The plan itself stays internal to the wizard pages.
    public static bool HasPendingWork(SetupContext context)
    {
        foreach (InstallItem item in Build(context))
        {
            if (!item.IsInstalled()) return true;
        }
        return false;
    }

    // Every estimate in the wizard (module selector, Folders, Choices) reads
    // this, so none can disagree with what the install step runs.
    internal static long PendingBytes(SetupContext context)
    {
        long pending = 0;
        foreach (InstallItem item in Build(context))
        {
            if (!item.IsInstalled()) pending += item.SizeBytes;
        }
        return pending;
    }

    internal static IReadOnlyList<InstallItem> Build(SetupContext context)
    {
        var items = new List<InstallItem>();

        if (context.SelectedModules.Contains(ModuleIds.Transcription))
        {
            items.Add(NativeRuntimeItem());
            // The wizard host seeds SelectedModel; the guard covers a host
            // that did not.
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
        RunAsync    = NativeRuntime.ProvisionAsync,
    };

    private static InstallItem WhisperModelItem(ModelEntry model) => new()
    {
        Id          = model.Id,
        DisplayName = model.DisplayName,
        SizeBytes   = model.SizeBytes,
        IsInstalled = () => SpeechModels.IsInstalled(model),
        RunAsync    = (progress, ct) => SpeechModels.ProvisionAsync(model, progress, ct),
    };

    // The Silero model is also lazily fetched by VadService at first use —
    // that path stays as the safety net; installing it here makes the first
    // dictation complete instead of untrimmed.
    private static InstallItem SileroItem() => new()
    {
        Id          = SileroItemId,
        DisplayName = Loc.Get("Setup_Item_SileroVad"),
        SizeBytes   = SileroVadModel.SizeBytes,
        IsInstalled = () => SileroVadModel.IsInstalled(AppPaths.ModelsDirectory),
        RunAsync    = (progress, ct) =>
            SileroVadModel.ProvisionAsync(AppPaths.ModelsDirectory, progress, ct),
    };

    // ── Autocorrect ───────────────────────────────────────────────────────────

    private static InstallItem CamembertItem() => new()
    {
        Id          = CamembertItemId,
        DisplayName = Loc.Get("Setup_Item_Camembert"),
        SizeBytes   = CamembertAssets.TotalSizeBytes,
        IsInstalled = () => CamembertAssets.IsInstalled(CamembertDirectory),
        RunAsync    = (progress, ct) =>
            CamembertAssets.ProvisionAsync(CamembertDirectory, progress, ct),
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
        RunAsync    = BackendInstallation.ProvisionAsync,
    };
}
