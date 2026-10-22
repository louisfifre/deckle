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

    // The body's {0} is the object's name, {1} its type key.
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

    public static IReadOnlyList<NotificationDescriptor> All { get; } = new[] { DeleteApproval };
}

// The resident app's second validation for a move to the bin: a toast the
// person answers outside the agent's conversation. Only the bin button
// approves; a click on the toast body opens nothing and decides nothing, so it
// reads as no answer.
public sealed class ToastDeletionApproval : IDeletionApproval
{
    public async Task<DeletionApprovalOutcome> RequestAsync(DeletionCandidate candidate, CancellationToken ct)
    {
        // Asked first because PromptAsync answers null both when nothing could
        // be shown (toasts off, elevated Deckle) and when nobody answered, and
        // the agent tells the person something different in each case.
        if (NotificationDispatcher.Instance is not { } dispatcher
            || !dispatcher.CanPrompt(AnytypeNotifications.DeleteApproval))
            return DeletionApprovalOutcome.Unavailable;

        NotificationResponse? response = await dispatcher.PromptAsync(
            AnytypeNotifications.DeleteApproval, new object?[] { candidate.Name, candidate.TypeKey }, ct)
            .ConfigureAwait(false);

        return response?.ActionId switch
        {
            AnytypeNotifications.BinAction => DeletionApprovalOutcome.Approved,
            AnytypeNotifications.KeepAction => DeletionApprovalOutcome.Declined,
            _ => DeletionApprovalOutcome.Unanswered,
        };
    }
}
