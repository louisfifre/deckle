namespace Deckle.Mail;

/// <summary>The durations the rules of Triage rest on, gathered so a decision changes one line.</summary>
public sealed record TriageOptions
{
    /// <summary>
    /// The instant after a gesture during which a click on the same row does
    /// nothing, so a double click on Add task never undoes the task it just
    /// created. The spec says « the instant » without a value; 500 ms is the
    /// Windows default double-click time, the case this guard exists for.
    /// </summary>
    public TimeSpan ClickGuard { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long a treated row stays greyed before it leaves by itself: « a long
    /// fixed time ». Not settled (spec Open 7: between one and four hours); one
    /// hour, the low end, until the maintainer chooses.
    /// </summary>
    public TimeSpan TreatedRowLifetime { get; init; } = TimeSpan.FromHours(1);
}
