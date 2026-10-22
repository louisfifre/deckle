using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Anytype;
using Deckle.Notifications;

namespace Deckle.App;

// Lives in the App so Deckle.Anytype stays free of any Notifications
// dependency: it only asks IDeletionApproval. The descriptor Id is a stable
// public contract (point.snake_case, never renamed or recycled). The strings are
// .resw keys resolved through Loc, kept in Deckle.App's Resources.resw.
public static class AnytypeNotifications
{
    // Echoed back in NotificationResponse.ActionId.
    public const string BinAction = "bin";
    public const string KeepAction = "keep";

    // The body's {0} is the object's name, {1} its type key. For an object that
    // nothing links to; see DeleteApprovalLinked for the one that has links.
    public static readonly NotificationDescriptor DeleteApproval = new(
        Id: "anytype.delete_approval",
        Category: "anytype",
        TitleKey: "AnytypeNotifications_DeleteApproval_Title",
        BodyKey: "AnytypeNotifications_DeleteApproval_Body",
        Severity: NotificationSeverity.Warning,
        Channel: NotificationChannel.Toast,
        Actions: new[]
        {
            new NotificationAction(BinAction, "AnytypeNotifications_DeleteApproval_Bin"),
            new NotificationAction(KeepAction, "AnytypeNotifications_DeleteApproval_Keep"),
        });

    // The same ask for an object other objects link to: the body adds {2}, how
    // many, so the person sees on the toast that the bin empties those links. A
    // second descriptor rather than a pre-built sentence, so the wording stays a
    // .resw string.
    public static readonly NotificationDescriptor DeleteApprovalLinked = new(
        Id: "anytype.delete_approval_linked",
        Category: "anytype",
        TitleKey: "AnytypeNotifications_DeleteApproval_Title",
        BodyKey: "AnytypeNotifications_DeleteApprovalLinked_Body",
        Severity: NotificationSeverity.Warning,
        Channel: NotificationChannel.Toast,
        Actions: DeleteApproval.Actions);

    public static IReadOnlyList<NotificationDescriptor> All { get; } = new[] { DeleteApproval, DeleteApprovalLinked };
}

// The resident app's second validation for a move to the bin: a toast the
// person answers outside the agent's conversation. Only the bin button
// approves; a click on the toast body opens nothing and decides nothing, so it
// reads as no answer.
public sealed class ToastDeletionApproval : IDeletionApproval
{
    public async Task<DeletionApprovalOutcome> RequestAsync(DeletionCandidate candidate, CancellationToken ct)
    {
        bool linked = candidate.LinkCount > 0;
        NotificationDescriptor descriptor = linked
            ? AnytypeNotifications.DeleteApprovalLinked
            : AnytypeNotifications.DeleteApproval;

        // Asked first because PromptAsync answers null both when nothing could
        // be shown (toasts off, elevated Deckle) and when nobody answered, and
        // the agent tells the person something different in each case.
        if (NotificationDispatcher.Instance is not { } dispatcher
            || !dispatcher.CanPrompt(descriptor))
            return DeletionApprovalOutcome.Unavailable;

        object?[] bodyArgs = linked
            ? new object?[] { candidate.Name, candidate.TypeKey, candidate.LinkCount }
            : new object?[] { candidate.Name, candidate.TypeKey };

        NotificationResponse? response = await dispatcher.PromptAsync(descriptor, bodyArgs, ct)
            .ConfigureAwait(false);

        return response?.ActionId switch
        {
            AnytypeNotifications.BinAction => DeletionApprovalOutcome.Approved,
            AnytypeNotifications.KeepAction => DeletionApprovalOutcome.Declined,
            _ => DeletionApprovalOutcome.Unanswered,
        };
    }
}
