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
// Stacks the five sections as autonomous UserControls (General, ShortcutSlots,
// Rules, Profiles, Models) + the Ollama status InfoBar. All functional content
// lives in Settings/Llm/. Only these remain here:
//
//  - orchestration (hydration + Ollama refresh)
//  - shared Ollama state via LlmOllamaContext
//  - inter-section events (EndpointChanged, ProfilesChanged, RefreshRequested)
//    that retrigger either an Ollama refresh or a targeted Reload of dependent
//    sections
//  - global Reset all
//
// The Models section depends on Ollama and receives the context via
// Initialize() + StateChanged. Others (General, Profiles, ShortcutSlots, Rules)
// reload directly from SettingsService.

public sealed partial class LlmPage : Page
{
    // Aggressive bound on Ollama admin calls (list, show). Without CTS, the
    // shared OllamaService HttpClient has a 30 min timeout, appropriate for
    // pushing a GGUF blob, fatal for a "quick" call expected to return almost
    // instantly. If Ollama is saturated (concurrent GPU benchmark, crashing
    // model), fall into "unavailable" state instead of freezing the Settings
    // page.
    private static readonly TimeSpan OllamaAdminTimeout = TimeSpan.FromSeconds(5);

    private readonly LlmOllamaContext _context = new();

    public LlmPage()
    {
        InitializeComponent();
        IsTabStop = true;
        NavigationCacheMode = NavigationCacheMode.Required;

        ModelsSection.Initialize(_context);

        // Named handlers (not inline async lambdas) because an
        // async (_, _) => await ... lambda is compiled as async void: any
        // unhandled exception bubbles to the dispatcher. The named methods
        // below carry their try/catch.
        GeneralSection.EndpointChanged += OnEndpointChanged;
        ProfilesSection.ProfilesChanged += OnProfilesChanged;
        ModelsSection.RefreshRequested += OnRefreshRequested;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        // OnNavigatedTo must be async void (WinUI-imposed override signature).
        // Global try/catch required: without this safety net, an unhandled
        // exception during Hydrate()/RefreshOllamaStateAsync() bubbles to the
        // dispatcher and can kill the app despite Application.UnhandledException.
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
        // Synchronous, no try/catch needed: Reload() only touches in-memory
        // collections. If a Reload throws, it is a state bug elsewhere and the
        // global UnhandledException captures it.
        RulesSection.Reload();
        ShortcutSlotsSection.Reload();
    }

    private void Hydrate()
    {
        GeneralSection.Reload();
        ProfilesSection.Reload();
        ShortcutSlotsSection.Reload();
        RulesSection.Reload();
    }

    private async Task RefreshOllamaStateAsync()
    {
        var service = new OllamaService(() => LlmSettingsService.Instance.Current.OllamaEndpoint);

        bool available = false;
        IReadOnlyList<OllamaModel> models = Array.Empty<OllamaModel>();

        // Broad try/catch: IsAvailableAsync is already fail-soft (returns false
        // on exception), but ListModelsAsync can throw (HttpRequest,
        // TaskCanceled if CTS expires, JsonException if Ollama returns an HTML
        // error). Falling into "unavailable" state covers every case without
        // breaking the page.
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

        // Update model ComboBoxes in profiles after Ollama responds.
        // ObservableCollection on the section propagates to bound ItemsSources
        // without needing to recreate the ProfileViewModels.
        var modelNames = new List<string>();
        foreach (var m in models) modelNames.Add(m.Name);
        ProfilesSection.SetAvailableModelNames(modelNames);
    }

    private void OnBackgroundTapped(object sender, TappedRoutedEventArgs e)
    {
        // Don't steal focus from a ComboBox — its Tapped bubbles up here
        // and re-focusing the page would close the dropdown before it opens,
        // forcing the user to click 2-3 times. Other interactive controls
        // (TextBox, NumberBox, editable ComboBox) mark Tapped as handled
        // internally so they don't reach this handler.
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
        // async void event handler: try/catch required to prevent an exception
        // (Save IO, UI hydration) from bubbling to the dispatcher.
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

            // Replace the live LlmSettings with a fresh defaults instance,
            // then run profile-id reconciliation so the freshly-built default
            // profiles get their stable 12-char Guid suffixes before any
            // dependent rule/slot binds to them.
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
