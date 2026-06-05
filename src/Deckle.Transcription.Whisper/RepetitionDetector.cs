namespace Deckle.Transcription.Whisper;

// ── RepetitionDetector ──────────────────────────────────────────────────────
//
// Guards against the Whisper hallucination loop: on a long audio with
// ambiguous trailing silence, the greedy decoder can enter a state where it
// emits the same segment forever. Observed 2026-04-18 — 84 identical
// segments on 237.7 s of audio with p̂ ≈ 0.99, so logprob_thold /
// entropy_thold never trip. We watch the segment stream ourselves and ask
// whisper.cpp to stop via abort_callback when a loop is recognised.
//
// Two loop shapes are guarded, both on a STRICT character-exact match (edge
// whitespace trimmed, case kept). A human never repeats a segment to the
// character, so a strict match is what keeps a legitimate refrain from
// tripping the guard — and the abort is non-destructive (it only stops the
// runaway decode, the segments produced so far are kept), so the thresholds
// are deliberately tight: catch early, false-positive near-zero.
//   • period-1  A A A …    — one segment repeating. Observed case above.
//                            Aborts at 3 identical in a row.
//   • period-2  A B A B …  — two segments alternating. Aborts on the first
//                            full strict repetition of the pair (A B A B).
//
// Lives in Deckle.Transcription.Whisper because the failure mode is specific
// to whisper.cpp's decoder behaviour — Voxtral and future backends will have
// their own characteristics and their own detectors if needed.
internal sealed class RepetitionDetector
{
    private readonly int _period1Threshold;
    private readonly int _period2Threshold;

    // A two-deep history of the last normalized segments, most recent in _prev.
    private string? _prev;
    private string? _prev2;
    private int _identicalStreak;     // period-1: identical segments in a row
    private int _alternationStreak;   // period-2: A-B-A-B alternation steps in a row

    // period1 = 3: three identical segments in a row (A A A).
    // period2 = 2: two alternation steps = one full strict A-B-A-B repetition.
    public RepetitionDetector(int period1Threshold = 3, int period2Threshold = 2)
    {
        _period1Threshold = period1Threshold;
        _period2Threshold = period2Threshold;
    }

    public void Reset()
    {
        _prev = null;
        _prev2 = null;
        _identicalStreak = 0;
        _alternationStreak = 0;
    }

    // Returns true the first time a loop is recognised. Caller is expected to
    // request whisper to abort and to log the trigger. Empty / whitespace-only
    // segments are ignored (common near silence, would create spurious streaks).
    // On a hit, `period` reports which shape tripped (1 or 2) and `streak` the
    // count that reached its threshold.
    public bool ObserveAndShouldAbort(string segmentText, out int streak, out int period)
    {
        // Strict, character-exact match: trim edge whitespace, keep case.
        string norm = segmentText.Trim();
        if (string.IsNullOrEmpty(norm))
        {
            streak = _identicalStreak;
            period = 1;
            return false;
        }

        // period-1 — N identical segments in a row (A A A).
        if (norm == _prev)
            _identicalStreak++;
        else
            _identicalStreak = 1;

        // period-2 — strict alternation A B A B: the incoming segment matches
        // two-back and differs from the one immediately before it.
        if (_prev2 is not null && norm == _prev2 && norm != _prev)
            _alternationStreak++;
        else
            _alternationStreak = 0;

        // Shift the two-deep history (most recent in _prev).
        _prev2 = _prev;
        _prev = norm;

        if (_identicalStreak >= _period1Threshold)
        {
            streak = _identicalStreak;
            period = 1;
            return true;
        }
        if (_alternationStreak >= _period2Threshold)
        {
            streak = _alternationStreak;
            period = 2;
            return true;
        }

        streak = _identicalStreak;
        period = 1;
        return false;
    }
}
