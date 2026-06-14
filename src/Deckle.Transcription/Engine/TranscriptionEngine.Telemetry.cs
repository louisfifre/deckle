using System.Runtime.InteropServices;
using Deckle.Audio;
using Deckle.Audio.Preprocessing;
using Deckle.Audio.Telemetry;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Core.Interop;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription.Corpus;
using Deckle.Transcription.Engine;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── Telemetry partial — post-recording auto-calibration and preprocessed-level telemetry ──

    // Auto-calibration heuristic — runs after every Recording when
    // LevelWindow.AutoCalibrationEnabled is true, independent of the
    // Log microphone toggle (the payload is always computed in
    // LogRecordingTelemetry above).
    //
    // Strategy:
    //   - Keep the last N MicrophoneTelemetryPayloads in a ring buffer
    //     (N = LevelWindow.AutoCalibrationSamples, default 5).
    //   - Once the buffer is full, recompute MinDbfs / MaxDbfs from
    //     median-across-sessions percentiles, with margins:
    //       MinDbfs = median(p25) - 5 dB  — p25 (not p10) so a noise gate
    //                                       cutting to digital silence
    //                                       (-97 dBFS) doesn't drag the
    //                                       floor into "anything below
    //                                       the gate threshold". Then
    //                                       -5 dB of headroom under the
    //                                       useful-signal minimum.
    //       MaxDbfs = median(p90) + 5 dB  — voice ceiling with breathing
    //                                       room above routine peaks.
    //   - Floor clamp at -75 dBFS to guarantee we never sit on the gate
    //     even if p25 itself is in the noise floor.
    //   - Refuse to write if the resulting window collapses to < 10 dB
    //     (pathological case — e.g. all-silence sessions).
    //   - Push to settings + AudioLevelMapper + log a Success line.
    //
    // The buffer is in-memory only: a fresh app launch starts collecting
    // again, which is fine — calibration only fires after N consecutive
    // recordings within one process anyway, and the persisted Min/Max
    // already reflects the last successful auto-calibration.
    //
    // The user's manual slider edits override auto-calibration until the
    // next time it fires — there's no "manual flag" gating; whoever wrote
    // last wins, which is the natural behaviour from the user's POV.
    private void TryAutoCalibrate(MicrophoneTelemetryPayload payload)
    {
        var lw = _host.Audio.LevelWindow;
        if (!lw.AutoCalibrationEnabled) return;

        int needed = Math.Max(1, lw.AutoCalibrationSamples);

        _autoCalibBuffer.Enqueue(payload);
        while (_autoCalibBuffer.Count > needed) _autoCalibBuffer.Dequeue();
        if (_autoCalibBuffer.Count < needed) return;

        // Pure compute lives in MicrophoneCalibrationCalculator — the
        // constants (-5 dB / +5 dB margins, -75 floor, ≥10 dB spread,
        // [-90,-10] / [-60,-10] clamps, 0.5 dB no-change tolerance) are
        // preserved exactly. The enveloppe (ring buffer, SaveSettings,
        // ApplyLevelWindow, log) stays here because the side effects
        // belong to the orchestrator.
        var calib = MicrophoneCalibrationCalculator.Compute(
            _autoCalibBuffer, lw.MinDbfs, lw.MaxDbfs);
        if (!calib.ShouldUpdate) return;

        lw.MinDbfs = calib.NewMinDbfs;
        lw.MaxDbfs = calib.NewMaxDbfs;
        _host.SaveSettings();

        // Push live into HudChrono so the next sub-window already uses the
        // new calibration. The host owns the static-field write
        // (App.ApplyLevelWindow on the App side).
        _host.ApplyLevelWindow(lw);

        DeckleWhispSource.Log.AutoCalibrated();
        DeckleWhispSource.Log.AutoCalibratedDetail(calib.NewMinDbfs, calib.NewMaxDbfs, needed);
    }

    // Emit the post-DSP level distribution — the processed-signal mirror of the raw
    // MicrophoneTelemetryRecorded, on this orchestrator's provider (the DSP is its
    // concern, not the capture module's). Same consent gate as the raw side. Callers
    // pass the buffer the backend received, and only when the DSP actually ran — off,
    // processed == raw and this would just duplicate the raw distribution. Rebuilds a
    // 50 ms RMS series so the shared Compute path yields a payload comparable, field
    // for field, with the raw one.
    private void EmitPreprocessedTelemetry(float[] processed)
    {
        if (!_recordingHost.MicrophoneTelemetryEnabled) return;

        var rms = MicrophoneTelemetryCalculator.RmsSeries(
            processed, MicrophoneTelemetryCalculator.SubWindowSamples);
        var tail = MicrophoneTelemetryCalculator.ComputeTail(rms);
        if (tail is null) return;

        var t = MicrophoneTelemetryCalculator.Compute(rms, tail.Value);
        if (t is null) return;

        DeckleWhispSource.Log.PreprocessedTelemetryRecorded(
            t.DurationSeconds, t.Samples,
            t.MinDbfs, t.P10Dbfs, t.P25Dbfs, t.P50Dbfs, t.P75Dbfs, t.P90Dbfs, t.MaxDbfs,
            t.MeanRms, t.MeanDbfs, t.TailRms, t.TailDbfs, t.TailState);
    }
}
