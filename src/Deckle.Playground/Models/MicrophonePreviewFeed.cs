using System;
using Deckle.Audio;
using Deckle.Transcription;

namespace Deckle.Playground;

// The live source of the HUD tab's Recording preview: it opens the microphone
// and hands the preview what a dictation hands the live HUD, the level of each
// 50 ms and the pause gauge computed from those levels with the dictation's own
// segmenter (UtteranceProgressMeter). It records nothing: the capture keeps no
// audio (RetainsAudio) and no meter or telemetry event is emitted for it.
//
// The microphone itself, its thread per run, its ordering of the sinks against
// Stop and its wait for the device between two runs, is Deckle.Audio's
// MicrophoneLevelFeed. What is the Playground's own is the gauge beside it:
// every sink below runs under that feed's lock, so once Stop returns no level
// and no gauge reaches the preview, and the caller can change the preview's
// state right after Stop with no late level landing on the new state.
//
// Whether waveIn lets this open while a live dictation holds the microphone is
// not known from the code. Both outcomes are handled the same way: an open
// that fails is reported to onFailed and the caller moves to the simulation.
internal sealed class MicrophonePreviewFeed
{
    private readonly MicrophoneLevelFeed _feed;
    private readonly Func<int> _deviceId;
    private readonly Func<EnergySegmenterSettings> _segmenterSettings;
    private readonly Func<double> _voiceLevelDbfs;
    private readonly Func<int> _minSpeechMs;
    private readonly Action<float> _onLevel;
    private readonly Action<UtteranceProgress> _onProgress;

    // Written on the UI thread while no run is open, or under the feed's lock
    // through RestartOpenRun; read by the sinks under that lock.
    private UtteranceProgressMeter? _meter;
    private PreviewHost? _host;

    // createCapture, deviceId, segmenterSettings, voiceLevelDbfs and minSpeechMs
    // are read at each Start, as a dictation reads them when it begins. onLevel and
    // onProgress are called from the capture thread; onFailed too, once, and
    // never for a run that was stopped.
    public MicrophonePreviewFeed(
        Func<IMicrophoneCapture> createCapture,
        Func<int> deviceId,
        Func<EnergySegmenterSettings> segmenterSettings,
        Func<double> voiceLevelDbfs,
        Func<int> minSpeechMs,
        Action<float> onLevel,
        Action<UtteranceProgress> onProgress,
        Action<MicrophonePreviewFailure> onFailed)
    {
        _deviceId = deviceId;
        _segmenterSettings = segmenterSettings;
        _voiceLevelDbfs = voiceLevelDbfs;
        _minSpeechMs = minSpeechMs;
        _onLevel = onLevel;
        _onProgress = onProgress;
        _feed = new MicrophoneLevelFeed(
            createCapture,
            host: () => _host!,
            onLevel: Deliver,
            onFailed: failure => onFailed(new MicrophonePreviewFailure(failure)),
            // Where a take stands before its first level, published before the
            // first one, as the engine does.
            onOpened: () => _onProgress(_meter!.Progress),
            // The gauge is dropped as the engine drops it when its capture ends.
            onClosed: () => _onProgress(default));
    }

    // Opens the microphone. When it is already open, the take starts over on the
    // same capture, with a fresh gauge: a preview that restarts (a knob moved)
    // does not close and reopen the device each time.
    public void Start()
    {
        double voiceLevelDbfs = _voiceLevelDbfs();
        var meter = new UtteranceProgressMeter(_segmenterSettings(), voiceLevelDbfs, _minSpeechMs());
        int device = _deviceId();

        if (_feed.RestartOpenRun(() =>
            {
                _meter = meter;
                _onProgress(meter.Progress);
            }))
            return;

        // No run is open, so no sink reads these until Start opens one.
        _meter = meter;
        _host = new PreviewHost(device, voiceLevelDbfs);
        _feed.Start();
    }

    // Closes the microphone. Final: once it returns, nothing more arrives.
    public void Stop() => _feed.Stop();

    private void Deliver(float rms)
    {
        _onLevel(rms);
        _onProgress(_meter!.Push(rms));
    }

    // What a preview takes from the microphone: the configured device, no audio
    // kept, no cap, no telemetry. The purpose tags the capture's own events, so
    // a preview does not read as a dictation in the log. The voice level is the
    // one the meter cuts on; the low-audio warning that reads it stays off for
    // a take that keeps no audio.
    private sealed class PreviewHost(int deviceId, double voiceLevelDbfs) : IAudioRecordingHost
    {
        public int AudioInputDeviceId => deviceId;
        public int MaxRecordingDurationSeconds => 0;
        public bool MicrophoneTelemetryEnabled => false;
        public double VoiceLevelDbfs => voiceLevelDbfs;
        public string RecordingPurpose => "playground";
        public bool RetainsAudio => false;
    }
}
