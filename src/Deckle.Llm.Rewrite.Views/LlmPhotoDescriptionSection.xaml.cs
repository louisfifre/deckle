using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Localization;

namespace Deckle.Llm.Rewrite.Views;

// ─── LlmPage Photo descriptions section ──────────────────────────────────────
//
// The one setting of Deckle Folders that concerns a model: which provider and
// which model describe photos. It sits on this page because this is where
// Deckle keeps its providers and their models, and the person who adds an
// Ollama here finds the photo choice beside it.
//
// Only providers whose address is on this PC are offered: a picture never
// leaves the machine (Deckle Folders' adapter refuses any other address again
// before sending). The value is Deckle Folders' (PhotoDescriptionModelSetting),
// written when its gesture ends: a provider picked, a model chosen or typed
// and left. Nothing chosen is the default, and the section's reset returns to
// it.
//
// Not gated by the rewriting switch: describing photos is not rewriting.
public sealed partial class LlmPhotoDescriptionSection : UserControl
{
    private const string Module = "Deckle.Llm.Rewrite.Views";

    private static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(5);

    private PhotoDescriptionModelSetting? _setting;
    private List<string> _modelNames = [];
    private bool _loading;
    private int _listingVersion;

    public LlmPhotoDescriptionSection()
    {
        InitializeComponent();
    }

    // Shown only when the host gave the setting, that is when Deckle Folders is
    // installed.
    public void Reload()
    {
        _setting = LlmSettingsModule.PhotoDescriptionModel;
        Visibility = _setting is null ? Visibility.Collapsed : Visibility.Visible;
        if (_setting is null) return;

        _loading = true;
        try
        {
            PhotoDescriptionModelPick? pick = _setting.Read();
            List<LlmProvider> local = [.. LlmSettingsService.Instance.Current.Providers
                .Where(p => p.Enabled && p.Kind != LlmProviderKind.Claude && LocalEndpoint.IsLoopback(p.Endpoint))];

            ProviderBox.ItemsSource = local;
            ProviderBox.SelectedItem = local.FirstOrDefault(p => p.Id == pick?.ProviderId);
            ProviderCardDescription(local.Count == 0);
            ModelBox.Text = pick?.Model ?? "";
            ShowModelCard();
            Section.IsResetEnabled = pick is not null;
        }
        finally
        {
            _loading = false;
        }
        _ = ListModelsAsync();
    }

    private void ProviderCardDescription(bool none)
    {
        ProviderCard.Description = none ? Loc.GetFrom(Module, "LlmPhotoDescription_NoLocalProvider") : null!;
    }

    // The model depends on the provider: hidden until one is picked.
    private void ShowModelCard() =>
        ModelCard.Visibility = ProviderBox.SelectedItem is null ? Visibility.Collapsed : Visibility.Visible;

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowModelCard();
        if (_loading) return;
        Save();
        _ = ListModelsAsync();
    }

    // What the picked provider serves, as suggestions; a provider that does
    // not answer leaves the field free to type in.
    private async Task ListModelsAsync()
    {
        int version = ++_listingVersion;
        _modelNames = [];
        if (ProviderBox.SelectedItem is not LlmProvider provider) return;
        using var cts = new CancellationTokenSource(ListingTimeout);
        ProviderListing listing = await ProviderModelCatalog.ListAsync(provider, null, cts.Token);
        if (version != _listingVersion) return;
        _modelNames = [.. listing.Names];
    }

    private List<string> Filter(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? _modelNames
            : [.. _modelNames.Where(n => n.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))];

    private void ModelBox_GotFocus(object sender, RoutedEventArgs e)
    {
        ModelBox.ItemsSource = Filter(null);
        ModelBox.IsSuggestionListOpen = _modelNames.Count > 0;
    }

    private void ModelBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        sender.ItemsSource = Filter(sender.Text);
    }

    private void ModelBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string selected) sender.Text = selected;
    }

    private void ModelBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        sender.Text = args.ChosenSuggestion as string ?? args.QueryText ?? "";
        Save();
    }

    private void ModelBox_LostFocus(object sender, RoutedEventArgs e) => Save();

    // A choice is a provider and a model: with either missing, nothing is
    // chosen, and Deckle Folders says so where photos are described.
    private void Save()
    {
        if (_setting is null) return;
        string model = ModelBox.Text?.Trim() ?? "";
        PhotoDescriptionModelPick? pick = ProviderBox.SelectedItem is LlmProvider provider && model.Length > 0
            ? new PhotoDescriptionModelPick(provider.Id, model)
            : null;
        if (pick == _setting.Read()) return;
        _setting.Write(pick);
        Section.IsResetEnabled = pick is not null;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _setting?.Write(null);
        Reload();
    }
}
