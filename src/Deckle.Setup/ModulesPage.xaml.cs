using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.WinUI.Controls;
using Deckle.Catalog;
using Deckle.Modules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Setup;

// The module selector, the wizard's first step. A module whose dependencies are
// not all checked is greyed out until they are, and unchecking one expels its
// dependents; the cascade lives in ModuleGraph, this page only renders the
// selection it returns. Presence acts at the next boot, and the App restarts
// after a successful run.
//
// Wording is keyed by module id from THIS module's resources (mirrored into
// the root map for Loc), not from the described module's assembly: the
// installer must be able to name a module whose DLLs are not on disk.
public sealed partial class ModulesPage : Page
{
    private SetupWindow? _setup;
    private readonly Dictionary<string, CheckBox> _boxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SettingsCard> _cards = new(StringComparer.Ordinal);
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
        setup.SetCurrentStep(SetupStep.Modules);
        setup.SetBackEnabled(false);
        setup.SetNextLabel(Loc.Get("Setup_NextLabel_Continue"));
        setup.SetNextEnabled(true);
        setup.SetNextVisible(true);
        setup.SetCancelVisible(true);
        setup.NextRequested += OnNextRequested;

        var catalog = ModuleRegistry.Modules;
        // Seeded from the context, not from the saved presence: in install mode
        // nothing is saved until Deploy, so re-reading presence on a Back from
        // Folders or Choices would discard the selection made a page ago. The
        // hosts already seed the context from presence.
        _selection = new HashSet<string>(
            ModuleGraph.WithoutUnmetDependencies(catalog, _setup.Context.SelectedModules),
            StringComparer.Ordinal);

        BuildModuleCards(catalog, setup.Context);
        RenderSelection();
        SyncSelectionToContext();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_setup is not null) _setup.NextRequested -= OnNextRequested;
    }

    // ── Cards ─────────────────────────────────────────────────────────────────

    private void BuildModuleCards(IReadOnlyList<ModuleDescriptor> catalog, SetupContext context)
    {
        ModulesList.Children.Clear();
        _boxes.Clear();
        _cards.Clear();

        foreach (var module in catalog)
        {
            var box = new CheckBox { MinWidth = 0 };
            box.Checked   += (_, _) => OnBoxToggled(module.Id, isChecked: true);
            box.Unchecked += (_, _) => OnBoxToggled(module.Id, isChecked: false);
            _boxes[module.Id] = box;

            // The cost sits beside the checkbox, as Windows shows a size beside
            // an optional feature, so the person weighs it where they decide.
            var cost = new TextBlock
            {
                Text              = DescribeCost(context, module.Id),
                Style             = (Style)Resources["ModuleCostTextStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            };

            var card = new SettingsCard
            {
                Header      = Loc.Get($"Setup_Module_{module.Id}_Label"),
                Description = DescribeModule(module),
                HeaderIcon  = new FontIcon { Glyph = module.Glyph },
                Content     = new StackPanel
                {
                    Orientation       = Orientation.Horizontal,
                    Spacing           = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children          = { cost, box },
                },
            };
            _cards[module.Id] = card;
            ModulesList.Children.Add(card);
        }
    }

    // Boxes follow the selection, and a card whose dependencies are not all
    // checked is disabled as a whole, the way Windows Settings greys a setting
    // that hangs on another. Greyed rather than hidden, unlike a settings page:
    // this page is the catalogue, and a hidden module could not show what to
    // check to get it. Its description already names what it requires.
    private void RenderSelection()
    {
        _syncing = true;
        try
        {
            var catalog = ModuleRegistry.Modules;
            foreach (var (moduleId, box) in _boxes)
            {
                box.IsChecked = _selection.Contains(moduleId);
                _cards[moduleId].IsEnabled = ModuleGraph.DependenciesMet(catalog, _selection, moduleId);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    // The module's own line, then what it needs: the dependency is named from the
    // same labels the cards use, so the card and the cascade say the same thing.
    private static string DescribeModule(ModuleDescriptor module)
    {
        string description = Loc.Get($"Setup_Module_{module.Id}_Description");
        if (module.DependsOn.Count == 0) return description;

        string needs = string.Join(", ", module.DependsOn.Select(id => Loc.Get($"Setup_Module_{id}_Label")));
        return Loc.Format("Setup_Module_Requires_Format", description, needs);
    }

    private static string DescribeCost(SetupContext context, string moduleId) =>
        InstallPlan.PendingBytesFor(context, moduleId) switch
        {
            null => Loc.Get("Setup_Module_NoDownload"),
            0    => Loc.Get("Setup_Module_Installed"),
            long bytes => Loc.Format("Setup_Module_Download_Format", ByteSizeFormatter.Format(bytes)),
        };

    private void OnBoxToggled(string id, bool isChecked)
    {
        if (_syncing) return;

        var catalog = ModuleRegistry.Modules;
        _selection = new HashSet<string>(
            isChecked
                ? ModuleGraph.WithDependencies(catalog, _selection, id)
                : ModuleGraph.WithoutDependents(catalog, _selection, id),
            StringComparer.Ordinal);

        RenderSelection();
        SyncSelectionToContext();
    }

    // The context mirrors the live selection so every estimate totals what the
    // install step would actually run.
    private void SyncSelectionToContext()
    {
        if (_setup is null) return;
        _setup.Context.SelectedModules = new HashSet<string>(_selection, StringComparer.Ordinal);

        // The Dictation step exists only while transcription is selected.
        _setup.RefreshStepRail();

        long pendingBytes = InstallPlan.PendingBytes(_setup.Context);
        TotalEstimateBar.Message = pendingBytes > 0
            ? Loc.Format("Setup_TotalEstimate_Pending_Format", ByteSizeFormatter.Format(pendingBytes))
            : Loc.Get("Setup_TotalEstimate_NothingPending");
    }

    // ── Next ──────────────────────────────────────────────────────────────────

    private void OnNextRequested()
    {
        if (_setup is null) return;

        // Install mode: the choice is NOT persisted from here — AppPaths froze
        // on the default data root in this temp process, while the user may
        // still pick a custom one on the next page. The Deploy step writes
        // presence.json into the chosen root via ModulePresence.SaveUnder.
        if (_setup.Context.InstallMode)
        {
            _setup.Body.Navigate(typeof(FoldersPage), _setup);
            return;
        }

        ModulePresence.Save(_selection.ToList());

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
