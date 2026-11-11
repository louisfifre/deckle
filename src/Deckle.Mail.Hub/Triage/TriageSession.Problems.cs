namespace Deckle.Mail.Hub;

// What the problems button of the title bar lists: whatever does not work,
// each with its useful command. Empty when everything works, and the button
// is then absent.
public sealed partial class TriageSession
{
    private bool _anytypeNoticeDismissed;

    public IReadOnlyList<MailProblem> Problems
    {
        get
        {
            var problems = new List<MailProblem>();
            foreach (Mailbox mailbox in _mailboxes)
            {
                if (mailbox.Connection != MailboxConnection.CannotConnect) continue;
                int waiting = _rows.Count(r => r.IsTreated
                    && string.Equals(r.Subject.MailboxId, mailbox.Id, StringComparison.Ordinal));
                problems.Add(new MailProblem(MailProblemKind.MailboxCannotConnect, mailbox.Id, waiting));
            }
            // The notice that Anytype is not connected says why Add task is
            // absent, and can be dismissed by a person who does not want
            // Anytype (spec, Adding a task; Open 35 leaves its form open).
            if (!IsAnytypeConnected && !_anytypeNoticeDismissed)
                problems.Add(new MailProblem(MailProblemKind.AnytypeNotConnected, null, 0));
            return problems;
        }
    }

    public void DismissAnytypeNotice()
    {
        if (_anytypeNoticeDismissed) return;
        _anytypeNoticeDismissed = true;
        Raise(SessionChange.Problems);
    }
}

// MailboxId names the mailbox of a connection problem; WaitingGestures counts
// the gestures kept until it is back.
public sealed record MailProblem(MailProblemKind Kind, string? MailboxId, int WaitingGestures)
{
    public bool CanBeDismissed => Kind == MailProblemKind.AnytypeNotConnected;
}

public enum MailProblemKind
{
    MailboxCannotConnect,
    AnytypeNotConnected,
}
