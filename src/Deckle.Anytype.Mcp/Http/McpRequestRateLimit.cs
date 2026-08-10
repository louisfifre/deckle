namespace Deckle.Anytype.Mcp;

// The authenticated request budget for one client surface. Tests can lower the
// budget through the internal host seam without changing the production limit.
internal sealed record McpRequestRateLimit
{
    public static McpRequestRateLimit Default { get; } =
        new(permitLimit: 60, window: TimeSpan.FromMinutes(1));

    public McpRequestRateLimit(int permitLimit, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));

        PermitLimit = permitLimit;
        Window = window;
    }

    public int PermitLimit { get; }

    public TimeSpan Window { get; }
}
