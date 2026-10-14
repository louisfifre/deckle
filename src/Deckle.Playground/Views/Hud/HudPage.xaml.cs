using System;
using System.ComponentModel;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Hud;
using Deckle.Composition;
using Deckle.Playground;
using Deckle.Settings;

namespace Deckle.Playground;

// ─── HUD tuning page ─────────────────────────────────────────────────────────
//
// The page is cached (NavigationCacheMode.Required), so its state survives
// navigation; PlaygroundWindow calls DisposeResources on close so the
// compositor never keeps dangling Forever animations.
//
// The knobs open on the HUD settings the live HUD uses and edit the previews
// only; the floating bar's Save is the one gesture that writes them back
// (HudSettingsService), Revert puts the stored values back, Reset all returns
// to the compiled defaults. The simulation knobs never count as a change.
public sealed partial class HudPage : Page
{
    public HudViewModel ViewModel { get; } = new();

    // Not readonly: OnResetAllClick swaps in a fresh TuningModel. The row
    // lambdas read `_tuning` through `this`, so they write to the new instance.
    private TuningModel _tuning = new();

    // What the store held when the knobs last matched it: the baseline the
    // bar compares against. Loaded once; the page is cached, so unsaved edits
    // survive a round-trip through another tab.
    private HudComposition.ConicArcStrokeConfig _savedStroke;
    private UtteranceCountdownConfig _savedCountdown = new();
    private bool _storeLoaded;

    // Paint-time changes rebuild the Win2D surfaces from scratch; one rebuild
    // per tick during a drag overloads the CanvasDevice, hence the debounce.
    private readonly DispatcherTimer _rebuildDebounce = new()
        { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _rebuildPending;

    // Breaks the Slider ↔ NumberBox feedback loop in the row factories: each
    // programmatic write fires the paired control's ValueChanged.
    private bool _syncing;
    private bool _tuningPanelInitialized;

    // Must be disposed before a new one is mounted: every rebuild otherwise
    // leaks two Forever ScalarKeyFrameAnimations, and after enough slider
    // moves the compositor saturates and the rotation freezes. See the
    // NakedPreview class comment in HudComposition.
    private HudComposition.NakedPreview? _nakedPreview;

    // 272×78 is the HUD's stroke surface; the 300×300 host leaves room so the
    // Cone target's outer radius doesn't clip at the corners.
    private static readonly Vector2 NakedHudSize = new(272f, 78f);
    private const           float   NakedHostDim = 300f;

    // Same disposal discipline as _nakedPreview.
    private HudComposition.ConicClonePreview? _digitRevealPreview;

    public HudPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        _rebuildDebounce.Tick += (_, _) =>
        {
            _rebuildDebounce.Stop();
            if (!_rebuildPending) return;
            _rebuildPending = false;
            ApplyTarget();
        };

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SettingsService.Instance.OverlayAnimationsChanged += OnOverlayAnimationsChanged;
        HudSettingsService.Instance.Changed += OnHudSettingsChanged;

        // Keep the footer spacer as tall as the floating save bar.
        UnsavedBar.SizeChanged += (_, e) => UnsavedSpacer.Height = e.NewSize.Height;

        if (this.Content is FrameworkElement root)
        {
            root.Loaded += OnPageLoaded;
        }
    }

