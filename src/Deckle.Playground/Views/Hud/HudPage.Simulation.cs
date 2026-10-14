using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Transcription;

namespace Deckle.Playground;

// ─── HudPage — Simulation section ───────────────────────────────────────────
//
// What the preview plays, never what the HUD is: these values live in page
// fields, outside TuningModel and the countdown config, so IsDirty never sees
// them, Save never writes them and the bar never shows for them.
public sealed partial class HudPage
{
    // Simulated dictation for the Recording preview: the level the stroke
    // follows and the utterance cycle the gauge follows. One class so a reset
    // is one `new()` and the defaults are written once.
    private sealed class SpeechSimulation
    {
        // 0.013 ≈ -38 dBFS, 0.100 ≈ -20 dBFS.
        public float RmsMin           = 0.013f;
        public float RmsMax           = 0.100f;
        public float RmsPeriodSeconds = 2.0f;
        public bool  ManualOverride   = false;
        public float ManualValue      = 0.012f;

        public double SpeechSeconds = 4;
        public double PauseSeconds  = EnginePauseSeconds();
        public double Speed         = 1;
    }

    private SpeechSimulation _speechSimulation = new();

    // The engine's 50 ms capture frames set the cadence, so the preview gets
    // the level and the progress as often as the live HUD does.
    private readonly DispatcherTimer _speechTimer = new()
        { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _speechClock = new();
    private double _speechLastTick;
    private UtteranceCycle _utteranceCycle = new();

    // The silence the engine waits before it cuts: EnergySegmenter waits
    // HangoverMaxMs while the open utterance is no longer than
    // HangoverRampStartMs (15 s by default), then shortens the wait. Speech
    // and pause at their defaults stay under that, so this is the real wait.
    // The stored value, which the Segmentation page edits; the compiled
    // default when nothing is stored.
    private static double EnginePauseSeconds()
    {
        int ms = TranscriptionSettingsService.Instance.Current.Streaming.Segmenter.HangoverMaxMs;
        return Math.Clamp(ms / 1000.0, PauseMinSeconds, PauseMaxSeconds);
    }

    // The Segmentation page's range for the same setting.
    private const double PauseMinSeconds = 0.5;
    private const double PauseMaxSeconds = 15;

    private void AddSimulationExpander()
    {
        var stack = NewTuningSection("Simulation", ResetSimulation);
        stack.Children.Add(TuningHelp("Simulation changes the preview only. Nothing here is saved."));
        switch (ViewModel.CurrentTarget)
        {
            case HudTarget.Recording: AddSpeechSimulationRows(stack); break;
            case HudTarget.Gauge:     AddGaugeSimulationRows(stack);  break;
        }
    }

    // Only the shown target's simulation: the other target's state is not on
    // screen, so resetting it would be a surprise on return.
    private void ResetSimulation()
    {
        switch (ViewModel.CurrentTarget)
        {
            case HudTarget.Recording: _speechSimulation = new(); break;
            case HudTarget.Gauge:     ResetGaugeSimulation();    break;
        }
        RebuildTuningPanel();
    }

    // The preview maps the simulated RMS through the live AudioLevelMapper
    // calibration, which Settings > Recording and auto-calibration own. The
    // Playground reads it and never writes it. No row rebuilds the preview:
    // the timer reads the fields on every tick, and a rebuild would restart
    // the take, its clock and its gauge.
    private void AddSpeechSimulationRows(StackPanel stack)
    {
        // Step 0.001: the defaults (0.013, 0.100) need three decimals.
        AddFloatRow(stack, "SimRmsMin", 0, 0.3, 0.001, _speechSimulation.RmsMin,
            v => _speechSimulation.RmsMin = (float)v);
        AddFloatRow(stack, "SimRmsMax", 0, 0.3, 0.001, _speechSimulation.RmsMax,
            v => _speechSimulation.RmsMax = (float)v);
        AddFloatRow(stack, "SimRmsPeriodSeconds", 0.2, 10, 0.1, _speechSimulation.RmsPeriodSeconds,
            v => _speechSimulation.RmsPeriodSeconds = (float)v);
        AddToggleRow(stack, "Manual override", _speechSimulation.ManualOverride,
            v => _speechSimulation.ManualOverride = v);
        AddFloatRow(stack, "SimRmsManualValue", 0, 0.3, 0.001, _speechSimulation.ManualValue,
            v => _speechSimulation.ManualValue = (float)v);

        stack.Children.Add(TuningHelp(
            "Each cycle plays speech, then a pause that fills the gauge until the cut. " +
            "Speed scales the whole cycle."));
        AddFloatRow(stack, "Speech (s)", 0.5, 15, 0.5, _speechSimulation.SpeechSeconds,
            v => _speechSimulation.SpeechSeconds = v);
        // Step 0.05: the engine counts the pause in 50 ms frames.
        AddFloatRow(stack, "Pause (s)", PauseMinSeconds, PauseMaxSeconds, 0.05, _speechSimulation.PauseSeconds,
            v => _speechSimulation.PauseSeconds = v);
        AddFloatRow(stack, "Speed", 0.25, 4, 0.25, _speechSimulation.Speed,
            v => _speechSimulation.Speed = v);
        stack.Children.Add(TuningHelp(
            "Pause starts at the silence dictation waits before a cut, set by Hangover (max) on the Segmentation page."));
        // HudChrono builds its gauge from the stored HUD settings and has no
        // override for it, unlike the stroke (SetNextStrokeConfig).
        stack.Children.Add(TuningHelp(
            "The gauge here uses the saved HUD settings, so save to see a change in it."));
    }

    // ── Speech timer (Recording only) ───────────────────────────────────────

    // HudChrono resets its gauge on every Recording entry, which ApplyTarget
    // always passes through, so the take restarts here too: a completed count
    // carried over would read as a cut and flash a full gauge.
    private void StartSpeechSimulation()
    {
        if (_speechTimer.IsEnabled) return;
        _utteranceCycle = new();
        _speechLastTick = 0;
        _speechClock.Restart();
        _speechTimer.Tick -= SpeechTimerTick;
        _speechTimer.Tick += SpeechTimerTick;
        _speechTimer.Start();
    }

    private void StopSpeechSimulation()
    {
        if (!_speechTimer.IsEnabled) return;
        _speechTimer.Stop();
        _speechTimer.Tick -= SpeechTimerTick;
        _speechClock.Stop();
    }

    private void SpeechTimerTick(object? sender, object e)
    {
        var speech = _speechSimulation;
        double now = _speechClock.Elapsed.TotalSeconds;
        // From the stopwatch, not a tick count: DispatcherTimer ticks drift.
        _utteranceCycle.Advance(now - _speechLastTick,
            speech.SpeechSeconds, speech.PauseSeconds, speech.Speed);
        _speechLastTick = now;

        float rms;
        if (_utteranceCycle.IsPausing)
        {
            // The pause is silence. The cycle drives the gauge itself, not
            // through a segmenter, so the floor need not sit below the
            // engine's speech threshold.
            rms = speech.RmsMin;
        }
        else if (speech.ManualOverride)
        {
            rms = speech.ManualValue;
        }
        else
        {
            float sweep = (float)(0.5 + 0.5 * Math.Sin(
                2 * Math.PI * now / Math.Max(0.1, speech.RmsPeriodSeconds)));
            rms = speech.RmsMin + sweep * (speech.RmsMax - speech.RmsMin);
        }
        ChronoPreview.UpdateAudioLevel(rms);
        // Active throughout, as the engine reports a running take.
        ChronoPreview.UpdateUtteranceProgress(
            active: true, _utteranceCycle.Fraction, _utteranceCycle.CompletedUtterances);
    }
}
