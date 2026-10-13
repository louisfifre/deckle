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
    // microphone card. Part of the resettable section, so it subscribes to
    // DirtyChanged and is folded into GateResetLink.
    private SettingsComposer? _recordingLimitComposer;

    private void ComposeRecordingLimitSection()
    {
        _recordingLimitComposer = new SettingsComposer(RecordingLimitHost, ViewModel);
        _recordingLimitComposer.Compose(ViewModel.RecordingLimitSettings);
        _recordingLimitComposer.DirtyChanged += OnComposedDirtyChanged;
    }

    // ── Composed pre-processing card ──────────────────────────────────────────
    //
    // The page only hosts: the composer builds the card from the ViewModel's
    // manifest and subscribes to the ViewModel, so the toggle reflects Load()
    // and the section "Reset" without any binding here. Every composer is
    // created before LoadAndSync so its subscription catches Load()'s
    // PropertyChanged, and held in a field so the subscription lives as long as
    // the (cached) page.
    private SettingsComposer? _preprocessingComposer;

    private void ComposePreprocessingSection()
    {
        _preprocessingComposer = new SettingsComposer(PreprocessingHost, ViewModel);
        _preprocessingComposer.Compose(ViewModel.PreprocessingSettings);
        // The section "Reset" link spans every resettable composed region, so any
        // one going dirty must re-gate it. The composer raises DirtyChanged at the
        // end of every RefreshAll (post-settle), so the handler reads true
        // aggregate dirtiness.
        _preprocessingComposer.DirtyChanged += OnComposedDirtyChanged;
    }

    // ── Composed voice-level group ────────────────────────────────────────────
    private SettingsComposer? _voiceLevelComposer;

    private void ComposeVoiceLevelSection()
    {
        _voiceLevelComposer = new SettingsComposer(VoiceLevelHost, ViewModel);
        _voiceLevelComposer.Compose(ViewModel.VoiceLevelSettings);
        // Same aggregate gating as the pre-processing region — see ComposePreprocessingSection.
        _voiceLevelComposer.DirtyChanged += OnComposedDirtyChanged;
    }

    // ── Composed telemetry section ────────────────────────────────────────────
    //
    // Deliberately NOT wired into the section-reset link: this is a privacy
    // opt-in, so it has no per-card reset and its section carries no "Reset"
    // link — hence no DirtyChanged subscription and no GateResetLink
    // participation.
    private SettingsComposer? _telemetryComposer;

    private void ComposeTelemetrySection()
    {
        _telemetryComposer = new SettingsComposer(TelemetryHost, ViewModel);
        _telemetryComposer.Compose(ViewModel.TelemetrySettings);
    }

    private void OnComposedDirtyChanged(object? sender, EventArgs e) => GateResetLink();

    // The section "Reset" link is enabled only while a composed value differs
    // from its default. Recomputed off the union of the resettable composers'
    // IsDirty() whenever one raises DirtyChanged, AND once after Load() — Load
    // only raises PropertyChanged for values it actually changes, so on a clean
    // profile no DirtyChanged fires and this explicit call is what settles the
    // link to disabled. The device-id is deliberately NOT folded in — it is
    // hardware enumeration, not a defaulted setting (a greyed link on a clean
    // section, with a non-default device, is the accepted outcome).
    private void GateResetLink()
    {
        ResetRecordingLink.IsEnabled =
            (_recordingLimitComposer?.IsDirty() ?? false) ||
            (_preprocessingComposer?.IsDirty() ?? false) ||
            (_voiceLevelComposer?.IsDirty() ?? false);
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

    // Section "Reset": two halves with different owners. The composed values go
    // back through their composers' ResetAll(), each value driven to its
    // POCO-sourced default via the normal setter + RefreshAll round-trip — which
    // also re-gates this link through OnComposedDirtyChanged. The microphone
    // device is NOT composed (runtime waveIn enumeration, not a defaulted
    // setting), so its reset stays hand-authored here.
    //
    // The device reset writes the VM property directly (= -1, system default) so
    // its OnAudioInputDeviceIdChanged push persists the change. The combo then
    // follows cosmetically under the _initializing guard: SelectedIndex = 0 must
    // NOT re-enter the VM, otherwise it would either no-op (already 0) or fight
    // the value we just set. The guard suppresses the combo's push, so the
    // source-of-truth write has to be the VM assignment.
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
}
