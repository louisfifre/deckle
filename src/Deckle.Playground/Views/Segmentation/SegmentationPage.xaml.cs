using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Globalization.NumberFormatting;

namespace Deckle.Playground;

// ─── Streaming segmentation tuning page ──────────────────────────────────────
//
// Edits the energy segmenter's hangover curve and detection knobs with the live
// curve plot right next to them. Every knob is a TunableRow (slider + editable
// NumberBox + per-parameter reset) bound two-way to SegmentationViewModel, and
// the curve's four Bézier control points are bound two-way to the plot's handles
// and to the manual-entry boxes under it — so the view-model is the single source
// and there is no imperative control sync to keep in step.
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

        // Curve-coordinate boxes : pin the display to two decimals so a control
        // point reads as 0.24, not 0.2400000002 (the double's full precision).
        // One stateless formatter shared across the four boxes.
        var curveFormatter = new DecimalFormatter
        {
            IntegerDigits  = 1,
            FractionDigits = 2,
            IsGrouped      = false,
            NumberRounder  = new IncrementNumberRounder
            {
                Increment         = 0.01,
                RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp,
            },
        };
        CurveX1Box.NumberFormatter = curveFormatter;
        CurveY1Box.NumberFormatter = curveFormatter;
        CurveX2Box.NumberFormatter = curveFormatter;
        CurveY2Box.NumberFormatter = curveFormatter;

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

    // ── Resize coalescing ────────────────────────────────────────────────────────
    //
    // The host window drives this off its ResizeCoalescer: true on a resize
    // gesture's rising edge, false when it settles. We forward it to the curve
    // canvas, whose OnDraw drops the costly axis-label text layout while it's true.
    // Guarded because the named part may not exist yet on a very early call.
    public void SetCurveResizeSuspended(bool suspended)
    {
        if (HangoverCurve is not null)
            HangoverCurve.SuspendExpensiveDraw = suspended;
    }

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

    private void UpdateReadouts()
    {
        double startSec = ViewModel.RampStartSec;
        double endSec   = Math.Max(startSec, ViewModel.RampEndSec);
        CurveReadoutText.Text =
            $"Waits {ViewModel.HangoverMaxSec:F1} s of silence for utterances up to {startSec:F0} s, then tightens to {ViewModel.HangoverMinSec:F1} s by {endSec:F0} s.";
        CurveBezierText.Text =
            $"cubic-bezier({ViewModel.CurveX1:0.00}, {ViewModel.CurveY1:0.00}, {ViewModel.CurveX2:0.00}, {ViewModel.CurveY2:0.00})";
    }

    private void UpdateUnsavedBar()
    {
        bool dirty = ViewModel.IsDirty;
        UnsavedBar.Visibility    = dirty ? Visibility.Visible : Visibility.Collapsed;
        UnsavedSpacer.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }
}
