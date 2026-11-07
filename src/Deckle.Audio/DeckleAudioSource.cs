using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Audio;

// Audio module provider: microphone capture, speaker render, audio-file
// decode, per-recording microphone telemetry and module settings persistence.
//
// snake_case parameter names directly become JSON keys in the payload emitted
// by JsonlSink.
[EventSource(Name = "Deckle-Audio")]
public sealed class DeckleAudioSource : DeckleEventSource
{
    public static readonly DeckleAudioSource Log = new();

    private DeckleAudioSource() { }

    // ── EventIds: sequential from 1, never reused (public in the ETW manifest)
    // 1 retired.
    // 2 retired.
    // 3 retired: the empty waveIn buffer; PortAudio reports no such buffer, a
    //   device that delivers nothing is NoAudioDataDetected.
    public const int EvtLowAudioDetected          = 4;
    public const int EvtCaptureLagDetected        = 5;
    public const int EvtDurationCapReached        = 6;
    // 7 retired.
    // 8 retired.
    public const int EvtCaptureCompleted          = 9;
    public const int EvtMicrophoneTelemetryEmpty  = 10;
    // 11 retired: the tail headline, folded into RecordingTailSummaryDetail's tail_state.
    public const int EvtMicrophoneTelemetryRecorded = 12;
    public const int EvtSettingsLoaded            = 13;
    public const int EvtSettingsLoadComplete      = 14;
    public const int EvtSettingsLoadWarning       = 15;
    public const int EvtSettingsLoadError         = 16;
    // 17 retired.
    // 18 retired: see 3.
    public const int EvtLowAudioDetectedDetail    = 19;
    public const int EvtCaptureLagDetectedDetail  = 20;
    public const int EvtDurationCapReachedDetail  = 21;
    // 22 retired.
    public const int EvtRecordingTailSummaryDetail = 23;
    // 24 retired.
    // 25 retired.
    public const int EvtAudioFileDecoded           = 26;
    public const int EvtAudioFileDecodedDetail     = 27;
    // 28 retired.
    // 29 retired.
    // 30 retired: see 3.
    // 31 retired: the per-take anomaly counts are RecordingAnomaliesSummary.
    public const int EvtCaptureLagRecovered        = 32;
    // 33 retired: see 31.
    public const int EvtRecordingStarted           = 34;
    public const int EvtCaptureStarted             = 35;
    public const int EvtMicrophoneOpenFailed       = 36;
    // 37 retired: folded into MicrophoneOpenFailed, whose stage names the call that failed.
    public const int EvtRecordingCompleted         = 38;
    public const int EvtRecordingFailed            = 39;
    public const int EvtNoAudioDataDetected        = 40;
    public const int EvtAudioDataRecovered         = 41;
    // 42 retired: see 31.
    public const int EvtPlaybackStarted            = 43;
    public const int EvtPlaybackCompleted          = 44;
    public const int EvtPlaybackFailed             = 45;
    public const int EvtPlaybackStalled            = 46;
    public const int EvtPlaybackSkipped            = 47;
    public const int EvtAudioFileDecodeStarted     = 48;
    public const int EvtAudioFileDecodeFailed      = 49;
    public const int EvtAudioFileDecodeFailedDetail = 50;
    public const int EvtAudioFileDecodeCrashed     = 51;
    public const int EvtCaptureSettingsResolved    = 52;
    public const int EvtCaptureReleaseFailed       = 53;
    public const int EvtCaptureReleaseRecovered    = 54;
    public const int EvtCaptureReleaseDetail       = 55;
    public const int EvtCaptureDrainDetail         = 56;
    public const int EvtRecordingAnomaliesSummary  = 57;
    public const int EvtCaptureModeFallback        = 58;
    public const int EvtMicrophoneDeviceLost       = 59;
    public const int EvtMicrophonePropertiesLaunchFailed = 60;

    // ── Recording lifecycle (milestones + verbose mirrors) ──────────────

