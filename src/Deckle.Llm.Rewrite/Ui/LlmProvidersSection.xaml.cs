using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Deckle.Catalog;
using Deckle.Llm;
using Deckle.Security;

namespace Deckle.Llm.Rewrite;

// ─── LlmPage Providers section ──────────────────────────────────────────────
//
// The editable list of places a model is served from. Each card writes its
// fields to settings as they change; the API key alone goes to the secret
// vault, under a name derived from the provider's Id, so settings.json never
// carries a secret and deleting the provider removes its key with it.
//
// Two events, because the page reacts differently: ProviderNamesChanged on a
// rename, which only the profiles' provider pickers show; ProvidersChanged on
// anything that changes what a provider serves (kind, address, enabled, key,
// add, delete, reset), which the page answers with a network refresh.

public sealed partial class LlmProvidersSection : UserControl
{
    public ObservableCollection<ProviderViewModel> Providers { get; } = new();

    public event EventHandler? ProviderNamesChanged;
    public event EventHandler? ProvidersChanged;

    private readonly ISecretVault _vault = SecretVault.CreateDefault();

    public LlmProvidersSection()
    {
        InitializeComponent();
    }

    public void Reload()
    {
        Providers.Clear();
        var s = LlmSettingsService.Instance.Current;
        for (int i = 0; i < s.Providers.Count; i++)
        {
            var vm = new ProviderViewModel();
            vm.LoadFrom(i, s.Providers[i], isNew: false, hasKeyStored: HasKey(s.Providers[i].Id));
            Track(vm);
            Providers.Add(vm);
        }
    }

    private bool HasKey(string providerId)
    {
        try { return _vault.Contains(LlmProviderSecrets.ApiKeyName(providerId)); }
        catch (SecretVaultException ex)
        {
            ShowError(Loc.Get("Settings_VaultError_Title"), ex.Message);
            return false;
        }
    }

    // No unsubscribe: the section outlives the VMs it subscribes to.
    private void Track(ProviderViewModel vm) => vm.PropertyChanged += OnProviderPropertyChanged;

    private void OnProviderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ProviderViewModel.Name):
                ProviderNamesChanged?.Invoke(this, EventArgs.Empty);
                break;
            case nameof(ProviderViewModel.KindIndex):
            case nameof(ProviderViewModel.Endpoint):
            case nameof(ProviderViewModel.Enabled):
                ProvidersChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    // ── TextChanged → push value immediately for live auto-save ────────────
    //
    // x:Bind TwoWay on TextBox updates the source on LostFocus only. These
    // handlers push the value on every keystroke so auto-save fires during
    // typing.

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is ProviderViewModel vm)
            vm.Name = tb.Text;
    }

    private void EndpointBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is ProviderViewModel vm)
            vm.Endpoint = tb.Text;
    }

    // ── API key: commands on the vault ──────────────────────────────────────

    private void KeyBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is PasswordBox box)
        {
            SaveKey(box);
            e.Handled = true;
        }
    }

    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        // The PasswordBox is the button's sibling in the same row; the Tag
        // resolves the card, the visual tree resolves the box.
        if (sender is not FrameworkElement fe || fe.Parent is not StackPanel row) return;
        foreach (var child in row.Children)
        {
            if (child is PasswordBox box) { SaveKey(box); return; }
        }
    }

    private void SaveKey(PasswordBox box)
    {
        if (box.Tag is not ProviderViewModel vm) return;
        string key = box.Password.Trim();
        if (key.Length == 0) return;

        try
        {
            _vault.Set(LlmProviderSecrets.ApiKeyName(vm.Id), key);
            box.Password = "";
            vm.HasKeyStored = true;
            ErrorBar.IsOpen = false;
            ProvidersChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (SecretVaultException ex)
        {
            ShowError(Loc.Get("Settings_VaultError_Title"), ex.Message);
        }
    }

    private void RemoveKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ProviderViewModel vm) return;
        try
        {
            _vault.Remove(LlmProviderSecrets.ApiKeyName(vm.Id));
            vm.HasKeyStored = false;
            ProvidersChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (SecretVaultException ex)
        {
            ShowError(Loc.Get("Settings_VaultError_Title"), ex.Message);
        }
    }

    // ── Add / delete / reset ────────────────────────────────────────────────

    private void AddProvider_Click(object sender, RoutedEventArgs e)
    {
        var s = LlmSettingsService.Instance.Current;
        s.Providers.Add(new LlmProvider
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12),
            Name = "",
            Kind = LlmProviderKind.OpenAiCompatible,
            Endpoint = "",
            Enabled = true,
        });
        LlmSettingsService.Instance.Save();

        int index = s.Providers.Count - 1;
        var vm = new ProviderViewModel();
        vm.LoadFrom(index, s.Providers[index], isNew: true, hasKeyStored: false);
        Track(vm);
        Providers.Add(vm);

        ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void DeleteProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ProviderViewModel vm)
            return;

        // Profiles that pointed at it keep their ProviderId: they fail
        // visibly at the next rewrite rather than silently switching.
        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_RemoveProviderDialog_Title"),
                Loc.Format("Settings_RemoveProviderDialog_Content_Format", vm.Name),
                Loc.Get("Common_Remove"),
                IsDestructive: true));
        if (!confirmed)
            return;

        var providers = LlmSettingsService.Instance.Current.Providers;
        if (vm.ProviderIndex < providers.Count)
        {
            string id = providers[vm.ProviderIndex].Id;
            providers.RemoveAt(vm.ProviderIndex);
            LlmSettingsService.Instance.Save();
            try { _vault.Remove(LlmProviderSecrets.ApiKeyName(id)); }
            catch (SecretVaultException ex) { ShowError(Loc.Get("Settings_VaultError_Title"), ex.Message); }
        }
        Reload();
        ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    // Scope: the Providers list only, replaced by the defaults. Keys stay in
    // the vault: the defaults reuse the same ids, so a stored Claude key
    // survives the reset.
    private async void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_ResetProvidersDialog_Title"),
                Loc.Get("Settings_ResetProvidersDialog_Content"),
                Loc.Get("Common_Reset"),
                IsDestructive: true));
        if (!confirmed)
            return;

        LlmSettingsService.Instance.Current.Providers = new LlmSettings().Providers;
        LlmSettingsMigrations.Apply(LlmSettingsService.Instance.Current);
        LlmSettingsService.Instance.Save();
        Reload();
        ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowError(string title, string message)
    {
        ErrorBar.Title = title;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
