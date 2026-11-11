using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Deckle.Catalog;
using Deckle.Llm;
using Deckle.Localization;

namespace Deckle.Llm.Rewrite.Views;

public sealed partial class LlmProfilesSection : UserControl
{
    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();

    // What each profile's provider ComboBox offers. Updated in place by
    // SetProviders so an open ComboBox keeps its selection through a rename.
    public ObservableCollection<ProviderOption> ProviderOptions { get; } = new();

    // Model names per provider id. FilterModels reads the profile's provider
    // entry, so a refresh shows on the next keystroke or focus.
    private readonly Dictionary<string, List<string>> _modelNamesByProvider = new();

    public event EventHandler? ProfilesChanged;

    public LlmProfilesSection()
    {
        InitializeComponent();
    }

    public void Reload()
    {
        SetProviders();
        Profiles.Clear();
        var s = LlmSettingsService.Instance.Current;
        for (int i = 0; i < s.Profiles.Count; i++)
        {
            var vm = new ProfileViewModel();
            vm.LoadFrom(i, s.Profiles[i], isNew: false);
            ApplyProvider(vm);
            TrackProfile(vm);
            Profiles.Add(vm);
        }
    }

    /// <summary>The provider list changed: refresh the choices every card
    /// offers and the provider name each summary shows.</summary>
    internal void SetProviders()
    {
        var providers = LlmSettingsService.Instance.Current.Providers;

        for (int i = ProviderOptions.Count - 1; i >= 0; i--)
        {
            if (!providers.Exists(p => p.Id == ProviderOptions[i].Id))
                ProviderOptions.RemoveAt(i);
        }
        foreach (var p in providers)
        {
            string name = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name;
            var option = ProviderOptions.FirstOrDefault(o => o.Id == p.Id);
            if (option is null) ProviderOptions.Add(new ProviderOption(p.Id) { Name = name });
            else option.Name = name;
        }

        foreach (var vm in Profiles) ApplyProvider(vm);
    }

    internal void SetAvailableModelNames(string providerId, IEnumerable<string> names)
    {
        _modelNamesByProvider[providerId] = names.ToList();
    }

    // The name and the hint visibility derive from the provider the profile
    // names; a provider no longer in the list leaves the name blank, which the
    // summary shows as "(no provider)".
    internal static void ApplyProvider(ProfileViewModel vm)
    {
        LlmProvider? provider = LlmSettingsService.Instance.Current.Providers
            .Find(p => p.Id == vm.ProviderId);
        vm.ProviderName = provider?.Name ?? "";
        vm.ShowsGenerationHints = provider is null || provider.Kind != LlmProviderKind.Claude;
        vm.ShowsReasoning = provider?.Kind == LlmProviderKind.Claude;
    }