    // Loaded re-fires on every return to the cached page: the store is read
    // and the panel built once, the preview is re-projected each time.
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (!_storeLoaded) LoadFromStore();
        if (!_tuningPanelInitialized) BuildTuningPanel();
        ApplyTarget();
        DecklePlaygroundSource.Log.PageReady(nameof(HudPage), DecklePlaygroundSource.NavClock.ElapsedMilliseconds);
    }

    // ── Stored HUD settings: baseline, Save, Revert ──────────────────────────

    private void LoadFromStore()
    {
        _storeLoaded = true;
        _savedStroke    = HudSettingsService.Instance.EffectiveStroke();
        _savedCountdown = HudSettingsService.Instance.EffectiveCountdown();
        _tuning.Load(_savedStroke);
        _countdownConfig = _savedCountdown;
    }

    private bool IsDirty =>
        !_tuning.ToConfig().Equals(_savedStroke) || !_countdownConfig.Equals(_savedCountdown);

    private void UpdateUnsavedBar()
    {
        bool dirty = IsDirty;
        UnsavedBar.Visibility    = dirty ? Visibility.Visible : Visibility.Collapsed;
        UnsavedSpacer.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _savedStroke    = _tuning.ToConfig();
        _savedCountdown = _countdownConfig;
        HudSettingsService.Instance.Save(_savedStroke, _savedCountdown);
        UpdateUnsavedBar();
    }

    private void OnRevertClick(object sender, RoutedEventArgs e)
    {
        _tuning.Load(_savedStroke);
        _countdownConfig = _savedCountdown;
        BuildTuningPanel();
        ApplyTarget();
    }

    // Changed fires on the store's writer thread, after the disk write: after
    // this page's own Save (the knobs already match, nothing to do) or after a
    // write from elsewhere. In the latter case the baseline moves; clean knobs
    // follow it, unsaved edits stay and the bar keeps showing.
    private void OnHudSettingsChanged()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(OnHudSettingsChanged);
            return;
        }
        if (!_storeLoaded) return;

        bool wasDirty = IsDirty;
        _savedStroke    = HudSettingsService.Instance.EffectiveStroke();
        _savedCountdown = HudSettingsService.Instance.EffectiveCountdown();
        if (!wasDirty && IsDirty)
        {
            _tuning.Load(_savedStroke);
            _countdownConfig = _savedCountdown;
            BuildTuningPanel();
            ApplyTarget();
            return;
        }
        UpdateUnsavedBar();
    }

    // ── Public surface called by PlaygroundWindow ────────────────────────────

    // Stops the preview on every window re-show, so it lands on a known state.
    public void ForcePause()
    {
        if (!ViewModel.IsPlaying) return;
        ViewModel.IsPlaying = false;
    }

    public void DisposeResources()
    {
        SettingsService.Instance.OverlayAnimationsChanged -= OnOverlayAnimationsChanged;
        HudSettingsService.Instance.Changed -= OnHudSettingsChanged;
        StopSpeechSimulation();
        CancelTransition();
        _rebuildDebounce.Stop();
        ElementCompositionPreview.SetElementChildVisual(NakedPreviewHost, null);
        _nakedPreview?.Dispose();
        _nakedPreview = null;
        _digitRevealPreview?.Dispose();
        _digitRevealPreview = null;
        _gaugePreview?.Dispose();
        _gaugePreview = null;
    }

    private void OnOverlayAnimationsChanged(bool enabled)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => OnOverlayAnimationsChanged(enabled));
            return;
        }

        if (ViewModel.IsPlaying)
            ApplyTarget();
    }

    // ── VM observer ──────────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HudViewModel.CurrentTarget):
                RebuildTuningPanel();
                ApplyTarget();
                break;

            case nameof(HudViewModel.IsPlaying):
                ApplyTarget();
                break;

            // The parts' material picks their material section and whether a
            // hue rotation shows, so the panel follows it.
            case nameof(HudViewModel.PreviewMaterial):
                BuildTuningPanel();
                ApplyTarget();
                break;
        }
    }

    // ── Target picker (DropDownButton MenuFlyout) ────────────────────────────

    private void OnTargetMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item) return;
        if (item.Tag is not string tag) return;
        if (!Enum.TryParse<HudTarget>(tag, out var next)) return;

        ViewModel.CurrentTarget = next;
    }

    // May fire once on load for the XAML's default selection (Colour); a
    // no-op, the value already matches. The items follow HudMaterial's
    // order, so the index is the material.
    private void OnPreviewMaterialChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not RadioButtons radios) return;
        ViewModel.PreviewMaterial = radios.SelectedIndex == 0 ? HudMaterial.Grey : HudMaterial.Colour;
    }

    // ── Transport (Start / Stop) ─────────────────────────────────────────────

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsPlaying = true;
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsPlaying = false;
    }

    // ── Core : apply target + play state to the preview ──────────────────────

    private void ApplyTarget()
    {
        CancelTransition();
        SnapshotCountdownReplay();
        ChronoPreview.ApplyState(HudState.Hidden);
        ElementCompositionPreview.SetElementChildVisual(NakedPreviewHost, null);
        _nakedPreview?.Dispose();
        _nakedPreview = null;
        _digitRevealPreview?.Dispose();
        _digitRevealPreview = null;
        _gaugePreview?.Dispose();
        _gaugePreview = null;
        StopSpeechSimulation();

        if (!ViewModel.IsPlaying)
        {
            // Stop leaves only the substrate: no residual Charging-like card.
            ChronoPreview.Visibility    = Visibility.Collapsed;
            NakedPreviewHost.Visibility = Visibility.Collapsed;
            MaterialToggle.Visibility   = Visibility.Collapsed;
            return;
        }

        var currentTarget = ViewModel.CurrentTarget;
        bool isNaked = currentTarget is HudTarget.Cone
                                     or HudTarget.SweepMask
                                     or HudTarget.MaskedCone
                                     or HudTarget.Gauge
                                     or HudTarget.DigitReveal;
        ChronoPreview.Visibility    = isNaked ? Visibility.Collapsed : Visibility.Visible;
        NakedPreviewHost.Visibility = isNaked ? Visibility.Visible   : Visibility.Collapsed;
        MaterialToggle.Visibility   = ViewModel.IsMaterialToggleVisible ? Visibility.Visible : Visibility.Collapsed;

        if (isNaked)
        {
            if (currentTarget == HudTarget.Gauge)
            {
                AttachGaugePreview();
                return;
            }

            if (currentTarget == HudTarget.DigitReveal)
            {
                AttachDigitRevealPreview();
                return;
            }

            var part = currentTarget switch
            {
                HudTarget.Cone      => HudComposition.NakedMaskPart.Conic,
                HudTarget.SweepMask => HudComposition.NakedMaskPart.ArcMask,
                _                   => HudComposition.NakedMaskPart.Combined,
            };
            AttachNakedPreview(part);
            return;
        }

        var state = currentTarget switch
        {
            HudTarget.Charging     => HudState.Charging,
            HudTarget.Recording    => HudState.Recording,
            HudTarget.Transcribing => HudState.Transcribing,
            _                      => HudState.Rewriting,
        };

        // Armed before ApplyState, whose single stroke creation consumes it.
        // Armed after, ApplyState would first build a shipping-default stroke.
        if (currentTarget is HudTarget.Recording
                          or HudTarget.Transcribing
                          or HudTarget.Rewriting)
        {
            ChronoPreview.SetNextStrokeConfig(_tuning.ToConfig());
        }
        ChronoPreview.ApplyState(state);

        // ApplyState only styles; the clock is driven here, as HudWindow.SetState
        // does for the live HUD.
        switch (state)
        {
            case HudState.Charging:     ChronoPreview.ResetClock(); break;
            case HudState.Recording:    ChronoPreview.StartClock(); break;
            case HudState.Transcribing:
            case HudState.Rewriting:    ChronoPreview.StopClock();  break;
        }

        if (currentTarget == HudTarget.Recording)
            StartSpeechSimulation();
    }

    private void AttachNakedPreview(HudComposition.NakedMaskPart part)
    {
        ElementCompositionPreview.SetElementChildVisual(NakedPreviewHost, null);
        _nakedPreview?.Dispose();
        _nakedPreview = null;

        var compositor = ElementCompositionPreview.GetElementVisual(NakedPreviewHost).Compositor;

        // Only the bare sweep mask shows the arc fill's colour, against a
        // LayerFill that is near-white in light theme, so the fill turns black
        // there. The masked cone goes through AlphaMaskEffect and ignores the
        // arc RGB.
        bool isLightTheme =
            Content is FrameworkElement fe &&
            fe.ActualTheme == ElementTheme.Light;
        var arcFill = isLightTheme ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;

        _nakedPreview = HudComposition.CreateNakedMaskPreview(
            compositor, NakedHudSize, _tuning.ToConfig(), part, arcFill,
            ViewModel.PreviewMaterial, isDark: !isLightTheme,
            animationsEnabled: SettingsService.Instance.Current.Overlay.Animations);

        float inset = (NakedHostDim - _nakedPreview.Container.Size.X) / 2f;
        _nakedPreview.Container.Offset = new Vector3(inset, inset, 0f);

        ElementCompositionPreview.SetElementChildVisual(NakedPreviewHost, _nakedPreview.Container);
    }

    private void AttachDigitRevealPreview()
    {
        ElementCompositionPreview.SetElementChildVisual(NakedPreviewHost, null);
        _digitRevealPreview?.Dispose();
        _digitRevealPreview = null;

        var compositor = ElementCompositionPreview.GetElementVisual(NakedPreviewHost).Compositor;

        bool isLightTheme =
            Content is FrameworkElement fe &&
            fe.ActualTheme == ElementTheme.Light;

        _digitRevealPreview = HudComposition.CreateConicClonePreview(
            compositor, NakedHudSize, _tuning.ToConfig(),
            ViewModel.PreviewMaterial, isDark: !isLightTheme,
            animationsEnabled: SettingsService.Instance.Current.Overlay.Animations);

        // Same seat as the XAML reference border, where the live row would sit.
        float insetX = (NakedHostDim - _digitRevealPreview.Container.Size.X) / 2f;
        float insetY = (NakedHostDim - _digitRevealPreview.Container.Size.Y) / 2f;
        _digitRevealPreview.Container.Offset = new Vector3(insetX, insetY, 0f);

        ElementCompositionPreview.SetElementChildVisual(NakedPreviewHost, _digitRevealPreview.Container);
    }

    private void RequestRebuild()
    {
        _rebuildPending = true;
        _rebuildDebounce.Stop();
        _rebuildDebounce.Start();
    }

    // ── Reset all ───────────────────────────────────────────────────────
    //
    // Resets the fields directly rather than chaining the per-section
    // Reset*() methods, which would each rebuild the panel. The compiled
    // defaults, not the stored values: Revert on the bar is the way back to
    // those.
    private void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        _tuning = new TuningModel();
        _countdownConfig = new();
        ResetGaugeSimulation();
        _speechSimulation = new();
        _transitionSimulation = new();

        BuildTuningPanel();
        ApplyTarget();
    }

    // ── Tuning panel dispatcher ─────────────────────────────────────────
    //
    // HudViewModel.ActiveTuningSections decides which sections show and in
    // which order; this only maps each one to its expander.

    private void BuildTuningPanel()
    {
        _tuningPanelInitialized = true;
        // Child 0 is the static XAML header; everything after it is rebuilt.
        while (TuningStack.Children.Count > 1)
            TuningStack.Children.RemoveAt(TuningStack.Children.Count - 1);

        var sections = ViewModel.ActiveTuningSections(_tuning.ToConfig());
        if (sections.Count == 0)
        {
            // An empty column would read as a bug; say why there is nothing.
            TuningStack.Children.Add(new TextBlock
            {
                Text         = "Charging has no stroke, so there is nothing to tune in this state.",
                Style        = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                Foreground   = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 4, 0, 0),
            });
            return;
        }

        foreach (var section in sections)
        {
            switch (section)
            {
                case HudTuningSection.Geometry:       AddHudGeometryExpander();    break;
                case HudTuningSection.ColourMaterial: AddColourMaterialExpander(); break;
                case HudTuningSection.GreyMaterial:   AddGreyMaterialExpander();   break;
                case HudTuningSection.SweepMask:      AddSweepMaskExpander();      break;
                case HudTuningSection.HueRotation:    AddHueRotationExpander();    break;
                case HudTuningSection.SweepRotation:  AddSweepRotationExpander();  break;
                case HudTuningSection.Recording:      AddRecordingExpander();      break;
                case HudTuningSection.Transcribing:   AddTranscribingExpander();   break;
                case HudTuningSection.Rewriting:      AddRewritingExpander();      break;
                case HudTuningSection.Gauge:          AddGaugeExpander();          break;
                case HudTuningSection.DigitReveal:    AddDigitRevealExpander();    break;
                case HudTuningSection.Simulation:     AddSimulationExpander();     break;
                case HudTuningSection.Orientations:   AddOrientationsExpander();   break;
            }
        }

        // Every reset and revert path rebuilds the panel, so the bar follows
        // the knobs from here; the row factories cover the knob edits.
        UpdateUnsavedBar();
    }

    // Rebuilt wholesale: tracking each row's controls would cost more code
    // than rebuilding a few dozen rows.
    private void RebuildTuningPanel()
    {
        BuildTuningPanel();
        RequestRebuild();
    }
}
