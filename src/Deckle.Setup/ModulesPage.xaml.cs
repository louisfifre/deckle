using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.WinUI.Controls;
using Deckle.Catalog;
using Deckle.Modules;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Setup;

// ── ModulesPage ──────────────────────────────────────────────────────────────
//
// The module selector — the wizard's first step. One checkbox card per
// catalogue module (ModuleRegistry), checked from the recorded choice
// (ModulePresence.Choice; no choice = everything checked). Checking a module
// pulls in its dependencies, unchecking one expels its dependents — the
// cascade itself lives in ModuleGraph, this page only renders the returned
// selection.
//
// Continue records the choice and routes on it: into the transcription
// provisioning flow (ChoicesPage) when Dictation is selected, straight to
// completion otherwise. Presence acts at the next boot — the App restarts
// after a successful wizard run, so the selection takes effect immediately
// from the user's point of view.
//
// Wording is keyed by module id from THIS module's resources (mirrored into
// the root map for Loc), not from the described module's assembly: the
// installer companion's end state must be able to name a module whose DLLs
// are not on disk (see Deckle.Modules/AGENTS.md).
public sealed partial class ModulesPage : Page
{
    private SetupWindow? _setup;
    private readonly Dictionary<string, CheckBox> _boxes = new(StringComparer.Ordinal);
    private HashSet<string> _selection = new(StringComparer.Ordinal);

    // True while the cascade re-syncs the boxes, so the Checked/Unchecked
    // handlers a sync fires do not cascade again.
    private bool _syncing;

    public ModulesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not SetupWindow setup) return;

        _setup = setup;

        setup.SetStepHeader(
            Loc.Get("Setup_StepTitle_Modules"),
            Loc.Get("Setup_StepSubtitle_Modules"));
        setup.SetBackEnabled(false);
        setup.SetNextLabel(Loc.Get("Setup_NextLabel_Continue"));
        setup.SetNextEnabled(true);
        setup.SetNextVisible(true);
        setup.SetCancelVisible(true);
        setup.NextRequested += OnNextRequested;

        var catalog = ModuleRegistry.Modules;
        _selection = new HashSet<string>(
            ModulePresence.Choice ?? catalog.Select(m => m.Id),
            StringComparer.Ordinal);

        BuildModuleCards(catalog);
        SyncSelectionToContext();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_setup is not null) _setup.NextRequested -= OnNextRequested;
    }

    // ── Cards ─────────────────────────────────────────────────────────────────

    private void BuildModuleCards(IReadOnlyList<ModuleDescriptor> catalog)
    {
        ModulesList.Children.Clear();
        _boxes.Clear();

        foreach (var module in catalog)
        {
            var box = new CheckBox
            {
                IsChecked = _selection.Contains(module.Id),
                MinWidth = 0,
            };
            box.Checked   += (_, _) => OnBoxToggled(module.Id, isChecked: true);
            box.Unchecked += (_, _) => OnBoxToggled(module.Id, isChecked: false);
            _boxes[module.Id] = box;

            ModulesList.Children.Add(new SettingsCard
            {
                Header      = Loc.Get($"Setup_Module_{module.Id}_Label"),
                Description = Loc.Get($"Setup_Module_{module.Id}_Description"),
                HeaderIcon  = new FontIcon { Glyph = module.Glyph },
                Content     = box,
            });
        }
    }

    private void OnBoxToggled(string id, bool isChecked)
    {
        if (_syncing) return;

        var catalog = ModuleRegistry.Modules;
        _selection = new HashSet<string>(
            isChecked
                ? ModuleGraph.WithDependencies(catalog, _selection, id)
                : ModuleGraph.WithoutDependents(catalog, _selection, id),
            StringComparer.Ordinal);

        // Re-sync every box to the cascaded selection; the guard keeps the
        // sync's own Checked/Unchecked events from cascading again.
        _syncing = true;
        try
        {
            foreach (var (moduleId, box) in _boxes)
                box.IsChecked = _selection.Contains(moduleId);
        }
        finally
        {
            _syncing = false;
        }

        SyncSelectionToContext();
    }

    // The context mirrors the live selection so the estimate (here and on the
    // Choices recap) always totals what the install step would actually run.
    private void SyncSelectionToContext()
    {
        if (_setup is null) return;
        _setup.Context.SelectedModules = new HashSet<string>(_selection, StringComparer.Ordinal);

        long pendingBytes = InstallPlan.PendingBytes(_setup.Context);
        TotalEstimateBar.Message = pendingBytes > 0
            ? Loc.Format("Setup_TotalEstimate_Pending_Format", FormatBytes(pendingBytes))
            : Loc.Get("Setup_TotalEstimate_NothingPending");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)               return $"{bytes} B";
        if (bytes < 1024L * 1024)       return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F0} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    // ── Next ──────────────────────────────────────────────────────────────────

    private void OnNextRequested()
    {
        if (_setup is null) return;

        // Install mode: the choice is NOT persisted from here — AppPaths froze
        // on the default data root in this temp process, while the user may
        // still pick a custom one on the next page. The Deploy step writes
        // presence.json into the chosen root via PresenceFile.SaveTo.
        if (_setup.Context.InstallMode)
        {
            _setup.Body.Navigate(typeof(FoldersPage), _setup);
            return;
        }

        ModulePresence.Save(_selection.ToList());

        // Dictation selected → its Choices page first (model pick). Otherwise
        // straight to the install step when a selected module still has
        // something to put on disk, and to completion when nothing does.
        if (_selection.Contains(ModuleIds.Transcription))
        {
            _setup.Body.Navigate(typeof(ChoicesPage), _setup);
            return;
        }

        bool anythingToInstall = InstallPlan.Build(_setup.Context).Any(i => !i.IsInstalled());
        if (anythingToInstall)
            _setup.Body.Navigate(typeof(InstallingPage), _setup);
        else
            _setup.Complete(true);
    }
}