    // Only a Name change raises ProfilesChanged: the name is the only profile
    // field shown elsewhere (the active-profile ComboBox). No unsubscribe: the
    // section outlives the VMs it subscribes to.
    private void TrackProfile(ProfileViewModel vm)
    {
        vm.PropertyChanged += OnProfilePropertyChanged;
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileViewModel.Name))
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Profile pages ───────────────────────────────────────────────────────

    private void ProfileCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProfileViewModel vm })
            OpenProfile(vm);
    }

    private void OpenProfile(ProfileViewModel vm) =>
        SettingsNavigation.OpenSubpage?.Invoke(typeof(LlmProfilePage), new LlmProfilePageArgs(this, vm));

    // The first profile's page, for the self-check's walk (LlmPage relays it).
    internal SettingsSubpage? SampleSubpage() =>
        Profiles.Count == 0
            ? null
            : new SettingsSubpage(typeof(LlmProfilePage), new LlmProfilePageArgs(this, Profiles[0]), Profiles[0].Name);

    // Asked from the profile's own page, whose XamlRoot hosts the dialog (this
    // section is off screen then). True once the profile is gone.
    internal async Task<bool> RemoveProfileAsync(ProfileViewModel vm, XamlRoot xamlRoot)
    {
        // Removing the active profile clears the active choice (back to plain
        // transcription), so the page and the engine agree instead of keeping
        // an orphan the ComboBox shows as (None).
        bool confirmed = await ConfirmationService.RequestAsync(
            xamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_RemoveProfileDialog_Title"),
                Loc.Format("Settings_RemoveProfileDialog_Content_Format", vm.Name),
                Loc.Get("Common_Remove"),
                IsDestructive: true));
        if (!confirmed)
            return false;

        var settings = LlmSettingsService.Instance.Current;
        var profiles = settings.Profiles;
        if (vm.ProfileIndex < profiles.Count)
        {
            RewriteProfile removed = profiles[vm.ProfileIndex];
            // Its later steps go with it, and the first step, which always
            // exists, takes another profile (LlmSettingsNormalizer).
            LlmSettingsNormalizer.RemoveProfileFromSteps(settings, removed.Id);
            profiles.RemoveAt(vm.ProfileIndex);
            LlmSettingsNormalizer.RepairProfileReferences(LlmSettingsService.Instance.Current);
            LlmSettingsService.Instance.Save();
        }
        Reload();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // The model names the profile's provider serves, for the page's model box.
    internal List<string> FilterModels(ProfileViewModel vm, string? query)
    {
        if (!_modelNamesByProvider.TryGetValue(vm.ProviderId, out var names))
            return new List<string>();

        if (string.IsNullOrWhiteSpace(query))
            return names.ToList();

        return names
            .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ── Reasoning and shortcut cards ────────────────────────────────────────
    //
    // Composed rather than written in the page's XAML, so the shortcut gets the
    // same chord editor as the Shortcuts page and the level a reset. The
    // profile's page composes them from here: the chord check reads the other
    // profiles.
    //
    // The reasoning level first, beside the model it depends on; then a
    // switch and the chord it reveals: off is "no shortcut", so the chord
    // editor needs no unbound state of its own, and switching off keeps the
    // chord for the next time.
    //
    // A list offers only what the profile's model honours, and a composed
    // list's options are fixed when it is built, so the level has two cards
    // on the same value, one with Off for a model that can stop thinking and
    // one without, and the model shows one of them.
    private static readonly IReadOnlyList<(ReasoningLevel Value, string LabelKey)> ReasoningOptionsWithOff =
    [
        (ReasoningLevel.Off, "LlmProfileReasoningOff"),
        (ReasoningLevel.Low, "LlmProfileReasoningLow"),
        (ReasoningLevel.Medium, "LlmProfileReasoningMedium"),
    ];

    private static readonly IReadOnlyList<(ReasoningLevel Value, string LabelKey)> ReasoningOptions =
    [
        (ReasoningLevel.Low, "LlmProfileReasoningLow"),
        (ReasoningLevel.Medium, "LlmProfileReasoningMedium"),
    ];

    internal IReadOnlyList<SettingDescriptor> ProfileCards(ProfileViewModel vm) =>
    [
        Setting.Choice("LlmProfileReasoningCard",
            () => vm.ShownReasoning, v => vm.Reasoning = v,
            ReasoningOptionsWithOff,
            visibleWhen: () => vm.ShowsReasoning && vm.OffersReasoningOff,
            defaultValue: () => ReasoningLevel.Low),
        Setting.Choice("LlmProfileReasoningNoOffCard",
            () => vm.ShownReasoning, v => vm.Reasoning = v,
            ReasoningOptions,
            visibleWhen: () => vm.ShowsReasoning && !vm.OffersReasoningOff,
            defaultValue: () => ReasoningLevel.Low),
        Setting.Toggle("LlmProfileHotkeyEnabledCard",
            () => vm.HotkeyEnabled, v => vm.HotkeyEnabled = v,
            defaultValue: () => false),
        Setting.Hotkey("LlmProfileHotkeyCard",
            () => vm.Hotkey, v => vm.Hotkey = v,
            new HotkeyArgs(AllowUnbound: false),
            visibleWhen: () => vm.HotkeyEnabled,
            advisory: () => HotkeyHeldByAnotherProfile(vm)),
    ];

    // Two profiles on one chord: Windows registers it once, so only the first
    // would ever start. The Shortcuts page checks against the other actions.
    private string? HotkeyHeldByAnotherProfile(ProfileViewModel vm)
    {
        if (!vm.HotkeyEnabled || vm.Hotkey is not { IsComplete: true } chord) return null;
        ProfileViewModel? other = Profiles.FirstOrDefault(p =>
            !ReferenceEquals(p, vm) && p.HotkeyEnabled && p.Hotkey == chord);
        return other is null
            ? null
            : string.Format(
                Loc.GetFrom("Deckle.Llm.Rewrite.Views", "LlmProfile_HotkeyUsedByProfile_Format"),
                other.Name);
    }

    // ── Add ─────────────────────────────────────────────────────────────────

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var s = LlmSettingsService.Instance.Current;
        s.Profiles.Add(new RewriteProfile
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12),
            Name = "",
            ProviderId = s.Providers.Find(p => p.Enabled)?.Id ?? "",
            Model = "",
            SystemPrompt = ""
        });
        LlmSettingsService.Instance.Save();

        int index = s.Profiles.Count - 1;
        var vm = new ProfileViewModel();
        vm.LoadFrom(index, s.Profiles[index], isNew: true);
        ApplyProvider(vm);
        TrackProfile(vm);
        Profiles.Add(vm);

        ProfilesChanged?.Invoke(this, EventArgs.Empty);

        // Straight to its page: a new profile is nothing until it has a name,
        // a model and a prompt.
        OpenProfile(vm);
    }

    // Scope: the Profiles list only, replaced by the defaults. The normalizer
    // gives the fresh defaults their ids and re-pairs the active profile to
    // them by name.
    private async void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_ResetProfilesDialog_Title"),
                Loc.Get("Settings_ResetProfilesDialog_Content"),
                Loc.Get("Common_Reset"),
                IsDestructive: true));
        if (!confirmed)
            return;

        var defaults = new LlmSettings();
        LlmSettingsService.Instance.Current.Profiles = defaults.Profiles;
        LlmSettingsNormalizer.Apply(LlmSettingsService.Instance.Current);
        LlmSettingsService.Instance.Save();
        Reload();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }
}

// What a profile's provider ComboBox lists: the id it stores and the name it
// shows. Observable so a rename reaches an open ComboBox without rebuilding
// the list, which would drop its selection.
public sealed partial class ProviderOption : ObservableObject
{
    public string Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    public ProviderOption(string id)
    {
        Id = id;
        Name = "";
    }
}
