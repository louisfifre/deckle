using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage General section ───────────────────────────────────────────────
//
// Handles the "Enable rewriting" toggle and "Ollama endpoint" field — the two
// cleanly-composable leaves of the whole page. Both COMPOSE, in two places: the
// module switch (ModuleSwitch) into MasterHost, first on the page and alone, and
// the endpoint (GeneralSettingsManifest) into ComposedHost inside the General
// section block. Both are declared as SettingDescriptors in
// LlmGeneralViewModel.Settings.cs and built by a SettingsComposer each, which
// carries each card's own inline reset. The page's other sections (profiles,
// rules, models, shortcut slots) stay hand-authored — dynamic runtime
// collections no composer kind models.
//
// The composers subscribe to the ViewModel, so the cards reflect Load() and the
// per-card reset with no code-behind sync; the change handlers (auto-save +
// EndpointChanged) live in the VM's partial setters, which the composers drive.
//
// The General block's reset wheel covers what the block holds, the endpoint: a
// section reset never flips the module switch above it, which the page's Reset
// all and the switch card's own reset cover.
//
// Thin bridges remain here:
//
//   • EndpointChanged is re-surfaced from the VM so LlmPage's wiring is unchanged
//     — it still re-probes Ollama availability when the URL changes.
//   • IsRewritingEnabled (a DependencyProperty) is kept and mirrored from the VM's
//     Enabled PropertyChanged, because LlmPage x:Binds the four dependent
//     sections' Visibility to it (mask-not-grey). The composed toggle writes
//     VM.Enabled → PropertyChanged → this mirror → the page's gating, so the
//     masking keeps firing exactly as before.
//   • The same Enabled change collapses the General block, so its title does not
//     stand over an empty body while the endpoint card is masked.

public sealed partial class LlmGeneralSection : UserControl
{
    public LlmGeneralViewModel ViewModel { get; } = new();

    // Drive the composed cards. Held in fields so their subscriptions to the
    // ViewModel live as long as the (cached) page — the same host-only pattern
    // AutocorrectPage / TrackpadPage use. One for the module switch, one for the
    // endpoint inside the General block.
    private SettingsComposer? _switchComposer;
    private SettingsComposer? _composer;

    public event EventHandler? EndpointChanged;

    // Exposed as a DependencyProperty so LlmPage can bind the Visibility of the
    // dependent sections (shortcut slots, rules, profiles, models) to this master
    // toggle via x:Bind OneWay through a bool→Visibility converter — masking, not
    // greying, per the mask-not-grey doctrine. Mirrored from the VM's Enabled
    // property (see OnViewModelChanged), which the composed toggle writes.
    public static readonly DependencyProperty IsRewritingEnabledProperty =
        DependencyProperty.Register(
            nameof(IsRewritingEnabled),
            typeof(bool),
            typeof(LlmGeneralSection),
            new PropertyMetadata(false));

    public bool IsRewritingEnabled
    {
        get => (bool)GetValue(IsRewritingEnabledProperty);
        private set => SetValue(IsRewritingEnabledProperty, value);
    }

    public LlmGeneralSection()
    {
        InitializeComponent();

        _switchComposer = new SettingsComposer(MasterHost, ViewModel);
        _switchComposer.Compose(ViewModel.ModuleSwitch);

        _composer = new SettingsComposer(ComposedHost, ViewModel);
        _composer.Compose(ViewModel.GeneralSettingsManifest);

        // The block's reset wheel is active while the endpoint differs from its
        // default; re-gate it whenever its composer goes dirty.
        _composer.DirtyChanged += (_, _) => GateReset();

        // Forward the VM's endpoint event to the page unchanged.
        ViewModel.EndpointChanged += (_, e) => EndpointChanged?.Invoke(this, e);

        // Mirror the master switch onto the gating DependencyProperty whenever it
        // changes — the composed toggle writes VM.Enabled, which raises
        // PropertyChanged, which keeps the page's section masking in sync.
        ViewModel.PropertyChanged += OnViewModelChanged;
        IsRewritingEnabled = ViewModel.Enabled;
        GeneralBlock.Visibility = ViewModel.Enabled ? Visibility.Visible : Visibility.Collapsed;
        GateReset();
    }

    // Re-pull the two values from the live settings (a Reset elsewhere, or the
    // page-level Reset all, may have replaced them while the page sat cached). The
    // composers re-sync the cards off the VM's PropertyChanged; the gating mirror
    // follows through OnViewModelChanged.
    public void Reload()
    {
        ViewModel.Load();
        // Load() may raise no PropertyChanged when nothing moved, so no composer
        // DirtyChanged would fire: settle the reset gate off the fresh values.
        GateReset();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LlmGeneralViewModel.Enabled))
        {
            IsRewritingEnabled = ViewModel.Enabled;
            GeneralBlock.Visibility = ViewModel.Enabled ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Active-when-dirty over the block's own composer.
    private void GateReset()
    {
        GeneralBlock.IsResetEnabled = _composer?.IsDirty() ?? false;
    }

    // Scope: the endpoint, through its descriptor's Default (a fresh LlmSettings).
    // The VM's endpoint setter fires EndpointChanged, so LlmPage re-checks Ollama
    // availability against the restored endpoint.
    private void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        _composer?.ResetAll();
    }
}
