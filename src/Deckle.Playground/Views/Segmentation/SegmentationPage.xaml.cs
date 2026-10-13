using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Playground;

// ─── Streaming segmentation tuning page ──────────────────────────────────────
//
// Edits the energy segmenter's hangover curve and detection knobs with the live
// curve plot right next to them. Every knob is a TunableRow (slider + editable
// NumberBox + per-parameter reset) bound two-way to SegmentationViewModel, and
// the curve's four Bézier control points are bound two-way to the shared curve
// block (its handles and its fields) — so the view-model is the single source
// and there is no imperative control sync to keep in step, the plot's frame
// apart.
//
// The persistence model is EPHEMERAL : nothing touches the store until Save. The
// footer Save / Revert bar and the Reset affordances are driven by
// SegmentationViewModel.IsDirty / CanReset. The page only watches the view-model
// to refresh the two plain-text readouts and toggle the unsaved bar.
public sealed partial class SegmentationPage : Page
{
    public SegmentationViewModel ViewModel { get; } = new();

    // Loaded re-fires on every navigate-back for a cached (NavigationCacheMode.
    // Required) page ; this gates the one-time load so unsaved edits survive a
    // round-trip through another Playground page (the ephemeral model must never
    // silently discard them).
    private bool _loaded;

    public SegmentationPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Keep the footer spacer as tall as the floating save bar, so the last
        // caption can scroll just clear of it without leaving a big empty gap.
        UnsavedBar.SizeChanged += (_, e) => UnsavedSpacer.Height = e.NewSize.Height;

        if (this.Content is FrameworkElement root)
            root.Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        // Pull the persisted settings into the view-model ; the two-way bindings
        // propagate them into the rows, the plot and the boxes.
        ViewModel.Load();
        UpdateReadouts();
        UpdateUnsavedBar();
        // First Loaded = page ready after the load above (gated by _loaded).
        // Measured from the nav-start clock set in PlaygroundWindow.
        DecklePlaygroundSource.Log.PageReady(nameof(SegmentationPage), DecklePlaygroundSource.NavClock.ElapsedMilliseconds);
    }

    // The view-model is the single source : watch it to keep the two plain-text
    // readouts current and to toggle the unsaved bar. Everything else flows
    // through the two-way bindings.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SegmentationViewModel.IsDirty):
                UpdateUnsavedBar();
                break;

            case nameof(SegmentationViewModel.HangoverMaxSec):
            case nameof(SegmentationViewModel.HangoverMinSec):
            case nameof(SegmentationViewModel.RampStartSec):
            case nameof(SegmentationViewModel.RampEndSec):
            case nameof(SegmentationViewModel.CurveX1):
            case nameof(SegmentationViewModel.CurveY1):
            case nameof(SegmentationViewModel.CurveX2):
            case nameof(SegmentationViewModel.CurveY2):
                UpdateReadouts();
                break;
        }
    }

    // ── Action bar ─────────────────────────────────────────────────────────────

    private void OnSaveClick(object sender, RoutedEventArgs e)         => ViewModel.Save();
    private void OnRevertClick(object sender, RoutedEventArgs e)       => ViewModel.Revert();
    private void OnResetDefaultsClick(object sender, RoutedEventArgs e) => ViewModel.ResetDefaults();

    // ── Per-section reset wheels ─────────────────────────────────────────────────

    private void OnResetHangoverRampClick(object sender, RoutedEventArgs e) => ViewModel.ResetHangoverRamp();
    private void OnResetCurveClick(object sender, RoutedEventArgs e)        => ViewModel.ResetCurve();
    private void OnResetDetectionClick(object sender, RoutedEventArgs e)    => ViewModel.ResetDetection();

    // ── Curve-shape reset reveal ─────────────────────────────────────────────────
    //
    // The curve editor lives in the left column rather than a TuningCard, so its
    // "Reset section" gets the same hover/focus reveal wired by hand — instant, no
    // animation, matching the cards and the HUD expanders.

    private bool _curvePointerOver;
    private bool _curveResetFocused;

    private void OnCurveSectionPointerEntered(object sender, PointerRoutedEventArgs e) { _curvePointerOver = true;  UpdateCurveResetReveal(); }
    private void OnCurveSectionPointerExited(object sender, PointerRoutedEventArgs e)  { _curvePointerOver = false; UpdateCurveResetReveal(); }
    private void OnCurveResetGotFocus(object sender, RoutedEventArgs e)  { _curveResetFocused = true;  UpdateCurveResetReveal(); }
    private void OnCurveResetLostFocus(object sender, RoutedEventArgs e) { _curveResetFocused = false; UpdateCurveResetReveal(); }

    private void UpdateCurveResetReveal() =>
        CurveResetButton.Opacity = _curvePointerOver || _curveResetFocused ? 1 : 0;

    // ── Graph show / hide ────────────────────────────────────────────────────────

    private void OnGraphToggleChanged(object sender, RoutedEventArgs e)
    {
        // IsChecked="True" can raise Checked during InitializeComponent, before the
        // sibling elements exist. The XAML defaults already match the shown state,
        // so bail out of that early fire.
        if (GraphToggleText is null || HangoverCurve is null) return;

        bool show = GraphToggle.IsChecked == true;
        GraphToggleText.Text = show ? "Hide graph" : "Show graph";
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        HangoverCurve.Visibility = vis;
        GraphLegend.Visibility   = vis;
    }

    // ── Readouts ────────────────────────────────────────────────────────────────
    //
    // The sentence under the plot, and the plot's frame: X is the open-utterance
    // length in seconds up to a little past the ramp end, Y the decision delay
    // from 0 to the maximum hangover, the unit square running from (ramp start,
    // max) down to (ramp end, min). The cubic-bezier readout is the block's own.

    private void UpdateReadouts()
    {
        double maxSec   = Math.Max(ViewModel.HangoverMaxSec, 1e-3);
        double minSec   = Math.Min(ViewModel.HangoverMinSec, maxSec);
        double startSec = Math.Max(0, ViewModel.RampStartSec);
        double endSec   = Math.Max(startSec, ViewModel.RampEndSec);
        CurveReadoutText.Text =
            $"Waits {ViewModel.HangoverMaxSec:F1} s of silence for utterances up to {startSec:F0} s, then tightens to {ViewModel.HangoverMinSec:F1} s by {endSec:F0} s.";
        HangoverCurve.Frame = new Deckle.Catalog.CurveFrame(
            startSec, maxSec, endSec, minSec,
            XMax: endSec + Math.Max((endSec - startSec) * 0.15, 5.0), YMax: maxSec,
            Axes: true, XUnit: "s", YUnit: "s");
    }

    private void UpdateUnsavedBar()
    {
        bool dirty = ViewModel.IsDirty;
        UnsavedBar.Visibility    = dirty ? Visibility.Visible : Visibility.Collapsed;
        UnsavedSpacer.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }
}
