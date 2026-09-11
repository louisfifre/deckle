using System;
using System.Numerics;
using System.Diagnostics;
using Deckle.Composition;
using Deckle.Hud;
using Deckle.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Deckle.Playground;

public sealed partial class HudPage
{
    private HudComposition.UtteranceCountdownVisual? _utteranceCountdownPreview;
    private UtteranceCountdownConfig _countdownConfig = new();
    private CountdownTuning _countdownTuning = new();
    private Action<double>? _setCountdownPosition;
    private readonly Stopwatch _countdownReplayClock = new();
    private double _countdownReplayDuration;

    private void SnapshotCountdownReplay()
    {
        if (!_countdownReplayClock.IsRunning) return;
        _countdownTuning.Progress = (float)Math.Clamp(
            _countdownReplayClock.Elapsed.TotalSeconds / _countdownReplayDuration, 0, 1);
        _countdownReplayClock.Stop();
        _setCountdownPosition?.Invoke(_countdownTuning.Progress * 100);
    }

    // Simulation and inspection never change the live transcription signal.
    private sealed class CountdownTuning
    {
        public float Progress;
        public float MaterialOpacity = 0.2f;
        public double DurationSeconds = 3;
    }

    private void LoadCountdownTuning() => _countdownConfig = UtteranceCountdownTuning.Current;
    private void PublishCountdownTuning() => UtteranceCountdownTuning.Apply(_countdownConfig);

    private void AttachUtteranceCountdownPreview()
    {
        var compositor = ElementCompositionPreview.GetElementVisual(NakedPreviewHost).Compositor;
        bool isDark = ActualTheme != ElementTheme.Light;
        bool animations = SettingsService.Instance.Current.Overlay.Animations;
        var material = HudMaterialTuning.Current;
        if (ViewModel.CurrentTarget == HudTarget.CountdownMaterial)
        {
            _nakedPreview = HudComposition.CreateNakedMaskPreview(
                compositor, NakedHudSize, material, HudComposition.NakedMaskPart.Conic,
                isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black,
                ProcessingVariant.Transcribing, isDark, animations);
            float inset = (NakedHostDim - _nakedPreview.Container.Size.X) / 2f;
            _nakedPreview.Container.Offset = new Vector3(inset, inset, 0);
            ElementCompositionPreview.SetElementChildVisual(NakedPreviewHost, _nakedPreview.Container);
            return;
        }

        bool maskOnly = ViewModel.CurrentTarget == HudTarget.CountdownMasks;
        float opacity = ViewModel.CurrentTarget == HudTarget.UtteranceCountdown
            ? _countdownTuning.MaterialOpacity : 1f;
        _utteranceCountdownPreview = HudComposition.CreateUtteranceCountdownVisual(
            compositor, new Vector2(NakedHostDim), NakedHudSize, material, isDark,
            _countdownConfig, opacity, maskOnly, animations);
        ElementCompositionPreview.SetElementChildVisual(NakedPreviewHost, _utteranceCountdownPreview.Container);
        _utteranceCountdownPreview.SetProgress(_countdownTuning.Progress);
    }

