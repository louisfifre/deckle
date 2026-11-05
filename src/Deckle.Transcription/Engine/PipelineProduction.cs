namespace Deckle.Transcription;

// What a producer (monolithic, streaming or file) hands to FinalizeTranscription.
// RawAudio is the untouched capture; BackendAudio is what the backend actually
// received, equal to RawAudio when no stage transformed it. Timings are summed
// across utterances on the segmented paths.
//
// A producer returns null to mean "already handled, do not finalize": an early
// exit (mic error, empty audio, backend failure, lost CAS) it reported on its
// own. A dictation strategy also raises Finished itself on that path.
//
// BatchRewrite is set only by the streaming strategy when the take was
// rewritten batch by batch during the recording; the finalize then assembles
// those batches instead of rewriting the whole take. Null everywhere else.
//
// SpeechSec is set by the streaming strategy: the summed speech of the
// utterances that gave text, silences cut out. Elsewhere the whole take went
// to the backend, so its length is what was sent. SentAudioSec is the one
// measure the rewrite threshold reads, whatever the path.
internal readonly record struct PipelineProduction(
    string RawText,
    ReadOnlyMemory<float> RawAudio,
    ReadOnlyMemory<float> BackendAudio,
    long TotalTranscribeMs,
    long InitMs,
    int NSegments,
    TranscriptionEngine.BatchRewriteRun? BatchRewrite = null,
    double? SpeechSec = null)
{
    public double SentAudioSec => SpeechSec ?? BackendAudio.Length / 16_000.0;
}
