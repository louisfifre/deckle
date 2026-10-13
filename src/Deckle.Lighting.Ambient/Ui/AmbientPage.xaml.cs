using System;
using System.Collections.Generic;
using System.ComponentModel;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;
using Deckle.Vision;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Lighting.Ambient;

// Settings page for the Ambient Light module. Resolved by the Settings
// NavigationView from src/Deckle.Settings/SettingsWindow.xaml via the
// item Tag "Deckle.Lighting.Ambient.AmbientPage, Deckle.Lighting.Ambient".
//
// Surface : most cards are composed by SettingsComposer from the manifests in
// AmbientViewModel.Settings.cs, one composer per host panel (StateHost,
// BehaviourHost, TuningHost, CaptureHost). Five parts stay hand-authored
// because the composer cannot express them, each marked "bespoke" in the
// XAML: the NotPaired InfoBar, the live preview (AmbientPreviewCard), the
// per-light rows (LightZonesCard), the display picker (runtime labels) and
// the Hue bridge expander (AmbientPage.Hue.cs). The brightness curve is a
// composed Curve card inside the preset fold.
//
// Sync : the view model writes every edit straight to AmbientSettingsService
// and saves. The page observes AmbientSettingsService.Changed and reloads the
// view model, so an edit from the tray or a preset lands here
// at once; the composers follow the view model's PropertyChanged. The
// HuePairingService.BridgeChanged event re-syncs the Hue expander. Both are
// wired in Loaded and dropped in Unloaded. A _loading flag keeps the display
// combo's SelectionChanged from saving while the page fills it.
public sealed partial class AmbientPage : Page
{
    public AmbientViewModel ViewModel { get; } = new();

    // Held so each composer's PropertyChanged subscription lives as long as
    // the page.
    private readonly List<SettingsComposer> _composers = new();

    private bool _loading = true;

    public AmbientPage()
    {
        InitializeComponent();

        // The page asks with its own wording (it says what the reset keeps), so
        // the header does not add its generic confirmation on top.
        Header.ConfirmsReset = false;

        // Load before composing so every composed card starts on the stored
        // value rather than the view model's constructor seed.
        ViewModel.Load();
        ComposeRegion(StateHost, ViewModel.StateSettings);
        ComposeRegion(BehaviourHost, ViewModel.BehaviourSettings);
        ComposeRegion(TuningHost, ViewModel.TuningSettings);
        ComposeRegion(CaptureHost, ViewModel.CaptureSettings);

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Header.IsResetAllEnabled = ViewModel.CanReset;

        Loaded   += AmbientPage_Loaded;
        Unloaded += AmbientPage_Unloaded;
    }

    private void ComposeRegion(Panel host, IReadOnlyList<SettingDescriptor> settings)
    {
        var composer = new SettingsComposer(host, ViewModel);
        composer.Compose(settings);
        _composers.Add(composer);
    }

    private void AmbientPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        ResyncFromSettings();
        SyncHueBridgeUi();

        AmbientSettingsService.Instance.Changed += OnSettingsChanged;
        HuePairingService.Instance.BridgeChanged += OnHueBridgeChanged;

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
        _loading = true;
    }

    // Pulls the persisted state the bespoke cards show: the display picker
    // and the NotPaired banner. Guarded by _loading so the display combo's
    // handler does not save while it is refilled.
    private void ResyncFromSettings()
    {
        bool prevLoading = _loading;
        _loading = true;
        try
        {
            var s = AmbientSettingsService.Instance.Current;

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
            callback: () =>
            {
                ViewModel.Load();
                ResyncFromSettings();
            },
            rejectSource: "AMBIENT", rejectWhat: "settings reload");
    }

    // ── Reset all gate ─────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Reset all is active only while the page is off its defaults; the
        // view model recomputes CanReset after every Load and edit.
        if (e.PropertyName == nameof(AmbientViewModel.CanReset))
            Header.IsResetAllEnabled = ViewModel.CanReset;
    }

    // ── Reset all ──────────────────────────────────────────────────────
    //
    // The page-level reset: every value the page tunes goes back to its
    // default; pairing, group, display, per-light rows and the on/off state
    // stay. Confirmed first because it wipes several groups at once. The
    // view model writes once and saves; Changed reloads the page.

    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_ResetAmbientDialog_Title"),
                Loc.Get("Settings_ResetAmbientDialog_Content"),
                Loc.Get("AmbientPageResetAllLabel.Text"),
                IsDestructive: true));
        if (!confirmed) return;

        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Ambient page");
        ViewModel.ResetDefaults();
    }

    // ── Capture display (bespoke) ──────────────────────────────────────

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

    // ── NotPaired banner (bespoke) ─────────────────────────────────────

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
