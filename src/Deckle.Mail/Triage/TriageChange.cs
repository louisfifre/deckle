namespace Deckle.Mail;

/// <summary>
/// The outcome of one transition: the new state, and the intents to send now,
/// in order. Intents for an unreachable target are not here: the state holds
/// them until <see cref="TriageState.TargetReachable"/> releases them.
/// </summary>
public sealed record TriageChange(TriageState State, IReadOnlyList<MailIntent> Intents)
{
    /// <summary>True when the transition did nothing: a click in the guarded instant, or a gesture the row does not offer.</summary>
    public bool IsUnchanged { get; private init; }

    internal static TriageChange Unchanged(TriageState state) => new(state, []) { IsUnchanged = true };
}
