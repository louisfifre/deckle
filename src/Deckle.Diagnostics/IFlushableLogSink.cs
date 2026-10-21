namespace Deckle.Diagnostics;

// A passive sink whose side effects continue away from the emitter thread.
// Flush is the clean-shutdown contract: when it returns, every entry accepted
// before the call has reached its destination, in order. TryFlush is the same
// drain with a deadline, for the callers that cannot wait on a stuck disk (a
// crash handler, ProcessExit): false means the deadline passed with entries
// still pending, and a destination error still throws. Dispose closes the
// sink after performing the same deterministic drain.
public interface IFlushableLogSink : ILogSink, IDisposable
{
    void Flush();

    bool TryFlush(TimeSpan timeout);
}
