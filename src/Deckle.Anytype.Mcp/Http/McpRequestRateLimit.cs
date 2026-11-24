namespace Deckle.Anytype.Mcp;

// The authenticated request budget for one client surface. Tests can lower the
// budget through the internal host seam without changing the production limit.
//
// One client token is shared by every agent of that client, so several agents
// calling together spend one budget. Over budget, a request waits in a bounded
// queue for its turn instead of being refused: the budget refills smoothly,
// one permit every Window / PermitLimit, so the longest wait is about
// QueueLimit of those intervals (30 s by default). Only a request that finds
// the queue full is refused, with a Retry-After: a runaway client still cannot
// make the gateway run more than PermitLimit calls per Window after its first
// burst, nor keep more than QueueLimit requests waiting.
internal sealed record McpRequestRateLimit
{
    public static McpRequestRateLimit Default { get; } =
        new(permitLimit: 60, window: TimeSpan.FromMinutes(1), queueLimit: 30);

    public McpRequestRateLimit(int permitLimit, TimeSpan window, int queueLimit = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(queueLimit);
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));

        PermitLimit = permitLimit;
        Window = window;
        QueueLimit = queueLimit;
    }

    // The burst a client may spend at once, and its sustained rate per Window.
    public int PermitLimit { get; }

    public TimeSpan Window { get; }

    // Requests that wait for a permit before any is refused.
    public int QueueLimit { get; }

    // One permit comes back every interval: a waiter is served as soon as one
    // permit returns, not at the end of a whole window.
    public TimeSpan ReplenishmentInterval => Window / PermitLimit;
}
