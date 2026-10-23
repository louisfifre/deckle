namespace Deckle.Anytype;

// Closed vocabulary of the `reason` field of DeleteRefused: why a move to the bin
// did not happen.
internal static class DeleteRefusalReasons
{
    // What links to the target could not be read, so nothing is let through.
    public const string ReferencesUnreadable = "references_unreadable";

    // The count of what links to the target moved while the toast waited.
    public const string LinksChanged = "links_changed";

    // The person answered no.
    public const string Declined = "declined";

    // The person did not answer inside the window.
    public const string Unanswered = "unanswered";

    // The notification that asks could not be shown.
    public const string ApprovalUnavailable = "approval_unavailable";
}

// Closed vocabulary of the `cause` field of DeleteRefused when the references
// could not be read and the failure carries no error code.
internal static class DeleteRefusalCauses
{
    // The search failed with something that is not an API refusal.
    public const string SearchFailed = "search_failed";

    // The search answered but listed no row for the object.
    public const string NotListed = "not_listed";
}