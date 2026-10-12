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

    private void AttachUtteranceCountdownPreview()
    {
        var compositor = ElementCompositionPreview.GetElementVisual(NakedPreviewHost).Compositor;
        bool isDark = ActualTheme != ElementTheme.Light;
        bool animations = SettingsService.Instance.Current.Overlay.Animations;
        // The page's own model: the material and geometry tuned on the other
        // targets. Nothing here is read by the live HUD.
        var material = _tuning.ToConfig();
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
                StartReach = defaults.StartReach, EndReach = defaults.EndReach,
            };
            RebuildTuningPanel();
        });
        geometry.Children.Add(TuningHelp(
            "Uses the Transcribing material tuned in Cone and Transcribing."));
        geometry.Children.Add(TuningHelp(
            "Reach is how far the light runs along the top and bottom edges, from their centre. " +
            "0 % is the centre and 100 % the corners. Above 100 %, the light runs past the corners. " +
            "Below 0 %, the gauge waits before it opens."));
        // Wider than the useful range on purpose, for testing: the renderer
        // lerps reach freely and clamps the spread to its minimum.
        AddFloatRow(geometry, "Start reach (%)", -50, 100, 1, _countdownConfig.StartReach * 100,
            value => _countdownConfig = _countdownConfig with { StartReach = (float)value / 100f }, rebuild: true);
        AddFloatRow(geometry, "End reach (%)", 0, 200, 1, _countdownConfig.EndReach * 100,
            value => _countdownConfig = _countdownConfig with { EndReach = (float)value / 100f }, rebuild: true);
        geometry.Children.Add(TuningHelp("Thickness and inset follow the HUD geometry section."));

        var fade = NewTuningSection("Countdown fade", () =>
        {
            var defaults = new UtteranceCountdownConfig();
            _countdownConfig = _countdownConfig with
            {
                EdgeFadeTurns = defaults.EdgeFadeTurns,
                FadeCurve = defaults.FadeCurve,
            };
            RebuildTuningPanel();
        });
        fade.Children.Add(TuningHelp("Same fade units as Sweep mask. The fade scales with the wedge opening."));
        AddFloatRow(fade, "Edge fade (turns)", 0, 1, 0.005, _countdownConfig.EdgeFadeTurns,
            value => _countdownConfig = _countdownConfig with { EdgeFadeTurns = (float)value }, rebuild: true);
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

        simulation.Children.Add(TuningHelp("Replay holds at the end. Resume speech plays the retreat to the edge centre."));
        var transport = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var replay = new Button { Content = "Replay pause" };
        replay.Click += (_, _) =>
        {
            _countdownReplayClock.Reset();
            _countdownTuning.Progress = 0;
            _setCountdownPosition?.Invoke(0);
            if (!ViewModel.IsPlaying) ViewModel.IsPlaying = true;
            FlushPendingRebuild();
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
            // Rebuild first so the retreat runs on a visual that carries the
            // latest knobs. ApplyTarget snapshots the replay clock, so the new
            // visual starts where the gauge stands and retreats from there.
            FlushPendingRebuild();
            _countdownReplayClock.Reset();
            _countdownTuning.Progress = 0;
            _setCountdownPosition?.Invoke(0);
            _utteranceCountdownPreview?.SetProgress(
                0, interpolate: SettingsService.Instance.Current.Overlay.Animations);
        };
        transport.Children.Add(replay);
        transport.Children.Add(resume);
        simulation.Children.Add(transport);
    }

    // A knob moved within the debounce window would otherwise rebuild the
    // preview after the transport action, on top of it.
    private void FlushPendingRebuild()
    {
        if (!_rebuildPending) return;
        _rebuildDebounce.Stop();
        _rebuildPending = false;
        ApplyTarget();
    }
}
