using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Lighting;
using Deckle.Lighting.Ambient;
using Deckle.Catalog;
using Deckle.Playground;
using Deckle.Shell;
using Deckle.Vision;

namespace Deckle.Playground;

// ─── Ambient lighting tuning page ────────────────────────────────────────────
//
// Unlike the HUD tab's sandbox, this page reads and writes the live
// AmbientSettingsService through AmbientViewModel: the scene it tunes is the
// lamps themselves.
//
// Lifetime contract:
//   • The page is cached (NavigationCacheMode.Required), so its runtime
//     resources (capture service, frame sampler, Hue light output, preview
//     cells) outlive a navigation until PlaygroundWindow calls
//     DisposeResources on Closed.
//   • The preview timer stops in OnNavigatingFrom, so an off-screen page
//     pumps no UI updates.
//   • The service observers are wired once in the constructor and torn down
//     in DisposeResources; edits from the Settings AmbientPage reflect here
//     without re-subscribing on every navigation.
public sealed partial class AmbientPage : Page
{
    public AmbientViewModel ViewModel { get; } = new();

    // Guards programmatic control writes during a ViewModel sync so the
    // Changed handlers don't write back through the VM. Page-level because
    // these sliders use code-behind handlers (not x:Bind) for their rescaling.
    private bool _initializing;

    // ── Screen capture — see AmbientPage.ScreenCapture.cs ───────────────────
    private ScreenCaptureService? _screenCapture;
    private readonly DispatcherTimer _screenCaptureFpsTimer = new()
        { Interval = TimeSpan.FromMilliseconds(1000) };
    private long _screenCaptureLastSampledFrames;
    private long _screenCaptureLastSampleTimestamp;

    // ── Hue REST driver — see AmbientPage.Hue.cs ────────────────────────────
    private CancellationTokenSource? _huePairCts;
    private HueBridge? _hueDiscoveredBridge;
    private bool _hueIsPairing;
    private IReadOnlyList<HueGroup> _hueGroups = [];
    private HueRestLightOutput? _hueLightOutput;
    private CancellationTokenSource? _hueRotationCts;
    private bool _hueGroupComboSuppress;
    private bool _hueGroupsFetchInFlight;
    private AmbientEngine? _observedEngine;

    // ── Preview grid + sampler — see AmbientPage.Preview.cs ─────────────────
    private FrameSampler? _frameSampler;
    private bool _pipelineStartedCapture;
    private DispatcherTimer? _previewTimer;
    private Microsoft.UI.Xaml.Shapes.Rectangle[]? _previewCells;
    private int _previewGridCols;
    private int _previewGridRows;

    // Native dip size of one preview cell; the Viewbox scales the whole grid
    // uniformly.
    private const double PreviewCellSize = 16;

    // Keyed by light id, or "group" in single-colour mode. Brushes are
    // mutated in place each tick rather than rebuilt.
    private readonly Dictionary<string, (Microsoft.UI.Xaml.Shapes.Rectangle Container, SolidColorBrush Fill, string DisplayName)> _swatchByLight = new();

    private readonly Dictionary<LightZone, SolidColorBrush> _zoneFillBrushes = new();

    // ── Light zones — see AmbientPage.LightZones.cs ─────────────────────────
    private List<LightDescriptor>? _placementLights;
    private Dictionary<string, LightZone>? _suggestedZones;

    public AmbientPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        // Minimum is set here, not in XAML: the WinUI 3 release parser throws
        // on a XAML Minimum above the default Value of 0.
        PlaygroundBorderDepthSlider.Minimum = 5;
        PlaygroundBorderCellsSlider.Minimum = 4;

        BuildAmbientModeComboItems();

        _screenCaptureFpsTimer.Tick += OnScreenCaptureFpsTick;

        // The overlay Canvas is a sibling of the Viewbox, not inside it, so
        // its rectangles stay in real DIPs; re-fit it to the scaled grid on
        // every resize.
        AmbientPreviewViewbox.SizeChanged += (_, _) => LayoutLightZonesOverlayFromViewbox();

        HuePairingService.Instance.BridgeChanged += OnHueBridgeChanged;
        AmbientSettingsService.Instance.Changed  += OnAmbientSettingsChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (AmbientEngine.Current is { } engine)
        {
            _observedEngine = engine;
            _observedEngine.StateChanged += OnAmbientEngineStateChanged;
        }

