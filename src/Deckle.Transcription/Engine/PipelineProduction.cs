namespace Deckle.Transcription;

// Result handed by a pipeline strategy (ProduceMonolithicAsync /
// ProduceStreamingAsync) to the shared FinalizeTranscription. Carries the
// assembled raw transcript plus everything the finalize step needs that only
// the strategy knows: the audio buffers for the corpus (ADR-0006 — RawAudio is
// the untouched capture, BackendAudio is what the backend actually received,
// equal to RawAudio when no DSP ran) and the backend timing roll-up
// (summed across utterances in the streaming case).
//
// A strategy returns null to mean "already handled, do not finalize" — an early
// exit (mic error, empty audio, backend failure, lost CAS) that the strategy
// reported on its own, raising Finished itself. A non-null value means "produce
// the user-facing output".
internal readonly record struct PipelineProduction(
    string RawText,
    ReadOnlyMemory<float> RawAudio,
    ReadOnlyMemory<float> BackendAudio,
    long TotalTranscribeMs,
    long InitMs,
    int NSegments);