    // The device and the format are in the milestone itself, not in a governed
    // detail: "which microphone actually ran" is the first question on a
    // machine with a wrong default device, and it must be answerable from a
    // default log. purpose separates dictation takes from Settings tests.
    //
    // A recording is lasting work: its start and its end travel in the log
    // and share a work_id, so the file alone shows a recording that never
    // ended. The no-data, empty-buffer and lag warnings that fall between them
    // belong to it by position, and RecordingAnomaliesSummary closes their count.
    // capture_mode is the processing mode obtained, raw or default; exclusive
    // says Deckle held the device alone. The format is the device's own, which
    // Deckle converts to 16 kHz mono itself, always 32-bit float. endpoint_id is
    // the MMDevice id the settings keep.
    [Event(EvtRecordingStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Version = 2,
           Message = "Recording started")]
    public void RecordingStarted(string purpose, string device_name, string endpoint_id, bool default_device, string capture_mode, bool exclusive, int sample_rate, int channels, string work_id)
    {
        if (IsEnabled())
            WriteEvent(EvtRecordingStarted, purpose, device_name, endpoint_id, default_device, capture_mode, exclusive, sample_rate, channels, work_id);
    }

    // input_latency_ms is the latency PortAudio reports for the opened stream.
    [Event(EvtCaptureStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 2,
           Message = "requested_endpoint_id={0} | input_latency_ms={1:F1} | exclusive={2} | max_recording_s={3}")]
    public void CaptureStarted(string requested_endpoint_id, double input_latency_ms, bool exclusive, int max_recording_s)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureStarted, requested_endpoint_id, input_latency_ms, exclusive, max_recording_s);
    }

    [Event(EvtRecordingCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "Recording complete")]
    public void RecordingCompleted(string purpose, string end_reason, double duration_s, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingCompleted, purpose, end_reason, duration_s, work_id);
    }

    // The exception still propagates to the caller, which owns the user-facing
    // outcome; this closes the take on the Audio provider's own timeline.
    [Event(EvtRecordingFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "The recording stopped on an error\nex_type={1} | ex_message={2} | purpose={0} | elapsed_ms={3} | work_id={4}")]
    public void RecordingFailed(string purpose, string ex_type, string ex_message, long elapsed_ms, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingFailed, purpose, ex_type, ex_message, elapsed_ms, work_id);
    }

    [Event(EvtCaptureCompleted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "audio_sec={0,6:F1} | buffers={1,5} | bytes={2,9} | rms_avg={3:F4} | rms_peak={4:F4} | dbfs_avg={5,6:F1}")]
    public void CaptureCompleted(double audio_sec, int buffers, int bytes, double rms_avg, double rms_peak, double dbfs_avg)
    {
        // The take's capture figures: a summary record of the dictation, so
        // the Summary verbosity admits it during a take.
        if (OperationalLogAdmission.IsScopedSummaryEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureCompleted, audio_sec, buffers, bytes, rms_avg, rms_peak, dbfs_avg);
    }

    // tail_ms is the audio still waiting in the device when the stop came,
    // which the drain read in tail_reads reads before stopping the stream.
    [Event(EvtCaptureDrainDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 2,
           Message = "drain_ms={0,5} | tail_ms={1,4} | tail_reads={2,2}")]
    public void CaptureDrainDetail(long drain_ms, int tail_ms, int tail_reads)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureDrainDetail, drain_ms, tail_ms, tail_reads);
    }

    // Optional per-take diagnosis of the last tail_ms of the recording, admitted
    // detail outside the permanent workflow timeline. tail_state (closed
    // vocabulary): active, the person was still speaking at Stop and the last
    // words may be clipped; silent, they stopped after a silence and the
    // capture ended cleanly. The sentence that once said so on its own line
    // (event 11, retired) carried nothing more than this word.
    [Event(EvtRecordingTailSummaryDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "tail_ms={0,4} | tail_dbfs={1,6:F1} | tail_state={2}")]
    public void RecordingTailSummaryDetail(int tail_ms, double tail_dbfs, string tail_state)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtRecordingTailSummaryDetail, tail_ms, tail_dbfs, tail_state);
    }

    // ── Microphone open and start ───────────────────────────────────────

    // The microphone could not be made to deliver. stage names the step that
    // was refused: load (portaudio.dll missing or not the expected build),
    // initialize (Pa_Initialize), resolve (no default microphone, or the saved
    // one is not plugged in), open (every mode refused) or start. error_code is
    // the HRESULT Windows gave, in hex, or PortAudio's own error, in decimal,
    // when Windows gave none (CaptureErrors).
    [Event(EvtMicrophoneOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Version = 2,
           Message = "The microphone could not be opened, so nothing is recorded\nstage={1} | error_code={2} | error_name={3} | device={5} | endpoint_id={4} | purpose={0}")]
    public void MicrophoneOpenFailed(string purpose, string stage, string error_code, string error_name, string endpoint_id, string device_name)
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneOpenFailed, purpose, stage, error_code, error_name, endpoint_id, device_name);
    }

    // The device was lost during a take: unplugged, disabled, or its format
    // changed under the stream. The audio captured before it is kept and goes on
    // through the pipeline; the take ends there.
    [Event(EvtMicrophoneDeviceLost,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "The microphone was lost during the recording, so it ends here\nerror_code={1} | error_name={2} | elapsed_ms={5} | device={4} | endpoint_id={3} | purpose={0}")]
    public void MicrophoneDeviceLost(string purpose, string error_code, string error_name, string endpoint_id, string device_name, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneDeviceLost, purpose, error_code, error_name, endpoint_id, device_name, elapsed_ms);
    }

    // What was asked of the audio stack and what it gave, one line per step
    // down. reason is exclusive_refused (the device could not be held alone,
    // so it is shared) or raw_refused (the device refused the raw processing
    // mode, so the default one, with the driver's effects, is used). A fallback
    // is a degradation, so it is a Warning, not a line of detail: the
    // maintainer asked for the rawest signal and must see when the microphone
    // did not give it.
    [Event(EvtCaptureModeFallback,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The microphone was opened in a less direct mode than asked for\nreason={1} | error_code={2} | error_name={3} | device={5} | endpoint_id={4} | purpose={0}")]
    public void CaptureModeFallback(string purpose, string reason, string error_code, string error_name, string endpoint_id, string device_name)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureModeFallback, purpose, reason, error_code, error_name, endpoint_id, device_name);
    }

    // The Recording page's link to the Windows microphone properties did not
    // open them: the launcher refused the ms-settings URI or threw. target is
    // default (the system default input) or device (the selected endpoint).
    // The person clicked and nothing happened, so it is a Warning.
    [Event(EvtMicrophonePropertiesLaunchFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The Windows microphone properties did not open\ntarget={0}")]
    public void MicrophonePropertiesLaunchFailed(string target)
    {
        if (IsEnabled()) WriteEvent(EvtMicrophonePropertiesLaunchFailed, target);
    }

    // The stream refused to stop at the end of a take. A device that will not
    // let go can fail the next open with AUDCLNT_E_DEVICE_IN_USE. One incident
    // across takes: it opens on the first take that ends this way and closes
    // on the first clean one, which carries how many takes failed between.
    [Event(EvtCaptureReleaseFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 2,
           Message = "The microphone was not released cleanly, so the next recording may fail to open it\nstage={0} | error_code={1} | error_name={2}")]
    public void CaptureReleaseFailed(string stage, string error_code, string error_name)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureReleaseFailed, stage, error_code, error_name);
    }

    [Event(EvtCaptureReleaseRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "The microphone is released cleanly again")]
    public void CaptureReleaseRecovered(int failures)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureReleaseRecovered, failures);
    }

    [Event(EvtCaptureReleaseDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 2,
           Message = "stage={0} | error_code={1} | failures={2}")]
    public void CaptureReleaseDetail(string stage, string error_code, int failures)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureReleaseDetail, stage, error_code, failures);
    }

    // ── Anomalies Captured In The Capture Loop ───────────────────────────

    // A device that opens and starts but hands back nothing: every call
    // succeeded, so no other event says the take is empty. reason is no_data:
    // no frame became ready for a second. A muted microphone, or one blocked
    // by the Windows privacy setting, still delivers frames of silence, which
    // the low-audio warning catches instead.
    [Event(EvtNoAudioDataDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 2,
           Message = "The microphone delivers no audio, so the recording is silent\nreason={0} | waited_ms={1} | device={3} | endpoint_id={2}")]
    public void NoAudioDataDetected(string reason, long waited_ms, string endpoint_id, string device_name)
    {
        if (IsEnabled()) WriteEvent(EvtNoAudioDataDetected, reason, waited_ms, endpoint_id, device_name);
    }

    [Event(EvtAudioDataRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "The microphone delivers audio again")]
    public void AudioDataRecovered(long waited_ms)
    {
        if (IsEnabled()) WriteEvent(EvtAudioDataRecovered, waited_ms);
    }

    [Event(EvtLowAudioDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Nothing rose above the voice level at the start of the recording, so the microphone may be muted or the voice level set too high")]
    public void LowAudioDetected()
    {
        if (IsEnabled()) WriteEvent(EvtLowAudioDetected);
    }

    [Event(EvtLowAudioDetectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "recording_ms={0} | min_sustained_ms={1} | voice_level_dbfs={2}")]
    public void LowAudioDetectedDetail(int recording_ms, int min_sustained_ms, double voice_level_dbfs)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtLowAudioDetectedDetail, recording_ms, min_sustained_ms, voice_level_dbfs);
    }

    // behind_ms is the audio waiting in the device when the loop woke (60 ms
    // or more is the threshold, CaptureLoop.LagThresholdMs): the least the
    // consumer had fallen behind by. PortAudio does not report audio the device
    // discarded, so a backlog is the only sign the loop came close to losing
    // some. The first lag of a recording opens the incident; later ones only
    // count in RecordingAnomaliesSummary.
    [Event(EvtCaptureLagDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 2,
           Message = "The capture loop fell behind the microphone\nbehind_ms={0}")]
    public void CaptureLagDetected(int behind_ms)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLagDetected, behind_ms);
    }

    // gc0/gc1/gc2 are the collections that happened DURING the lagging
    // iteration (the one whose body caused the pile-up), not a running total
    // since recording start, so a non-zero value indicts that GC directly.
    [Event(EvtCaptureLagDetectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Version = 2,
           Message = "behind_ms={0} | iter={1} | wait_ms={2} | prev_iter_ms={3} | gc0={4} | gc1={5} | gc2={6}")]
    public void CaptureLagDetectedDetail(int behind_ms, long iter, long wait_ms, long prev_iter_ms, int gc0, int gc1, int gc2)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureLagDetectedDetail, behind_ms, iter, wait_ms, prev_iter_ms, gc0, gc1, gc2);
    }

    [Event(EvtCaptureLagRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "The capture loop recovered")]
    public void CaptureLagRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLagRecovered);
    }

    // One line per recording that met either anomaly above, written when the
    // loop ends and whatever the verbosity: a recording that ends while an
    // anomaly is still open leaves the Warning in the log and no closer, so
    // this is the line that says how far it went. A recording with neither
    // writes nothing. *_recovered is false when the anomaly was still open at
    // the end (and when its own count is 0); lag_max_behind_ms is the worst
    // backlog seen.
    [Event(EvtRecordingAnomaliesSummary,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Version = 2,
           Message = "Recording anomalies")]
    public void RecordingAnomaliesSummary(
        int no_data_stalls, long no_data_longest_gap_ms, bool no_data_recovered,
        int lag_occurrences, bool lag_recovered, int lag_max_behind_ms,
        int recording_ms, string work_id)
    {
        if (IsEnabled())
            WriteEvent(EvtRecordingAnomaliesSummary,
                no_data_stalls, no_data_longest_gap_ms, no_data_recovered,
                lag_occurrences, lag_recovered, lag_max_behind_ms,
                recording_ms, work_id);
    }

    [Event(EvtDurationCapReached,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Recording stopped at the maximum duration, so later speech was not recorded")]
    public void DurationCapReached()
    {
        if (IsEnabled()) WriteEvent(EvtDurationCapReached);
    }

    [Event(EvtDurationCapReachedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "audio_sec={0:F1} | cap_sec={1}")]
    public void DurationCapReachedDetail(double audio_sec, int cap_sec)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)(Keywords.Capture | Keywords.Lifecycle)))
            WriteEvent(EvtDurationCapReachedDetail, audio_sec, cap_sec);
    }

    // ── Speaker playback ────────────────────────────────────────────────
    //
    // One play per user action (listening test variant, read-aloud), so the
    // lifecycle pair is Informational: a playback that never sounds must leave
    // its device and its outcome in a default log.

    [Event(EvtPlaybackStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Milestone,
           Message = "Playback started")]
    public void PlaybackStarted(string device_name, int device_id, int sample_rate, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPlaybackStarted, device_name, device_id, sample_rate, duration_ms);
    }

    // outcome: completed | cancelled | failed.
    [Event(EvtPlaybackCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Milestone,
           Message = "Playback ended")]
    public void PlaybackCompleted(string outcome, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPlaybackCompleted, outcome, elapsed_ms);
    }

    // stage: create_event (win32_error) | open | prepare_header | write
    // (mmsys_err). open covers no render device or an exclusive-mode conflict.
    [Event(EvtPlaybackFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Push | Keywords.Lifecycle),
           Message = "Audio could not play on the speaker\nstage={0} | mmsys_err={1} | mmsys_name={2} | win32_error={3} | device={4} | sample_rate={5}")]
    public void PlaybackFailed(string stage, uint mmsys_err, string mmsys_name, int win32_error, string device_name, int sample_rate)
    {
        if (IsEnabled())
            WriteEvent(EvtPlaybackFailed, stage, mmsys_err, mmsys_name, win32_error, device_name, sample_rate);
    }

    // The driver had the clip long past its length without flagging it done.
    // The wait goes on until the caller cancels; this says why nothing ended.
    [Event(EvtPlaybackStalled,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "The speaker did not finish the clip\nreason={0} | waited_ms={1} | duration_ms={2}")]
    public void PlaybackStalled(string reason, long waited_ms, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPlaybackStalled, reason, waited_ms, duration_ms);
    }

    // Play returns false here, which the listening test shows as a failure
    // like a device that would not open.
    [Event(EvtPlaybackSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Audio playback was skipped, so nothing was heard\nreason={0}")]
    public void PlaybackSkipped(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtPlaybackSkipped, reason);
    }

    // ── Audio-file decode (file transcription) ──────────────────────────────
    //
    // The failure milestone is Warning, not Error: a bad file is an expected
    // user outcome, not a broken device. A thrown exception, the STA-thread
    // precondition among them, is a programming or environment fault: Error.

    [Event(EvtAudioFileDecodeStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Decoding an audio file")]
    public void AudioFileDecodeStarted(string extension)
    {
        if (IsEnabled()) WriteEvent(EvtAudioFileDecodeStarted, extension);
    }

    [Event(EvtAudioFileDecoded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Decoded an audio file for transcription")]
    public void AudioFileDecoded()
    {
        if (IsEnabled()) WriteEvent(EvtAudioFileDecoded);
    }

    [Event(EvtAudioFileDecodedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "source={0} | duration_sec={1:F1} | decoded_samples={2} | elapsed_ms={3}")]
    public void AudioFileDecodedDetail(string source, double duration_sec, int decoded_samples, long elapsed_ms)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtAudioFileDecodedDetail, source, duration_sec, decoded_samples, elapsed_ms);
    }

    // reason: file_not_found | unsupported_format | no_audio_track |
    // protected_content | read_error. hr is 0 when the decision came before
    // any Media Foundation call.
    [Event(EvtAudioFileDecodeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "An audio file could not be decoded, so its audio is unavailable\nreason={0} | hr=0x{1:X8} | elapsed_ms={2}")]
    public void AudioFileDecodeFailed(string reason, int hr, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtAudioFileDecodeFailed, reason, hr, elapsed_ms);
    }

    [Event(EvtAudioFileDecodeFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "source={0}")]
    public void AudioFileDecodeFailedDetail(string source)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)(Keywords.Capture | Keywords.Lifecycle)))
            WriteEvent(EvtAudioFileDecodeFailedDetail, source);
    }

    [Event(EvtAudioFileDecodeCrashed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Decoding an audio file stopped on an error, so its audio is unavailable\nex_type={0} | ex_message={1} | elapsed_ms={2}")]
    public void AudioFileDecodeCrashed(string ex_type, string ex_message, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtAudioFileDecodeCrashed, ex_type, ex_message, elapsed_ms);
    }

    // A short take with no complete RMS window is ordinary per-take detail, not
    // a durable microphone incident.
    [Event(EvtMicrophoneTelemetryEmpty,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "The recording was too short to measure microphone levels")]
    public void MicrophoneTelemetryEmpty()
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtMicrophoneTelemetryEmpty);
    }

    // ── Structured Heartbeat: Microphone Telemetry Payload ──────────────
    //
    // EventSource does not accept complex types in signatures, so the
    // MicrophoneTelemetryPayload is flattened into primitive parameters.

    [Event(EvtMicrophoneTelemetryRecorded,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "duration_seconds={0:F1} | samples={1} | min_dbfs={2:F1} | p10_dbfs={3:F1} | p25_dbfs={4:F1} | p50_dbfs={5:F1} | p75_dbfs={6:F1} | p90_dbfs={7:F1} | max_dbfs={8:F1} | mean_rms={9:F4} | mean_dbfs={10:F1}")]
    public void MicrophoneTelemetryRecorded(
        double duration_seconds,
        int    samples,
        double min_dbfs,
        double p10_dbfs,
        double p25_dbfs,
        double p50_dbfs,
        double p75_dbfs,
        double p90_dbfs,
        double max_dbfs,
        double mean_rms,
        double mean_dbfs,
        double tail_rms,
        double tail_dbfs,
        string tail_state)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtMicrophoneTelemetryRecorded,
            duration_seconds, samples,
            min_dbfs, p10_dbfs, p25_dbfs, p50_dbfs, p75_dbfs, p90_dbfs, max_dbfs,
            mean_rms, mean_dbfs, tail_rms, tail_dbfs, tail_state);
    }

    // ── Module Settings Persistence ─────────────────────────────────────
    //
    // Exception to the "strict-typed per operation" doctrine: the
    // JsonSettingsStore<T> delegates in Deckle.Core are Action<string>, so the
    // call site cannot tell "Settings loaded" from "Settings initialized
    // (defaults)" or "reloaded from disk". These 4 events are typed by level,
    // not by operation, and all carry the Lifecycle keyword: Informational
    // relays the store's milestone sentence (loaded, initialized, reloaded),
    // Verbose its structured detail, Warning and Error its problems. The
    // message prefix names the module so a reader knows which settings file
    // spoke.

    [Event(EvtSettingsLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The audio settings were loaded")]
    public void SettingsLoaded(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoaded, message);
    }

    [Event(EvtSettingsLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "detail={0}")]
    public void SettingsLoadComplete(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadComplete, PathRedaction.Redact(message));
    }

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The audio settings store worked around a problem\ndetail={0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The audio settings could not be saved or applied\ndetail={0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    // The values a recording will read: the chosen microphone's endpoint id (empty
    // is the system default), the duration cap (0 is none), the opt-in DSP switch and the
    // voice level. The voice level is here because a monolithic take writes
    // no segmenter snapshot: this line is the only trace of the level its
    // low-audio warning read. Emitted at load and after each change, at the
    // rate a person edits them, so the object-array WriteEvent costs nothing
    // that matters.
    [Event(EvtCaptureSettingsResolved,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Version = 2,
           Message = "endpoint_id={0} | max_recording_s={1} | preprocessing={2} | voice_level_dbfs={3}")]
    public void CaptureSettingsResolved(string endpoint_id, int max_recording_s, bool preprocessing, double voice_level_dbfs)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCaptureSettingsResolved, endpoint_id, max_recording_s, preprocessing, voice_level_dbfs);
    }
}
