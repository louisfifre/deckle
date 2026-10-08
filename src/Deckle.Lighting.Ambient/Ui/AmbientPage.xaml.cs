using System;
using System.Collections.Generic;
using System.ComponentModel;
using Deckle.Catalog;
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
// BehaviourHost, TuningHost, CaptureHost). Six parts stay hand-authored
// because the composer cannot express them, each marked "bespoke" in the
// XAML: the NotPaired InfoBar, the live preview (AmbientPreviewCard), the
// per-light rows (LightZonesCard), the
// brightness-curve editor, the display picker (runtime labels) and the Hue
// bridge expander (AmbientPage.Hue.cs).
//
// Sync : the view model writes every edit straight to AmbientSettingsService
// and saves. The page observes AmbientSettingsService.Changed and reloads the
// view model, so an edit from the tray, the Playground or a preset lands here
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

        // Load before composing so every composed card starts on the stored
        // value rather than the view model's constructor seed.
        ViewModel.Load();
        ComposeRegion(StateHost, ViewModel.StateSettings);
        ComposeRegion(BehaviourHost, ViewModel.BehaviourSettings);
        ComposeRegion(TuningHost, ViewModel.TuningSettings);
        ComposeRegion(CaptureHost, ViewModel.CaptureSettings);

        // The curve card's hand-authored reset: shown on hover like the
        // composed resets, enabled only while the curve is off its default.
        BrightnessCurveCard.PointerEntered += (_, _) => BrightnessCurveReset.Opacity = 1;
        BrightnessCurveCard.PointerExited += (_, _) => BrightnessCurveReset.Opacity = 0;
        ToolTipService.SetToolTip(BrightnessCurveReset, Loc.Get("SettingsComposer_ResetToDefault"));
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateBrightnessCurveReset();

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

    // ── Brightness curve (bespoke) ─────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AmbientViewModel.BrightnessCurveX1):
            case nameof(AmbientViewModel.BrightnessCurveY1):
            case nameof(AmbientViewModel.BrightnessCurveX2):
            case nameof(AmbientViewModel.BrightnessCurveY2):
                UpdateBrightnessCurveReset();
                break;
        }
    }

    private void UpdateBrightnessCurveReset()
        => BrightnessCurveReset.IsEnabled = ViewModel.IsBrightnessCurveDirty();

    private void BrightnessCurveReset_Click(object sender, RoutedEventArgs e)
        => ViewModel.ResetBrightnessCurve();

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
