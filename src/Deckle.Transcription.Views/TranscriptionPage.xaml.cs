using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Settings;
using Deckle.Transcription;
using Deckle.Diagnostics;
using Deckle.Localization;

namespace Deckle.Transcription.Views;

public sealed partial class TranscriptionPage : Page
{
    public TranscriptionViewModel ViewModel { get; } = new();

    // Guards the restart state during the initial sync: Load() sets VM
    // properties whose change handlers must not read as the person's edits.
    private bool _initializing;

    // Values at page load that require a restart. The footer only appears
    // when the current value differs from the snapshot: the model folder and
    // the encoder precision are read at each model load, and the GPU switch at
    // the next load too, so the same footer carries all three.
    private bool _startupUseGpu;
    private string _startupParakeetFolder = "";
    private ParakeetEncoderPrecision _startupParakeetEncoder;

    public TranscriptionPage()
    {
        DeckleWhispSource.Log.PageInitStart();
        try
        {
            InitializeComponent();
            DeckleWhispSource.Log.PageInitComplete();
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.PageInitFailed(ex.GetType().Name, ex.Message);
            DeckleWhispSource.Log.PageStackTrace(ex.StackTrace ?? "(no stack)");
            throw;
        }

        // The header asks; the page adds what its reset keeps.
        Header.ResetDetail = Loc.Get("Settings_ResetTranscriptionDialog_Detail");

        // Make the Page focusable so clicking on the background can
        // steal focus from the active control (triggering LostFocus →
        // binding update → auto-save).
        IsTabStop = true;

        NavigationCacheMode = NavigationCacheMode.Required;

        // Compose every region before the first Load(). Load() runs in
        // OnNavigatedTo (after this constructor), so each composer's
        // PropertyChanged subscription is already in place to catch Load()'s
        // refresh — the same Compose-before-Load ordering RecordingPage uses. The
        // composers are held in fields so their subscriptions live as long as the
        // cached page.
        ComposeSettings();

        Loaded += (_, _) =>
        {
            DeckleWhispSource.Log.PageLoadedStart();
            try
            {
                // React to VM property changes for the restart footer. Every card
                // is composed, and the composer wires each card's own per-card
                // reset and hover reveal.
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;

                DeckleWhispSource.Log.PageReady();
                DeckleWhispSource.Log.PageLoadedComplete();
            }
            catch (Exception ex)
            {
                DeckleWhispSource.Log.PageLoadedFailed(ex.GetType().Name, ex.Message);
                DeckleWhispSource.Log.PageStackTrace(ex.StackTrace ?? "(no stack)");
            }
        };
    }

    // ── Composed settings ────────────────────────────────────────────────────
    //
    // The page only hosts: each method hands the host StackPanel and the VM's
    // settings manifest (declared beside the VM in TranscriptionViewModel.Settings.cs)
    // to a composer, which builds the SettingsExpander — master toggle, child
    // cards, per-card and per-group reset — and subscribes to the VM so the
    // controls reflect Load() and any external reset without per-control binding
    // here. Composed in the constructor (before the first Load() in OnNavigatedTo)
    // so the subscription catches Load()'s refresh; held in fields so it lives as
    // long as the cached page.

    // The value composers whose values the page's Reset all covers. The streaming
    // switch is composed apart (_switchComposer) and is not in this list: on or
    // off is the person's choice, not a value, so neither the reset nor its gate
    // reads it.
    private readonly List<SettingsComposer> _composers = new();
    // The Segmentation section's composers: counted by Reset all and its gate
    // only while the section is shown (developer mode on at navigation).
    private readonly List<SettingsComposer> _segmentationComposers = new();
    private SettingsComposer? _switchComposer;
    private SettingsComposer? _overlayComposer;
    private SettingsComposer? _recordingLimitComposer;
    private bool _segmentationShown;

    private void ComposeSettings()
    {
        _switchComposer = new SettingsComposer(StreamingSwitchHost, ViewModel);
        _switchComposer.Compose(ViewModel.StreamingSwitch);

        _overlayComposer = ComposeRegion(BehaviourHost, ViewModel.BehaviourSettings);
        _recordingLimitComposer = ComposeRegion(RecordingLimitHost, ViewModel.RecordingLimitSettings);

        ComposeRegion(SpeechEngineHost, ViewModel.SpeechEngineSettings);
        ComposeRegion(ModelsDirectoryHost, ViewModel.ModelsDirectorySettingsManifest);
        ComposeRegion(UseGpuHost, ViewModel.UseGpuSettingsManifest);

        ComposeRegion(SegmentationHost, ViewModel.SegmentationSettings, _segmentationComposers);
        ComposeRegion(SilenceGuardsHost, ViewModel.SilenceGuardSettings, _segmentationComposers);
        // The lexicon manifest composes on LexiconPage, this module's other
        // page; the dataset consents compose on the Diagnostics page
        // (TranscriptionTelemetryViewModel).
    }

    private SettingsComposer ComposeRegion(
        Panel host,
        IReadOnlyList<SettingDescriptor> settings,
        List<SettingsComposer>? into = null)
    {
        var composer = new SettingsComposer(host, ViewModel);
        // Subscribed before Compose so its closing RefreshAll lands the first gate.
        composer.DirtyChanged += (_, _) => GateResetAll();
        composer.Compose(settings);
        (into ?? _composers).Add(composer);
        return composer;
    }

