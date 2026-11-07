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
        // Before LoadAndSync, so the meter's marks follow Load()'s voice level.
        VoiceMeter.ViewModel = ViewModel;
        // The microphone check and the listening test open the microphone on
        // their own; Listen closes first, since two waveIn opens of one device
        // at once are not known to be safe. The page and the view-model live
        // and die together, so the subscription needs no removal.
        ViewModel.MicrophoneNeeded += (_, _) => VoiceMeter.Stop();
        ComposeVoiceLevelSection();
        ComposeRecordingLimitSection();
        ComposePreprocessingSection();
        LoadAndSync();
    }

    // ── Composed voice-level card ─────────────────────────────────────────────
    //
    // Composed into its own host, the top row of the Voice section's one card.
    private SettingsComposer? _voiceLevelComposer;

    private void ComposeVoiceLevelSection()
    {
        _voiceLevelComposer = new SettingsComposer(VoiceLevelHost, ViewModel);
        _voiceLevelComposer.Compose(ViewModel.VoiceLevelSettings);
        _voiceLevelComposer.DirtyChanged += OnComposedDirtyChanged;
        FrameVoiceLevelCard();
    }

    // The composed card becomes the top row of the frame that holds the meter
    // too: no background or border of its own, the frame draws them. Not
    // clickable, a SettingsCard takes no pointer-over state, so the transparent
    // row never lights up alone. The meter takes the card's own padding, read
    // from the card once its style applies (the Toolkit's value, not a copy),
    // so the track starts at the edge of the card's text and the frame's
    // bottom margin matches its top.
    private void FrameVoiceLevelCard()
    {
        foreach (UIElement child in VoiceLevelHost.Children)
        {
            if (child is not CommunityToolkit.WinUI.Controls.SettingsCard card) continue;
            card.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            card.BorderThickness = new Thickness(0);
            card.Loaded += (_, _) =>
            {
                Thickness padding = card.Padding;
                VoiceMeter.Margin = new Thickness(padding.Left, 0, padding.Right, padding.Bottom);
            };
        }
    }

    // ── Composed maximum-recording-length card ────────────────────────────────
    //
    // Composed into its own host, under the voice level.
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
        MoveChecksIntoPreprocessingFold();
    }

    // The two checks move from their XAML holder into the composed fold's
    // items, after the composer's own (it has none), the GeneralPage backup
    // fold's pattern in reverse. The x:Bind bindings travel with the elements:
    // they were set on the elements, not on the holder. The holder is left
    // empty and collapsed (in XAML), so it takes no room.
    private void MoveChecksIntoPreprocessingFold()
    {
        CommunityToolkit.WinUI.Controls.SettingsExpander? fold = null;
        foreach (UIElement child in PreprocessingHost.Children)
            if (child is CommunityToolkit.WinUI.Controls.SettingsExpander expander) fold = expander;
        if (fold is null) return;

        while (PreprocessingChecks.Children.Count > 0)
        {
            UIElement card = PreprocessingChecks.Children[0];
            PreprocessingChecks.Children.RemoveAt(0);
            fold.Items.Add(card);
        }
    }

    private void OnComposedDirtyChanged(object? sender, EventArgs e) => GateResetLink();

    // The device id is deliberately not folded in: it is hardware enumeration, not
    // a defaulted setting, so a non-default device leaves the links greyed. Each
    // section's reset follows its own composers, the header's "Reset all" any
    // of them; the meter and the checks in the pre-processing fold keep no value.
    private void GateResetLink()
    {
        bool voiceDirty = _voiceLevelComposer?.IsDirty() ?? false;
        bool recordingDirty =
            (_recordingLimitComposer?.IsDirty() ?? false) ||
            (_preprocessingComposer?.IsDirty() ?? false);
        VoiceSection.IsResetEnabled = voiceDirty;
        RecordingSection.IsResetEnabled = recordingDirty;
        Header.IsResetAllEnabled = voiceDirty || recordingDirty;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadAndSync();
    }

    // The page is cached, so a listening-test clip would keep playing behind
    // the next Settings page, and the voice meter would keep the microphone
    // open; leaving the page stops both.
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.StopListeningPlayback();
        VoiceMeter.Stop();
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

    // Each section's reset covers its own composed settings only. The
    // microphone card stands above every section, so the section wheels leave
    // it alone; only the header's "Reset all" returns it to the system default.
    private void ResetVoice_Click(object sender, RoutedEventArgs e)
    {
        _voiceLevelComposer?.ResetAll();

        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Voice");
    }

    private void ResetRecording_Click(object sender, RoutedEventArgs e)
    {
        _recordingLimitComposer?.ResetAll();
        _preprocessingComposer?.ResetAll();

        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Recording");
    }

    // The microphone device is not composed, so its reset is written here. The VM
    // assignment is the write that persists (system default, -1); the combo then
    // follows under the _initializing guard so SelectedIndex = 0 does not re-enter
    // the VM and fight the value just set.
    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _voiceLevelComposer?.ResetAll();
        _recordingLimitComposer?.ResetAll();
        _preprocessingComposer?.ResetAll();

        ViewModel.AudioInputDeviceId = -1;
        _initializing = true;
        try
        {
            AudioInputCombo.SelectedIndex = 0;
        }
        finally { _initializing = false; }

        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Recording (all)");
    }
}
