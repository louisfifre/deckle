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
public static class InstallPlan
{
    public const string NativeRuntimeItemId = "native-runtime";
    public const string SileroItemId        = "silero-vad";
    public const string CamembertItemId     = "camembert-base";
    public const string AnytypeItemId       = "anytype-cli";

    // The one question the App's install-continuation branch asks before
    // opening the wizard on the provisioning step: is there anything left to
    // put on disk for this selection? The plan itself (items, sizes) stays
    // internal — only the wizard pages consume it.
    public static bool HasPendingWork(SetupContext context)
    {
        foreach (InstallItem item in Build(context))
        {
            if (!item.IsInstalled()) return true;
        }
        return false;
    }

    // The download weight still ahead of the user: the plan's items not yet
    // on disk, summed. Both estimate bars (module selector and Choices recap)
    // read this, so they can never disagree with what the install step runs.
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
