using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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

    // Defaults resolved from POCOs — single source of truth for Reset.
    // ModelsDirectory's default lives on TranscriptionSettings since slice C2b
    // (it migrated off PathsSettings into the Whisp module's POCO).
    private static readonly TranscriptionSettings _transcriptionDefaults = new();
    private static readonly EngineSettings _engineDefaults = new();
    private static readonly DecodingSettings _decodingDefaults = new();
    private static readonly ConfidenceSettings _confidenceDefaults = new();
    private static readonly OutputFilterSettings _outputDefaults = new();
    private static readonly ContextSettings _contextDefaults = new();

    public WhisperPage()
    {
        DeckleWhispSource.Log.PageInitStart();
        try
        {
            InitializeComponent();
            DeckleWhispSource.Log.PageInitComplete();

            // WinUI 3 release bug: cannot set Minimum > defaultValue in XAML
            // without a parser crash under trimming. We set Minimum (and
            // Maximum for LogprobSlider) in code-behind. The x:Bind TwoWay
            // binding has already set Value from the VM constructor defaults
            // during InitializeComponent — those defaults are chosen to be
            // valid with Minimum=0 and default Maximum, so no clamping issue
            // except for LogprobSlider (VM default -1.0 gets clamped to 0,
            // then to -0.4 when Maximum is set; Load() corrects it).
            EntropySlider.Minimum = 1.5;
            LogprobSlider.Minimum = -1.5;
            LogprobSlider.Maximum = -0.4;
            NoSpeechSlider.Minimum = 0.05;

            DeckleWhispSource.Log.PageBuggedSliderSet();
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
        ComposeVadSection();
        ComposeStreamingSection();

        Loaded += (_, _) =>
        {
            DeckleWhispSource.Log.PageLoadedStart();
            try
            {
                // Hover reveal for reset buttons — one-time setup.
                // ModelCard is a SettingsExpander (with the Models directory
                // as a child) so we hook PointerEntered/Exited directly,
                // same as InitialPromptCard. WireHover only handles
                // SettingsCard. The hover wiring on ModelCard reveals
                // ModelReset only — ModelsDirectoryReset gets its own
                // hover via the inner card's bubbled pointer events.
                ModelCard.PointerEntered += (_, _) =>
                {
                    ModelReset.Opacity = 1;
                    ModelsDirectoryReset.Opacity = 1;
                };
                ModelCard.PointerExited += (_, _) =>
                {
                    ModelReset.Opacity = 0;
                    ModelsDirectoryReset.Opacity = 0;
                };
                WireHover(UseGpuCard, UseGpuReset);
                WireHover(LanguageCard, LanguageReset);
                InitialPromptCard.PointerEntered += (_, _) => InitialPromptReset.Opacity = 1;
                InitialPromptCard.PointerExited += (_, _) => InitialPromptReset.Opacity = 0;
                // FolderPickerEditableCard.DefaultPath drives the TextBox
                // PlaceholderText shown when ModelsDirectory is empty (the
                // legacy "(auto)" placeholder is gone — users see the actual
                // resolved path instead). Set once on first load; the value
                // is stable for the lifetime of the process.
                ModelsDirectoryPicker.DefaultPath = AppPaths.ModelsDirectory;
                // The VAD fold (toggle + four Silero parameters) is composed now —
                // the composer wires its own per-card reset reveal, so no WireHover
                // for those cards here.
                WireHover(TemperatureCard, TemperatureReset);
                WireHover(TemperatureIncrementCard, TemperatureIncrementReset);
                WireHover(EntropyCard, EntropyReset);
                WireHover(LogprobCard, LogprobReset);
                WireHover(NoSpeechCard, NoSpeechReset);
                WireHover(SuppressNstCard, SuppressNstReset);
                WireHover(SuppressBlankCard, SuppressBlankReset);
                WireHover(UseContextCard, UseContextReset);
                WireHover(MaxTokensCard, MaxTokensReset);
                SuppressRegexCard.PointerEntered += (_, _) => SuppressRegexReset.Opacity = 1;
                SuppressRegexCard.PointerExited += (_, _) => SuppressRegexReset.Opacity = 0;

                // React to VM property changes for side effects (restart
                // state, model folder re-scan).
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

    // ── Composed folds (VAD + Streaming) ────────────────────────────────────
    //
    // The page only hosts: each method hands the host StackPanel and the VM's
    // settings manifest (declared beside the VM in WhisperViewModel.Settings.cs)
    // to a composer, which builds the SettingsExpander — master toggle, child
    // cards, per-card and per-group reset — and subscribes to the VM so the
    // controls reflect Load() and any external reset without per-control binding
    // here. Composed in the constructor (before the first Load() in OnNavigatedTo)
    // so the subscription catches Load()'s refresh; held in fields so it lives as
    // long as the cached page.

    private SettingsComposer? _vadComposer;
    private SettingsComposer? _streamingComposer;

    private void ComposeVadSection()
    {
        _vadComposer = new SettingsComposer(VadHost, ViewModel);
        _vadComposer.Compose(ViewModel.VadSettings);
    }

    private void ComposeStreamingSection()
    {
        _streamingComposer = new SettingsComposer(StreamingHost, ViewModel);
        _streamingComposer.Compose(ViewModel.StreamingSettings);
    }

    // NavigationCacheMode.Required reuses the page instance. Loaded + hover
    // wiring only fire once (first navigation). On subsequent navigations we
    // reload settings from the POCO via the VM.
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

    // ── Slider display text ─────────────────────────────────────────────────
    //
    // ValueChanged fires both from user interaction and from binding updates
    // (during Load). The handlers only update the display TextBlock — all
    // persistence flows through the VM via x:Bind TwoWay.

    private static string Fmt(double v) =>
        v.ToString("0.0", CultureInfo.InvariantCulture);

    private static string FmtTwo(double v) =>
        v.ToString("0.00", CultureInfo.InvariantCulture);

    private void TemperatureSlider_ValueChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        TemperatureValue.Text = Fmt(e.NewValue);

    private void TemperatureIncrementSlider_ValueChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        TemperatureIncrementValue.Text = Fmt(e.NewValue);
        TemperatureIncrementWarning.IsOpen = e.NewValue == 0.0;
    }

    private void EntropySlider_ValueChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        EntropyValue.Text = Fmt(e.NewValue);

    private void LogprobSlider_ValueChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        LogprobValue.Text = FmtTwo(e.NewValue);

    private void NoSpeechSlider_ValueChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        NoSpeechValue.Text = FmtTwo(e.NewValue);

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

    private void ModelsDirectoryReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ModelsDirectory = _transcriptionDefaults.ModelsDirectory;

    private void ModelReset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Model = _engineDefaults.Model;
        ModelSuggest.Text = _engineDefaults.Model;
    }

    private void UseGpuReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.UseGpu = _engineDefaults.UseGpu;

    private void LanguageReset_Click(object sender, RoutedEventArgs e)
    {
        _initializing = true;
        LanguageCombo.Text = _engineDefaults.Language;
        _initializing = false;
        ViewModel.Language = _engineDefaults.Language;
    }

    private void InitialPromptReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.InitialPrompt = _engineDefaults.InitialPrompt;

    private void TemperatureReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.Temperature = _decodingDefaults.Temperature;

    private void TemperatureIncrementReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.TemperatureIncrement = _decodingDefaults.TemperatureIncrement;

    private void EntropyReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.EntropyThreshold = _confidenceDefaults.EntropyThreshold;

    private void LogprobReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.LogprobThreshold = _confidenceDefaults.LogprobThreshold;

    private void NoSpeechReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.NoSpeechThreshold = _confidenceDefaults.NoSpeechThreshold;

    private void SuppressNstReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.SuppressNonSpeechTokens = _outputDefaults.SuppressNonSpeechTokens;

    private void SuppressBlankReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.SuppressBlank = _outputDefaults.SuppressBlank;

    private void SuppressRegexReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.SuppressRegex = _outputDefaults.SuppressRegex;

    private void UseContextReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.UseContext = _contextDefaults.UseContext;

    private void MaxTokensReset_Click(object sender, RoutedEventArgs e) =>
        ViewModel.MaxTokens = _contextDefaults.MaxTokens;

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
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Settings_ResetWhisperDialog_Title"),
            Content = Loc.Get("Settings_ResetWhisperDialog_Content"),
            PrimaryButtonText = Loc.Get("WhisperPageResetAllLabel.Text"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
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
