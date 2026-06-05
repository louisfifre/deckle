namespace Deckle.Hud;

// Aggregator for the HUD proximity session. Folds min/max alpha
// incrementally (no alpha buffer) and keeps a bounded distance ring buffer
// for percentile computation flushed at the end of the session.
//
// Why a ring instead of a truncated window. WM_INPUT at ~125 Hz during a
// typical ~10 s HUD session is ~1250 samples; capacity 2048 fully covers
// ~16 s, i.e. almost all real visibility sessions. For the rare longer
// sessions, percentiles reflect the last ~16 seconds: a still diagnostically
// representative window, unlike a truncated buffer that would freeze the
// samples from the beginning.
internal sealed class ProximityRollupAggregator
{
    internal const int Capacity = 2048;

    private readonly int[] _distances = new int[Capacity];
    private int _writeIdx;
    private int _totalSamples;
    private byte _minAlpha;
    private byte _maxAlpha;

    public ProximityRollupAggregator() { Reset(); }

    public int TotalSamples => _totalSamples;
    public byte MinAlpha => _minAlpha;
    public byte MaxAlpha => _maxAlpha;

    public void Reset()
    {
        _writeIdx = 0;
        _totalSamples = 0;
        _minAlpha = 255;
        _maxAlpha = 0;
    }

    public void Add(int distanceDip, byte alpha)
    {
        _distances[_writeIdx] = distanceDip;
        _writeIdx = (_writeIdx + 1) % Capacity;
        _totalSamples++;
        if (alpha < _minAlpha) _minAlpha = alpha;
        if (alpha > _maxAlpha) _maxAlpha = alpha;
    }

    // Returns (p50, p95) in DIPs over the min(TotalSamples, Capacity) latest
    // collected samples. Called only once at the end of the session; the copy
    // and sort allocation is acceptable at this cadence (once per HUD show,
    // not per WM_INPUT). Throws InvalidOperationException if no sample was
    // added; the caller must gate on TotalSamples > 0 first.
    public (int P50, int P95) ComputePercentiles()
    {
        if (_totalSamples == 0)
            throw new InvalidOperationException("ComputePercentiles requires at least one sample.");

        int count = Math.Min(_totalSamples, Capacity);
        var snap = new int[count];
        Array.Copy(_distances, snap, count);
        Array.Sort(snap);
        int p50 = snap[count / 2];
        int p95Idx = (int)Math.Min(count - 1, Math.Floor(count * 0.95));
        return (p50, snap[p95Idx]);
    }
}
