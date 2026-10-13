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

namespace Deckle.Llm.Rewrite;

// ─── LlmPage — thin host ───────────────────────────────────────────────────
//
// The four sections are autonomous UserControls. The page owns only the
// orchestration: hydration, the Ollama refresh, the shared Ollama state
// (LlmOllamaContext), the inter-section events that retrigger a refresh or a
// targeted Reload, and Reset all.

public sealed partial class LlmPage : Page
{
    // Without a token, the shared OllamaService HttpClient waits out its long
    // timeout. If Ollama is saturated (concurrent GPU job, crashing model),
    // the page falls into the "unavailable" state instead of freezing.
    private static readonly TimeSpan OllamaAdminTimeout = TimeSpan.FromSeconds(5);

    private readonly LlmOllamaContext _context = new();

    public LlmPage()
    {
        InitializeComponent();
        IsTabStop = true;
        NavigationCacheMode = NavigationCacheMode.Required;

        ModelsSection.Initialize(_context);

        // Named handlers, not inline async lambdas: such a lambda compiles as
        // async void and any unhandled exception bubbles to the dispatcher.
        // The named methods below carry their try/catch.
        GeneralSection.EndpointChanged += OnEndpointChanged;
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
            await RefreshOllamaStateAsync();
        }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.PageNavigatedToFailed();
            DeckleLlmSource.Log.PageNavigatedToFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    private async void OnEndpointChanged(object? sender, EventArgs e)
    {
        try { await RefreshOllamaStateAsync(); }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.EndpointRefreshFailed();
            DeckleLlmSource.Log.EndpointRefreshFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        try { await RefreshOllamaStateAsync(); }
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
        ProfilesSection.Reload();
        ActiveProfileSection.Reload();
    }

    private async Task RefreshOllamaStateAsync()
    {
        var service = new OllamaService(() => LlmSettingsService.Instance.Current.OllamaEndpoint);

        bool available = false;
        IReadOnlyList<OllamaModel> models = Array.Empty<OllamaModel>();

        // Broad catch: IsAvailableAsync is fail-soft, but ListModelsAsync can
        // throw (HttpRequest, TaskCanceled when the token expires, JsonException
        // when Ollama returns an HTML error). "Unavailable" covers every case.
        try
        {
            available = await service.IsAvailableAsync();
            if (available)
            {
                using var cts = new CancellationTokenSource(OllamaAdminTimeout);
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

        _context.Service = service;
        _context.Available = available;
        _context.Models = models;

        string ep = LlmSettingsService.Instance.Current.OllamaEndpoint;
        OllamaStatusBar.Message = Loc.Format("Settings_OllamaStatusMessage_Format", ep);
        OllamaStatusBar.IsOpen = !available;

        _context.RaiseStateChanged();

        var modelNames = new List<string>();
        foreach (var m in models) modelNames.Add(m.Name);
        ProfilesSection.SetAvailableModelNames(modelNames);
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
            LlmSettingsMigrations.RepairProfileReferences(fresh);
            LlmSettingsService.Instance.Replace(fresh);
            Hydrate();
            await RefreshOllamaStateAsync();
        }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.ResetAllFailed();
            DeckleLlmSource.Log.ResetAllFailedDetail(ex.GetType().Name, ex.Message);
        }
    }
}
