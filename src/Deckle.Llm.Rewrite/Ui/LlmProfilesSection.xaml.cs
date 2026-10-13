using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

public sealed partial class LlmProfilesSection : UserControl
{
    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();

    // FilterModels rebuilds each AutoSuggestBox's ItemsSource from this list,
    // so a refresh shows on the next keystroke or focus.
    public ObservableCollection<string> AvailableModelNames { get; } = new();

    internal void SetAvailableModelNames(IEnumerable<string> names)
    {
        AvailableModelNames.Clear();
        foreach (var n in names) AvailableModelNames.Add(n);
    }

    public event EventHandler? ProfilesChanged;

    public LlmProfilesSection()
    {
        InitializeComponent();
    }

    public void Reload()
    {
        Profiles.Clear();
        var s = LlmSettingsService.Instance.Current;
        for (int i = 0; i < s.Profiles.Count; i++)
        {
            var vm = new ProfileViewModel();
            vm.LoadFrom(i, s.Profiles[i], isNew: false);
            TrackProfile(vm);
            Profiles.Add(vm);
        }
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

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ProfileViewModel vm)
            return;

        // Removing the active profile clears the active choice (back to plain
        // transcription), so the page and the engine agree instead of keeping
        // an orphan the ComboBox shows as (None). Legacy auto-rule references
        // stay untouched for lossless deserialization.
        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_RemoveProfileDialog_Title"),
                Loc.Format("Settings_RemoveProfileDialog_Content_Format", vm.Name),
                Loc.Get("Common_Remove"),
                IsDestructive: true));
        if (!confirmed)
            return;

        var settings = LlmSettingsService.Instance.Current;
        var profiles = settings.Profiles;
        if (vm.ProfileIndex < profiles.Count)
        {
            RewriteProfile removed = profiles[vm.ProfileIndex];
            if (!string.IsNullOrEmpty(settings.ActiveProfileId)
                    ? settings.ActiveProfileId == removed.Id
                    : string.Equals(settings.ActiveProfileName, removed.Name, StringComparison.OrdinalIgnoreCase))
            {
                settings.ActiveProfileId = null;
                settings.ActiveProfileName = null;
            }
            profiles.RemoveAt(vm.ProfileIndex);
            LlmSettingsMigrations.RepairProfileReferences(LlmSettingsService.Instance.Current);
            LlmSettingsService.Instance.Save();
        }
        Reload();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── TextChanged → push value immediately for live auto-save ────────────
    //
    // x:Bind TwoWay on TextBox updates the source on LostFocus only. These
    // handlers push the value on every keystroke so auto-save fires during
    // typing.

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is ProfileViewModel vm)
            vm.Name = tb.Text;
    }

    private void SystemPromptBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is ProfileViewModel vm)
            vm.SystemPrompt = tb.Text;
    }

    // ── Model picker (AutoSuggestBox) ──────────────────────────────────────
    //
    // Not an editable ComboBox: its dropdown-arrow mouse interaction is
    // unreliable in WinUI 3. Text is OneWay (VM → UI) so the stored model name
    // shows even when it is not in the suggestion list (Ollama offline, model
    // renamed); the handlers below carry UI → VM.

    private void ModelSuggest_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not AutoSuggestBox box) return;

        box.ItemsSource = FilterModels(box.Text);
        box.IsSuggestionListOpen = true;
    }

    private void ModelSuggest_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Only react to user typing — ignore programmatic updates (x:Bind
        // OneWay push, SuggestionChosen echo) to avoid filtering the list
        // on values the user did not intend to search for.
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        if (sender.Tag is ProfileViewModel vm)
            vm.Model = sender.Text ?? string.Empty;

        sender.ItemsSource = FilterModels(sender.Text);
    }

    private void ModelSuggest_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (sender.Tag is ProfileViewModel vm && args.SelectedItem is string selected)
            vm.Model = selected;
    }

    private void ModelSuggest_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.Tag is not ProfileViewModel vm) return;

        var text = args.ChosenSuggestion is string s ? s : args.QueryText;
        vm.Model = text ?? string.Empty;
    }

    private List<string> FilterModels(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return AvailableModelNames.ToList();

        return AvailableModelNames
            .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ── Add ─────────────────────────────────────────────────────────────────

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var s = LlmSettingsService.Instance.Current;
        s.Profiles.Add(new RewriteProfile
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12),
            Name = "",
            Model = "",
            SystemPrompt = ""
        });
        LlmSettingsService.Instance.Save();

        int index = s.Profiles.Count - 1;
        var vm = new ProfileViewModel();
        vm.LoadFrom(index, s.Profiles[index], isNew: true);
        TrackProfile(vm);
        Profiles.Add(vm);

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    // Scope: the Profiles list only, replaced by the defaults.
    // RepairProfileReferences gives the fresh defaults their ids and re-pairs
    // the active profile and legacy references to them by name.
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
        LlmSettingsMigrations.RepairProfileReferences(LlmSettingsService.Instance.Current);
        LlmSettingsService.Instance.Save();
        Reload();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }
}
