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
        // their own; Listen closes first, so the take is not shared with a
        // meter that would hold the device open beside it. The page and the view-model live
        // and die together, so the subscription needs no removal.
        ViewModel.MicrophoneNeeded += (_, _) => VoiceMeter.Stop();
        ComposeVoiceLevelSection();
        ComposeRecordingLimitSection();
        ComposePreprocessingSection();
        ComposeExclusiveModeCard();
        LoadAndSync();
    }

    // ── Composed exclusive-mode card ──────────────────────────────────────────
    //
    // Under the microphone and its Windows properties: it says how the device
    // is opened, so it belongs with the device, outside the Recording section
    // and its reset. The header's "Reset all" turns it back off.
    private SettingsComposer? _exclusiveModeComposer;

    private void ComposeExclusiveModeCard()
    {
        _exclusiveModeComposer = new SettingsComposer(ExclusiveModeHost, ViewModel);
        _exclusiveModeComposer.Compose(ViewModel.ExclusiveModeSettings);
        _exclusiveModeComposer.DirtyChanged += OnComposedDirtyChanged;
    }

    // ── Composed voice-level card ─────────────────────────────────────────────
    //
    // Composed into its own host, the first card of the Recording section.
    private SettingsComposer? _voiceLevelComposer;

    private void ComposeVoiceLevelSection()
    {
        _voiceLevelComposer = new SettingsComposer(VoiceLevelHost, ViewModel);
        _voiceLevelComposer.Compose(ViewModel.VoiceLevelSettings);
        _voiceLevelComposer.DirtyChanged += OnComposedDirtyChanged;
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
    }

    private void OnComposedDirtyChanged(object? sender, EventArgs e) => GateResetLink();

    // The device id is deliberately not folded in: it is hardware enumeration, not
    // a defaulted setting, so a non-default device leaves the link greyed. The
    // header's "Reset all" shares this gate, since the Recording section is the
    // page's single resettable section; the Checks section keeps no value.
    private void GateResetLink()
    {
        bool dirty =
            (_voiceLevelComposer?.IsDirty() ?? false) ||
            (_recordingLimitComposer?.IsDirty() ?? false) ||
            (_preprocessingComposer?.IsDirty() ?? false);
        RecordingSection.IsResetEnabled = dirty;
        Header.IsResetAllEnabled = dirty || (_exclusiveModeComposer?.IsDirty() ?? false);
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
    // Populated from the active capture endpoints, through the same enumerator
    // the capture opens them with; stays in code-behind because this is
    // hardware enumeration, not a setting. The combo shows names, and
    // _audioInputIds holds the endpoint id at each index: "System default"
    // (empty id) at index 0, then the devices in the order Windows lists them.
    //
    // A saved microphone that is not plugged in keeps its place in the list,
    // named as missing, rather than showing "System default": the setting still
    // names it, and the capture will look for it again once it is back. Showing
    // the default would say something the setting does not.
    private readonly List<string> _audioInputIds = [];

    private void PopulateAudioInputDevices()
    {
        AudioInputCombo.Items.Clear();
        _audioInputIds.Clear();

        AudioInputCombo.Items.Add(Loc.Get("Settings_AudioInput_SystemDefault"));
        _audioInputIds.Add("");

        IReadOnlyList<Internal.MicrophoneEndpoint> endpoints = Internal.MicrophoneEndpointCatalog.Active();
        for (int i = 0; i < endpoints.Count; i++)
        {
            Internal.MicrophoneEndpoint endpoint = endpoints[i];
            string name = endpoint.Name == Internal.CaptureDeviceIdentity.UnknownName
                ? Loc.Format("Settings_AudioInput_Device_Format", i + 1)
                : endpoint.Name;
            AudioInputCombo.Items.Add(name);
            _audioInputIds.Add(endpoint.Id);
        }

        string saved = ViewModel.AudioInputEndpointId;
        int comboIndex = _audioInputIds.IndexOf(saved);
        if (comboIndex < 0)
        {
            AudioInputCombo.Items.Add(Loc.Get("Settings_AudioInput_Missing"));
            _audioInputIds.Add(saved);
            comboIndex = _audioInputIds.Count - 1;
        }
        AudioInputCombo.SelectedIndex = comboIndex;
    }

    private void AudioInputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = AudioInputCombo.SelectedIndex;
        if (_initializing || index < 0 || index >= _audioInputIds.Count) return;
        ViewModel.AudioInputEndpointId = _audioInputIds[index];
    }

    // ── Windows microphone properties ────────────────────────────────────────
    //
    // A command, not a setting: it opens the page of Windows Settings where the
    // selected microphone's input volume is set. That volume is the endpoint's
    // own gain, applied before any processing mode, the raw one included, so it
    // is the one dial that decides how close to clipping the signal Deckle
    // receives sits; Deckle neither reads nor writes it. With "System default"
    // selected, Windows opens the default input device's page; with a device,
    // that device's page, named by its endpoint id.
    private async void MicrophoneProperties_Click(object sender, RoutedEventArgs e)
    {
        string id = ViewModel.AudioInputEndpointId;
        var uri = new Uri(id.Length == 0
            ? "ms-settings:sound-defaultinputproperties"
            : "ms-settings:sound-properties?endpointId=" + Uri.EscapeDataString(id));
        bool launched;
        try
        {
            launched = await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception)
        {
            // An async void handler: whatever the launcher throws would
            // otherwise reach the dispatcher and take the app down for a link.
            launched = false;
        }
        if (!launched)
            DeckleAudioSource.Log.MicrophonePropertiesLaunchFailed(id.Length == 0 ? "default" : "device");
    }

    // The Recording section's reset: its three composed settings only. The
    // microphone card stands above every section, so the section wheel leaves
    // it alone; only the header's "Reset all" returns it to the system default.
    private void ResetRecording_Click(object sender, RoutedEventArgs e)
    {
        _voiceLevelComposer?.ResetAll();
        _recordingLimitComposer?.ResetAll();
        _preprocessingComposer?.ResetAll();

        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Recording");
    }

    // The microphone device is not composed, so its reset is written here. The VM
    // assignment is the write that persists (system default, the POCO's empty
    // id); the list is rebuilt under the _initializing guard, so a saved device
    // shown as missing leaves it and SelectedIndex = 0 does not re-enter the VM.
    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _voiceLevelComposer?.ResetAll();
        _recordingLimitComposer?.ResetAll();
        _preprocessingComposer?.ResetAll();
        _exclusiveModeComposer?.ResetAll();

        ViewModel.AudioInputEndpointId = new CaptureSettings().AudioInputEndpointId;
        _initializing = true;
        try
        {
            PopulateAudioInputDevices();
        }
        finally { _initializing = false; }

        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Recording (all)");
    }
}
