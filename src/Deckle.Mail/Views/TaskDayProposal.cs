namespace Deckle.Mail;

/// <summary>
/// The day Add task proposes. One fixed rule, without a setting: two days
/// before the deadline, and never before today. A deadline of today or
/// already past proposes today.
/// </summary>
public static class TaskDayProposal
{
    /// <summary>
    /// How many days before the deadline the task is proposed. Spec Open 6
    /// leaves two or three; two is the option the spec writes.
    /// </summary>
    public const int DaysBeforeDeadline = 2;

    /// <summary>The proposed day, or null when the mail states no deadline.</summary>
    public static DateOnly? For(Deadline? deadline, DateOnly today)
    {
        if (deadline is null)
        {
            return null;
        }

        var proposed = deadline.Day.AddDays(-DaysBeforeDeadline);
        return proposed < today ? today : proposed;
    }
}
