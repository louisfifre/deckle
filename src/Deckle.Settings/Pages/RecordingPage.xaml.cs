using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Settings;
using Deckle.Shell;

namespace Deckle.Settings;

// ── RecordingPage ───────────────────────────────────────────────────────────
//
// Extracted from GeneralPage in slice S3. In pass2 the Behaviour
// settings (auto-paste + overlay HUD) were moved back to GeneralPage —
// what remains is the capture pipeline itself : microphone device
// selection (Win32 waveIn enumeration) and voice level window
// calibration (sliders + auto-calibration toggle). Same patterns as
// GeneralPage / DiagnosticsPage : NavigationCacheMode.Required and
// the _initializing guard around the initial sync pass.
public sealed partial class RecordingPage : Page
{
    public RecordingViewModel ViewModel { get; } = new();

    private bool _initializing;

    public RecordingPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ComposePreprocessingSection();
        ComposeVoiceLevelSection();
        LoadAndSync();
    }

    // ── Composed pre-processing card ──────────────────────────────────────────
    //
    // The page only hosts: it hands the host panel and the ViewModel's settings
    // manifest (declared beside the VM in RecordingViewModel.Settings.cs) to the
    // composer, which builds the single SettingsCard. The composer subscribes to
    // the ViewModel so the toggle reflects Load() and the section "Reset" without
    // any per-toggle binding here. Composed before LoadAndSync so the
    // subscription catches Load()'s PropertyChanged. Held in a field so the
    // subscription lives as long as the (cached) page.
    private SettingsComposer? _preprocessingComposer;

    private void ComposePreprocessingSection()
    {
        _preprocessingComposer = new SettingsComposer(PreprocessingHost, ViewModel);
        _preprocessingComposer.Compose(ViewModel.PreprocessingSettings);
        // The section "Reset" link spans BOTH composed regions, so either one going
        // dirty must re-gate it. The composer raises DirtyChanged at the end of every
        // RefreshAll (post-settle), so the handler reads true aggregate dirtiness.
        _preprocessingComposer.DirtyChanged += OnComposedDirtyChanged;
    }

    // ── Composed voice-level group ────────────────────────────────────────────
    //
    // Same host-only pattern as the pre-processing card: the page hands the host
    // panel and the VM's voice-level manifest to the composer, which builds the
    // SettingsExpander (master toggle + three sliders). The master projects the
    // inverse of LevelWindowAutoCalibration, so it reads "set the window manually";
    // the composer hides the sliders while the master is off (auto on). Composed
    // before LoadAndSync so the subscription catches Load()'s PropertyChanged, and
    // held in a field so the subscription lives as long as the (cached) page.
    private SettingsComposer? _voiceLevelComposer;

    private void ComposeVoiceLevelSection()
    {
        _voiceLevelComposer = new SettingsComposer(VoiceLevelHost, ViewModel);
        _voiceLevelComposer.Compose(ViewModel.VoiceLevelSettings);
        // Same aggregate gating as the pre-processing region — see ComposePreprocessingSection.
        _voiceLevelComposer.DirtyChanged += OnComposedDirtyChanged;
    }

    private void OnComposedDirtyChanged(object? sender, EventArgs e) => GateResetLink();

    // The section "Reset" link is active-when-dirty (Playground model): enabled only
    // while a composed value differs from its default. Recomputed off the union of
    // both composers' IsDirty() whenever either raises DirtyChanged, AND once after
    // Load() — Load only raises PropertyChanged for values it actually changes, so on
    // a clean profile (loaded values equal the POCO-seeded defaults) no DirtyChanged
    // fires and this explicit call is what settles the link to disabled. The device-id
    // is deliberately NOT folded in — it is hardware enumeration, not a defaulted
    // setting, so it never gates this link (a greyed link on a clean section, with a
    // non-default device, is the accepted outcome).
    private void GateResetLink()
    {
        ResetRecordingLink.IsEnabled =
            (_preprocessingComposer?.IsDirty() ?? false) ||
            (_voiceLevelComposer?.IsDirty() ?? false);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadAndSync();
    }

    private void LoadAndSync()
    {
        _initializing = true;
        ViewModel.Load();
        PopulateAudioInputDevices();
        // Settle the reset link off the freshly-loaded values — Load() may have raised
        // no PropertyChanged (clean profile), so DirtyChanged would not have fired.
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

    // Section "Reset": two halves with different owners. The composed values (the
    // pre-processing toggle and the voice-level group) go back through their
    // composers' ResetAll(), each value driven to its POCO-sourced default via the
    // normal setter + RefreshAll round-trip — which also re-gates this link through
    // OnComposedDirtyChanged. The microphone device is NOT composed (runtime waveIn
    // enumeration, not a defaulted setting), so its reset stays hand-authored here.
    //
    // The device reset writes the VM property directly (= -1, system default) so its
    // OnAudioInputDeviceIdChanged push persists the change — exactly what the deleted
    // VM ResetRecordingDefaults used to do. The combo then follows cosmetically under
    // the _initializing guard: SelectedIndex = 0 must NOT re-enter the VM, otherwise
    // it would either no-op (already 0) or fight the value we just set. Relying on the
    // combo's SelectionChanged alone would not work — the guard suppresses its push,
    // so the source-of-truth write has to be the VM assignment above.
    //
    // The section-reset logging, formerly inside that deleted VM method, now lives at
    // this gesture — the one place that resets the whole section.
    private void ResetRecording_Click(object sender, RoutedEventArgs e)
    {
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
