using Deckle.Anytype;

namespace Deckle.Home.Tests;

// Stands in for the Windows notification: answers with a fixed outcome, keeps what
// it was asked, and can run a step while the person "reads" the notification.
internal sealed class FakeDeletionApproval(
    DeletionApprovalOutcome outcome,
    Action? whileAsking = null,
    bool neverAnswersInTime = false) : IDeletionApproval
{
    public List<DeletionCandidate> Asked { get; } = [];

    public Task<DeletionApprovalOutcome> RequestAsync(DeletionCandidate candidate, CancellationToken ct)
    {
        Asked.Add(candidate);
        whileAsking?.Invoke();

        // The gesture's own window cancelling the wait, as the real toast sees it.
        if (neverAnswersInTime) throw new OperationCanceledException();
        return Task.FromResult(outcome);
    }
}