        if (this.Content is FrameworkElement root)
        {
            root.Loaded += OnPageLoaded;
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _initializing = true;
        try
        {
            ViewModel.Load();
            SyncHueUiFromService();
            SyncPipelineUiFromViewModel();
            PushViewModelToControls();
            ApplyPipelineReadiness();
        }
        finally
        {
            DispatcherQueue.TryEnqueueObserved(
                operation: "init-flag-clear", caller: "playground-ambient-page",
                callback: () => _initializing = false,
                rejectSource: "PLAYGROUND", rejectWhat: "init flag",
                priority: DispatcherQueuePriority.Low);
        }
        DecklePlaygroundSource.Log.PageReady(nameof(AmbientPage), DecklePlaygroundSource.NavClock.ElapsedMilliseconds);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        bool prev = _initializing;
        _initializing = true;
        try
        {
            ViewModel.Load();
            PushViewModelToControls();
            SyncPipelineUiFromViewModel();
            ApplyPipelineReadiness();
        }
        finally
        {
            _initializing = prev;
        }

        EvaluatePreviewTimerState();
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        // Only the preview stops; the AmbientEngine keeps pushing to Hue.
        _previewTimer?.Stop();
    }

    public void DisposeResources()
    {
        _previewTimer?.Stop();
        _screenCaptureFpsTimer.Stop();

        StopScreenCaptureIfRunning();
        TeardownHueIfActive();

        HuePairingService.Instance.BridgeChanged -= OnHueBridgeChanged;
        AmbientSettingsService.Instance.Changed  -= OnAmbientSettingsChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_observedEngine is { } engine)
        {
            engine.StateChanged -= OnAmbientEngineStateChanged;
            _observedEngine = null;
        }
    }

    // ── Service observers ────────────────────────────────────────────────────

    private void OnHueBridgeChanged()
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            SyncHueUiFromService();
            ApplyPipelineReadiness();
        }
        else
        {
            DispatcherQueue.TryEnqueueObserved(
                operation: "ui-update", caller: "playground-ambient-page-hue",
                callback: () =>
                {
                    SyncHueUiFromService();
                    ApplyPipelineReadiness();
                },
                rejectSource: "PLAYGROUND", rejectWhat: "hue bridge sync");
        }
    }

    private void OnAmbientSettingsChanged()
    {
        DispatcherQueue.TryEnqueueObserved(
            operation: "settings-reload", caller: "playground-ambient-page",
            callback: () =>
            {
                bool prev = _initializing;
                _initializing = true;
                try
                {
                    ViewModel.Load();
                    PushViewModelToControls();
                    SyncPipelineUiFromViewModel();
                    ApplyPipelineReadiness();
                    // Repaints the zone bands, so a band edit made in
                    // Settings shows here without a window resize.
                    LayoutLightZonesOverlayFromViewbox();
                }
                finally
                {
                    _initializing = prev;
                }
            },
            rejectSource: "PLAYGROUND", rejectWhat: "settings reload");
    }

    private void OnAmbientEngineStateChanged(AmbientEngineState state)
    {
        // StateChanged can fire from any thread.
        if (DispatcherQueue.HasThreadAccess)
        {
            EvaluatePreviewTimerState();
        }
        else
        {
            DispatcherQueue.TryEnqueueObserved(
                operation: "engine-state-sync", caller: "playground-ambient-page",
                callback: EvaluatePreviewTimerState,
                rejectSource: "PLAYGROUND", rejectWhat: "preview timer eval");
        }
    }

    // ── Toolbar handlers ─────────────────────────────────────────────────────

    private void OnShowZoneOverlaysToggled(object sender, RoutedEventArgs e)
    {
        if (LightZonesOverlay is null) return;
        LightZonesOverlay.Visibility = (ShowZoneOverlaysToggle.IsChecked == true)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdatePreviewViewboxVisibility();
    }

    // ── Action bar ───────────────────────────────────────────────────────────

    // The resets save to the store; OnAmbientSettingsChanged then reloads the
    // VM and repaints the sliders, so nothing is pushed from here.
    private void OnAmbientResetDefaultsClick(object sender, RoutedEventArgs e)
        => ViewModel.ResetDefaults();

    private void OnResetTuningClick(object sender, RoutedEventArgs e)
        => ViewModel.ResetTuningSection();

    private void OnResetZoneClick(object sender, RoutedEventArgs e)
        => ViewModel.ResetZoneSamplingSection();

    // ── Theme resource helper ───────────────────────────────────────────────
    //
    // The ThemeResource markup extension only resolves in XAML; code-behind
    // reads the brush from Application.Current.Resources.
    private static Brush GetThemeBrush(string key)
        => (Brush)Application.Current.Resources[key];
}
