using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Transcription;

namespace Deckle.Playground;

// ─── Streaming segmentation tuning page ──────────────────────────────────────
//
// Edits the energy segmenter's hangover curve and detection knobs with the live
// curve plot right next to the sliders. Same imperative slider-sync pattern as
// AmbientPage (code-behind ValueChanged → VM, PushViewModelToControls the other
// way, _initializing guard), but the persistence model is EPHEMERAL : nothing
// touches the store until Save. The footer Save / Revert / Reset bar is driven by
// SegmentationViewModel.IsDirty / CanReset.
//
// HangoverCurve (HangoverCurveCanvas) is refreshed from the VM on every curve-
// shaping change ; the three detection knobs (threshold / margin / min utterance)
// don't shape the curve, so they skip the canvas refresh.
public sealed partial class SegmentationPage : Page
{
    public SegmentationViewModel ViewModel { get; } = new();

    // Guards programmatic Slider writes (range setup + PushViewModelToControls)
    // so the ValueChanged handlers don't mistake them for user edits — same role
    // as _initializing on AmbientPage.
    private bool _initializing;

    // Loaded re-fires on every navigate-back for a cached (NavigationCacheMode.
    // Required) page ; this gates the one-time load so unsaved edits survive a
    // round-trip through another Playground page (the ephemeral model must never
    // silently discard them).
    private bool _loaded;

    public SegmentationPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        _initializing = true;
        SetupSliderRanges();

