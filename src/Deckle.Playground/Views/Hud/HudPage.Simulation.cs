using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Hud;
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
        // The stroke maps the RMS through the shipping level window, −55 to
        // −32 dBFS by default (AudioLevelMapper). The floor must be silence:
        // the first default, 0.013, is −38 dBFS, three quarters of the way
        // up that window, so a pause left the outline at 75 % and the sweep
        // barely moved. 0 is the one value every window reads as silence.
        // 0.030 ≈ −30 dBFS clears the window's top, so the sweep spans the
        // whole visible range.
        public float RmsMin           = 0f;
        public float RmsMax           = 0.030f;
        public float RmsPeriodSeconds = 2.0f;
        public bool  ManualOverride   = false;
        public float ManualValue      = 0.006f;

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
    // The stored value, which the Utterance segmentation page edits; the compiled
    // default when nothing is stored.
    private static double EnginePauseSeconds()
    {
        int ms = TranscriptionSettingsService.Instance.Current.Streaming.Segmenter.HangoverMaxMs;
        return Math.Clamp(ms / 1000.0, PauseMinSeconds, PauseMaxSeconds);
    }

    // The Utterance segmentation page's range for the same setting.
    private const double PauseMinSeconds = 0.5;
    private const double PauseMaxSeconds = 15;

    private void AddSimulationExpander()
    {
        var stack = NewTuningSection("Simulation", ResetSimulation);
        stack.Children.Add(TuningHelp("Simulation changes the preview only. Nothing here is saved."));
        switch (ViewModel.CurrentTarget)
        {
            case HudTarget.Recording:    AddSpeechSimulationRows(stack);     break;
            case HudTarget.Transcribing:
            case HudTarget.Rewriting:    AddTransitionSimulationRows(stack); break;
            case HudTarget.Gauge:        AddGaugeSimulationRows(stack);      break;
        }
    }

    // Only the shown target's simulation: the other target's state is not on
    // screen, so resetting it would be a surprise on return.
    private void ResetSimulation()
    {
        switch (ViewModel.CurrentTarget)
        {
            case HudTarget.Recording:    _speechSimulation = new(); break;
            case HudTarget.Transcribing: _transitionSimulation.TranscribingFrom = TransitionSimulation.DefaultTranscribingFrom; break;
            case HudTarget.Rewriting:    _transitionSimulation.RewritingFrom    = TransitionSimulation.DefaultRewritingFrom;    break;
            case HudTarget.Gauge:        ResetGaugeSimulation();    break;
        }
        RebuildTuningPanel();
    }

    // The preview maps the simulated RMS through the live AudioLevelMapper
    // calibration, which Settings > Recording and auto-calibration own. The
    // Playground reads it and never writes it. No row rebuilds the preview:
    // the timer reads the fields on every tick, and a rebuild would restart
    // the take, its clock and its gauge.
    //
    // Under the source row: Microphone shows none of these, the preview then
    // follows the real sound and the real gauge.
    private void AddSpeechSimulationRows(StackPanel section)
    {
        // The rows below belong to the simulation source alone: they sit in
        // their own panel, which the source row (HudPage.Microphone.cs) shows
        // or hides in place. Same spacing as the section, so they read as its
        // direct rows.
        var stack = new StackPanel { Spacing = 4 };
        AddPreviewSourceRows(section, stack);
        section.Children.Add(stack);

        // Range 0.05: the default window reads everything above 0.025
        // (−32 dBFS) as full level, so a wider slider spent most of its
        // travel on values that all look the same. Step 0.001: silence sits
        // under 0.002 in that window, so the floor needs three decimals.
        AddFloatRow(stack, "SimRmsMin", 0, 0.05, 0.001, _speechSimulation.RmsMin,
            v => _speechSimulation.RmsMin = (float)v);
        AddFloatRow(stack, "SimRmsMax", 0, 0.05, 0.001, _speechSimulation.RmsMax,
            v => _speechSimulation.RmsMax = (float)v);
        AddFloatRow(stack, "SimRmsPeriodSeconds", 0.2, 10, 0.1, _speechSimulation.RmsPeriodSeconds,
            v => _speechSimulation.RmsPeriodSeconds = (float)v);
        AddToggleRow(stack, "Manual override", _speechSimulation.ManualOverride,
            v => _speechSimulation.ManualOverride = v);
        AddFloatRow(stack, "SimRmsManualValue", 0, 0.05, 0.001, _speechSimulation.ManualValue,
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
            "Pause starts at the silence dictation waits before a cut, set by Hangover (max) on the Utterance segmentation page."));
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

    // ── Transition replay (Transcribing and Rewriting) ──────────────────────

    // The state each processing target blends in from. The defaults follow
    // the live order, Recording, then Transcribing, then Rewriting.
    private sealed class TransitionSimulation
    {
        public const HudState DefaultTranscribingFrom = HudState.Recording;
        public const HudState DefaultRewritingFrom    = HudState.Transcribing;

        public HudState TranscribingFrom = DefaultTranscribingFrom;
        public HudState RewritingFrom    = DefaultRewritingFrom;
    }

    private TransitionSimulation _transitionSimulation = new();

    // One-shot: fires once, when the From state has settled.
    private readonly DispatcherTimer _transitionTimer = new();
    private HudState _transitionFrom;
    private HudState _transitionTo;

    private void AddTransitionSimulationRows(StackPanel stack)
    {
        var to = ProcessingStateOf(ViewModel.CurrentTarget);
        HudState[] options = to == HudState.Transcribing
            ? [HudState.Recording, HudState.Rewriting]
            : [HudState.Recording, HudState.Transcribing];
        var radios = AddRadioRow(stack, "From",
            [options[0].ToString(), options[1].ToString()],
            Math.Max(0, Array.IndexOf(options, TransitionFromOf(to))));
        // Read when the button plays, so a choice rebuilds nothing.
        radios.SelectionChanged += (_, _) =>
        {
            if (radios.SelectedIndex < 0) return;
            var from = options[radios.SelectedIndex];
            if (to == HudState.Transcribing) _transitionSimulation.TranscribingFrom = from;
            else                             _transitionSimulation.RewritingFrom    = from;
        };

        stack.Children.Add(TuningHelp(
            "Play transition shows the From state, then blends into this state with the current values."));
        stack.Children.Add(TuningHelp(
            "From Recording, the stroke is rebuilt as in the live HUD: it starts on Recording's material, invisible as after a silence, and blends in over this state's Blend."));
        var transport = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var play = new Button { Content = "Play transition" };
        play.Click += (_, _) => PlayTransition();
        transport.Children.Add(play);
        stack.Children.Add(transport);
    }

    private void PlayTransition()
    {
        // Start a stopped preview, and land a knob edit still in the debounce
        // first: the rebuild it is due would otherwise cut the replay.
        if (!ViewModel.IsPlaying) ViewModel.IsPlaying = true;
        FlushPendingRebuild();
        CancelTransition();

        var to = ProcessingStateOf(ViewModel.CurrentTarget);
        var from = TransitionFromOf(to);

        // From a fresh stroke, as ApplyTarget shows a state, so the From
        // state looks as it does on its own target.
        StopSpeechSimulation();
        ChronoPreview.ApplyState(HudState.Hidden);
        EnterTransitionState(from, buildsStroke: true);
        // The Recording outline rises only with the level: without the
        // speech simulation, From Recording would show no stroke at all.
        if (from == HudState.Recording) StartSpeechSimulation();

        // The settle wait. A stroke blends into a state over that state's own
        // BlendSeconds, so the From blend is over by then; the half second
        // shows From at rest before the blend under study starts.
        var cfg = _tuning.ToConfig();
        double settle = from switch
        {
            HudState.Recording    => cfg.RecordingBlendSeconds,
            HudState.Transcribing => cfg.TranscribingBlendSeconds,
            _                     => cfg.RewritingBlendSeconds,
        };
        _transitionFrom = from;
        _transitionTo   = to;
        _transitionTimer.Interval = TimeSpan.FromSeconds(settle + 0.5);
        _transitionTimer.Tick -= TransitionTimerTick;
        _transitionTimer.Tick += TransitionTimerTick;
        _transitionTimer.Start();
    }

    private void TransitionTimerTick(object? sender, object e)
    {
        CancelTransition();
        // The level and the gauge feed Recording only; HudChrono drops them
        // past it.
        StopSpeechSimulation();
        // HudChrono rebuilds the stroke only across Recording and the
        // processing states. Between Transcribing and Rewriting it keeps the
        // stroke and blends it, so nothing is built there.
        EnterTransitionState(_transitionTo, buildsStroke: _transitionFrom == HudState.Recording);
    }

    private void EnterTransitionState(HudState state, bool buildsStroke)
    {
        // The clock before ApplyState, in HudWindow.SetState's order:
        // StopClock latches the last digit, which must land before the
        // processing reveal starts.
        if (state == HudState.Recording) ChronoPreview.StartClock();
        else                             ChronoPreview.StopClock();
        // Armed only where a stroke is built, so the state shows the current
        // knobs and no one-shot waits for an unrelated later build.
        if (buildsStroke) ArmPreviewConfigs();
        ChronoPreview.ApplyState(state);
    }

    // ApplyTarget calls this first: any rebuild, Stop or target change ends
    // a replay rather than letting its second half land on another preview.
    private void CancelTransition()
    {
        _transitionTimer.Stop();
        _transitionTimer.Tick -= TransitionTimerTick;
    }

    private HudState TransitionFromOf(HudState to) => to == HudState.Transcribing
        ? _transitionSimulation.TranscribingFrom
        : _transitionSimulation.RewritingFrom;

    private static HudState ProcessingStateOf(HudTarget target) =>
        target == HudTarget.Transcribing ? HudState.Transcribing : HudState.Rewriting;
}