    private void AddUtteranceCountdownExpander()
    {
        if (ViewModel.CurrentTarget == HudTarget.CountdownMaterial)
        {
            AddCountdownPreviewSection();
            return;
        }
        var geometry = NewTuningSection("Countdown geometry", () =>
        {
            var defaults = new UtteranceCountdownConfig();
            _countdownConfig = _countdownConfig with
            {
                StartDegrees = defaults.StartDegrees, EndDegrees = defaults.EndDegrees,
                StrokeThickness = defaults.StrokeThickness,
            };
            RebuildTuningPanel();
        });
        geometry.Children.Add(TuningHelp(
            "Uses the shared Transcribing material. These settings update the live HUD."));
        geometry.Children.Add(TuningHelp("0 degrees is the side midpoint. Positive angles point down; negative angles point up."));
        // Include the corner region and retain sub-degree precision.
        double angleLimit = HudComposition.UtteranceCountdownAngleLimit(NakedHudSize);
        AddFloatRow(geometry, "Start angle (°)", -angleLimit, angleLimit, 0.1, _countdownConfig.StartDegrees,
            value => _countdownConfig = _countdownConfig with { StartDegrees = (float)value }, rebuild: true);
        AddFloatRow(geometry, "End angle (°)", -angleLimit, angleLimit, 0.1, _countdownConfig.EndDegrees,
            value => _countdownConfig = _countdownConfig with { EndDegrees = (float)value }, rebuild: true);
        AddFloatRow(geometry, "Stroke thickness (dip)", 0.5, 12, 0.5, _countdownConfig.StrokeThickness,
            value => _countdownConfig = _countdownConfig with { StrokeThickness = (float)value }, rebuild: true);

        var fade = NewTuningSection("Countdown fade", () =>
        {
            var defaults = new UtteranceCountdownConfig();
            _countdownConfig = _countdownConfig with
            {
                LeadFadeTurns = defaults.LeadFadeTurns, TailFadeTurns = defaults.TailFadeTurns,
                FadeCurve = defaults.FadeCurve,
            };
            RebuildTuningPanel();
        });
        fade.Children.Add(TuningHelp("Same fade units as Sweep mask. The fades scale with the cone opening."));
        AddFloatRow(fade, "Lead fade (turns)", 0, 1, 0.005, _countdownConfig.LeadFadeTurns,
            value => _countdownConfig = _countdownConfig with { LeadFadeTurns = (float)value }, rebuild: true);
        AddFloatRow(fade, "Tail fade (turns)", 0, 1, 0.005, _countdownConfig.TailFadeTurns,
            value => _countdownConfig = _countdownConfig with { TailFadeTurns = (float)value }, rebuild: true);
        AddFloatRow(fade, "Fade curve", 0.5, 10, 0.1, _countdownConfig.FadeCurve,
            value => _countdownConfig = _countdownConfig with { FadeCurve = (float)value }, rebuild: true);

        AddCountdownPreviewSection();
    }

    private void AddCountdownPreviewSection()
    {
        var preview = NewTuningSection("Preview only", () =>
        {
            _countdownReplayClock.Reset();
            _countdownTuning = new();
            RebuildTuningPanel();
        });
        preview.Children.Add(TuningHelp("The shared palette and its rotation are adjusted in Cone; grayscale grading is adjusted in Transcribing."));
        if (ViewModel.CurrentTarget == HudTarget.CountdownMaterial) return;
        preview.Children.Add(TuningHelp("Simulation does not affect dictation. Choose another inspection view in Target."));

        var simulation = new StackPanel { Spacing = 4 };
        preview.Children.Add(simulation);
        _setCountdownPosition = AddFloatRow(simulation, "Manual position (%)", 0, 100, 1, _countdownTuning.Progress * 100,
            value =>
            {
                _countdownReplayClock.Reset();
                _countdownTuning.Progress = (float)value / 100f;
                _utteranceCountdownPreview?.SetProgress(_countdownTuning.Progress);
            });
        AddFloatRow(simulation, "Pause duration (s)", 0.5, 5, 0.1, _countdownTuning.DurationSeconds,
            value => _countdownTuning.DurationSeconds = value);
        if (ViewModel.CurrentTarget == HudTarget.UtteranceCountdown)
            AddFloatRow(simulation, "Cone visibility (%)", 0, 100, 5, _countdownTuning.MaterialOpacity * 100,
                value => _countdownTuning.MaterialOpacity = (float)value / 100f, rebuild: true);

        simulation.Children.Add(TuningHelp("Replay holds at the end. Resume speech returns to the start."));
        var transport = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var replay = new Button { Content = "Replay pause" };
        replay.Click += (_, _) =>
        {
            _countdownReplayClock.Reset();
            _countdownTuning.Progress = 0;
            _setCountdownPosition?.Invoke(0);
            if (!ViewModel.IsPlaying) ViewModel.IsPlaying = true;
            if (_rebuildPending)
            {
                _rebuildDebounce.Stop();
                _rebuildPending = false;
                ApplyTarget();
            }
            bool animations = SettingsService.Instance.Current.Overlay.Animations;
            _utteranceCountdownPreview?.Replay(_countdownTuning.DurationSeconds, animations);
            if (animations)
            {
                _countdownReplayDuration = _countdownTuning.DurationSeconds;
                _countdownReplayClock.Restart();
            }
            else
            {
                _countdownTuning.Progress = 1f;
                _setCountdownPosition?.Invoke(100);
            }
        };
        var resume = new Button { Content = "Resume speech" };
        resume.Click += (_, _) =>
        {
            _countdownReplayClock.Reset();
            _countdownTuning.Progress = 0;
            _setCountdownPosition?.Invoke(0);
            _utteranceCountdownPreview?.SetProgress(0);
        };
        transport.Children.Add(replay);
        transport.Children.Add(resume);
        simulation.Children.Add(transport);
    }
}
