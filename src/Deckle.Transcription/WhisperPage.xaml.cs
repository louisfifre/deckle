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
    // when inapplicable, not greyed. The segmenter parameter cards appear only
    // when the streaming pipeline is on.
    private Visibility VisibleWhenStreaming(bool on) =>
        on ? Visibility.Visible : Visibility.Collapsed;

    // Guards the Language combo's SelectionChanged and the model
    // AutoSuggestBox's SuggestionChosen during initial sync — these handlers
    // set VM properties which would trigger PushToSettings() needlessly.
    private bool _initializing;

    // Full list of installed Whisper models on disk, scanned once per
    // page entry. Feeds the model AutoSuggestBox as a filterable source.
    private List<string> _modelNames = new();

    // Values at page load that require a restart. The footer only appears
    // when the current value differs from the snapshot.
    private string _startupModel = "";
    private bool _startupUseGpu;

    // Defaults resolved from the Engine POCO — single source of truth for the
    // bespoke Model / Language / InitialPrompt resets below.
    private static readonly EngineSettings _engineDefaults = new();
    // ModelsDirectory's default no longer has a code-behind home either: its card is
    // composed now, reading its default (new TranscriptionSettings().ModelsDirectory)
    // from the manifest, like the OutputFilter / Context / Decoding / Confidence cards.

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

        // Make the Page focusable so clicking on the background can
        // steal focus from the active control (triggering LostFocus →
        // binding update → auto-save).
        IsTabStop = true;

        NavigationCacheMode = NavigationCacheMode.Required;

        // Compose the VAD and Streaming folds before the first Load(). Load() runs
        // in OnNavigatedTo (after this constructor), so each composer's
        // PropertyChanged subscription is already in place to catch Load()'s refresh
        // — the same Compose-before-Load ordering RecordingPage uses. The composers
        // are held in fields so their subscriptions live as long as the cached page.
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
                // GPU acceleration and the models directory are composed now — the
                // composer wires each card's own per-card reset and hover reveal (and
                // the Path card's AppPaths fallback rides in PathArgs.DefaultPath), so
                // no WireHover or DefaultPath assignment for those cards here.
                // The VAD fold (toggle + four Silero parameters) is composed now —
                // the composer wires its own per-card reset reveal, so no WireHover
                // for those cards here.
                // Output filters (SuppressNst / SuppressBlank / SuppressRegex),
                // Context (UseContext / MaxTokens), Decoding (beam search + temperature)
                // and Confidence (the three thresholds) are composed now — the composer
                // wires each card's own per-card reset and hover reveal, so no WireHover
                // for those cards here.

                // React to VM property changes for side effects (restart
                // state, model folder re-scan).
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
    //
    // The page only hosts: each method hands the host StackPanel and the VM's
    // settings manifest (declared beside the VM in WhisperViewModel.Settings.cs)
    // to a composer, which builds the SettingsExpander — master toggle, child
    // cards, per-card and per-group reset — and subscribes to the VM so the
    // controls reflect Load() and any external reset without per-control binding
    // here. Composed in the constructor (before the first Load() in OnNavigatedTo)
    // so the subscription catches Load()'s refresh; held in fields so it lives as
    // long as the cached page.

    private readonly List<SettingsComposer> _composers = new();
    private SettingsComposer? _behaviourComposer;

    private void ComposeSettings()
    {
        SettingsComposer behaviourComposer = ComposeRegion(
            BehaviourHost,
            ViewModel.BehaviourSettings,
            composer => composer.DirtyChanged += (_, _) =>
                BehaviourResetLink.IsEnabled = composer.IsDirty());
        _behaviourComposer = behaviourComposer;

        ComposeRegion(FileTranscriptionHost, ViewModel.FileTranscriptionSettingsManifest);
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
        composer.Compose(settings);
        _composers.Add(composer);
        return composer;
    }

    // NavigationCacheMode.Required reuses the page instance. On every navigation
    // we reload settings from the POCO via the VM; constructor-time hover wiring
    // remains unique for the lifetime of the cached page.
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
    //
    // During _initializing (Load + combo population), these are skipped.
    // After that, user interaction triggers:
    //   Model / UseGpu → restart footer
    //   ModelsDirectory → re-scan model combo

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
    //
    // Model is an AutoSuggestBox (no dropdown chevron) populated dynamically
    // from disk: filtering by substring while typing, full list on focus
    // when empty. Language is an editable ComboBox with ComboBoxItem children.

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
        // When the field gains focus, show the full list so the user can
        // browse all available models without typing. Matches the ComboBox
        // affordance the control replaces.
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
    //
    // Set the VM property (or combo for Model/Language) → OnXChanged fires →
    // PushToSettings. For combos, SelectionChanged fires → handler sets VM.

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

    // The output-filter (SuppressNst / SuppressBlank / SuppressRegex), context
    // (UseContext / MaxTokens), decoding (beam search + temperature) and confidence
    // (the three thresholds) reset handlers are gone: those cards are composed, and
    // the composer drives each reset to its POCO-sourced default through the normal
    // VM setter — the same round-trip these hand-authored handlers performed.

    // ── Restart state — highlight + footer ─────────────────────────────────
    //
    // Model and GPU require a restart. When their current value differs from
    // the startup snapshot, the footer appears (pattern Windows Terminal).

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
            // Revert the VM properties to the startup snapshot.
            ViewModel.Model = _startupModel;
            ViewModel.UseGpu = _startupUseGpu;

            // Sync the AutoSuggestBox (not bound).
            ModelSuggest.Text = _startupModel;
        }
        finally
        {
            _initializing = false;
        }

        // The VM's OnChanged handlers are NOT suppressed by _initializing
        // (they check _isSyncing, not _initializing). But since we set the
        // VM properties directly, PushToSettings fires and saves. The
        // _initializing guard only prevents the combo handler from double-
        // writing. Model and UseGpu are now reverted — update footer.
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

        // After slice C2b, all Whisper settings (including ModelsDirectory)
        // live in a single TranscriptionSettings POCO at modules/transcription/settings.json.
        // PathsSettings used to be reset alongside, but BackupDirectory is
        // unrelated to Whisper — leaving it untouched fixes a long-standing
        // pre-existing bug where "Reset Whisper" wiped the user's backup
        // directory override.
        TranscriptionSettingsService.Instance.Replace(new TranscriptionSettings());

        // Reload everything from the fresh POCO defaults.
        _initializing = true;
        ViewModel.Load();
        RefreshModelNames();
        SyncLanguageCombo();
        _initializing = false;
        UpdateRestartState();
    }
}
