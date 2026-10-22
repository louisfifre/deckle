namespace Deckle.Input.PrecisionScroll;

// What PrecisionScrollRuntime needs from the engine: start it, retune it, dispose
// it, and hear when its worker ends without anyone asking. The runtime is tested
// against a fake of this, because the real engine creates a synthetic touchpad
// device that a test must not leave on the machine.
internal interface IPrecisionScrollEngine : IDisposable
{
    bool Start();

    void SetTuning(PrecisionScrollTuning tuning);

    // Raised on the worker thread, once it has finished, when it ended by itself
    // (queue_full, injection_failed, worker_faulted) and not because of a Stop.
    // A subscriber must not wait on the engine from inside the handler: the
    // worker is the thread that is calling it.
    event Action<string>? EndedOnItsOwn;
}
