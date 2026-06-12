using System;
using System.Collections.Generic;
using Deckle.Audio;

namespace Deckle.Transcription.Streaming;

// A point-in-time view of the segmenter's internal state, consumed by the
// streaming pipeline to populate the 1 Hz StreamingHeartbeat. HangoverRequired
// frames is the silence the state machine WOULD currently demand to cut — it
// shrinks as the open utterance grows past the ramp, so the log shows the
// dynamic hangover in action.
internal readonly record struct SegmenterSnapshot(
    string State,
    int CurrentUtteranceFrames,
    int RequiredHangoverFrames,
    int TotalUtterancesEmitted);

// ── EnergySegmenter ──────────────────────────────────────────────────────────
//
// Threshold-on-RMS state machine that places utterance boundaries on the live
// capture stream (see CONTEXT.md § Speech segmentation). NOT a model and NOT
// speech recognition — it reads energy dips to decide where to cut. It consumes
// CaptureFrames (50 ms sub-windows + their RAW linear RMS) one at a time and
// emits an Utterance each time it decides a speech span has ended.
//
// Pure and single-threaded by construction: Push and Flush are called from the
// one producer thread (inside the capture loop, via the Frame event), so there
// is no shared mutable state and no lock. This is the testable heart of the
// streaming socle — feed it synthetic frames, assert the utterances out.
//
// ── The three states ─────────────────────────────────────────────────────────
//   Silence  — no open utterance. Silent frames are discarded. The first VOICED
//              frame opens an utterance and moves to Speech.
//   Speech   — building an utterance, last frame was voiced. A silent frame
//              opens the hangover (move to Hangover); a voiced frame extends.
//   Hangover — building an utterance, in a trailing silence we are not yet sure
//              is the end. A voiced frame means speech resumed (back to Speech);
//              enough consecutive silence (current hangover reached) ends it.
//
// ── Hangover vs margin (the two values people conflate) ───────────────────────
//   Hangover = DECISION delay. We wait this much silence after the last voiced
//   frame before declaring the utterance over, so a brief intra-phrase pause
//   does not split a sentence. The required delay SHRINKS as the utterance
//   grows: HangoverMaxMs while the utterance is below HangoverRampStartMs,
//   HangoverMinMs at and above HangoverRampEndMs, log-linear decay in between.
//   No hard cap — a very long utterance ends on a micro-pause, never mid-word.
//
//   Margin = CUT POSITION. The emitted span ends MarginMs after the last voiced
//   frame. The silence between the margin and the hangover expiry is DROPPED.
//
// All durations resolve to whole 50 ms frames (the capture sub-window size).
internal sealed class EnergySegmenter
{
    // A capture sub-window is a fixed 50 ms / 800-sample frame (WaveInLoop only
    // emits full sub-windows), so every frame carries the same duration.
    private const double FrameMs  = 50.0;
    private const double FrameSec = FrameMs / 1000.0;

    private readonly Action<Utterance> _onUtterance;

    // Derived from settings once, in frame units.
    private readonly float _rmsThreshold;       // linear RMS at/above which a frame is voiced
    private readonly int   _hangoverMaxFrames;  // hangover at utterance start
    private readonly int   _hangoverMinFrames;  // hangover floor past the ramp
    private readonly int   _rampStartFrames;    // utterance length above which hangover starts shrinking
    private readonly int   _rampEndFrames;      // utterance length at/above which hangover = min
    private readonly int   _marginFrames;       // trailing silence frames kept after last voiced
    private readonly int   _minVoicedFrames;    // shorter voiced extent → dropped as a blip

    private enum State { Silence, Speech, Hangover }
    private State _state = State.Silence;

    // Frames of the utterance currently being built (each item is one 50 ms
    // sub-window's samples — a fresh array owned by the producer, safe to hold).
    private readonly List<ReadOnlyMemory<float>> _frames = new();
    private int  _lastVoicedIdx = -1; // index in _frames of the last voiced frame
    private int  _hangoverCount;      // consecutive silent frames seen in Hangover
    private long _utteranceStartFrame;// global frame index where the utterance began

    private long _totalFrames; // monotonic frame counter since construction (for timing)
    private int  _nextIndex;   // emission order counter

    public EnergySegmenter(EnergySegmenterSettings settings, Action<Utterance> onUtterance)
    {
        _onUtterance = onUtterance;

        // dBFS → linear once, so the per-frame test is a plain comparison (no log).
        _rmsThreshold      = (float)Math.Pow(10.0, settings.ThresholdDbfs / 20.0);
        _hangoverMaxFrames = FramesFromMs(settings.HangoverMaxMs,       min: 1);
        _hangoverMinFrames = FramesFromMs(settings.HangoverMinMs,       min: 1);
        _rampStartFrames   = FramesFromMs(settings.HangoverRampStartMs, min: 1);
        _rampEndFrames     = FramesFromMs(settings.HangoverRampEndMs,   min: 1);
        _marginFrames      = FramesFromMs(settings.MarginMs,            min: 0);
        _minVoicedFrames   = FramesFromMs(settings.MinUtteranceMs,      min: 1);

        // Min must not exceed Max, and RampEnd must not precede RampStart — keep
        // the curve monotone non-increasing even if settings are inconsistent.
        if (_hangoverMinFrames > _hangoverMaxFrames) _hangoverMinFrames = _hangoverMaxFrames;
        if (_rampEndFrames     < _rampStartFrames)   _rampEndFrames     = _rampStartFrames;
    }

    private static int FramesFromMs(int ms, int min)
        => Math.Max(min, (int)Math.Round(ms / FrameMs));

