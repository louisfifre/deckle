namespace Deckle.Anytype;

// One request in flight at a time against the local Anytype backend, whichever
// API version sends it. The backend's limiter (~1 rps sustained, burst of 60)
// counts every call of every client together, so two transports that each kept
// their own gate could still overlap and spend that budget twice over. The gate
// therefore belongs to the backend connection, not to a transport: AnytypeApiClient
// creates it and hands it to the v2 client it builds. A v2 client built alone
// (tests, a standalone gateway) makes its own.
//
// It paces by serializing, not by timing: there is no minimum interval between
// two requests. A transport that gets a 429 waits out Retry-After while still
// holding the gate, so the refusal pauses the whole stream instead of letting a
// sibling request burn the budget the backend just said was spent.
internal sealed class AnytypeRequestGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

    public void Release() => _semaphore.Release();

    public void Dispose() => _semaphore.Dispose();
}
