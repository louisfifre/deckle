namespace Deckle.Anytype;

// The human's second word on a move to the bin, asked outside the chat. The
// agent can confirm a delete by itself; this asks the person directly, on a
// channel the host owns (a Windows toast in the resident app), so a bin never
// happens on the agent's say-so alone.
//
// A checkpoint, not a lock: it proves someone answered, and an agent that drives
// the desktop could answer it too. What it shows is what makes the answer
// informed, so it carries everything the bin would change, the links included.
public interface IDeletionApproval
{
    Task<DeletionApprovalOutcome> RequestAsync(DeletionCandidate candidate, CancellationToken ct);
}

// LinkCount is how many other objects link to the target, which the bin empties.
public sealed record DeletionCandidate(string Id, string Name, string TypeKey, int LinkCount);

// Only Approved lets the delete through. Each other outcome is its own refusal,
// so the agent can tell the person what to do: answer, look in the notification
// center, or bin it by hand in Anytype.
public enum DeletionApprovalOutcome
{
    Approved,
    Declined,
    Unanswered,
    Unavailable,
}

// The approval the MCP surfaces use when a gesture is built without one. The
// resident app installs its toast at startup; until then, and in any host that
// installs none, every request answers Unavailable, so a delete fails closed.
public static class DeletionApproval
{
    public static IDeletionApproval Current { get; set; } = new NoApproval();

    sealed class NoApproval : IDeletionApproval
    {
        public Task<DeletionApprovalOutcome> RequestAsync(DeletionCandidate candidate, CancellationToken ct) =>
            Task.FromResult(DeletionApprovalOutcome.Unavailable);
    }
}
