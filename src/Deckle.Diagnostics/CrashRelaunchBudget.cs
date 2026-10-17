namespace Deckle.Diagnostics;

// The relaunch allowance of one launcher: CrashRelaunchPolicy.MaxRelaunches
// inside CrashRelaunchPolicy.RelaunchWindow. The clock is a parameter so the
// budget is testable without waiting ten minutes.
public sealed class CrashRelaunchBudget
{
    private readonly Queue<DateTimeOffset> _spent = new();

    // Relaunches granted so far, including those that left the window.
    public int Granted { get; private set; }

    // Spends one relaunch at `now` and returns true, or returns false when
    // the window already holds the maximum.
    public bool TrySpend(DateTimeOffset now)
    {
        while (_spent.Count > 0 && now - _spent.Peek() >= CrashRelaunchPolicy.RelaunchWindow)
            _spent.Dequeue();

        if (_spent.Count >= CrashRelaunchPolicy.MaxRelaunches) return false;

        _spent.Enqueue(now);
        Granted++;
        return true;
    }
}