    // Current hangover requirement, based on how long the open utterance has
    // grown. Below RampStart: max. At/above RampEnd: min. In between: cubic
    // ease-in (p³) wrapped around the log-linear decay — the hangover stays
    // near the max in the first half of the ramp window, then drops sharply in
    // the last quarter. This is the curve Louis wanted: long stability, then
    // brutal switch.
    private int RequiredHangoverFrames()
    {
        int len = _frames.Count;
        if (len <= _rampStartFrames) return _hangoverMaxFrames;
        if (len >= _rampEndFrames)   return _hangoverMinFrames;

        double p = (double)(len - _rampStartFrames) / (_rampEndFrames - _rampStartFrames);
        double pSteep = p * p * p;
        double ratio = (double)_hangoverMinFrames / _hangoverMaxFrames;
        int frames = (int)Math.Round(_hangoverMaxFrames * Math.Pow(ratio, pSteep));
        return Math.Max(_hangoverMinFrames, frames);
    }

    // Feed one capture frame. Emits an Utterance via the callback if this frame
    // completes one (silence-bounded end).
    public void Push(CaptureFrame frame)
    {
        bool voiced = frame.Rms >= _rmsThreshold;
        long frameIndex = _totalFrames;
        _totalFrames++;

        switch (_state)
        {
            case State.Silence:
                if (voiced)
                {
                    // First voiced frame opens an utterance.
                    _frames.Clear();
                    _frames.Add(frame.Samples);
                    _lastVoicedIdx = 0;
                    _hangoverCount = 0;
                    _utteranceStartFrame = frameIndex;
                    _state = State.Speech;
                }
                // else: discard the silent frame, stay in Silence.
                break;

            case State.Speech:
                _frames.Add(frame.Samples);
                if (voiced)
                {
                    _lastVoicedIdx = _frames.Count - 1;
                }
                else
                {
                    // First silent frame after speech opens the hangover.
                    _state = State.Hangover;
                    _hangoverCount = 1;
                    if (_hangoverCount >= RequiredHangoverFrames()) { EmitOnSilence(); return; }
                }
                break;

            case State.Hangover:
                _frames.Add(frame.Samples);
                if (voiced)
                {
                    // Speech resumed within the hangover — it was an intra-phrase
                    // pause, not the end. Keep building.
                    _lastVoicedIdx = _frames.Count - 1;
                    _hangoverCount = 0;
                    _state = State.Speech;
                }
                else
                {
                    _hangoverCount++;
                    if (_hangoverCount >= RequiredHangoverFrames()) { EmitOnSilence(); return; }
                }
                break;
        }
    }

    // End of capture (Stop / cap). Emits the open utterance, if any, applying the
    // same margin rule — clamped to what we have, so a Stop mid-word still ships
    // the in-progress speech. A no-op when no utterance is open.
    public void Flush()
    {
        if (_state == State.Silence) return;
        EmitKept(endedOnSilence: false);
        ResetToSilence();
    }

    // Silence-bounded end: cut at lastVoiced + margin, drop the rest, reset.
    private void EmitOnSilence()
    {
        EmitKept(endedOnSilence: true);
        ResetToSilence();
    }

    // Emit the kept span (voiced extent + margin, clamped) unless the voiced
    // extent is below the min-duration floor (then drop as a blip).
    private void EmitKept(bool endedOnSilence)
    {
        int voicedExtent = _lastVoicedIdx + 1;
        int keptCount    = Math.Min(_frames.Count, voicedExtent + _marginFrames);
        if (voicedExtent < _minVoicedFrames)
        {
            DeckleWhispSource.Log.SegmenterBlipDropped(
                voicedExtent, (int)Math.Round(voicedExtent * FrameMs));
            return; // blip — dropped
        }

        Emit(keptCount, voicedExtent, _hangoverCount, endedOnSilence);
    }

    private void Emit(int frameCount, int voicedFrames, int hangoverUsedFrames, bool endedOnSilence)
    {
        double startSec = _utteranceStartFrame * FrameSec;
        double endSec   = startSec + frameCount * FrameSec;
        int index = _nextIndex++;
        _onUtterance(new Utterance(Concat(frameCount), index, startSec, endSec, endedOnSilence));

        DeckleWhispSource.Log.SegmenterUtteranceEmitted(
            index, voicedFrames, frameCount, startSec, endSec,
            (int)Math.Round(hangoverUsedFrames * FrameMs));
    }

    // Snapshot reads the live state. Frame counts come from the running
    // utterance (0 when in Silence); the required hangover is computed from the
    // current length so the heartbeat sees the ramp moving as the chunk grows.
    internal SegmenterSnapshot Snapshot()
        => new(
            State: _state.ToString(),
            CurrentUtteranceFrames: _state == State.Silence ? 0 : _frames.Count,
            RequiredHangoverFrames: _state == State.Silence ? _hangoverMaxFrames : RequiredHangoverFrames(),
            TotalUtterancesEmitted: _nextIndex);

    private float[] Concat(int frameCount)
    {
        int total = 0;
        for (int i = 0; i < frameCount; i++) total += _frames[i].Length;

        var result = new float[total];
        int pos = 0;
        for (int i = 0; i < frameCount; i++)
        {
            var span = _frames[i].Span;
            span.CopyTo(result.AsSpan(pos));
            pos += span.Length;
        }
        return result;
    }

    private void ResetToSilence()
    {
        _frames.Clear();
        _lastVoicedIdx = -1;
        _hangoverCount = 0;
        _state = State.Silence;
    }
}
