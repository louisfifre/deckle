using System;
using System.Collections.Generic;
using System.ComponentModel;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;
using Deckle.Vision;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Deckle.Lighting.Ambient;

// Settings page for the Ambient Light module. Resolved by the Settings
// NavigationView through the PageTag declared in AmbientSettingsModule.
//
// Surface : most cards are composed by SettingsComposer from the manifests in
// AmbientViewModel.Settings.cs, one composer per host panel (StateHost,
// BehaviourHost, TuningHost, CaptureHost). Four parts stay hand-authored
// because the composer cannot express them, each marked "bespoke" in the
// XAML: the NotPaired InfoBar, the per-light rows (LightZonesCard), the
// display picker (runtime labels) and the Hue bridge expander
// (AmbientPage.Hue.cs). The brightness curve is a composed Curve card, first
// of the fine-tuning cards. The live preview (AmbientPreviewCard) sits in a
// pane at the right of the page, behind a splitter, so it stays in view
// while the values change; the Live preview tile under the module switch and
// the Behaviour and Fine tuning title links open that pane, the preview's
// Close closes it (SyncPreviewPane).
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

        // The XAML column carries the pane's default width and floor; read
        // once, then the pane opens and closes on the user's word
        // (SyncPreviewPane). Closed until the tile or a section link is clicked.
        _paneWidth    = PreviewColumn.Width;
        _paneMinWidth = PreviewColumn.MinWidth;
        SyncPreviewPane();
        WireSplitterReveal();

        Loaded   += AmbientPage_Loaded;
        Unloaded += AmbientPage_Unloaded;
    }

    // ── Preview pane ───────────────────────────────────────────────────
    //
    // The pane is the user's: the Live preview tile under the module switch
    // and the title links of the Behaviour and Fine tuning sections open it,
    // the preview's own Close closes it, and nothing else does; the control
    // inside says on its own whether the engine gives it something live. The
    // column's width is the user's too, written on the ColumnDefinition by the
    // splitter at each drag, and kept for the page's lifetime only: nothing is
    // persisted in this pass.

    private GridLength _paneWidth;
    private double _paneMinWidth;
    private bool _paneOpen;

    // One handler for the tile's Click and the sections' LinkClicked. A click
    // while the pane is already open changes nothing: SyncPreviewPane rewrites
    // the same state (Louis, 2026-10-15: keep it simple).
    private void OpenPreview_Click(object sender, RoutedEventArgs e)
    {
        _paneOpen = true;
        SyncPreviewPane();
    }

    private void Preview_CloseRequested(object sender, RoutedEventArgs e)
    {
        _paneOpen = false;
        SyncPreviewPane();
    }

    private void SyncPreviewPane()
    {
        if (_paneOpen)
        {
            PreviewColumn.MinWidth = _paneMinWidth;
            PreviewColumn.Width    = _paneWidth;
        }
        else
        {
            // Keep the dragged width before closing, so the pane reopens
            // where the user left it.
            if (PreviewColumn.Width.IsAbsolute && PreviewColumn.Width.Value > 0)
                _paneWidth = PreviewColumn.Width;
            PreviewColumn.MinWidth = 0;
            PreviewColumn.Width    = new GridLength(0);
        }

        var visibility = _paneOpen ? Visibility.Visible : Visibility.Collapsed;
        PreviewDivider.Visibility  = visibility;
        PreviewSplitter.Visibility = visibility;
        PreviewPane.Visibility     = visibility;
    }

    // The grip shows while the pointer is over the splitter, while it is
    // pressed and while it holds keyboard focus; at rest the divider line is
    // the edge. The splitter marks its pointer events handled, so the press
    // and release are taken with handledEventsToo; the pointer may leave the
    // 12 px strip mid-drag, so the pressed state keeps the grip until the
    // release or the capture's loss. Pointer focus does not count: a click
    // focuses the splitter too, and the grip would stay after the drag.
    private void WireSplitterReveal()
    {
        bool over = false, pressed = false, focused = false;
        void Update() => PreviewSplitter.IsThumbVisible = over || pressed || focused;

        PreviewSplitter.PointerEntered += (_, _) => { over = true;  Update(); };
        PreviewSplitter.PointerExited  += (_, _) => { over = false; Update(); };
        PreviewSplitter.AddHandler(PointerPressedEvent,
            new PointerEventHandler((_, _) => { pressed = true;  Update(); }), handledEventsToo: true);
        PreviewSplitter.AddHandler(PointerReleasedEvent,
            new PointerEventHandler((_, _) => { pressed = false; Update(); }), handledEventsToo: true);
        PreviewSplitter.AddHandler(PointerCaptureLostEvent,
            new PointerEventHandler((_, _) => { pressed = false; Update(); }), handledEventsToo: true);
        PreviewSplitter.GotFocus  += (_, _) => { focused = PreviewSplitter.FocusState == FocusState.Keyboard; Update(); };
        PreviewSplitter.LostFocus += (_, _) => { focused = false; Update(); };
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

            // The criteria mirror AmbientEngine.StartAsync's validation,
            // so the InfoBar shows before a toggle ON fails and after
            // its auto-revert.
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
        HueBridgeExpander.IsExpanded = true;
        HueBridgeExpander.StartBringIntoView();
    }
}
