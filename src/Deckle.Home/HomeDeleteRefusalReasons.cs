namespace Deckle.Home;

// Closed vocabulary of the `reason` field of DeckleHomeSource.DeleteRefused: why
// a move to the bin did not happen. It mirrors the Anytype delete's reasons that
// a Home object can meet; a Home object has no rapport.
internal static class HomeDeleteRefusalReasons
{
    // Another object references the target.
    public const string Referenced = "referenced";

    // The person answered no.
    public const string Declined = "declined";

    // The person did not answer inside the window.
    public const string Unanswered = "unanswered";

    // The notification that asks could not be shown.
    public const string ApprovalUnavailable = "approval_unavailable";
}