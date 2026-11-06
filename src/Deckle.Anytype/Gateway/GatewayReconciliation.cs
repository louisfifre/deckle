namespace Deckle.Anytype;

internal enum GatewayAction
{
    // The listener is the gateway this Deckle carries: keep it, no gap.
    Adopt,
    // The listener is a gateway of another build: drain it, start this one.
    Replace,
    // Nothing listens: start this build.
    Spawn,
    // Something else holds the port, or the owner cannot be read.
    Conflict,
}

internal sealed record GatewayDecision(GatewayAction Action, int ProcessId = 0, string Reason = "");

// Who owns the gateway port, and what the supervisor does about it. Identity is
// the owner's image path, as for the backend (ADR-0004): a process listening on
// the port is a gateway only if its image is a staged gateway executable.
internal static class GatewayReconciliation
{
    public static GatewayDecision Decide(
        BackendListenerSnapshot listener,
        string? ownerPath,
        string expectedExecutable,
        string versionsRoot)
    {
        switch (listener.State)
        {
            case BackendListenerState.Unbound:
                return new(GatewayAction.Spawn);
            case BackendListenerState.Ambiguous:
                return new(GatewayAction.Conflict, Reason: "ambiguous_owner");
            case BackendListenerState.Failed:
                return new(GatewayAction.Conflict, Reason: "owner_unreadable");
        }

        if (ownerPath is null)
            return new(GatewayAction.Conflict, listener.ProcessId, "owner_unreadable");
        if (string.Equals(Path.GetFullPath(ownerPath), Path.GetFullPath(expectedExecutable), StringComparison.OrdinalIgnoreCase))
            return new(GatewayAction.Adopt, listener.ProcessId);
        if (GatewayPayload.IsStagedGateway(ownerPath, versionsRoot))
            return new(GatewayAction.Replace, listener.ProcessId);
        return new(GatewayAction.Conflict, listener.ProcessId, "foreign_owner");
    }
}
