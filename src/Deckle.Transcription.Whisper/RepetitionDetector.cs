namespace Deckle.Transcription.Whisper;

// Guards against the Whisper hallucination loop: on a long audio with
// ambiguous trailing silence, the greedy decoder can enter a state where it
// emits the same segment forever, at a confidence high enough that
// logprob_thold / entropy_thold never trip. We watch the segment stream
// ourselves and ask whisper.cpp to stop via abort_callback when a loop is
// recognised.
//
// Two loop shapes are guarded, both on a STRICT character-exact match (edge
// whitespace trimmed, case kept). A human never repeats a segment to the
// character, so a strict match is what keeps a legitimate refrain from
// tripping the guard — and the abort is non-destructive (it only stops the
// runaway decode, the segments produced so far are kept), so the thresholds
// are deliberately tight: catch early, false-positive near-zero.
//   • period-1  A A A …    — one segment repeating.
//   • period-2  A B A B …  — two segments alternating.
//
// Lives in Deckle.Transcription.Whisper because the failure mode is specific
// to whisper.cpp's decoder behaviour; another backend fails differently.
internal sealed class RepetitionDetector
{
    private readonly int _period1Threshold;
    private readonly int _period2Threshold;

    // Most recent normalized segment in _prev.
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

    // Returns true the first time a loop is recognised. Empty / whitespace-only
    // segments are ignored: common near silence, they would create spurious
    // streaks. On a hit, `period` reports which shape tripped (1 or 2) and
    // `streak` the count that reached its threshold.
    public bool ObserveAndShouldAbort(string segmentText, out int streak, out int period)
    {
        string norm = segmentText.Trim();
        if (string.IsNullOrEmpty(norm))
        {
            streak = _identicalStreak;
            period = 1;
            return false;
        }

        if (norm == _prev)
            _identicalStreak++;
        else
            _identicalStreak = 1;

        if (_prev2 is not null && norm == _prev2 && norm != _prev)
            _alternationStreak++;
        else
            _alternationStreak = 0;

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
