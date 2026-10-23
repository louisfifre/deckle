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
    public const int EvtEmptyBufferReceived       = 3;
    public const int EvtLowAudioDetected          = 4;
    public const int EvtCaptureLagDetected        = 5;
    public const int EvtDurationCapReached        = 6;
    // 7 retired.
    // 8 retired.
    public const int EvtCaptureCompleted          = 9;
    public const int EvtMicrophoneTelemetryEmpty  = 10;
    public const int EvtRecordingTailSummary      = 11;
    public const int EvtMicrophoneTelemetryRecorded = 12;
    public const int EvtSettingsLoaded            = 13;
    public const int EvtSettingsLoadComplete      = 14;
    public const int EvtSettingsLoadWarning       = 15;
    public const int EvtSettingsLoadError         = 16;
    // 17 retired.
    public const int EvtEmptyBufferReceivedDetail = 18;
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
    public const int EvtEmptyBufferRecovered       = 30;
    // 31 retired: the per-take anomaly counts are RecordingAnomaliesSummary.
    public const int EvtCaptureLagRecovered        = 32;
    // 33 retired: see 31.
    public const int EvtRecordingStarted           = 34;
    public const int EvtCaptureStarted             = 35;
    public const int EvtMicrophoneOpenFailed       = 36;
    public const int EvtMicrophoneStartFailed      = 37;
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
    [Event(EvtRecordingStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "Recording started")]
    public void RecordingStarted(string purpose, string device_name, int device_id, bool default_device, int sample_rate, int channels, int bits_per_sample, string work_id)
    {
        if (IsEnabled())
            WriteEvent(EvtRecordingStarted, purpose, device_name, device_id, default_device, sample_rate, channels, bits_per_sample, work_id);
    }

    [Event(EvtCaptureStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "requested_device_id={0} | buffers={1} | buffer_bytes={2} | max_recording_s={3}")]
    public void CaptureStarted(int requested_device_id, int buffers, int buffer_bytes, int max_recording_s)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureStarted, requested_device_id, buffers, buffer_bytes, max_recording_s);
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
           Message = "Recording failed\nex_type={1} | ex_message={2} | purpose={0} | elapsed_ms={3} | work_id={4}")]
    public void RecordingFailed(string purpose, string ex_type, string ex_message, long elapsed_ms, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingFailed, purpose, ex_type, ex_message, elapsed_ms, work_id);
    }

    [Event(EvtCaptureCompleted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "audio_sec={0:F1} | buffers={1} | bytes={2} | rms_avg={3:F4} | rms_peak={4:F4} | dbfs_avg={5:F1}")]
    public void CaptureCompleted(double audio_sec, int buffers, int bytes, double rms_avg, double rms_peak, double dbfs_avg)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureCompleted, audio_sec, buffers, bytes, rms_avg, rms_peak, dbfs_avg);
    }

    // pending_buffers counts the headers still queued after waveInStop and the
    // drain sleep: waveInStop returns only the current buffer, so a non-zero
    // value is ordinary; tail_bytes is what the drain recovered.
    [Event(EvtCaptureDrainDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "drain_ms={0} | tail_bytes={1} | pending_buffers={2}")]
    public void CaptureDrainDetail(long drain_ms, int tail_bytes, int pending_buffers)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureDrainDetail, drain_ms, tail_bytes, pending_buffers);
    }

    // Optional per-take diagnosis. Both the sentence and its measurements are
    // admitted detail; neither belongs to the permanent workflow timeline.
    [Event(EvtRecordingTailSummary,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "tail_headline={0}")]
    public void RecordingTailSummary(string tail_headline)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtRecordingTailSummary, tail_headline);
    }

    [Event(EvtRecordingTailSummaryDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "tail_ms={0} | tail_dbfs={1:F1}")]
    public void RecordingTailSummaryDetail(int tail_ms, double tail_dbfs)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtRecordingTailSummaryDetail, tail_ms, tail_dbfs);
    }

    // ── Microphone open and start ───────────────────────────────────────

    [Event(EvtMicrophoneOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Cannot open the microphone\nmmsys_err={1} | mmsys_name={2} | device={4} | device_id={3} | purpose={0}")]
    public void MicrophoneOpenFailed(string purpose, uint mmsys_err, string mmsys_name, int device_id, string device_name)
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneOpenFailed, purpose, mmsys_err, mmsys_name, device_id, device_name);
    }

    // The device opened but could not be made to deliver: event creation
    // (win32_error), header preparation, buffer queueing or waveInStart
    // (mmsys_err). Before this event those codes were ignored and the take
    // ran silent.
    [Event(EvtMicrophoneStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Cannot start the microphone\nstage={1} | mmsys_err={2} | mmsys_name={3} | win32_error={4} | device={6} | device_id={5} | purpose={0}")]
    public void MicrophoneStartFailed(string purpose, string stage, uint mmsys_err, string mmsys_name, int win32_error, int device_id, string device_name)
    {
        if (IsEnabled())
            WriteEvent(EvtMicrophoneStartFailed, purpose, stage, mmsys_err, mmsys_name, win32_error, device_id, device_name);
    }

    // waveInReset, waveInStop, waveInUnprepareHeader or waveInClose refused at
    // the end of a take. A refused close leaves the device held by this
    // process, so a later open can fail with MMSYSERR_ALLOCATED. One incident
    // across takes: it opens on the first take that ends this way and closes
    // on the first clean one, which carries how many takes failed between.
    [Event(EvtCaptureReleaseFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The microphone was not released cleanly\nstage={0} | mmsys_err={1} | mmsys_name={2}")]
    public void CaptureReleaseFailed(string stage, uint mmsys_err, string mmsys_name)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureReleaseFailed, stage, mmsys_err, mmsys_name);
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
           Message = "stage={0} | mmsys_err={1} | failures={2}")]
    public void CaptureReleaseDetail(string stage, uint mmsys_err, int failures)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureReleaseDetail, stage, mmsys_err, failures);
    }

    // ── Anomalies Captured In The waveIn Loop ───────────────────────────

    // A device that opens and starts but hands back nothing: every waveIn call
    // succeeded, so no other event says the take is empty. reason is no_buffer
    // when no buffer came back at all, empty_buffers when buffers came back
    // with zero bytes.
    [Event(EvtNoAudioDataDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The microphone delivers no audio\nreason={0} | waited_ms={1} | device={3} | device_id={2}")]
    public void NoAudioDataDetected(string reason, long waited_ms, int device_id, string device_name)
    {
        if (IsEnabled()) WriteEvent(EvtNoAudioDataDetected, reason, waited_ms, device_id, device_name);
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

    [Event(EvtEmptyBufferReceived,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "An empty capture buffer was received")]
    public void EmptyBufferReceived()
    {
        if (IsEnabled()) WriteEvent(EvtEmptyBufferReceived);
    }

    [Event(EvtEmptyBufferReceivedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "index={0}")]
    public void EmptyBufferReceivedDetail(int index)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtEmptyBufferReceivedDetail, index);
    }

    [Event(EvtEmptyBufferRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "Capture buffers recovered")]
    public void EmptyBufferRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtEmptyBufferRecovered);
    }

    [Event(EvtLowAudioDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "No healthy voice was detected at the start of the recording")]
    public void LowAudioDetected()
    {
        if (IsEnabled()) WriteEvent(EvtLowAudioDetected);
    }

    [Event(EvtLowAudioDetectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "recording_ms={0} | min_sustained_ms={1} | dbfs_threshold={2}")]
    public void LowAudioDetectedDetail(int recording_ms, int min_sustained_ms, double dbfs_threshold)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtLowAudioDetectedDetail, recording_ms, min_sustained_ms, dbfs_threshold);
    }

    // buffers_ready is how many 50 ms buffers were waiting when the loop woke
    // (3 or more is the threshold) and behind_ms is the audio they hold, the
    // least the consumer had fallen behind by. The first lag of a recording
    // opens the incident; later ones only count in RecordingAnomaliesSummary.
    [Event(EvtCaptureLagDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The capture loop fell behind the microphone\nbuffers_ready={0} | behind_ms={1}")]
    public void CaptureLagDetected(int buffers_ready, int behind_ms)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLagDetected, buffers_ready, behind_ms);
    }

    // gc0/gc1/gc2 are the collections that happened DURING the lagging
    // iteration (the one whose body caused the pile-up), not a running total
    // since recording start — so a non-zero value indicts that GC directly.
    [Event(EvtCaptureLagDetectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "buffers_ready={0} | iter={1} | wait_ms={2} | prev_iter_ms={3} | gc0={4} | gc1={5} | gc2={6}")]
    public void CaptureLagDetectedDetail(int buffers_ready, long iter, long wait_ms, long prev_iter_ms, int gc0, int gc1, int gc2)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureLagDetectedDetail, buffers_ready, iter, wait_ms, prev_iter_ms, gc0, gc1, gc2);
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

    // One line per recording that met any of the three anomalies above, written
    // when the loop ends and whatever the verbosity: a recording that ends while
    // an anomaly is still open leaves the Warning in the log and no closer,
    // so this is the line that says how far it went. A recording with none of
    // them writes nothing. *_recovered is false when the anomaly was still open
    // at the end (and when its own count is 0); lag_max_buffers_ready is the
    // worst pile-up seen.
    [Event(EvtRecordingAnomaliesSummary,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "Recording anomalies")]
    public void RecordingAnomaliesSummary(
        int no_data_stalls, long no_data_longest_gap_ms, bool no_data_recovered,
        int empty_buffer_occurrences, bool empty_buffer_recovered,
        int lag_occurrences, bool lag_recovered, int lag_max_buffers_ready,
        int recording_ms, string work_id)
    {
        if (IsEnabled())
            WriteEvent(EvtRecordingAnomaliesSummary,
                no_data_stalls, no_data_longest_gap_ms, no_data_recovered,
                empty_buffer_occurrences, empty_buffer_recovered,
                lag_occurrences, lag_recovered, lag_max_buffers_ready,
                recording_ms, work_id);
    }

    [Event(EvtDurationCapReached,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Recording stopped at the maximum duration")]
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
           Message = "Cannot play on the speaker\nstage={0} | mmsys_err={1} | mmsys_name={2} | win32_error={3} | device={4} | sample_rate={5}")]
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
           Message = "Playback skipped\nreason={0}")]
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
           Message = "Could not decode the audio file\nreason={0} | hr=0x{1:X8} | elapsed_ms={2}")]
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
           Message = "Audio file decoding crashed\nex_type={0} | ex_message={1} | elapsed_ms={2}")]
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

    // The values a recording will read: the chosen device (-1 is the system
    // default), the duration cap (0 is none) and the opt-in DSP switches.
    // Emitted at load and after each change, at the rate a person edits them.
    [Event(EvtCaptureSettingsResolved,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "device_id={0} | max_recording_s={1} | preprocessing={2} | auto_calibration={3}")]
    public void CaptureSettingsResolved(int device_id, int max_recording_s, bool preprocessing, bool auto_calibration)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCaptureSettingsResolved, device_id, max_recording_s, preprocessing, auto_calibration);
    }
}
