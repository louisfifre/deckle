using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage General section ───────────────────────────────────────────────
//
// The composer subscribes to the ViewModel, so the cards reflect Load() and the
// per-card reset with no code-behind sync; the change handlers (auto-save and
// EndpointChanged) live in the VM's partial setters.

public sealed partial class LlmGeneralSection : UserControl
{
    public LlmGeneralViewModel ViewModel { get; } = new();

    // Held in a field so its subscription to the ViewModel lives as long as the
    // cached page.
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

        _composer = new SettingsComposer(ComposedHost, ViewModel);
        _composer.Compose(ViewModel.GeneralSettingsManifest);

        ViewModel.EndpointChanged += (_, e) => EndpointChanged?.Invoke(this, e);

        ViewModel.PropertyChanged += OnViewModelChanged;
        IsRewritingEnabled = ViewModel.Enabled;
    }

    public void Reload()
    {
        ViewModel.Load();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LlmGeneralViewModel.Enabled))
            IsRewritingEnabled = ViewModel.Enabled;
    }

    private void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetToDefaults();
    }
}
