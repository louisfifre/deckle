using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Globalization.NumberFormatting;

namespace Deckle.Playground;

// ─── Streaming segmentation tuning page ──────────────────────────────────────
//
// Every control binds two-way to SegmentationViewModel, the single source, so
// there is no imperative control sync. Nothing touches the store until Save.
// The page watches the view-model only to refresh the two text readouts and
// toggle the unsaved bar.
public sealed partial class SegmentationPage : Page
{
    public SegmentationViewModel ViewModel { get; } = new();

    // Loaded re-fires on every return to the cached page; loading once keeps
    // unsaved edits across a round-trip through another Playground page.
    private bool _loaded;

    public SegmentationPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Two decimals, so a control point reads 0.24, not 0.2400000002.
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

        UnsavedBar.SizeChanged += (_, e) => UnsavedSpacer.Height = e.NewSize.Height;

        if (this.Content is FrameworkElement root)
            root.Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        ViewModel.Load();
        UpdateReadouts();
        UpdateUnsavedBar();
        DecklePlaygroundSource.Log.PageReady(nameof(SegmentationPage), DecklePlaygroundSource.NavClock.ElapsedMilliseconds);
    }

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
    // The curve editor is not a TuningCard, so its "Reset section" gets the
    // cards' hover/focus reveal wired by hand: instant, no animation.

    private bool _curvePointerOver;
    private bool _curveResetFocused;

    private void OnCurveSectionPointerEntered(object sender, PointerRoutedEventArgs e) { _curvePointerOver = true;  UpdateCurveResetReveal(); }
    private void OnCurveSectionPointerExited(object sender, PointerRoutedEventArgs e)  { _curvePointerOver = false; UpdateCurveResetReveal(); }
    private void OnCurveResetGotFocus(object sender, RoutedEventArgs e)  { _curveResetFocused = true;  UpdateCurveResetReveal(); }
    private void OnCurveResetLostFocus(object sender, RoutedEventArgs e) { _curveResetFocused = false; UpdateCurveResetReveal(); }

    private void UpdateCurveResetReveal() =>
        CurveResetButton.Opacity = _curvePointerOver || _curveResetFocused ? 1 : 0;

    // ── Resize coalescing ────────────────────────────────────────────────────────
    //
    // The window's ResizeCoalescer sets this while a resize gesture runs; the
    // canvas then skips its costly axis-label layout. The null guard covers a
    // call before the named part exists.
    public void SetCurveResizeSuspended(bool suspended)
    {
        if (HangoverCurve is not null)
            HangoverCurve.SuspendExpensiveDraw = suspended;
    }

    // ── Graph show / hide ────────────────────────────────────────────────────────

    private void OnGraphToggleChanged(object sender, RoutedEventArgs e)
    {
        // IsChecked="True" can raise Checked during InitializeComponent, before
        // the sibling elements exist; the XAML defaults already match it.
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
