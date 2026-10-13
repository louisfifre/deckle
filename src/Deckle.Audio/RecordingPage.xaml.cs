using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Audio;

// ── RecordingPage ───────────────────────────────────────────────────────────
//
// Settings page of the capture pipeline. Cached (NavigationCacheMode.Required);
// the _initializing guard keeps the microphone combo from writing back to the
// ViewModel during the sync pass.
public sealed partial class RecordingPage : Page
{
    public RecordingViewModel ViewModel { get; } = new();

    private bool _initializing;

    public RecordingPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ComposeRecordingLimitSection();
        ComposePreprocessingSection();
        ComposeVoiceLevelSection();
        ComposeTelemetrySection();
        LoadAndSync();
    }

    // ── Composed maximum-recording-length card ────────────────────────────────
    //
    // Composed into its own host so it can sit right under the bespoke
    // microphone card.
    private SettingsComposer? _recordingLimitComposer;

    private void ComposeRecordingLimitSection()
    {
        _recordingLimitComposer = new SettingsComposer(RecordingLimitHost, ViewModel);
        _recordingLimitComposer.Compose(ViewModel.RecordingLimitSettings);
        _recordingLimitComposer.DirtyChanged += OnComposedDirtyChanged;
    }

    // ── Composed pre-processing card ──────────────────────────────────────────
    //
    // The composer subscribes to the ViewModel, so the toggle reflects Load() and
    // the section "Reset" without any binding here. Every composer is created
    // before LoadAndSync so its subscription catches Load()'s PropertyChanged,
    // and held in a field so the subscription lives as long as the cached page.
    private SettingsComposer? _preprocessingComposer;

    private void ComposePreprocessingSection()
    {
        _preprocessingComposer = new SettingsComposer(PreprocessingHost, ViewModel);
        _preprocessingComposer.Compose(ViewModel.PreprocessingSettings);
        // The section "Reset" link spans every resettable composed region, so any
        // one going dirty must re-gate it. The composer raises DirtyChanged at the
        // end of every RefreshAll, so the handler reads the settled dirtiness.
        _preprocessingComposer.DirtyChanged += OnComposedDirtyChanged;
    }

    // ── Composed voice-level group ────────────────────────────────────────────
    private SettingsComposer? _voiceLevelComposer;

    private void ComposeVoiceLevelSection()
    {
        _voiceLevelComposer = new SettingsComposer(VoiceLevelHost, ViewModel);
        _voiceLevelComposer.Compose(ViewModel.VoiceLevelSettings);
        _voiceLevelComposer.DirtyChanged += OnComposedDirtyChanged;
    }

    // ── Composed telemetry section ────────────────────────────────────────────
    //
    // Deliberately not wired into the section-reset link: a privacy opt-in is
    // never reset from here.
    private SettingsComposer? _telemetryComposer;

    private void ComposeTelemetrySection()
    {
        _telemetryComposer = new SettingsComposer(TelemetryHost, ViewModel);
        _telemetryComposer.Compose(ViewModel.TelemetrySettings);
    }

    private void OnComposedDirtyChanged(object? sender, EventArgs e) => GateResetLink();

    // The device id is deliberately not folded in: it is hardware enumeration, not
    // a defaulted setting, so a non-default device leaves the link greyed. The
    // header's "Reset all" shares this gate, since the page has a single resettable
    // section.
    private void GateResetLink()
    {
        bool dirty =
            (_recordingLimitComposer?.IsDirty() ?? false) ||
            (_preprocessingComposer?.IsDirty() ?? false) ||
            (_voiceLevelComposer?.IsDirty() ?? false);
        DefaultsSection.IsResetEnabled = dirty;
        Header.IsResetAllEnabled = dirty;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadAndSync();
    }

    // The page is cached, so a listening-test clip would keep playing behind
    // the next Settings page; leaving the page stops it.
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.StopListeningPlayback();
        base.OnNavigatedFrom(e);
    }

    private void LoadAndSync()
    {
        _initializing = true;
        ViewModel.Load();
        PopulateAudioInputDevices();
        // Load() raises PropertyChanged only for values it changes, so on a clean
        // profile DirtyChanged never fires and this call settles the link.
        GateResetLink();
        DispatcherQueue.TryEnqueueObserved(
            operation: "init-flag-clear", caller: "recording-page",
            callback: () => _initializing = false,
            rejectSource: "SETTINGS", rejectWhat: "init flag",
            priority: DispatcherQueuePriority.Low);
    }

    // ── Audio input ──────────────────────────────────────────────────────────
    //
    // Dynamically populated through Win32 waveIn; stays in code-behind because
    // this is hardware enumeration, not a setting. The combo has "System
    // default" at index 0, so comboIndex ↔ deviceId requires conversion.

    private void PopulateAudioInputDevices()
    {
        AudioInputCombo.Items.Clear();
        AudioInputCombo.Items.Add(Loc.Get("Settings_AudioInput_SystemDefault"));

        uint numDevs = NativeMethods.waveInGetNumDevs();
        for (uint i = 0; i < numDevs; i++)
        {
            var caps = new NativeMethods.WAVEINCAPSW();
            uint err = NativeMethods.waveInGetDevCapsW(i, ref caps,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WAVEINCAPSW>());
            string name = err == 0 ? caps.szPname : Loc.Format("Settings_AudioInput_Device_Format", i);
            AudioInputCombo.Items.Add(name);
        }

        int deviceId = ViewModel.AudioInputDeviceId;
        int comboIndex = deviceId < 0 ? 0 : deviceId + 1;
        if (comboIndex >= AudioInputCombo.Items.Count)
            comboIndex = 0;
        AudioInputCombo.SelectedIndex = comboIndex;
    }

    private void AudioInputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || AudioInputCombo.SelectedIndex < 0) return;
        int deviceId = AudioInputCombo.SelectedIndex <= 0 ? -1 : AudioInputCombo.SelectedIndex - 1;
        ViewModel.AudioInputDeviceId = deviceId;
    }

    // The microphone device is not composed, so its reset is written here. The VM
    // assignment is the write that persists (system default, -1); the combo then
    // follows under the _initializing guard so SelectedIndex = 0 does not re-enter
    // the VM and fight the value just set.
    private void ResetRecording_Click(object sender, RoutedEventArgs e)
    {
        _recordingLimitComposer?.ResetAll();
        _preprocessingComposer?.ResetAll();
        _voiceLevelComposer?.ResetAll();

        ViewModel.AudioInputDeviceId = -1;
        _initializing = true;
        try
        {
            AudioInputCombo.SelectedIndex = 0;
        }
        finally { _initializing = false; }

        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Recording");
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e) =>
        ResetRecording_Click(sender, e);
}
