using System;
using System.Collections.Generic;
using Deckle.Audio;

namespace Deckle.Transcription;

// Feeds the 1 Hz StreamingHeartbeat. RequiredHangoverFrames is the silence the
// state machine would currently demand to cut; it shrinks as the open
// utterance grows past the ramp.
internal readonly record struct SegmenterSnapshot(
    string State,
    int CurrentUtteranceFrames,
    int RequiredHangoverFrames,
    int TotalUtterancesEmitted);

// ── EnergySegmenter ──────────────────────────────────────────────────────────
//
// Threshold-on-RMS state machine that places utterance boundaries on the
// capture stream. NOT a model and NOT speech recognition: it reads energy dips
// to decide where to cut. It consumes CaptureFrames (50 ms sub-windows + their
// RAW linear RMS) one at a time and emits an Utterance each time a speech span
// ends.
//
// Single-threaded by construction: Push and Flush run on the one producer
// thread, so there is no shared mutable state and no lock.
//
// ── Hangover vs margin (the two values people conflate) ───────────────────────
//   Hangover = DECISION delay: how much silence after the last voiced frame
//   before the utterance is declared over, so a brief intra-phrase pause does
//   not split a sentence. It SHRINKS as the utterance grows (see
//   RequiredHangoverFrames). No hard cap: a very long utterance ends on a
//   micro-pause, never mid-word.
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

    // False for a segmenter that is not cutting a dictation (the live pause
    // gauge in the Playground): its cuts would otherwise read in the detailed
    // log exactly like the cuts of a take.
    private readonly bool _logsDetail;

    // Derived from settings once, in frame units.
    private readonly float _rmsThreshold;       // linear RMS at/above which a frame is voiced
    private readonly int   _hangoverMaxFrames;  // hangover at utterance start
    private readonly int   _hangoverMinFrames;  // hangover floor past the ramp
    private readonly int   _rampStartFrames;    // utterance length above which hangover starts shrinking
    private readonly int   _rampEndFrames;      // utterance length at/above which hangover = min
    private readonly int   _marginFrames;       // trailing silence frames kept after last voiced
    private readonly int   _minVoicedFrames;    // shorter voiced extent → dropped as a blip
    private readonly UnitBezier _hangoverCurve;  // cubic-Bézier easing of the ramp decay

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

    public EnergySegmenter(
        EnergySegmenterSettings settings, Action<Utterance> onUtterance, bool logsDetail = true)
    {
        _onUtterance = onUtterance;
        _logsDetail = logsDetail;

        // dBFS → linear once, so the per-frame test is a plain comparison (no log).
        _rmsThreshold      = (float)Math.Pow(10.0, settings.ThresholdDbfs / 20.0);
        _hangoverMaxFrames = FramesFromMs(settings.HangoverMaxMs,       min: 1);
        _hangoverMinFrames = FramesFromMs(settings.HangoverMinMs,       min: 1);
        _rampStartFrames   = FramesFromMs(settings.HangoverRampStartMs, min: 1);
        _rampEndFrames     = FramesFromMs(settings.HangoverRampEndMs,   min: 1);
        _marginFrames      = FramesFromMs(settings.MarginMs,            min: 0);
        _minVoicedFrames   = FramesFromMs(settings.MinUtteranceMs,      min: 1);
        _hangoverCurve     = new UnitBezier(
            settings.HangoverCurveX1, settings.HangoverCurveY1,
            settings.HangoverCurveX2, settings.HangoverCurveY2);

        // Min must not exceed Max, and RampEnd must not precede RampStart — keep
        // the curve monotone non-increasing even if settings are inconsistent.
        if (_hangoverMinFrames > _hangoverMaxFrames) _hangoverMinFrames = _hangoverMaxFrames;
        if (_rampEndFrames     < _rampStartFrames)   _rampEndFrames     = _rampStartFrames;
    }

    private static int FramesFromMs(int ms, int min)
        => Math.Max(min, (int)Math.Round(ms / FrameMs));

    // Between RampStart and RampEnd the delay eases from max to min along the
    // cubic-Bézier curve (see UnitBezier), which declines from the very start of
    // the ramp (no built-in plateau, whole-frame rounding aside), with entry and
    // exit slopes set by the two control points.
    private int RequiredHangoverFrames()
    {
        int len = _frames.Count;
        if (len <= _rampStartFrames) return _hangoverMaxFrames;
        if (len >= _rampEndFrames)   return _hangoverMinFrames;

        double p = (double)(len - _rampStartFrames) / (_rampEndFrames - _rampStartFrames);
        double t = _hangoverCurve.Solve(p);
        int frames = (int)Math.Round(_hangoverMaxFrames - (_hangoverMaxFrames - _hangoverMinFrames) * t);
        return Math.Clamp(frames, _hangoverMinFrames, _hangoverMaxFrames);
    }

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
                    _frames.Clear();
                    _frames.Add(frame.Samples);
                    _lastVoicedIdx = 0;
                    _hangoverCount = 0;
                    _utteranceStartFrame = frameIndex;
                    _state = State.Speech;
                }
                break;

            case State.Speech:
                _frames.Add(frame.Samples);
                if (voiced)
                {
                    _lastVoicedIdx = _frames.Count - 1;
                }
                else
                {
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

    // End of capture (Stop / cap). Emits the open utterance with the same margin
    // rule, clamped to what we have, so a Stop mid-word still ships the
    // in-progress speech.
    public void Flush()
    {
        if (_state == State.Silence) return;
        EmitKept(endedOnSilence: false);
        ResetToSilence();
    }

    private void EmitOnSilence()
    {
        EmitKept(endedOnSilence: true);
        ResetToSilence();
    }

    private void EmitKept(bool endedOnSilence)
    {
        int voicedExtent = _lastVoicedIdx + 1;
        int keptCount    = Math.Min(_frames.Count, voicedExtent + _marginFrames);
        if (voicedExtent < _minVoicedFrames)
        {
            if (_logsDetail)
            {
                DeckleWhispSource.Log.SegmenterBlipDropped(
                    voicedExtent, (int)Math.Round(voicedExtent * FrameMs));
            }
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

        if (_logsDetail)
        {
            DeckleWhispSource.Log.SegmenterUtteranceEmitted(
                index, voicedFrames, frameCount, startSec, endSec,
                (int)Math.Round(hangoverUsedFrames * FrameMs));
        }
    }

    internal SegmenterSnapshot Snapshot()
        => new(
            State: _state.ToString(),
            CurrentUtteranceFrames: _state == State.Silence ? 0 : _frames.Count,
            RequiredHangoverFrames: _state == State.Silence ? _hangoverMaxFrames : RequiredHangoverFrames(),
            TotalUtterancesEmitted: _nextIndex);

    // Read only on the producer thread, immediately after Push. This shares
    // the exact frame counter and threshold used by the cutting decision.
    internal UtteranceProgress Progress => new(
        IsActive: true,
        IsPausing: _state == State.Hangover,
        SilenceElapsedMs: (int)(_hangoverCount * FrameMs),
        SilenceRequiredMs: RequiredHangoverFrames() * (int)FrameMs,
        CompletedUtterances: _nextIndex);

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
