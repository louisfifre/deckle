namespace Deckle.Lighting.Ambient;

internal sealed class AmbientHeartbeatWindow
{
    private long _startedAt;

    public bool IsActive { get; private set; }

    public bool StartIfNeeded(long timestamp)
    {
        if (IsActive) return false;

        IsActive = true;
        _startedAt = timestamp;
        return true;
    }

    public void Stop()
        => IsActive = false;

    public void Restart(long timestamp)
    {
        if (!IsActive)
            throw new InvalidOperationException("Cannot restart an inactive heartbeat window.");

        _startedAt = timestamp;
    }

    public long ElapsedTicks(long timestamp)
        => timestamp - _startedAt;
}
