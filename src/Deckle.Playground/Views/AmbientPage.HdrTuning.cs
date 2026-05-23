using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Deckle.Catalog;
using Deckle.Lighting.Ambient;

namespace Deckle.Playground;

// ─── AmbientPage — pipeline + HDR tuning sliders ────────────────────────────
//
// Pipeline toggle (master Enabled flag of the AmbientEngine) and the
// HDR knobs sandbox : Mode preset, Brightness curve (type + param),
// Smoothing α, Change threshold, Exposure, Saturation, Min brightness.
// Slider handlers write through the ViewModel which persists into
// AmbientSettings via AmbientSettingsService — same store the Settings
// AmbientPage and the engine itself consume.
//
// PushViewModelToControls is the inverse pump : after ViewModel.Load
// pulls fresh values from AmbientSettings, this method seeds every
// slider / combo / canvas so the visuals match. Wrapped by the caller
// in `_initializing = true` so the synthetic ValueChanged events fired
// by Slider.Value = X don't loop back through the VM setters.

public sealed partial class AmbientPage
{
    // ── Pipeline ────────────────────────────────────────────────────────────

    private void SetPipelineReady()
    {
        PipelineToggleButton.IsEnabled = true;
        SyncPipelineUiFromViewModel();
    }

    private void SetPipelineNotReady()
    {
        PipelineToggleButton.IsEnabled = false;
        PipelineToggleIcon.Glyph = Glyphs.Transport.Play;
        PipelineToggleLabel.Text = "Turn Ambient Light on";
        PipelineStatusText.Text = "Pair a bridge and pick a group first";
        PipelineStatusDot.Fill = GetThemeBrush("SystemFillColorNeutralBrush");
    }

    private void OnPipelineToggleClick(object sender, RoutedEventArgs e)
    {
        // Flip AmbientSettings.Enabled through the VM ; the VM's
        // OnEnabledChanged persists, the AmbientEngine observer in App
        // starts / stops the canonical pipeline.
        ViewModel.Enabled = !ViewModel.Enabled;
    }

    private void SyncPipelineUiFromViewModel()
    {
        if (PipelineToggleButton is null) return;
        bool enabled = ViewModel.Enabled;
        PipelineToggleIcon.Glyph  = enabled ? Glyphs.Transport.Stop : Glyphs.Transport.Play;
        PipelineToggleLabel.Text  = enabled ? "Turn Ambient Light off" : "Turn Ambient Light on";
        PipelineStatusText.Text   = enabled ? "Running" : "Stopped";
        PipelineStatusDot.Fill    = GetThemeBrush(enabled
            ? "SystemFillColorSuccessBrush"
            : "SystemFillColorNeutralBrush");

        int desiredIndex = ViewModel.UseMultiLight ? 1 : 0;
        if (PipelineModeRadios is not null
            && PipelineModeRadios.SelectedIndex != desiredIndex)
        {
            PipelineModeRadios.SelectedIndex = desiredIndex;
        }
    }

    private void ApplyPipelineReadiness()
    {
        if (PipelineToggleButton is null) return;
        var s = AmbientSettingsService.Instance.Current;
        bool paired = HuePairingService.Instance.Bridge?.IsPaired == true;
        bool hasGroup = !string.IsNullOrEmpty(s.HueLastGroupId);
        if (paired && hasGroup) SetPipelineReady();
        else                    SetPipelineNotReady();
    }

