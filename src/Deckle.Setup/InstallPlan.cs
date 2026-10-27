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

        // Install order: dictation first, then autocorrect, then Anytype.
        foreach (string moduleId in ModulesInInstallOrder)
        {
            if (context.SelectedModules.Contains(moduleId))
                items.AddRange(ItemsFor(context, moduleId));
        }

        return items;
    }

    // What one module costs to choose, read from the same items Build would
    // install so the module card cannot disagree with the install. Null when
    // the module installs nothing; 0 when everything it needs is already on
    // disk. Independent of whether the module is currently selected: the card
    // states the cost before the person ticks it.
    internal static long? PendingBytesFor(SetupContext context, string moduleId)
    {
        IReadOnlyList<InstallItem> items = ItemsFor(context, moduleId);
        if (items.Count == 0) return null;

        long pending = 0;
        foreach (InstallItem item in items)
        {
            if (!item.IsInstalled()) pending += item.SizeBytes;
        }
        return pending;
    }

    private static readonly string[] ModulesInInstallOrder =
    [
        ModuleIds.Transcription,
        ModuleIds.Autocorrect,
        ModuleIds.Anytype,
    ];

    // The items one module brings; empty for a module with nothing to download
    // here.
    private static IReadOnlyList<InstallItem> ItemsFor(SetupContext context, string moduleId)
    {
        var items = new List<InstallItem>();

        if (moduleId == ModuleIds.Transcription)
        {
            items.Add(NativeRuntimeItem());
            // The wizard host seeds SelectedModel; the guard covers a host
            // that did not.
            if (context.SelectedModel is { } model) items.Add(WhisperModelItem(model));
            items.Add(SileroItem());
        }
        else if (moduleId == ModuleIds.Autocorrect)
        {
            items.Add(CamembertItem());
        }
        else if (moduleId == ModuleIds.Anytype)
        {
            items.Add(AnytypeItem());
        }

        return items;
    }

    // ── Dictation ─────────────────────────────────────────────────────────────

    // Usable, not merely present: a catalog whose files are all there but do
    // not load on this machine is reinstalled like a missing one, which is
    // what brings an install back after the bundle changed version.
    private static InstallItem NativeRuntimeItem() => new()
    {
        Id          = NativeRuntimeItemId,
        DisplayName = NativeRuntime.CurrentBundle.DisplayName,
        SizeBytes   = NativeRuntime.CurrentBundle.SizeBytes,
        IsInstalled = NativeRuntime.IsUsable,
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
