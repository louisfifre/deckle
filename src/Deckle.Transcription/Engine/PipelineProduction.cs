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
internal readonly record struct PipelineProduction(
    string RawText,
    ReadOnlyMemory<float> RawAudio,
    ReadOnlyMemory<float> BackendAudio,
    long TotalTranscribeMs,
    long InitMs,
    int NSegments,
    TranscriptionEngine.BatchRewriteRun? BatchRewrite = null);
