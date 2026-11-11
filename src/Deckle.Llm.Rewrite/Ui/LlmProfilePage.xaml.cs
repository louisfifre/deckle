using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Localization;

namespace Deckle.Llm.Rewrite;

// What the Profiles section hands its subpage: the profile to edit and the
// section that owns the list, the provider choices and the model names. The
// section outlives the subpage (LlmPage is cached), so the page borrows them
// rather than copying them.
public sealed record LlmProfilePageArgs(LlmProfilesSection Owner, ProfileViewModel Profile);

// One rewrite profile on a page of its own, one level under Rewriting. The
// view model auto-saves every change; this page only relays the fields the
// view model cannot bind TwoWay on every keystroke, and the removal.
public sealed partial class LlmProfilePage : Page
{
    private LlmProfilesSection? _owner;

    public ProfileViewModel Profile { get; private set; } = new();

    public LlmProfilePage()
    {
        InitializeComponent();
        Header.ParentPageTag = LlmSettingsModule.PageTag;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not LlmProfilePageArgs args) return;

        _owner = args.Owner;
        Profile = args.Profile;
        Bindings.Update();

        ShowName();
        Profile.PropertyChanged += OnProfilePropertyChanged;

        ProviderBox.ItemsSource = _owner.ProviderOptions;
        ProviderBox.SelectedValue = Profile.ProviderId;

        ComposeCards();

        // A profile just added opens here with its name to write first.
        if (Profile.IsNew)
            Loaded += FocusNameOnce;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Profile.PropertyChanged -= OnProfilePropertyChanged;
    }

    private void FocusNameOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= FocusNameOnce;
        NameBox.Focus(FocusState.Programmatic);
    }

    private void OnProfilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileViewModel.Name)) ShowName();
    }

    // The header names the profile, and follows a rename as it is typed. A
    // profile without a name yet still needs a last segment to read.
    private void ShowName() =>
        Header.Title = string.IsNullOrWhiteSpace(Profile.Name)
            ? Loc.GetFrom("Deckle.Llm.Rewrite", "LlmProfilePage_UntitledTitle")
            : Profile.Name.Trim();

    // The reasoning level and the shortcut, composed by the section (it knows
    // the other profiles a chord may clash with), between the fields and the
    // removal card.
    private void ComposeCards()
    {
        if (_owner is null) return;
        var host = new StackPanel();
        new SettingsComposer(host, Profile).Compose(_owner.ProfileCards(Profile));

        int at = Cards.Children.IndexOf(RemoveCard);
        while (host.Children.Count > 0)
        {
            UIElement card = host.Children[0];
            host.Children.RemoveAt(0);
            Cards.Children.Insert(at++, card);
        }
    }

    // ── Fields ──────────────────────────────────────────────────────────────
    //
    // x:Bind TwoWay on a TextBox updates the source on LostFocus only; these
    // handlers push on every keystroke so auto-save fires while typing.

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Profile.Name = ((TextBox)sender).Text;

    private void PromptBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Profile.SystemPrompt = ((TextBox)sender).Text;

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderBox.SelectedItem is not ProviderOption option || option.Id == Profile.ProviderId) return;
        Profile.ProviderId = option.Id;
        LlmProfilesSection.ApplyProvider(Profile);
    }

    private void ModelBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_owner is null) return;
        var box = (AutoSuggestBox)sender;
        box.ItemsSource = _owner.FilterModels(Profile, box.Text);
        box.IsSuggestionListOpen = true;
    }

    // Only typing filters: a programmatic update (the OneWay push, the echo of
    // a chosen suggestion) is not something to search for.
    private void ModelBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || _owner is null) return;
        Profile.Model = sender.Text ?? string.Empty;
        sender.ItemsSource = _owner.FilterModels(Profile, sender.Text);
    }

    private void ModelBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string selected) Profile.Model = selected;
    }

    private void ModelBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        Profile.Model = (args.ChosenSuggestion as string ?? args.QueryText) ?? string.Empty;

    // ── Removal ─────────────────────────────────────────────────────────────

    // The section asks and removes; once the profile is gone there is nothing
    // left to show here, so the page goes back to Rewriting. Like the removal
    // it replaces in the old fold, the work is in-memory plus one Save.
    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_owner is null) return;
        if (await _owner.RemoveProfileAsync(Profile, XamlRoot))
            SettingsNavigation.GoToPage?.Invoke(LlmSettingsModule.PageTag);
    }
}
