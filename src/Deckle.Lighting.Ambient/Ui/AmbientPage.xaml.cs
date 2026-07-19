using System;
using System.Collections.Generic;
using System.Threading;
using Deckle.Lighting;
using Deckle.Catalog;
using Deckle.Shell;
using Deckle.Vision;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Lighting.Ambient;

// Settings page for the Ambient Light module. Resolved by the Settings
// NavigationView from src/Deckle.Settings/SettingsWindow.xaml via the
// item Tag "Deckle.Lighting.Ambient.AmbientPage, Deckle.Lighting.Ambient".
//
// Surface : master Enabled toggle, mode selector, Hue bridge pairing
// expander (Discover / Pair / List groups / Forget), and an entry point
// to the Playground for detailed tuning. The heavy visual tuning UI lives
// in the Playground so Settings stays operational rather than experimental.
//
// Persistence : event-handler style (Toggled / SelectionChanged /
// ValueChanged) that mutates AmbientSettings.Current and calls
// AmbientSettingsService.Instance.Save() inline. No view-model layer.
//
// Sync state : three subscriptions wired in Loaded and dropped in
// Unloaded. The Settings Changed event re-syncs the controls from
// settings so a flip from the tray / Playground propagates immediately
// to the ToggleSwitch. The engine StateChanged event drives the
// transient UI (ModeCombo gating). The HuePairingService.BridgeChanged
// event re-syncs the Hue expander row so a re-pair / forget from the
// Playground reflects live. All three are guarded by a _loading flag
// that suppresses the re-fire loop when handlers touch the same
// controls that triggered them.
public sealed partial class AmbientPage : Page
{
    private bool _loading = true;
    private AmbientEngine? _observedEngine;

    public AmbientPage()
    {
        InitializeComponent();
        Loaded   += AmbientPage_Loaded;
        Unloaded += AmbientPage_Unloaded;
    }

    private void AmbientPage_Loaded(object sender, RoutedEventArgs e)
    {
        var engine = AmbientEngine.Current;

        ResyncFromSettings();
        SyncHueBridgeUi();
        ApplyEngineState(engine?.State ?? AmbientEngineState.Off);

        AmbientSettingsService.Instance.Changed += OnSettingsChanged;
        HuePairingService.Instance.BridgeChanged += OnHueBridgeChanged;
        if (engine is not null)
        {
            _observedEngine = engine;
            _observedEngine.StateChanged += OnEngineStateChanged;
        }

        _loading = false;
    }

    private void AmbientPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Cancel any in-flight pair countdown if the user navigates
        // away — PairAsync exits with OperationCanceledException, the
        // catch in OnHuePairClick resets the visuals.
        try { _huePairCts?.Cancel(); } catch { /* best effort */ }
        _huePairCts?.Dispose();
        _huePairCts = null;

