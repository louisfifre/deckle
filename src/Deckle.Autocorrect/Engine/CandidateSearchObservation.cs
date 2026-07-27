namespace Deckle.Autocorrect;

// Internal measurement seam for the offline benchmark. Production constructs
// the corrector without an observer, so no per-candidate payload or allocation
// exists on the live path. The observation counts the strings the existing
// generate-and-test search actually creates, the distinct forms it looks up,
// and the valid surface forms it retains.
internal enum CandidateSearchPath
{
    Commit,
    Sentence,
}

internal readonly record struct CandidateSearchObservation(
    CandidateSearchPath Path,
    int EditDistance,
    int Generated,
    int DistinctLookups,
    int Matches);

internal delegate void CandidateSearchObserver(CandidateSearchObservation observation);
