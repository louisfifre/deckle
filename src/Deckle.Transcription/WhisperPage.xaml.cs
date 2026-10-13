using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Settings;
using Deckle.Transcription;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class WhisperPage : Page
{
    public WhisperViewModel ViewModel { get; } = new();

    // Settings-UX doctrine: an option that only makes sense in a context is hidden
    // when inapplicable, not greyed. The speech-filtering section appears only
    // when the streaming pipeline is on.
    private Visibility VisibleWhenStreaming(bool on) =>
        on ? Visibility.Visible : Visibility.Collapsed;

    // Guards the Language combo's SelectionChanged and the model
    // AutoSuggestBox's SuggestionChosen during initial sync — these handlers
    // set VM properties which would trigger PushToSettings() needlessly.
    private bool _initializing;

    private List<string> _modelNames = new();

    // Values at page load that require a restart. The footer only appears
    // when the current value differs from the snapshot.
    private string _startupModel = "";
    private bool _startupUseGpu;

    // Defaults resolved from the Engine POCO — single source of truth for the
    // bespoke Model / Language / InitialPrompt resets below.
    private static readonly EngineSettings _engineDefaults = new();

    public WhisperPage()
    {
        DeckleWhispSource.Log.PageInitStart();
        try
        {
            InitializeComponent();
            DeckleWhispSource.Log.PageInitComplete();
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.PageInitFailed();
            DeckleWhispSource.Log.PageInitFailedDetail(ex.GetType().Name, ex.Message);
            DeckleWhispSource.Log.PageStackTrace(ex.StackTrace ?? "(no stack)");
            throw;
        }

        // The page asks with its own wording (it says what the reset keeps), so the
        // header's generic confirmation stays off.
        Header.ConfirmsReset = false;

        // Make the Page focusable so clicking on the background can
        // steal focus from the active control (triggering LostFocus →
        // binding update → auto-save).
        IsTabStop = true;

        NavigationCacheMode = NavigationCacheMode.Required;

        // Composed before the first Load(), which runs in OnNavigatedTo, so each
        // composer's PropertyChanged subscription catches Load()'s refresh. The
        // composers are held in fields so their subscriptions live as long as the
        // cached page.
        ComposeSettings();

        // The cached page can be loaded again after navigation. Wire these once in
        // the constructor so hover handlers never accumulate across page entries.
        WireHover(ModelCard, ModelReset);
        WireHover(LanguageCard, LanguageReset);
        InitialPromptCard.PointerEntered += (_, _) => InitialPromptReset.Opacity = 1;
        InitialPromptCard.PointerExited += (_, _) => InitialPromptReset.Opacity = 0;

        Loaded += (_, _) =>
        {
            DeckleWhispSource.Log.PageLoadedStart();
            try
            {
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;

                DeckleWhispSource.Log.PageReady();
                DeckleWhispSource.Log.PageLoadedComplete();
            }
            catch (Exception ex)
            {
                DeckleWhispSource.Log.PageLoadedFailed();
                DeckleWhispSource.Log.PageLoadedFailedDetail(ex.GetType().Name, ex.Message);
                DeckleWhispSource.Log.PageStackTrace(ex.StackTrace ?? "(no stack)");
            }
        };
    }

    // ── Composed settings ────────────────────────────────────────────────────

    private readonly List<SettingsComposer> _composers = new();
    private SettingsComposer? _behaviourComposer;

    private void ComposeSettings()
    {
        SettingsComposer behaviourComposer = ComposeRegion(
            BehaviourHost,
            ViewModel.BehaviourSettings,
            composer => composer.DirtyChanged += (_, _) =>
                BehaviourSection.IsResetEnabled = composer.IsDirty());
        _behaviourComposer = behaviourComposer;

        ComposeRegion(UseGpuHost, ViewModel.UseGpuSettingsManifest);
        ComposeRegion(ModelsDirectoryHost, ViewModel.ModelsDirectorySettingsManifest);
        ComposeRegion(VadHost, ViewModel.VadSettings);
        ComposeRegion(StreamingHost, ViewModel.StreamingSettings);
        ComposeRegion(OutputFiltersHost, ViewModel.OutputFilterSettingsManifest);
        ComposeRegion(ContextHost, ViewModel.ContextSettingsManifest);
        ComposeRegion(DecodingHost, ViewModel.DecodingSettingsManifest);
        ComposeRegion(ConfidenceHost, ViewModel.ConfidenceSettingsManifest);
        ComposeRegion(DiagnosticsHost, ViewModel.DiagnosticsSettings);
    }

    private SettingsComposer ComposeRegion(
        Panel host,
        IReadOnlyList<SettingDescriptor> settings,
        Action<SettingsComposer>? configure = null)
    {
        var composer = new SettingsComposer(host, ViewModel);
        configure?.Invoke(composer);
        // Subscribed before Compose so its closing RefreshAll lands the first gate.
        composer.DirtyChanged += (_, _) => GateResetAll();
        composer.Compose(settings);
        _composers.Add(composer);
        return composer;
    }

    // Re-gated once after Load() too: Load() raises no PropertyChanged on a clean
    // profile, so no DirtyChanged would settle it. The bespoke Model / Language /
    // InitialPrompt cards have no composer, so they do not feed this gate.
    private void GateResetAll() =>
        Header.IsResetAllEnabled = _composers.Any(c => c.IsDirty());

    // The cached page reloads its settings on every navigation.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _initializing = true;
        ViewModel.Load();
        RefreshModelNames();
        SyncLanguageCombo();
        _startupModel = ViewModel.Model;
        _startupUseGpu = ViewModel.UseGpu;
        _initializing = false;
        GateResetAll();

        // Speech isn't provisioned until the runtime + model are on disk. When
        // they aren't, surface a call-to-action that reopens the setup wizard
        // rather than leaving the tuning controls implying a working engine.
        // The App answers the probe (this page can't see the Whisper module);
        // unwired (tests, previews) reads as provisioned.
        bool speechReady = SettingsHost.IsSpeechProvisioned?.Invoke() ?? true;
        SetupInfoBar.IsOpen = !speechReady;
    }

    private void OnSetupClick(object sender, RoutedEventArgs e) =>
        SettingsHost.OpenSetupWizard?.Invoke();

    // ── VM PropertyChanged side effects ─────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_initializing) return;
        switch (e.PropertyName)
        {
            case nameof(WhisperViewModel.Model):
            case nameof(WhisperViewModel.UseGpu):
                UpdateRestartState();
                break;
            case nameof(WhisperViewModel.ModelsDirectory):
                _initializing = true;
                try { RefreshModelNames(); } finally { _initializing = false; }
                break;
        }
    }

    // ── Combo handlers (Model, Language) ────────────────────────────────────

    private static void WireHover(SettingsCard card, Button resetButton)
    {
        card.PointerEntered += (_, _) => resetButton.Opacity = 1;
        card.PointerExited += (_, _) => resetButton.Opacity = 0;
    }

    private void RefreshModelNames()
    {
        var items = new List<string>();
        try
        {
            string dir = TranscriptionSettingsService.Instance.ResolveModelsDirectory();
            if (Directory.Exists(dir))
            {
                items = Directory.EnumerateFiles(dir, "*.bin")
                    .Select(Path.GetFileName)
                    .Where(n => n is not null && !n!.Contains("silero", StringComparison.OrdinalIgnoreCase))
                    .Select(n => n!)
                    .OrderBy(n => n)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.PageModelScanFailed();
            DeckleWhispSource.Log.PageModelScanFailedDetail(ex.Message);
        }

        string current = ViewModel.Model;
        if (!string.IsNullOrEmpty(current) && !items.Contains(current))
            items.Insert(0, current);

        _modelNames = items;
        ModelSuggest.Text = current ?? "";
        ModelSuggest.ItemsSource = _modelNames;
    }

    private void SyncLanguageCombo()
    {
        string lang = ViewModel.Language;
        for (int i = 0; i < LanguageCombo.Items.Count; i++)
        {
            if (LanguageCombo.Items[i] is ComboBoxItem item
                && item.Content is string s && s == lang)
            {
                LanguageCombo.SelectedIndex = i;
                return;
            }
        }
        // Custom language code not in the predefined list.
        LanguageCombo.SelectedIndex = -1;
        LanguageCombo.Text = lang;
    }

    private void ModelSuggest_GotFocus(object sender, RoutedEventArgs e)
    {
        // Show the full list on focus, so the models can be browsed without
        // typing.
        ModelSuggest.ItemsSource = _modelNames;
        ModelSuggest.IsSuggestionListOpen = true;
    }

    private void ModelSuggest_TextChanged(AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        string query = sender.Text ?? "";
        if (string.IsNullOrEmpty(query))
        {
            sender.ItemsSource = _modelNames;
            return;
        }

        sender.ItemsSource = _modelNames
            .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void ModelSuggest_SuggestionChosen(AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (_initializing || args.SelectedItem is not string model) return;
        ViewModel.Model = model;
    }

    private void ModelSuggest_LostFocus(object sender, RoutedEventArgs e)
    {
        // Free-form text is not a valid model — revert to the current VM
        // value so the field always shows an installed model name.
        if (!_modelNames.Contains(ModelSuggest.Text ?? ""))
            ModelSuggest.Text = ViewModel.Model ?? "";
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        string text = LanguageCombo.Text ?? "";
        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Content is string s)
            text = s;
        ViewModel.Language = text;
    }

    private void LanguageCombo_LostFocus(object sender, RoutedEventArgs e)
    {
        string text = (LanguageCombo.Text ?? "").Trim();
        if (text != ViewModel.Language)
            ViewModel.Language = text;
    }

    // ── Click-to-unfocus ────────────────────────────────────────────────────
    //
    // Tapped on the background Grid steals focus from the active control.
    // This triggers LostFocus on TextBox / NumberBox / editable ComboBox,
    // which fires their binding update → auto-save. Interactive controls
    // (Button, Slider, etc.) handle Tapped internally, so this handler
    // only fires for non-interactive areas (descriptions, spacing, margins).

    private void OnBackgroundTapped(object sender, TappedRoutedEventArgs e)
    {
        // Don't steal focus from a ComboBox — its Tapped bubbles up here
        // and re-focusing the page would close the dropdown before it opens.
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

    // ── Reset handlers ──────────────────────────────────────────────────────

    private void ResetBehaviour_Click(object sender, RoutedEventArgs e)
    {
        _behaviourComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Dictation experience");
    }

    private void ModelReset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Model = _engineDefaults.Model;
        ModelSuggest.Text = _engineDefaults.Model;
    }

    private void LanguageReset_Click(object sender, RoutedEventArgs e)
    {
        _initializing = true;
        LanguageCombo.Text = _engineDefaults.Language;
        _initializing = false;
        ViewModel.Language = _engineDefaults.Language;
    }

    private void InitialPromptReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.InitialPrompt = _engineDefaults.InitialPrompt;

    // ── Restart state — footer ──────────────────────────────────────────────
    //
    // Model and GPU require a restart. When their current value differs from
    // the startup snapshot, the footer appears (Windows Terminal pattern).

    private void UpdateRestartState()
    {
        bool dirty = ViewModel.Model != _startupModel
                  || ViewModel.UseGpu != _startupUseGpu;
        RestartFooter.Visibility = dirty
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RestartNow_Click(object sender, RoutedEventArgs e)
    {
        SettingsHost.RestartApp?.Invoke("Deckle.Transcription.WhisperPage, Deckle.Transcription");
    }

    private void RestartDiscard_Click(object sender, RoutedEventArgs e)
    {
        DeckleWhispSource.Log.PageDiscardRestartChanges();

        _initializing = true;
        try
        {
            ViewModel.Model = _startupModel;
            ViewModel.UseGpu = _startupUseGpu;

            // The AutoSuggestBox is not bound.
            ModelSuggest.Text = _startupModel;
        }
        finally
        {
            _initializing = false;
        }

        // The VM's own change handlers check _isSyncing, not _initializing, so
        // the revert still saves; _initializing only keeps the page's handlers
        // from writing twice.
        UpdateRestartState();
    }

    // ── Reset all ──────────────────────────────────────────────────────────

    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_ResetWhisperDialog_Title"),
                Loc.Get("Settings_ResetWhisperDialog_Content"),
                Loc.Get("WhisperPageResetAllLabel.Text"),
                IsDestructive: true));
        if (!confirmed)
            return;

        DeckleWhispSource.Log.PageResetAll();

        TranscriptionSettingsService.Instance.Replace(new TranscriptionSettings());

        _initializing = true;
        ViewModel.Load();
        RefreshModelNames();
        SyncLanguageCombo();
        _initializing = false;
        UpdateRestartState();
    }
}
