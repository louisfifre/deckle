using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage General section ───────────────────────────────────────────────
//
// The composers subscribe to the ViewModel, so the cards reflect Load() and the
// per-card reset with no code-behind sync; the change handlers (auto-save and
// EndpointChanged) live in the VM's partial setters.
//
// The General block's reset covers what the block holds, the endpoint: a section
// reset never flips the module switch above it, which the page's Reset all and
// the switch card's own reset cover.

public sealed partial class LlmGeneralSection : UserControl
{
    public LlmGeneralViewModel ViewModel { get; } = new();

    // Held in fields so their subscriptions to the ViewModel live as long as the
    // cached page.
    private SettingsComposer? _switchComposer;
    private SettingsComposer? _composer;

    public event EventHandler? EndpointChanged;

    // A DependencyProperty so LlmPage can x:Bind the dependent sections'
    // Visibility to it (mask-not-grey). Mirrored from the VM's Enabled, which
    // the composed toggle writes.
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

        _composer.DirtyChanged += (_, _) => GateReset();

        ViewModel.EndpointChanged += (_, e) => EndpointChanged?.Invoke(this, e);

        ViewModel.PropertyChanged += OnViewModelChanged;
        IsRewritingEnabled = ViewModel.Enabled;
        GeneralBlock.Visibility = ViewModel.Enabled ? Visibility.Visible : Visibility.Collapsed;
        GateReset();
    }

    // A reset elsewhere may have replaced the values while the page sat cached.
    public void Reload()
    {
        ViewModel.Load();
        // Load() raises no PropertyChanged when nothing moved, so no composer
        // DirtyChanged fires and this call settles the reset gate.
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

    private void GateReset()
    {
        GeneralBlock.IsResetEnabled = _composer?.IsDirty() ?? false;
    }

    // The VM's endpoint setter fires EndpointChanged, so LlmPage re-checks Ollama
    // availability against the restored endpoint.
    private void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        _composer?.ResetAll();
    }
}
