using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Anytype;
using Deckle.Core;
using Deckle.Modules;
using Deckle.Transcription;
using Deckle.Transcription.Parakeet;
using Deckle.Vad;
using Deckle.Localization;

namespace Deckle.Setup;

// Maps the wizard's module selection to the install items those modules need.
// Autocorrect brings nothing to download here: its lexicons ship with the app.
// Anytype brings the pinned anytype-cli binary; the bot account and API key are
// a later, interactive provisioning step. Each module owns its provisioning
// gesture; this plan only composes them.
public static class InstallPlan
{
    public const string ParakeetModelItemId = "parakeet-model";
    public const string SileroItemId        = "silero-vad";
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

    // The sentence of the window's total band, worded once for every page
    // that shows it.
    internal static string DescribeTotal(SetupContext context)
    {
        long pending = PendingBytes(context);
        return pending > 0
            ? Loc.Format("Setup_TotalEstimate_Pending_Format", ByteSizeFormatter.Format(pending))
            : Loc.Get("Setup_TotalEstimate_NothingPending");
    }

    internal static IReadOnlyList<InstallItem> Build(SetupContext context)
    {
        var items = new List<InstallItem>();

        // Install order: transcription first, then autocorrect, then Anytype.
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
            if (ParakeetModelItemFor(context) is { } model) items.Add(model);
            items.Add(SileroItem());
        }
        else if (moduleId == ModuleIds.Anytype)
        {
            items.Add(AnytypeItem());
        }

        return items;
    }

    // ── Transcription ─────────────────────────────────────────────────────────

    // Where the engine will read the model and at which encoder precision. The
    // install-mode temp process asks no settings service: it would write a default
    // settings.json under the default root and start a watcher on it, and nothing
    // is configured yet there, so the model goes into the chosen data root's models
    // folder at the default precision.
    private readonly record struct ModelSite(string ConfiguredDirectory, string ModelsDirectory, ParakeetEncoderPrecision Precision);

    private static ModelSite SiteOf(SetupContext context)
    {
        if (context.InstallMode)
            return new("", Path.Combine(context.DataDirectory, "models"), new EngineSettings().ParakeetEncoder);

        TranscriptionSettingsService settings = TranscriptionSettingsService.Instance;
        EngineSettings engine = settings.Current.Engine;
        return new(engine.ParakeetModelDirectory, settings.ResolveModelsDirectory(), engine.ParakeetEncoder);
    }

    // The files the engine reads at the chosen precision, not only the model: a
    // folder holding the graphs without their weights is reinstalled like an
    // empty one. Only the folder Deckle provisions is ever filled: a folder the
    // person configured is theirs, so the plan leaves it out and the speech-model
    // incident and the page readout are what report it.
    private static InstallItem? ParakeetModelItemFor(SetupContext context)
    {
        ModelSite site = SiteOf(context);
        if (!string.IsNullOrWhiteSpace(site.ConfiguredDirectory)) return null;

        return ParakeetModelItem(
            ParakeetModelFiles.Resolve("", site.ModelsDirectory).Directory,
            site.Precision);
    }

    // Whether the Parakeet model is part of the plan and still to download.
    public static bool IsParakeetModelPending(SetupContext context) =>
        Build(context).Any(item => item.Id == ParakeetModelItemId && !item.IsInstalled());

    internal static InstallItem ParakeetModelItem(string modelFolder, ParakeetEncoderPrecision precision) => new()
    {
        Id          = ParakeetModelItemId,
        DisplayName = Loc.Get("Setup_Item_ParakeetModel"),
        SizeBytes   = ParakeetModelCatalog.TotalBytesFor(precision),
        IsInstalled = () => ParakeetModelCatalog.IsInstalled(modelFolder, precision),
        RunAsync    = (progress, ct) => ParakeetModelCatalog.ProvisionAsync(modelFolder, precision, progress, ct),
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
