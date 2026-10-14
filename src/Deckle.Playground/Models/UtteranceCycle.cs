using System;

namespace Deckle.Playground;

// One simulated utterance after another for the HUD tab's Recording preview:
// speech, then a pause that runs to the cut, then speech again. It reports
// what the streaming engine reports to the live HUD (UtteranceProgress), so
// the preview's gauge opens, fills and closes the way it does in a dictation.
//
// Mirrors the engine's samples rather than an idealised gauge:
// - the take stays active through speech, pause and cut; the engine sends an
//   inactive sample only when capture ends, and HudChrono drops the gauge
//   visual on one, so the cycle never sends it;
// - the fraction is 0 while speech runs and rises through the pause, never
//   reaching 1 (EnergySegmenter cuts on the frame that would);
// - the cut sample carries fraction 0 with one more completed utterance; the
//   full gauge seen at a cut is HudChrono's completion hold, not a sample.
internal sealed class UtteranceCycle
{
    // Seconds into the current utterance, speech then pause, on the
    // simulation's own clock (wall time scaled by Speed).
    private double _position;

    public bool IsPausing { get; private set; }

    public float Fraction { get; private set; }

    // Cumulative over the take, as the engine counts it: a consumer that
    // misses a frame still sees the cut.
    public int CompletedUtterances { get; private set; }

    // Integrates the scaled time step by step, so a Speed or duration change
    // mid-cycle carries on from where the cycle stands instead of jumping.
    public void Advance(double elapsedSeconds, double speechSeconds, double pauseSeconds, double speed)
    {
        double speech = Math.Max(0.05, speechSeconds);
        double pause  = Math.Max(0.05, pauseSeconds);
        _position += Math.Max(0, elapsedSeconds) * Math.Max(0, speed);

        if (_position >= speech + pause)
        {
            CompletedUtterances++;
            _position = 0;
        }

        IsPausing = _position >= speech;
        Fraction  = IsPausing ? (float)Math.Clamp((_position - speech) / pause, 0, 1) : 0f;
    }
}
