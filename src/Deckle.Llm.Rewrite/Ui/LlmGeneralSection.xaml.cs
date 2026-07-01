using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage General section ───────────────────────────────────────────────
//
// Handles the "Enable rewriting" toggle and "Ollama endpoint" field. Autosaves
// on every change. Emits EndpointChanged to signal the host that it must
// re-check Ollama availability (availability depends on URL, so a URL change
// invalidates the cache).

public sealed partial class LlmGeneralSection : UserControl
{
    private bool _loading;

    public event EventHandler? EndpointChanged;

    // Exposed as a DependencyProperty so LlmPage (and this control's own
    // endpoint expander) can bind the Visibility of dependent sections
    // (endpoint, shortcut slots, rules, profiles, models) to this master
    // toggle via x:Bind OneWay through a bool→Visibility converter — masking,
    // not greying, per the mask-not-grey doctrine.
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
    }

    public void Reload()
    {
        _loading = true;
        var s = LlmSettingsService.Instance.Current;
        EnabledToggle.IsOn = s.Enabled;
        EndpointBox.Text = s.OllamaEndpoint;
        IsRewritingEnabled = s.Enabled;
        _loading = false;
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        LlmSettingsService.Instance.Current.Enabled = EnabledToggle.IsOn;
        LlmSettingsService.Instance.Save();
        IsRewritingEnabled = EnabledToggle.IsOn;
    }

    private void EndpointBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        LlmSettingsService.Instance.Current.OllamaEndpoint = EndpointBox.Text.Trim();
        LlmSettingsService.Instance.Save();
        EndpointChanged?.Invoke(this, EventArgs.Empty);
    }

    // Scope: Enabled + OllamaEndpoint. Source of truth for defaults is a fresh
    // LlmSettings — no duplication of literal defaults here. Fires
    // EndpointChanged so LlmPage re-checks Ollama availability against the
    // restored endpoint.
    private void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new LlmSettings();
        var s = LlmSettingsService.Instance.Current;
        s.Enabled = defaults.Enabled;
        s.OllamaEndpoint = defaults.OllamaEndpoint;
        LlmSettingsService.Instance.Save();
        Reload();
        EndpointChanged?.Invoke(this, EventArgs.Empty);
    }
}
