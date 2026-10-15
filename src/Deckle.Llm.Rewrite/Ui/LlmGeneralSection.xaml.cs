using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage General section ───────────────────────────────────────────────
//
// The module switch alone. The composer subscribes to the ViewModel, so the
// card reflects Load() and its own reset with no code-behind sync; the
// auto-save lives in the VM's partial setter.

public sealed partial class LlmGeneralSection : UserControl
{
    public LlmGeneralViewModel ViewModel { get; } = new();

    // Held in a field so its subscription to the ViewModel lives as long as
    // the cached page.
    private SettingsComposer? _switchComposer;

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

        ViewModel.PropertyChanged += OnViewModelChanged;
        IsRewritingEnabled = ViewModel.Enabled;
    }

    // A reset elsewhere may have replaced the value while the page sat cached.
    public void Reload() => ViewModel.Load();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LlmGeneralViewModel.Enabled))
            IsRewritingEnabled = ViewModel.Enabled;
    }
}
