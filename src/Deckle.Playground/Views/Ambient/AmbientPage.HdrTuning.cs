using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Deckle.Catalog;
using Deckle.Lighting.Ambient;

namespace Deckle.Playground;

// ─── AmbientPage — pipeline + tuning controls ──────────────────────────────
//
// Pipeline toggle (master Enabled flag of the AmbientEngine) and the
// tuning knobs sandbox : preset, compact Bézier brightness response,
// optional minimum-brightness floor, Smoothing α, Change threshold,
// Exposure and Saturation. The knobs apply to SDR and HDR alike : HDR
// content is tone-mapped to 8 bits before this stage. Handlers write
// through the ViewModel which persists into AmbientSettings via
// AmbientSettingsService — same store the Settings AmbientPage and the
// engine consume.
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

    // ── Tuning : selectors + live controls ──────────────────────────────────

    // One ComboBoxItem per preset option, in the family's declared order with
    // Custom last : Tag = the preset id, Content = its label from the
    // Deckle.Lighting.Ambient .resw ("<LabelKey>.Content", read as
    // "<LabelKey>/Content" from the module's PRI subtree — the same entry the
    // Settings preset selector reads). Built once from the constructor ; the
    // options are fixed for the process lifetime.
    private void BuildAmbientModeComboItems()
    {
        PlaygroundAmbientModeCombo.Items.Clear();
        foreach (var (id, labelKey) in AmbientSettingsService.Instance.Presets.SelectorOptions("AmbientPresetCustom"))
        {
            PlaygroundAmbientModeCombo.Items.Add(new ComboBoxItem
            {
                Tag     = id,
                Content = Loc.GetFrom(AmbientModuleResources, $"{labelKey}/Content"),
            });
        }
    }

    // PRI subtree of the module that owns the preset labels — the
    // OwningAssembly AmbientSettingsModule declares.
    private const string AmbientModuleResources = "Deckle.Lighting.Ambient";

    private void SelectAmbientModeInCombo(string preset)
    {
        for (int i = 0; i < PlaygroundAmbientModeCombo.Items.Count; i++)
        {
            if (PlaygroundAmbientModeCombo.Items[i] is ComboBoxItem cbi
                && (cbi.Tag as string) == preset)
            {
                PlaygroundAmbientModeCombo.SelectedIndex = i;
                return;
            }
        }
        // Unknown id : fall back to Custom, the last option.
        PlaygroundAmbientModeCombo.SelectedIndex = PlaygroundAmbientModeCombo.Items.Count - 1;
    }

    // ── Push VM → controls ──────────────────────────────────────────────────
    //
    // Inverse of the slider handlers below. Seeded after ViewModel.Load
    // by both OnPageLoaded (first nav) and OnNavigatedTo (cached page
    // reuse) ; the caller wraps in `_initializing = true` so synthetic
    // ValueChanged / Toggled events don't loop back through the VM.

    private void PushViewModelToControls()
    {
        PlaygroundExposureSlider.Value         = ViewModel.ExposureEv;
        PlaygroundSaturationSlider.Value       = ViewModel.SaturationBoost * 100.0;
        PlaygroundMinBrightnessSlider.Value    = ViewModel.MinBrightness;
        PlaygroundMinBrightnessToggle.IsOn     = ViewModel.MinBrightnessEnabled;

        PlaygroundBrightnessCurveCanvas.X1 = ViewModel.BrightnessCurveX1;
        PlaygroundBrightnessCurveCanvas.Y1 = ViewModel.BrightnessCurveY1;
        PlaygroundBrightnessCurveCanvas.X2 = ViewModel.BrightnessCurveX2;
        PlaygroundBrightnessCurveCanvas.Y2 = ViewModel.BrightnessCurveY2;
        PlaygroundBrightnessCurveCanvas.MinBrightnessEnabled = ViewModel.MinBrightnessEnabled;
        PlaygroundBrightnessCurveCanvas.MinBrightness = ViewModel.MinBrightness;

        SelectAmbientModeInCombo(ViewModel.Preset);

        PlaygroundSmoothingSlider.Maximum = 1.0;
        PlaygroundSmoothingSlider.Minimum = 0.05;
        PlaygroundSmoothingSlider.Value   = ViewModel.SmoothingAlpha;
        PlaygroundChangeThresholdSlider.Value = ViewModel.ChangeThreshold;

        // Zone sampling. Share slider is in percent (5–50, integer
        // steps for clean ticks) ; the ViewModel stores the fraction
        // (0.05–0.50). Cells slider is the raw count (1–15). Clamp
        // before assignment so a hand-edited settings.json out of
        // range doesn't throw inside RangeBase.
        PlaygroundBorderDepthSlider.Value = Math.Clamp(ViewModel.BorderDepth, 0.05, 0.5) * 100.0;
        PlaygroundBorderCellsSlider.Value = Math.Clamp(ViewModel.BorderCells, 4, 24);
        SelectBorderModeInRadios(ViewModel.BorderMode);
        UpdateBorderRowsVisibility(ViewModel.BorderMode);

        UpdatePlaygroundExposureText();
        UpdatePlaygroundSaturationText();
        UpdatePlaygroundMinBrightnessText();
        UpdatePlaygroundMinBrightnessUi();
        UpdatePlaygroundBrightnessCurveText();
        UpdatePlaygroundSmoothingText();
        UpdatePlaygroundChangeThresholdText();
        UpdatePlaygroundBorderDepthText();
        UpdatePlaygroundBorderCellsText();
    }

    // ── Slider / switch handlers ────────────────────────────────────────────

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

    private void OnPlaygroundMinBrightnessEnabledToggled(object sender, RoutedEventArgs e)
    {
        UpdatePlaygroundMinBrightnessUi();
        if (_initializing) return;
        ViewModel.MinBrightnessEnabled = PlaygroundMinBrightnessToggle.IsOn;
    }

    private void OnPlaygroundMinBrightnessSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePlaygroundMinBrightnessText();
        PlaygroundBrightnessCurveCanvas.MinBrightness = (int)Math.Round(PlaygroundMinBrightnessSlider.Value);
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

    private void OnPlaygroundAmbientModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (PlaygroundAmbientModeCombo.SelectedItem is ComboBoxItem cbi
            && cbi.Tag is string id)
        {
            // VM.OnPresetChanged selects the preset in the family, which
            // writes the preset's eight tuning values (or the kept Custom
            // values) onto the store, then saves ; Changed →
            // OnAmbientSettingsChanged → PushViewModelToControls refreshes
            // the sliders in one pass. The brightness floor is not part of a
            // preset : the Min brightness row keeps its value.
            ViewModel.Preset = id;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AmbientViewModel.BrightnessCurveX1):
            case nameof(AmbientViewModel.BrightnessCurveY1):
            case nameof(AmbientViewModel.BrightnessCurveX2):
            case nameof(AmbientViewModel.BrightnessCurveY2):
                if (PlaygroundBrightnessCurveText is not null)
                    UpdatePlaygroundBrightnessCurveText();
                break;
            case nameof(AmbientViewModel.MinBrightnessEnabled):
            case nameof(AmbientViewModel.MinBrightness):
                if (PlaygroundBrightnessCurveCanvas is not null)
                {
                    PlaygroundBrightnessCurveCanvas.MinBrightnessEnabled = ViewModel.MinBrightnessEnabled;
                    PlaygroundBrightnessCurveCanvas.MinBrightness = ViewModel.MinBrightness;
                }
                break;
        }
    }

    // ── Value-text formatters ───────────────────────────────────────────────

    private void UpdatePlaygroundBrightnessCurveText()
        => PlaygroundBrightnessCurveText.Text =
            $"cubic-bezier({ViewModel.BrightnessCurveX1:0.00}, {ViewModel.BrightnessCurveY1:0.00}, {ViewModel.BrightnessCurveX2:0.00}, {ViewModel.BrightnessCurveY2:0.00})";

    private void UpdatePlaygroundMinBrightnessUi()
    {
        bool enabled = PlaygroundMinBrightnessToggle.IsOn;
        PlaygroundMinBrightnessRow.Visibility = enabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaygroundMinBrightnessCaption.Text = enabled
            ? "Keeps bulb brightness at or above the selected floor, even on black scenes."
            : "Allows brightness to fall below the selected floor, including to black.";
        PlaygroundBrightnessCurveCanvas.MinBrightnessEnabled = enabled;
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
