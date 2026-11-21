namespace Deckle.Contributions;

// A composition the host refuses to start with. Thrown at registration, before
// any window exists, so a wrong list fails the first start and never a later
// click.
public sealed class ContributionRegistrationException(string reason, string id)
    : InvalidOperationException($"Contribution '{id}' refused: {reason}.")
{
    // A value of DeckleContributionsSource.RegistrationRefusals.
    public string Reason { get; } = reason;

    public string Id { get; } = id;
}
