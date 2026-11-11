using System;
using System.Collections.Generic;
using System.Linq;
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
using Deckle.Localization;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage — thin host ───────────────────────────────────────────────────
//
// The six sections are autonomous UserControls. The page owns only the
// orchestration: hydration, the provider refresh (what each provider serves,
// the Ollama status), the shared Ollama state (LlmOllamaContext), the
// inter-section events that retrigger a refresh or a targeted Reload, and
// Reset all.

public sealed partial class LlmPage : Page, ISettingsSubpageSample
{
    public SettingsSubpage? SampleSubpage() => ProfilesSection.SampleSubpage();

    // Without a token, a provider that is saturated or asleep would hold the
    // page; with it the page falls into "unavailable" instead of freezing.
    private static readonly TimeSpan ProviderAdminTimeout = TimeSpan.FromSeconds(5);

    private readonly LlmOllamaContext _context = new();

    public LlmPage()
    {
        InitializeComponent();

        // The header asks; the page adds what its reset keeps and what it loses.
        Header.ResetDetail = Loc.Get("Settings_ResetLlmDialog_Detail");

        // The reset spans profiles, providers and slots that no composer
        // models, so a dirty check cannot be computed from the page's composers
        // (they live inside LlmGeneralSection and LlmDictationSection and cover
        // only the switch and the batch rewriting).
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
            DeckleLlmSource.Log.PageNavigatedToFailed(ex.GetType().Name, ex.Message);
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
            DeckleLlmSource.Log.EndpointRefreshFailed(ex.GetType().Name, ex.Message);
        }
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        try { await RefreshProvidersAsync(); }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.ManualRefreshFailed(ex.GetType().Name, ex.Message);
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
        DictationSection.Reload();
    }

    // ── Provider refresh ────────────────────────────────────────────────────
    //
    // Asks every enabled provider what it serves (the profiles' model
    // suggestions), and keeps the Ollama administration state for the first
    // enabled Ollama provider, the one the Models section manages.
    //
    // Three rules keep the bars in step with the switches:
    // - a provider switched off or removed loses its message at once, before
    //   any network call, so its bar follows the click (it used to wait for
    //   every other provider's answer, a slow Ollama included);
    // - the providers are asked together, so the wait is the slowest answer,
    //   not the sum of all of them;
    // - each refresh takes a generation number, and only the latest one may
    //   touch the page: an older refresh finishing last cannot reopen a bar
    //   for a provider switched off meanwhile.

    // The hosted providers' messages, by provider id. Ollama has its own bar.
    private readonly Dictionary<string, string> _providerProblems = new();

    private int _refreshGeneration;

    private async Task RefreshProvidersAsync()
    {
        int generation = ++_refreshGeneration;
        var settings = LlmSettingsService.Instance.Current;

        // A snapshot: the list may be edited while a listing is in flight.
        LlmProvider[] enabled = settings.Providers.Where(p => p.Enabled).ToArray();

        foreach (string id in _providerProblems.Keys.ToArray())
        {
            if (!enabled.Any(p => p.Id == id)) _providerProblems.Remove(id);
        }
        ShowProviderProblems();

        LlmProvider? ollama = enabled.FirstOrDefault(p => p.Kind == LlmProviderKind.Ollama);
        if (ollama is null) ApplyOllamaState(null, available: false, Array.Empty<OllamaModel>());

        // The keys are read before any request leaves, so a vault failure
        // cannot strand requests already in flight.
        ISecretVault vault = SecretVault.CreateDefault();
        string?[] apiKeys = enabled.Select(p => ReadApiKey(vault, p)).ToArray();

        var work = new List<Task>(enabled.Length + 1);
        if (ollama is not null) work.Add(RefreshOllamaStateAsync(ollama, generation));
        for (int i = 0; i < enabled.Length; i++)
            work.Add(RefreshListingAsync(enabled[i], apiKeys[i], generation));

        await Task.WhenAll(work);
    }

    private static string? ReadApiKey(ISecretVault vault, LlmProvider provider)
    {
        try
        {
            vault.TryGet(LlmProviderSecrets.ApiKeyName(provider.Id), out string? apiKey);
            return apiKey;
        }
        catch (SecretVaultException ex)
        {
            // No key: the catalog answers what it can, and the listing's own
            // Warning says so if the provider then refuses.
            DeckleLlmSource.Log.ProviderKeyReadFailedDetail(provider.Name, ex.GetType().Name, ex.Message);
            return null;
        }
    }

    // Each listing updates its own provider as soon as it answers; a stale
    // generation drops its answer.
    private async Task RefreshListingAsync(LlmProvider provider, string? apiKey, int generation)
    {
        using var cts = new CancellationTokenSource(ProviderAdminTimeout);
        ProviderListing listing = await ProviderModelCatalog.ListAsync(provider, apiKey, cts.Token);
        if (generation != _refreshGeneration) return;

        ProfilesSection.SetAvailableModelNames(provider.Id, listing.Names);

        // Ollama has its own bar above; the hosted providers share this one,
        // which is the only place a stored key shows whether it actually
        // opens the API.
        if (listing.Failure is not null && provider.Kind != LlmProviderKind.Ollama)
            _providerProblems[provider.Id] = RewriteFailureMessage.ForProviderListing(provider, listing.Failure);
        else
            _providerProblems.Remove(provider.Id);
        ShowProviderProblems();
    }

    // In the order of the providers list, so the lines do not swap places
    // with the order the answers arrive in.
    private void ShowProviderProblems()
    {
        var lines = LlmSettingsService.Instance.Current.Providers
            .Where(p => _providerProblems.ContainsKey(p.Id))
            .Select(p => _providerProblems[p.Id])
            .ToList();
        ProviderStatusBar.Message = string.Join(Environment.NewLine, lines);
        ProviderStatusBar.IsOpen = lines.Count > 0;
    }

    private async Task RefreshOllamaStateAsync(LlmProvider ollama, int generation)
    {
        bool available = false;
        IReadOnlyList<OllamaModel> models = Array.Empty<OllamaModel>();
        string endpoint = ollama.Endpoint;
        var service = new OllamaService(() => endpoint);

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
            DeckleLlmSource.Log.OllamaRefreshSkipped(ex.GetType().Name, ex.Message);
            available = false;
            models = Array.Empty<OllamaModel>();
        }

        if (generation != _refreshGeneration) return;
        ApplyOllamaState(ollama, available, models, service);
    }

    // A null provider means no Ollama is on: no bar, no Models section.
    private void ApplyOllamaState(LlmProvider? ollama, bool available,
        IReadOnlyList<OllamaModel> models, OllamaService? service = null)
    {
        if (ollama is not null)
            OllamaStatusBar.Message = Loc.Format("Settings_OllamaStatusMessage_Format", ollama.Endpoint);

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
        // The header has already asked (title, verb and body are the same on
        // every page); this only carries the reset out.
        try
        {
            LlmSettingsService.Instance.Replace(LlmResetDefaults.From(LlmSettingsService.Instance.Current));
            Hydrate();
            await RefreshProvidersAsync();
        }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.ResetAllFailed(ex.GetType().Name, ex.Message);
        }
    }
}