        AmbientSettingsService.Instance.Changed -= OnSettingsChanged;
        HuePairingService.Instance.BridgeChanged -= OnHueBridgeChanged;
        if (_observedEngine is not null)
        {
            _observedEngine.StateChanged -= OnEngineStateChanged;
            _observedEngine = null;
        }
        _loading = true;
    }

    // Pulls the current persisted state into the controls. Called on
    // first load and on every Changed event. Guarded by _loading so
    // the handlers don't re-fire Save during the assignment loop.
    private void ResyncFromSettings()
    {
        bool prevLoading = _loading;
        _loading = true;
        try
        {
            var s = AmbientSettingsService.Instance.Current;

            EnabledToggle.IsOn = s.Enabled;

            ComboBoxItem? toSelect = null;
            foreach (var item in ModeCombo.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Tag is string tag && tag == s.Mode.ToString())
                {
                    toSelect = cbi;
                    break;
                }
            }
            ModeCombo.SelectedItem = toSelect ?? ModeCombo.Items[0];
            PopulateMonitorChoices(s.SelectedMonitorDeviceName);

            // Pair completeness drives the NotPaired InfoBar. The
            // criteria mirror AmbientEngine.StartAsync's validation
            // so a user who toggles ON in this state sees the InfoBar
            // BEFORE clicking (forewarned) and also AFTER the auto-
            // revert (still incomplete).
            bool paired = !string.IsNullOrEmpty(s.HueBridgeIp)
                       && !string.IsNullOrEmpty(s.HueBridgeId)
                       && !string.IsNullOrEmpty(s.HueUsername)
                       && !string.IsNullOrEmpty(s.HueLastGroupId);
            NotPairedInfoBar.IsOpen = !paired;
        }
        finally
        {
            _loading = prevLoading;
        }
    }

    private void OnSettingsChanged()
    {
        DispatcherQueue.TryEnqueueObserved(
            operation: "settings-reload", caller: "ambient-page",
            callback: ResyncFromSettings,
            rejectSource: "AMBIENT", rejectWhat: "settings reload");
    }

    private void OnEngineStateChanged(AmbientEngineState state)
    {
        DispatcherQueue.TryEnqueueObserved(
            operation: "engine-state-sync", caller: "ambient-page",
            callback: () => ApplyEngineState(state),
            rejectSource: "AMBIENT", rejectWhat: "engine state sync");
    }

    // Surfaces the engine's transition state on the page. The previous
    // pass surfaced a ProgressRing for the transient Starting /
    // Stopping states, but the ring stayed stuck in one runtime
    // observation and the feature offered marginal value while a Hue
    // pair takes only ~300–800 ms ; it has been retired. The
    // ApplyEngineState pass is kept for the ModeCombo gating, which is
    // genuinely useful : changing Mode mid-Running silently desyncs
    // the radios from the pipeline shape, so we lock the combo while
    // the engine runs.
    private void ApplyEngineState(AmbientEngineState state)
    {
        bool canReconfigure = state != AmbientEngineState.Running;
        ModeCombo.IsEnabled = canReconfigure;
        MonitorCombo.IsEnabled = canReconfigure;
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AmbientSettingsService.Instance.Current.Enabled = EnabledToggle.IsOn;
        AmbientSettingsService.Instance.Save();
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ModeCombo.SelectedItem is ComboBoxItem cbi
            && cbi.Tag is string tag
            && Enum.TryParse<AmbientMode>(tag, out var mode))
        {
            // ApplyPreset copies the preset's full tuning snapshot
            // onto Current and saves in one shot. Custom is a special
            // case : it just sets Mode = Custom without touching any
            // other knob, so the Playground's hand-tuned values stay
            // exactly where the user left them.
            AmbientSettingsService.Instance.ApplyPreset(mode);
        }
    }

    private void PopulateMonitorChoices(string? selectedDeviceName)
    {
        MonitorCombo.Items.Clear();
        MonitorCombo.Items.Add(new ComboBoxItem
        {
            Content = Loc.Get("AmbientMonitor_Primary"),
            Tag = null,
        });

        ComboBoxItem? selected = null;
        var monitors = ScreenCaptureService.GetAvailableMonitors();
        for (var index = 0; index < monitors.Count; index++)
        {
            var monitor = monitors[index];
            var primarySuffix = monitor.IsPrimary
                ? Loc.Get("AmbientMonitor_CurrentPrimarySuffix")
                : string.Empty;
            var item = new ComboBoxItem
            {
                Content = Loc.Format(
                    "AmbientMonitor_DisplayFormat",
                    index + 1,
                    monitor.Width,
                    monitor.Height,
                    primarySuffix),
                Tag = monitor.DeviceName,
            };
            MonitorCombo.Items.Add(item);

            if (string.Equals(
                selectedDeviceName,
                monitor.DeviceName,
                StringComparison.Ordinal))
            {
                selected = item;
            }
        }

        if (selected is null && !string.IsNullOrEmpty(selectedDeviceName))
        {
            selected = new ComboBoxItem
            {
                Content = Loc.Format("AmbientMonitor_UnavailableFormat", selectedDeviceName),
                Tag = selectedDeviceName,
            };
            MonitorCombo.Items.Add(selected);
        }

        MonitorCombo.SelectedItem = selected ?? MonitorCombo.Items[0];
    }

    private void MonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MonitorCombo.SelectedItem is not ComboBoxItem item) return;

        AmbientSettingsService.Instance.Current.SelectedMonitorDeviceName = item.Tag as string;
        AmbientSettingsService.Instance.Save();
    }

    // The four HDR slider handlers (ExposureSlider_ValueChanged et al.)
    // and their per-knob / per-section reset companions used to live
    // here. They moved out to the Playground in the V0.3 refactor —
    // the live preview next to them makes empirical tuning so much
    // cheaper that duplicating the controls in Settings was net
    // negative. The Mode preset selector above is what carries the
    // tuning intent in Settings now ; pick Game / Movie / Ambient
    // to apply a snapshot, or open the Playground for the fine-grain
    // knobs.

    // Opens the Playground via the same callback the AmbientEngine
    // uses for its NotPaired InfoBar action. The App wires this slot
    // at boot to its lazy ShowPlaygroundLazy(), so picking the card
    // here brings the Playground forward without forcing a reference
    // from Lighting.Ambient back to the app host.
    private void OpenPlaygroundCard_Click(object sender, RoutedEventArgs e)
    {
        AmbientEngine.OpenPlaygroundRequested?.Invoke();
    }

    private void ConfigureBridgeButton_Click(object sender, RoutedEventArgs e)
    {
        // Expand the Hue bridge expander and scroll it into view so the
        // user lands on the pair flow without manual scrolling. The
        // SettingsExpander.IsExpanded property is two-way bindable and
        // immediately triggers the visual transition.
        HueBridgeExpander.IsExpanded = true;
        HueBridgeExpander.StartBringIntoView();
    }
}
