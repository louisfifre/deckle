using System.Runtime.InteropServices;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── Telemetry partial — post-recording auto-calibration and preprocessed-level telemetry ──

    // Runs after every recording when LevelWindow.AutoCalibrationEnabled is
    // on, independent of the microphone telemetry consent toggle (capture
    // always computes the payload). The heuristic is
    // MicrophoneCalibrationCalculator; the ring buffer and side effects stay on
    // the orchestrator.
    //
    // The buffer is in-memory only: a fresh launch starts collecting again,
    // and the persisted Min/Max already reflect the last calibration. There is
    // no manual-edit flag: a slider edit holds until calibration next fires,
    // and whoever wrote last wins.
    private void TryAutoCalibrate(MicrophoneTelemetryPayload payload)
    {
        var lw = _host.Audio.LevelWindow;
        if (!lw.AutoCalibrationEnabled) return;

        int needed = Math.Max(1, lw.AutoCalibrationSamples);

        _autoCalibBuffer.Enqueue(payload);
        while (_autoCalibBuffer.Count > needed) _autoCalibBuffer.Dequeue();
        if (_autoCalibBuffer.Count < needed) return;

        var calib = MicrophoneCalibrationCalculator.Compute(
            _autoCalibBuffer, lw.MinDbfs, lw.MaxDbfs);
        if (!calib.ShouldUpdate) return;

        lw.MinDbfs = calib.NewMinDbfs;
        lw.MaxDbfs = calib.NewMaxDbfs;
        _host.SaveSettings();

        _host.ApplyLevelWindow(lw);

        DeckleWhispSource.Log.AutoCalibrated();
        DeckleWhispSource.Log.AutoCalibratedDetail(calib.NewMinDbfs, calib.NewMaxDbfs, needed);
    }

    // The post-DSP mirror of the raw MicrophoneTelemetryRecorded, on this
    // orchestrator's provider because the DSP is its concern. Same consent gate
    // as the raw side. Callers pass the buffer the backend received, and only
    // when the DSP actually ran; otherwise it would duplicate the raw
    // distribution. The 50 ms RMS series keeps the payload comparable, field
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
