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
    // ── Telemetry partial — preprocessed-level telemetry ──

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
