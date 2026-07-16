namespace Deckle.Diagnostics;

// A passive sink whose side effects continue away from the emitter thread.
// Flush is the clean-shutdown contract: when it returns, every entry accepted
// before the call has reached its destination, in order. Dispose closes the
// sink after performing the same deterministic drain.
public interface IFlushableLogSink : ILogSink, IDisposable
{
    void Flush();
}