    private void OnPipelineModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (sender is not RadioButtons radios) return;
        bool useMulti = radios.SelectedIndex switch
        {
            0 => false,
            1 => true,
            _ => ViewModel.UseMultiLight,
        };
        if (ViewModel.UseMultiLight == useMulti) return;
        ViewModel.UseMultiLight = useMulti;
    }

    // ── HDR tuning : combo selectors + slider Min/Max + canvas type ─────────

    private double SelectCurveParamForType(BrightnessCurveType type)
        => type switch
        {
            BrightnessCurveType.Gamma  => ViewModel.BrightnessCurveParam,
            BrightnessCurveType.SCurve => ViewModel.BrightnessCurveSCurveSteepness,
            _                          => ViewModel.BrightnessCurveParam,
        };

    private void SelectBrightnessCurveTypeInCombo(BrightnessCurveType type)
    {
        string tag = type.ToString();
        for (int i = 0; i < PlaygroundBrightnessCurveCombo.Items.Count; i++)
        {
            if (PlaygroundBrightnessCurveCombo.Items[i] is ComboBoxItem cbi
                && (cbi.Tag as string) == tag)
            {
                PlaygroundBrightnessCurveCombo.SelectedIndex = i;
                return;
            }
        }
        PlaygroundBrightnessCurveCombo.SelectedIndex = 1;
    }

    private void UpdatePlaygroundBrightnessCurveDependentUi()
    {
        var type = ReadBrightnessCurveTypeFromCombo();
        bool paramHasEffect = type == BrightnessCurveType.Gamma
                           || type == BrightnessCurveType.SCurve;
        PlaygroundGammaSlider.IsEnabled = paramHasEffect;

        // Slider range follows the active curve.
        //   - Gamma [0.3, 3.0] with 1.0 as the neutral mid-point.
        //     γ > 1 squashes the bottom of the range (legacy direction) ;
        //     γ < 1 lifts the bottom, behaving like a tunable
        //     Logarithmic. The asymmetric range mirrors that γ = 1/x
        //     is the symmetric reflection through y = x, but the lower
        //     half is sharper perceptually so [0.3, 1] covers enough.
        //   - SCurve [-5.0, 5.0] symmetric around 0. k > 0 is the
        //     classic S that pushes mid-tones away from grey ; k < 0
        //     is the anti-S that flattens mid-tones toward grey. The
        //     dead-zone around 0 reads as Linear (the engine traps
        //     |k| < 0.05). 5 reads "near-step" per the engine notes,
        //     overshoot beyond that buys nothing.
        // Order matters when shrinking (Max < current Value clamps
        // Value), so we always set Max before Min and the caller
        // re-projects Value right after.
        switch (type)
        {
            case BrightnessCurveType.Gamma:
                PlaygroundGammaSlider.Maximum = 3.0;
                PlaygroundGammaSlider.Minimum = 0.3;
                break;
            case BrightnessCurveType.SCurve:
                PlaygroundGammaSlider.Maximum = 5.0;
                PlaygroundGammaSlider.Minimum = -5.0;
                break;
            default:
                PlaygroundGammaSlider.Maximum = 5.0;
                PlaygroundGammaSlider.Minimum = -5.0;
                break;
        }

        PlaygroundGammaCanvas.CurveType = type;
        PlaygroundGammaCanvas.Opacity   = paramHasEffect ? 1.0 : 0.4;

        PlaygroundGammaSliderRow.Visibility = paramHasEffect
            ? Visibility.Visible
            : Visibility.Collapsed;

        PlaygroundGammaCaption.Text = type switch
        {
            BrightnessCurveType.Linear      => "Direct pass-through — input max channel is sent to the lamp as-is. No parameter to tune ; rely on smoothing and min brightness for fine control.",
            BrightnessCurveType.Gamma       => "Power-law shaping on the bri range. γ > 1 squashes dim scenes harder without touching saturated highlights. γ < 1 lifts the bottom of the range — same direction as Logarithmic, with the slider letting you dial how hard. 0.5 — strongly lifted shadows · 1.0 — linear · 1.8 — default · 2.5 — strongly dimmed shadows.",
            BrightnessCurveType.SCurve      => "Logistic shaping around the mid-grey. k > 0 pushes mid-tones away from grey (dim scenes darker, bright scenes brighter — high-contrast feel). k < 0 mirrors the curve into an anti-S that flattens mid-tones toward grey (calmer, averaging feel). −5.0 — near anti-step · −2.0 — soft anti-S · 0 — linear · 2.0 — default · 5.0 — near-step.",
            BrightnessCurveType.Logarithmic => "Lifts the bottom of the range so even very dim scenes stay clearly lit. No parameter to tune — the curve is fixed.",
            _ => string.Empty,
        };

        // Smoothing slider range — same constraint as the gamma
        // slider above. Maximum first, then Minimum (Range invariant).
        PlaygroundSmoothingSlider.Maximum = 1.0;
        PlaygroundSmoothingSlider.Minimum = 0.05;
    }

    private BrightnessCurveType ReadBrightnessCurveTypeFromCombo()
    {
        if (PlaygroundBrightnessCurveCombo.SelectedItem is ComboBoxItem cbi
         && cbi.Tag is string tag
         && Enum.TryParse<BrightnessCurveType>(tag, out var parsed))
        {
            return parsed;
        }
        return BrightnessCurveType.Gamma;
    }

    private void SelectAmbientModeInCombo(AmbientMode mode)
    {
        string tag = mode.ToString();
        for (int i = 0; i < PlaygroundAmbientModeCombo.Items.Count; i++)
        {
            if (PlaygroundAmbientModeCombo.Items[i] is ComboBoxItem cbi
                && (cbi.Tag as string) == tag)
            {
                PlaygroundAmbientModeCombo.SelectedIndex = i;
                return;
            }
        }
        PlaygroundAmbientModeCombo.SelectedIndex = 3;
    }

    // ── Push VM → controls ──────────────────────────────────────────────────
    //
    // Inverse of the slider handlers below. Seeded after ViewModel.Load
    // by both OnPageLoaded (first nav) and OnNavigatedTo (cached page
    // reuse) ; the caller is responsible for wrapping in
    // `_initializing = true` so the synthetic ValueChanged events fired
    // here don't loop back through the VM setters and re-Save.

    private void PushViewModelToControls()
    {
        PlaygroundExposureSlider.Value         = ViewModel.ExposureEv;
        PlaygroundSaturationSlider.Value       = ViewModel.SaturationBoost * 100.0;
        PlaygroundMinBrightnessSlider.Value    = ViewModel.MinBrightness;

        // ComboBoxes first : SelectBrightnessCurveTypeInCombo drives
        // the curve type that UpdatePlaygroundBrightnessCurveDependentUi
        // reads to rescale the param slider range. Then Min/Max are
        // set. Only then can the Value safely take any SCurve k (which
        // may be negative now) without being clamped by a stale Gamma
        // [0.3, 3.0] window.
        SelectBrightnessCurveTypeInCombo(ViewModel.BrightnessCurveType);
        SelectAmbientModeInCombo(ViewModel.Mode);
        UpdatePlaygroundBrightnessCurveDependentUi();

        double curveParam = SelectCurveParamForType(ViewModel.BrightnessCurveType);
        PlaygroundGammaSlider.Value            = curveParam;
        PlaygroundGammaCanvas.Gamma            = curveParam;
        PlaygroundSmoothingSlider.Value        = ViewModel.SmoothingAlpha;
        PlaygroundChangeThresholdSlider.Value  = ViewModel.ChangeThreshold;

        // Zone sampling. Share slider is in percent (5–50, integer
        // steps for clean ticks) ; the ViewModel stores the fraction
        // (0.05–0.50). Cells slider is the raw count (1–15). Clamp
        // before assignment so a hand-edited settings.json out of
        // range doesn't throw inside RangeBase. SelectBorderModeInRadios
        // + UpdateBorderRowsVisibility hide/show the active row.
        PlaygroundBorderDepthSlider.Value      = Math.Clamp(ViewModel.BorderDepth, 0.05, 0.5) * 100.0;
        PlaygroundBorderCellsSlider.Value      = Math.Clamp(ViewModel.BorderCells, 1, 15);
        SelectBorderModeInRadios(ViewModel.BorderMode);
        UpdateBorderRowsVisibility(ViewModel.BorderMode);

        UpdatePlaygroundExposureText();
        UpdatePlaygroundSaturationText();
        UpdatePlaygroundMinBrightnessText();
        UpdatePlaygroundGammaText();
        UpdatePlaygroundSmoothingText();
        UpdatePlaygroundChangeThresholdText();
        UpdatePlaygroundBorderDepthText();
        UpdatePlaygroundBorderCellsText();
    }

    // ── Slider handlers ─────────────────────────────────────────────────────

    private void OnPlaygroundGammaSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePlaygroundGammaText();
        PlaygroundGammaCanvas.Gamma = PlaygroundGammaSlider.Value;

        if (_initializing) return;

        // Route to the parameter that belongs to the active curve,
        // leaving the other curves' parameters untouched. Linear /
        // Logarithmic ignore the slider but we still write through
        // into the Gamma slot so the value is preserved if the user
        // switches back later.
        var type = ReadBrightnessCurveTypeFromCombo();
        switch (type)
        {
            case BrightnessCurveType.SCurve:
                ViewModel.BrightnessCurveSCurveSteepness = PlaygroundGammaSlider.Value;
                break;
            default:
                ViewModel.BrightnessCurveParam = PlaygroundGammaSlider.Value;
                break;
        }
    }

    private void OnPlaygroundExposureSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePlaygroundExposureText();
        if (_initializing) return;
        ViewModel.ExposureEv = PlaygroundExposureSlider.Value;
    }

    private void OnPlaygroundSaturationSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePlaygroundSaturationText();
        if (_initializing) return;
        ViewModel.SaturationBoost = PlaygroundSaturationSlider.Value / 100.0;
    }

    private void OnPlaygroundMinBrightnessSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePlaygroundMinBrightnessText();
        if (_initializing) return;
        ViewModel.MinBrightness = (int)Math.Round(PlaygroundMinBrightnessSlider.Value);
    }

    private void OnPlaygroundSmoothingSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePlaygroundSmoothingText();
        if (_initializing) return;
        ViewModel.SmoothingAlpha = PlaygroundSmoothingSlider.Value;
    }

    private void OnPlaygroundChangeThresholdSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePlaygroundChangeThresholdText();
        if (_initializing) return;
        ViewModel.ChangeThreshold = (int)Math.Round(PlaygroundChangeThresholdSlider.Value);
    }

    private void OnPlaygroundBorderDepthSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePlaygroundBorderDepthText();
        if (_initializing) return;
        // Slider is in percent (5–50) for clean tick spacing ;
        // ViewModel stores the fraction (0.05–0.50).
        ViewModel.BorderDepth = PlaygroundBorderDepthSlider.Value / 100.0;
    }

    private void OnPlaygroundBorderCellsSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePlaygroundBorderCellsText();
        if (_initializing) return;
        ViewModel.BorderCells = (int)Math.Round(PlaygroundBorderCellsSlider.Value);
    }

    private void OnPlaygroundBorderModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not RadioButtons radios) return;
        if (radios.SelectedItem is not RadioButton rb || rb.Tag is not string tag) return;
        if (!Enum.TryParse<BorderThicknessMode>(tag, out var mode)) return;

        UpdateBorderRowsVisibility(mode);
        if (_initializing) return;
        if (ViewModel.BorderMode == mode) return;
        ViewModel.BorderMode = mode;
    }

    private void SelectBorderModeInRadios(BorderThicknessMode mode)
    {
        string tag = mode.ToString();
        for (int i = 0; i < PlaygroundBorderModeRadios.Items.Count; i++)
        {
            if (PlaygroundBorderModeRadios.Items[i] is RadioButton rb
                && (rb.Tag as string) == tag)
            {
                PlaygroundBorderModeRadios.SelectedIndex = i;
                return;
            }
        }
        PlaygroundBorderModeRadios.SelectedIndex = 0;
    }

    private void UpdateBorderRowsVisibility(BorderThicknessMode mode)
    {
        PlaygroundBorderShareRow.Visibility = mode == BorderThicknessMode.Share
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaygroundBorderCellsRow.Visibility = mode == BorderThicknessMode.Cells
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnPlaygroundBrightnessCurveTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        var type = ReadBrightnessCurveTypeFromCombo();

        // Order matters when the slider range is shrinking : Min/Max
        // must be set before Value so we don't transiently clamp a
        // valid SCurve k = −4 onto Gamma's 0.3 floor (or k = 4 onto
        // Gamma's 3.0 ceiling) and lose the user's intent.
        UpdatePlaygroundBrightnessCurveDependentUi();
        bool prev = _initializing;
        _initializing = true;
        try
        {
            PlaygroundGammaSlider.Value = SelectCurveParamForType(type);
        }
        finally
        {
            _initializing = prev;
        }

        UpdatePlaygroundGammaText();

        if (_initializing) return;
        ViewModel.BrightnessCurveType = type;
    }

    private void OnPlaygroundAmbientModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (PlaygroundAmbientModeCombo.SelectedItem is ComboBoxItem cbi
            && cbi.Tag is string tag
            && Enum.TryParse<AmbientMode>(tag, out var mode))
        {
            // VM.OnModeChanged calls ApplyPreset which copies the
            // preset's tunings onto every other knob and fires Changed
            // → OnAmbientSettingsChanged → PushViewModelToControls.
            // All sliders refresh in one pass.
            ViewModel.Mode = mode;
        }
    }

    // ── Value-text formatters ───────────────────────────────────────────────

    private void UpdatePlaygroundGammaText()
    {
        var type = ReadBrightnessCurveTypeFromCombo();
        PlaygroundGammaValueText.Text = type switch
        {
            BrightnessCurveType.Gamma  => $"γ {PlaygroundGammaSlider.Value:F2}",
            BrightnessCurveType.SCurve => $"k {PlaygroundGammaSlider.Value:F2}",
            _                          => "—",
        };
    }

    private void UpdatePlaygroundExposureText()
    {
        double v = PlaygroundExposureSlider.Value;
        PlaygroundExposureValueText.Text = $"{(v >= 0 ? "+" : "")}{v:F1} EV";
    }

    private void UpdatePlaygroundSaturationText()
        => PlaygroundSaturationValueText.Text = $"{(int)Math.Round(PlaygroundSaturationSlider.Value)} %";

    private void UpdatePlaygroundMinBrightnessText()
        => PlaygroundMinBrightnessValueText.Text = $"{(int)Math.Round(PlaygroundMinBrightnessSlider.Value)}";

    private void UpdatePlaygroundSmoothingText()
        => PlaygroundSmoothingValueText.Text = $"α {PlaygroundSmoothingSlider.Value:F2}";

    private void UpdatePlaygroundChangeThresholdText()
        => PlaygroundChangeThresholdValueText.Text = $"{(int)Math.Round(PlaygroundChangeThresholdSlider.Value)}";

    private void UpdatePlaygroundBorderDepthText()
        => PlaygroundBorderDepthValueText.Text = $"{(int)Math.Round(PlaygroundBorderDepthSlider.Value)} %";

    private void UpdatePlaygroundBorderCellsText()
    {
        int cells = (int)Math.Round(PlaygroundBorderCellsSlider.Value);
        PlaygroundBorderCellsValueText.Text = cells == 1 ? "1 cell" : $"{cells} cells";
    }
}
