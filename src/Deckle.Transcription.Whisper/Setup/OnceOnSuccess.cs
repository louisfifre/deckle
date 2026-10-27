using System;

namespace Deckle.Transcription.Whisper;

// Runs an attempt until one succeeds, then never again. The flag is set from
// the attempt's answer, after it returns: set before, as it once was, a throw
// left the work marked done without having happened, and nothing in the
// process could retry it. Serialized, so two callers never run it at once.
internal sealed class OnceOnSuccess
{
    private readonly object _lock = new();
    private bool _done;

    public bool Done
    {
        get { lock (_lock) return _done; }
    }

    // attempt returns true when the work is done; false or a throw leaves it
    // to the next call. The exception reaches the caller unchanged.
    public void Run(Func<bool> attempt)
    {
        lock (_lock)
        {
            if (_done) return;
            _done = attempt();
        }
    }
}
