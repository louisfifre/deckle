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
// The knobs apply to SDR and HDR alike: HDR content is tone-mapped to 8 bits
// before this stage. Handlers write through the ViewModel into
// AmbientSettingsService, the store the Settings AmbientPage and the engine
// read.
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
        // Only the stored flag flips here; the App's AmbientEngine observer
        // starts or stops the pipeline.
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

    // The label is the module .resw entry "<LabelKey>.Content", read as
    // "<LabelKey>/Content" from the module's PRI subtree: the same entry the
    // Settings preset selector reads.
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
    // Callers set `_initializing` first so the synthetic ValueChanged /
    // Toggled events don't write back through the VM.

    private void PushViewModelToControls()
    {
        PlaygroundExposureSlider.Value         = ViewModel.ExposureEv;
        PlaygroundSaturationSlider.Value       = ViewModel.SaturationBoost * 100.0;
        PlaygroundMinBrightnessSlider.Value    = ViewModel.MinBrightness;
        PlaygroundMinBrightnessToggle.IsOn     = ViewModel.MinBrightnessEnabled;

        // The curve's four points ride the two-way binds; only the floor guide
        // is pushed by hand.
        UpdatePlaygroundCurveFloor();

        SelectAmbientModeInCombo(ViewModel.Preset);

        PlaygroundSmoothingSlider.Maximum = 1.0;
        PlaygroundSmoothingSlider.Minimum = 0.05;
        PlaygroundSmoothingSlider.Value   = ViewModel.SmoothingAlpha;
        PlaygroundChangeThresholdSlider.Value = ViewModel.ChangeThreshold;

        // The share slider is in percent, the ViewModel stores the fraction.
        // Clamped for a hand-edited settings.json.
        PlaygroundBorderDepthSlider.Value = Math.Clamp(ViewModel.BorderDepth, 0.05, 0.5) * 100.0;
        PlaygroundBorderCellsSlider.Value = Math.Clamp(ViewModel.BorderCells, 4, 24);
        SelectBorderModeInRadios(ViewModel.BorderMode);
        UpdateBorderRowsVisibility(ViewModel.BorderMode);

        UpdatePlaygroundExposureText();
        UpdatePlaygroundSaturationText();
        UpdatePlaygroundMinBrightnessText();
        UpdatePlaygroundMinBrightnessUi();
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
        PlaygroundBrightnessCurve.Floor = PlaygroundMinBrightnessToggle.IsOn
            ? Math.Round(PlaygroundMinBrightnessSlider.Value) / 254.0
            : null;
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
            // The sliders refresh through the store's Changed event. The
            // brightness floor belongs to no preset, so the Min brightness
            // row keeps its value.
            ViewModel.Preset = id;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AmbientViewModel.MinBrightnessEnabled):
            case nameof(AmbientViewModel.MinBrightness):
                if (PlaygroundBrightnessCurve is not null)
                    UpdatePlaygroundCurveFloor();
                break;
        }
    }

    // ── Value-text formatters ───────────────────────────────────────────────

    // The floor guide on the curve: the minimum brightness as a share of the
    // Hue range, or no guide while the floor is off.
    private void UpdatePlaygroundCurveFloor()
        => PlaygroundBrightnessCurve.Floor = ViewModel.MinBrightnessEnabled
            ? ViewModel.MinBrightness / 254.0
            : null;

    private void UpdatePlaygroundMinBrightnessUi()
    {
        bool enabled = PlaygroundMinBrightnessToggle.IsOn;
        PlaygroundMinBrightnessRow.Visibility = enabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaygroundMinBrightnessCaption.Text = enabled
            ? "Keeps bulb brightness at or above the selected floor, even on black scenes."
            : "Allows brightness to fall below the selected floor, including to black.";
        PlaygroundBrightnessCurve.Floor = enabled
            ? Math.Round(PlaygroundMinBrightnessSlider.Value) / 254.0
            : null;
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
