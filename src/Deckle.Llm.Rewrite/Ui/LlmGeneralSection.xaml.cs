using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage General section ───────────────────────────────────────────────
//
// Handles the "Enable rewriting" toggle and "Ollama endpoint" field — the two
// cleanly-composable leaves of the whole page. Both now COMPOSE: declared as
// SettingDescriptors in LlmGeneralViewModel.Settings.cs and built into
// ComposedHost by the SettingsComposer, which carries each card's own inline
// reset. The page's other sections (profiles, rules, models, shortcut slots)
// stay hand-authored — dynamic runtime collections no composer kind models.
//
// The composer subscribes to the ViewModel, so the cards reflect Load() and the
// per-card reset with no code-behind sync; the change handlers (auto-save +
// EndpointChanged) live in the VM's partial setters, which the composer drives.
//
// Two thin bridges remain here:
//
//   • EndpointChanged is re-surfaced from the VM so LlmPage's wiring is unchanged
//     — it still re-probes Ollama availability when the URL changes.
//   • IsRewritingEnabled (a DependencyProperty) is kept and mirrored from the VM's
//     Enabled PropertyChanged, because LlmPage x:Binds the four dependent
//     sections' Visibility to it (mask-not-grey). The composed toggle writes
//     VM.Enabled → PropertyChanged → this mirror → the page's gating, so the
//     masking keeps firing exactly as before.

public sealed partial class LlmGeneralSection : UserControl
{
    public LlmGeneralViewModel ViewModel { get; } = new();

    // Drives the composed cards. Held in a field so its subscription to the
    // ViewModel lives as long as the (cached) page — the same host-only pattern
    // AutocorrectPage / TrackpadPage use.
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

        _composer = new SettingsComposer(ComposedHost, ViewModel);
        _composer.Compose(ViewModel.GeneralSettingsManifest);

        // Forward the VM's endpoint event to the page unchanged.
        ViewModel.EndpointChanged += (_, e) => EndpointChanged?.Invoke(this, e);

        // Mirror the master switch onto the gating DependencyProperty whenever it
        // changes — the composed toggle writes VM.Enabled, which raises
        // PropertyChanged, which keeps the page's section masking in sync.
        ViewModel.PropertyChanged += OnViewModelChanged;
        IsRewritingEnabled = ViewModel.Enabled;
    }

    // Re-pull the two values from the live settings (a Reset elsewhere, or the
    // page-level Reset all, may have replaced them while the page sat cached). The
    // composer re-syncs the cards off the VM's PropertyChanged; the gating mirror
    // follows through OnViewModelChanged.
    public void Reload()
    {
        ViewModel.Load();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LlmGeneralViewModel.Enabled))
            IsRewritingEnabled = ViewModel.Enabled;
    }

    // Scope: Enabled + OllamaEndpoint. Source of truth for defaults is a fresh
    // LlmSettings — no duplication of literal defaults here. The VM fires
    // EndpointChanged so LlmPage re-checks Ollama availability against the
    // restored endpoint.
    private void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetToDefaults();
    }
}