        // Mirror the dirty flag into the "Unsaved changes" caption (the buttons
        // bind IsEnabled directly via x:Bind OneWay).
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SegmentationViewModel.IsDirty))
                UpdateUnsavedIndicator();
        };

        if (this.Content is FrameworkElement root)
            root.Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        ViewModel.Load();
        PushViewModelToControls();

        // The sliders coerce the freshly-loaded values onto their StepFrequency
        // grid. A value persisted off-grid (the Whisper settings page edits the
        // same store through free-entry NumberBoxes) would otherwise leave the
        // thumb, the VM and the readout disagreeing. Adopt the coerced slider
        // values as both the live state and the saved baseline.
        ViewModel.AdoptAsSaved(
            SegHangoverMaxSlider.Value, SegHangoverMinSlider.Value,
            SegRampStartSlider.Value, SegRampEndSlider.Value,
            SegContrastSlider.Value, SegPositionSlider.Value, SegSharpnessSlider.Value,
            SegThresholdSlider.Value,
            (int)Math.Round(SegMarginSlider.Value), (int)Math.Round(SegMinUtteranceSlider.Value));

        UpdateAllValueTexts();
        RefreshCanvas();
        UpdateUnsavedIndicator();
        _initializing = false;
    }

    // Minimum / Maximum live here, not in XAML : the WinUI 3 parser throws when a
    // Slider declares Minimum > its default Value (0). Setting them at runtime
    // just coerces Value into the band — Minimum first so a below-zero Maximum
    // (the dBFS threshold) isn't clamped up to the old Minimum. The real Value is
    // seeded later by PushViewModelToControls.
    private void SetupSliderRanges()
    {
        SetRange(SegHangoverMaxSlider,   0.5, 15.0);
        SetRange(SegHangoverMinSlider,   0.1,  2.0);
        SetRange(SegRampStartSlider,     0.0, 180.0);
        SetRange(SegRampEndSlider,      30.0, 240.0);
        SetRange(SegContrastSlider,      0.2, 20.0);
        SetRange(SegPositionSlider,      0.0,  1.0);
        SetRange(SegSharpnessSlider,     1.0, 30.0);
        SetRange(SegThresholdSlider,   -70.0, -20.0);
        SetRange(SegMarginSlider,        0.0, 1000.0);
        SetRange(SegMinUtteranceSlider,  0.0, 1000.0);
    }

    private static void SetRange(Slider s, double min, double max)
    {
        s.Minimum = min;
        s.Maximum = max;
    }

    // VM → controls. Self-guards _initializing so the seeded Slider.Value writes
    // don't re-enter the handlers, then refreshes the readouts the guarded
    // handlers skipped.
    private void PushViewModelToControls()
    {
        bool prev = _initializing;
        _initializing = true;
        try
        {
            SegHangoverMaxSlider.Value   = ViewModel.HangoverMaxSec;
            SegHangoverMinSlider.Value   = ViewModel.HangoverMinSec;
            SegRampStartSlider.Value     = ViewModel.RampStartSec;
            SegRampEndSlider.Value       = ViewModel.RampEndSec;
            SegContrastSlider.Value      = ViewModel.Contrast;
            SegPositionSlider.Value      = ViewModel.Position;
            SegSharpnessSlider.Value     = ViewModel.Sharpness;
            SegThresholdSlider.Value     = ViewModel.ThresholdDbfs;
            SegMarginSlider.Value        = ViewModel.MarginMs;
            SegMinUtteranceSlider.Value  = ViewModel.MinUtteranceMs;
        }
        finally
        {
            _initializing = prev;
        }

        UpdateAllValueTexts();
        RefreshCanvas();
    }

    // ── Curve-shaping sliders : write the VM, update the readout, redraw ───────

    private void OnSegHangoverMaxChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.HangoverMaxSec = SegHangoverMaxSlider.Value;
        SegHangoverMaxValueText.Text = $"{ViewModel.HangoverMaxSec:F1} s";
        RefreshCanvas();
    }

    private void OnSegHangoverMinChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.HangoverMinSec = SegHangoverMinSlider.Value;
        SegHangoverMinValueText.Text = $"{ViewModel.HangoverMinSec:F1} s";
        RefreshCanvas();
    }

    private void OnSegRampStartChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.RampStartSec = SegRampStartSlider.Value;
        SegRampStartValueText.Text = $"{ViewModel.RampStartSec:F0} s";
        RefreshCanvas();
    }

    private void OnSegRampEndChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.RampEndSec = SegRampEndSlider.Value;
        SegRampEndValueText.Text = $"{ViewModel.RampEndSec:F0} s";
        RefreshCanvas();
    }

    private void OnSegContrastChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.Contrast = SegContrastSlider.Value;
        SegContrastValueText.Text = $"{ViewModel.Contrast:F1}";
        RefreshCanvas();
    }

    private void OnSegPositionChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.Position = SegPositionSlider.Value;
        SegPositionValueText.Text = $"{ViewModel.Position:F2}";
        RefreshCanvas();
    }

    private void OnSegSharpnessChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.Sharpness = SegSharpnessSlider.Value;
        SegSharpnessValueText.Text = $"{ViewModel.Sharpness:F1}";
        RefreshCanvas();
    }

    // ── Detection sliders : write the VM + readout, no curve change ────────────

    private void OnSegThresholdChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.ThresholdDbfs = SegThresholdSlider.Value;
        SegThresholdValueText.Text = $"{ViewModel.ThresholdDbfs:F0} dBFS";
    }

    private void OnSegMarginChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.MarginMs = (int)Math.Round(SegMarginSlider.Value);
        SegMarginValueText.Text = $"{ViewModel.MarginMs} ms";
    }

    private void OnSegMinUtteranceChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.MinUtteranceMs = (int)Math.Round(SegMinUtteranceSlider.Value);
        SegMinUtteranceValueText.Text = $"{ViewModel.MinUtteranceMs} ms";
    }

    // ── Action bar ─────────────────────────────────────────────────────────────

    private void OnSaveClick(object sender, RoutedEventArgs e) => ViewModel.Save();

    private void OnRevertClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Revert();
        PushViewModelToControls();
    }

    private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetDefaults();
        PushViewModelToControls();
    }

    // ── Readouts ────────────────────────────────────────────────────────────────

    private void UpdateAllValueTexts()
    {
        SegHangoverMaxValueText.Text  = $"{ViewModel.HangoverMaxSec:F1} s";
        SegHangoverMinValueText.Text  = $"{ViewModel.HangoverMinSec:F1} s";
        SegRampStartValueText.Text    = $"{ViewModel.RampStartSec:F0} s";
        SegRampEndValueText.Text      = $"{ViewModel.RampEndSec:F0} s";
        SegContrastValueText.Text     = $"{ViewModel.Contrast:F1}";
        SegPositionValueText.Text     = $"{ViewModel.Position:F2}";
        SegSharpnessValueText.Text    = $"{ViewModel.Sharpness:F1}";
        SegThresholdValueText.Text    = $"{ViewModel.ThresholdDbfs:F0} dBFS";
        SegMarginValueText.Text       = $"{ViewModel.MarginMs} ms";
        SegMinUtteranceValueText.Text = $"{ViewModel.MinUtteranceMs} ms";
    }

    private void RefreshCanvas()
    {
        HangoverCurve.HangoverMaxSec = ViewModel.HangoverMaxSec;
        HangoverCurve.HangoverMinSec = ViewModel.HangoverMinSec;
        HangoverCurve.RampStartSec   = ViewModel.RampStartSec;
        HangoverCurve.RampEndSec     = ViewModel.RampEndSec;
        HangoverCurve.Contrast       = ViewModel.Contrast;
        HangoverCurve.Position       = ViewModel.Position;
        HangoverCurve.Sharpness      = ViewModel.Sharpness;
        UpdateAxisAndReadout();
    }

    private void UpdateAxisAndReadout()
    {
        double startSec = ViewModel.RampStartSec;
        double endSec   = Math.Max(startSec, ViewModel.RampEndSec);
        double span     = endSec - startSec;
        double xMax     = endSec + Math.Max(span * 0.15, 5.0);

        AxisYMaxText.Text = $"{ViewModel.HangoverMaxSec:F1} s";
        AxisXMaxText.Text = $"{xMax:F0} s";
        CurveReadoutText.Text =
            $"Holds {ViewModel.HangoverMaxSec:F1} s up to {startSec:F0} s, then declines to {ViewModel.HangoverMinSec:F1} s by {endSec:F0} s.";
    }

    private void UpdateUnsavedIndicator()
        => UnsavedIndicator.Visibility = ViewModel.IsDirty ? Visibility.Visible : Visibility.Collapsed;
}
