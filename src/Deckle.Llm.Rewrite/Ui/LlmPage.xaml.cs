using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Llm;
using Deckle.Security;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage — thin host ───────────────────────────────────────────────────
//
// The five sections are autonomous UserControls. The page owns only the
// orchestration: hydration, the provider refresh (what each provider serves,
// the Ollama status), the shared Ollama state (LlmOllamaContext), the
// inter-section events that retrigger a refresh or a targeted Reload, and
// Reset all.

public sealed partial class LlmPage : Page
{
    // Without a token, a provider that is saturated or asleep would hold the
    // page; with it the page falls into "unavailable" instead of freezing.
    private static readonly TimeSpan ProviderAdminTimeout = TimeSpan.FromSeconds(5);

    private readonly LlmOllamaContext _context = new();

    public LlmPage()
    {
        InitializeComponent();

        // The page asks with its own wording (it says what the reset wipes), so
        // the header does not add its generic confirmation on top.
        Header.ConfirmsReset = false;

        // The reset spans profiles, providers and slots that no composer
        // models, so a dirty check cannot be computed from the page's composer
        // (the only one lives inside LlmGeneralSection and covers the switch).
        // Reset all stays enabled, as it was before the shared header.
        Header.IsResetAllEnabled = true;

        IsTabStop = true;
        NavigationCacheMode = NavigationCacheMode.Required;

        ModelsSection.Initialize(_context);

        // Named handlers, not inline async lambdas: such a lambda compiles as
        // async void and any unhandled exception bubbles to the dispatcher.
        // The named methods below carry their try/catch.
        GeneralSection.ViewModel.PropertyChanged += OnGeneralChanged;
        ProvidersSection.ProviderNamesChanged += OnProviderNamesChanged;
        ProvidersSection.ProvidersChanged += OnProvidersChanged;
        ProfilesSection.ProfilesChanged += OnProfilesChanged;
        ModelsSection.RefreshRequested += OnRefreshRequested;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        // async void is imposed by the override signature, so the try/catch is
        // required: an unhandled exception here bubbles to the dispatcher and
        // can kill the app despite Application.UnhandledException.
        base.OnNavigatedTo(e);
        try
        {
            Hydrate();
            await RefreshProvidersAsync();
        }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.PageNavigatedToFailed();
            DeckleLlmSource.Log.PageNavigatedToFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    // The Models section hides with the rest when rewriting is off, and also
    // when no Ollama provider is on, so it follows the switch from code rather
    // than from the page's x:Bind.
    private void OnGeneralChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LlmGeneralViewModel.Enabled))
            GateModelsSection();
    }

    private void OnProviderNamesChanged(object? sender, EventArgs e)
    {
        // Synchronous and in-memory, like OnProfilesChanged.
        ProfilesSection.SetProviders();
    }

    private async void OnProvidersChanged(object? sender, EventArgs e)
    {
        try
        {
            ProfilesSection.SetProviders();
            await RefreshProvidersAsync();
        }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.EndpointRefreshFailed();
            DeckleLlmSource.Log.EndpointRefreshFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        try { await RefreshProvidersAsync(); }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.ManualRefreshFailed();
            DeckleLlmSource.Log.ManualRefreshFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        // No try/catch: Reload() is synchronous and touches only in-memory
        // state, so a throw is a state bug for the global UnhandledException.
        ActiveProfileSection.Reload();
    }

    private void Hydrate()
    {
        GeneralSection.Reload();
        ProvidersSection.Reload();
        ProfilesSection.Reload();
        ActiveProfileSection.Reload();
    }

    // Asks every enabled provider what it serves (the profiles' model
    // suggestions), and keeps the Ollama administration state for the first
    // enabled Ollama provider, the one the Models section manages.
    private async Task RefreshProvidersAsync()
    {
        var settings = LlmSettingsService.Instance.Current;
        ISecretVault vault = SecretVault.CreateDefault();

        LlmProvider? ollama = settings.Providers.Find(p => p.Enabled && p.Kind == LlmProviderKind.Ollama);
        await RefreshOllamaStateAsync(ollama);

        // A snapshot: the list may be edited while a listing is in flight.
        foreach (var provider in settings.Providers.ToArray())
        {
            if (!provider.Enabled) continue;

            string? apiKey = null;
            try { vault.TryGet(LlmProviderSecrets.ApiKeyName(provider.Id), out apiKey); }
            catch (SecretVaultException) { /* no key: the catalog answers what it can */ }

            using var cts = new CancellationTokenSource(ProviderAdminTimeout);
            IReadOnlyList<string> names = await ProviderModelCatalog.ListAsync(provider, apiKey, cts.Token);
            ProfilesSection.SetAvailableModelNames(provider.Id, names);
        }
    }

    private async Task RefreshOllamaStateAsync(LlmProvider? ollama)
    {
        bool available = false;
        IReadOnlyList<OllamaModel> models = Array.Empty<OllamaModel>();
        OllamaService? service = null;

        if (ollama is not null)
        {
            string endpoint = ollama.Endpoint;
            service = new OllamaService(() => endpoint);

            // Broad catch: IsAvailableAsync is fail-soft, but ListModelsAsync can
            // throw (HttpRequest, TaskCanceled when the token expires, JsonException
            // when Ollama returns an HTML error). "Unavailable" covers every case.
            try
            {
                available = await service.IsAvailableAsync();
                if (available)
                {
                    using var cts = new CancellationTokenSource(ProviderAdminTimeout);
                    models = await service.ListModelsAsync(cts.Token);
                }
            }
            catch (Exception ex)
            {
                DeckleLlmSource.Log.OllamaRefreshSkipped();
                DeckleLlmSource.Log.OllamaRefreshSkippedDetail(ex.GetType().Name, ex.Message);
                available = false;
                models = Array.Empty<OllamaModel>();
            }

            OllamaStatusBar.Message = Loc.Format("Settings_OllamaStatusMessage_Format", ollama.Endpoint);
        }

        _context.Service = service;
        _context.Available = available;
        _context.Models = models;

        OllamaStatusBar.IsOpen = ollama is not null && !available;
        GateModelsSection();

        _context.RaiseStateChanged();
    }

    private void GateModelsSection()
    {
        bool shown = GeneralSection.IsRewritingEnabled && _context.Service is not null;
        ModelsSection.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBackgroundTapped(object sender, TappedRoutedEventArgs e)
    {
        // A ComboBox's Tapped bubbles up here, and re-focusing the page would
        // close its dropdown before it opens. Other interactive controls
        // (TextBox, NumberBox) mark Tapped as handled and never reach this
        // handler.
        if (e.OriginalSource is DependencyObject obj && IsInsideComboBox(obj))
            return;
        this.Focus(FocusState.Programmatic);
    }

    private static bool IsInsideComboBox(DependencyObject node)
    {
        while (node is not null)
        {
            if (node is ComboBox) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        // async void: the try/catch keeps an exception (Save IO, UI hydration)
        // from bubbling to the dispatcher.
        try
        {
            bool confirmed = await ConfirmationService.RequestAsync(
                this.XamlRoot,
                new ConfirmationRequest(
                    Loc.Get("Settings_ResetLlmDialog_Title"),
                    Loc.Get("Settings_ResetLlmDialog_Content"),
                    Loc.Get("LlmPageResetAllLabel.Text"),
                    IsDestructive: true));
            if (!confirmed)
                return;

            // Fresh defaults carry no profile ids: reconcile them before the
            // instance goes live.
            var fresh = new LlmSettings();
            LlmSettingsMigrations.Apply(fresh);
            LlmSettingsService.Instance.Replace(fresh);
            Hydrate();
            await RefreshProvidersAsync();
        }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.ResetAllFailed();
            DeckleLlmSource.Log.ResetAllFailedDetail(ex.GetType().Name, ex.Message);
        }
    }
}
