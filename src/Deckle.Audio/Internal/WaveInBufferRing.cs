namespace Deckle.Audio;

// The order in which a waveIn buffer ring hands its audio back.
//
// The driver fills the queued buffers strictly in the order they were queued
// (FIFO) and marks each WHDR_DONE as it returns. The ring is queued 0, 1, 2, 3
// and every processed buffer is requeued at the back, so the chronological
// order is cyclic and starts at the OLDEST OUTSTANDING buffer, not at index 0.
// Scanning by index is only right until the first wrap: with buffers 3 and 0
// both done at one wake, index order appends 0 (newer) before 3 (older) and
// swaps two 50 ms chunks in the PCM and in the frame stream. At the end of the
// take the partial buffer that waveInStop returns can sit at a lower index than
// the last completed one, so that is exactly where the swap would hit.
//
// The cursor is that oldest outstanding buffer. It only moves forward, one
// buffer per TakeNext, and the caller requeues what it takes in the same order
// so the invariant holds.
internal struct WaveInBufferRing
{
    private readonly int _count;
    private int _oldest;

    public WaveInBufferRing(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        _count = count;
        _oldest = 0;
    }

    // Index of the buffer the driver returns next.
    public readonly int Oldest => _oldest;

    // Takes the oldest outstanding buffer when it is done and moves the cursor
    // past it. It stops at the first buffer not done yet: FIFO means nothing
    // after it can be done either, so a later done flag is never read out of
    // order.
    public bool TakeNext(Func<int, bool> isDone, out int index)
    {
        index = _oldest;
        if (!isDone(index)) return false;

        _oldest = (_oldest + 1) % _count;
        return true;
    }
}
