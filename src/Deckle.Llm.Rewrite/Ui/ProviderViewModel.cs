using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// One provider card of the Providers section. Auto-saves on every change like
// ProfileViewModel; the API key never passes through here, the section writes
// it to the vault and only tells the card whether one is stored.
public partial class ProviderViewModel : ObservableObject
{
    // ComboBox index ↔ LlmProviderKind, in the order the XAML lists the items.
    internal static readonly LlmProviderKind[] Kinds =
        [LlmProviderKind.Ollama, LlmProviderKind.OpenAiCompatible, LlmProviderKind.Claude];

    private bool _isSyncing;

    public int ProviderIndex { get; set; }

    public string Id { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(ShowsEndpoint), nameof(ShowsKey), nameof(EndpointPlaceholder))]
    public partial int KindIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string Endpoint { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // Set by the section from the vault; no setter path to settings.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyStatus))]
    public partial bool HasKeyStored { get; set; }

    // Drives IsExpanded so a freshly added provider opens.
    [ObservableProperty]
    public partial bool IsNew { get; set; }

    public LlmProviderKind Kind => Kinds[Math.Clamp(KindIndex, 0, Kinds.Length - 1)];

    // Claude's host is the SDK's business; Ollama takes no key.
    public bool ShowsEndpoint => Kind != LlmProviderKind.Claude;
    public bool ShowsKey => Kind != LlmProviderKind.Ollama;

    public string EndpointPlaceholder => Kind switch
    {
        LlmProviderKind.Ollama => "http://localhost:11434/api/generate",
        _                      => "http://localhost:1234/v1",
    };

    public string KeyStatus => Loc.Get(HasKeyStored ? "Settings_KeyStored" : "Settings_NoKeyStored");

    public string Summary
    {
        get
        {
            string kind = Loc.Get($"LlmProviderKind_{Kind}");
            return ShowsEndpoint && !string.IsNullOrWhiteSpace(Endpoint)
                ? $"{kind}  ·  {Endpoint.Trim()}"
                : kind;
        }
    }

    partial void OnNameChanged(string value)     { if (!_isSyncing) PushToSettings(); }
    partial void OnKindIndexChanged(int value)   { if (!_isSyncing) PushToSettings(); }
    partial void OnEndpointChanged(string value) { if (!_isSyncing) PushToSettings(); }
    partial void OnEnabledChanged(bool value)    { if (!_isSyncing) PushToSettings(); }

    public ProviderViewModel()
    {
        _isSyncing = true;
        Name = "";
        KindIndex = 1;
        Endpoint = "";
        Enabled = true;
        _isSyncing = false;
    }

    public void LoadFrom(int index, LlmProvider provider, bool isNew, bool hasKeyStored)
    {
        _isSyncing = true;
        ProviderIndex = index;
        IsNew = isNew;
        Id = provider.Id;
        Name = provider.Name;
        KindIndex = Math.Max(0, Array.IndexOf(Kinds, provider.Kind));
        Endpoint = provider.Endpoint;
        Enabled = provider.Enabled;
        HasKeyStored = hasKeyStored;
        _isSyncing = false;
    }

    // Name and Endpoint are trimmed at write time only: trimming the bound
    // property would fight the cursor during typing.
    private void PushToSettings()
    {
        var providers = LlmSettingsService.Instance.Current.Providers;
        if (ProviderIndex >= providers.Count) return;

        var p = providers[ProviderIndex];
        p.Name = Name.Trim();
        p.Kind = Kind;
        p.Endpoint = Endpoint.Trim();
        p.Enabled = Enabled;
        LlmSettingsService.Instance.Save();
    }
}