    // The composers Reset all covers right now: the page's own, plus the
    // Segmentation section's while it is shown. Hidden values are neither reset
    // nor counted by the gate.
    private IEnumerable<SettingsComposer> ResettableComposers() =>
        _segmentationShown ? _composers.Concat(_segmentationComposers) : _composers;

    // The page-level "Reset all" is active-when-dirty: enabled only while some
    // value on the page differs from its default. Re-gated off every composer's
    // DirtyChanged, and once after Load() (OnNavigatedTo), since Load() raises no
    // PropertyChanged on a clean profile.
    private void GateResetAll()
    {
        Header.IsResetAllEnabled = ResettableComposers().Any(c => c.IsDirty());
        // The section's own wheel follows its two composed regions, not the switch.
        WhileYouSpeakSection.IsResetEnabled =
            (_overlayComposer?.IsDirty() ?? false) || (_recordingLimitComposer?.IsDirty() ?? false);
    }

    // NavigationCacheMode.Required reuses the page instance. On every navigation
    // we reload settings from the POCO via the VM.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _initializing = true;
        ViewModel.Load();
        _startupUseGpu = ViewModel.UseGpu;
        _startupParakeetFolder = ViewModel.ParakeetModelDirectory;
        _startupParakeetEncoder = ViewModel.ParakeetEncoder;
        _initializing = false;
        UpdateRestartState();

        // The Segmentation section follows developer mode, read here at each
        // navigation: a person who turns it on in General finds the section on
        // coming back to this page.
        _segmentationShown = SettingsService.Instance.Current.Developer.DeveloperMode;
        SegmentationSection.Visibility = _segmentationShown ? Visibility.Visible : Visibility.Collapsed;
        GateResetAll();

        // Speech isn't provisioned until the model is on disk. When it isn't,
        // surface a call-to-action that reopens the setup wizard rather than
        // leaving the tuning controls implying a working engine. The App answers
        // the probe (this page can't see the Parakeet module); unwired (tests,
        // previews) reads as provisioned.
        bool speechReady = SettingsHost.IsSpeechProvisioned?.Invoke() ?? true;
        SetupInfoBar.IsOpen = !speechReady;
    }

    private void OnSetupClick(object sender, RoutedEventArgs e) =>
        SettingsHost.OpenSetupWizard?.Invoke();

    // ── VM PropertyChanged side effects ─────────────────────────────────────
    //
    // During _initializing (Load), these are skipped. After that, user
    // interaction on a load-time value shows the restart footer.

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_initializing) return;
        switch (e.PropertyName)
        {
            case nameof(TranscriptionViewModel.UseGpu):
            case nameof(TranscriptionViewModel.ParakeetModelDirectory):
            case nameof(TranscriptionViewModel.ParakeetEncoder):
                UpdateRestartState();
                break;
        }
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

    // The section's reset covers the overlay and the recording length, never the
    // streaming switch.
    private void ResetWhileYouSpeak_Click(object sender, RoutedEventArgs e)
    {
        _overlayComposer?.ResetAll();
        _recordingLimitComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("While you speak");
    }

    // ── Restart state — highlight + footer ─────────────────────────────────
    //
    // The model folder, the encoder precision and the GPU switch require a
    // restart. When their current value differs from the startup snapshot, the
    // footer appears (pattern Windows Terminal).

    private void UpdateRestartState()
    {
        bool dirty = ViewModel.UseGpu != _startupUseGpu
                  || ViewModel.ParakeetModelDirectory != _startupParakeetFolder
                  || ViewModel.ParakeetEncoder != _startupParakeetEncoder;
        RestartFooter.Visibility = dirty
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RestartNow_Click(object sender, RoutedEventArgs e)
    {
        SettingsHost.RestartApp?.Invoke("Deckle.Transcription.Views.TranscriptionPage, Deckle.Transcription.Views");
    }

    private void RestartDiscard_Click(object sender, RoutedEventArgs e)
    {
        DeckleWhispSource.Log.PageDiscardRestartChanges();

        _initializing = true;
        try
        {
            // Revert the VM properties to the startup snapshot.
            ViewModel.UseGpu = _startupUseGpu;
            ViewModel.ParakeetModelDirectory = _startupParakeetFolder;
            ViewModel.ParakeetEncoder = _startupParakeetEncoder;
        }
        finally
        {
            _initializing = false;
        }

        // The VM's OnChanged handlers are NOT suppressed by _initializing
        // (they check _isSyncing, not _initializing), so PushToSettings fires and
        // saves the reverted values. The footer follows.
        UpdateRestartState();
    }

    // ── Reset all ──────────────────────────────────────────────────────────

    // The header has already confirmed, as on every page.
    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        DeckleWhispSource.Log.PageResetAll();

        // Resets exactly the cards this page shows: every card is composed, so
        // each composer drives its cards to their POCO-sourced defaults. The
        // lexicon values live in the same TranscriptionSettings but belong to
        // their own page, so they stay; so does the streaming switch, and the
        // Segmentation values while the section is hidden. The overlay card
        // lives in AppSettings and the recording length in the capture settings.
        foreach (SettingsComposer composer in ResettableComposers().ToList()) composer.ResetAll();
        UpdateRestartState();
    }
}
