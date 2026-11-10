namespace Deckle.Mail;

/// <summary>
/// A deadline the text of a mail states: a day, and a time when the mail gives one.
/// It is kept as data, never written apart from the sentence: it orders the
/// list and proposes the day of the task. The date a mail arrived is never one.
/// </summary>
public sealed record Deadline(DateOnly Day, TimeOnly? Time = null) : IComparable<Deadline>
{
    /// <summary>
    /// Earlier first. On the same day, a deadline with a time comes before a
    /// deadline of the whole day, which runs until the day ends.
    /// </summary>
    public int CompareTo(Deadline? other)
    {
        if (other is null)
        {
            return -1;
        }

        var byDay = Day.CompareTo(other.Day);
        if (byDay != 0)
        {
            return byDay;
        }

        return (Time, other.Time) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            ({ } mine, { } theirs) => mine.CompareTo(theirs),
        };
    }
}
