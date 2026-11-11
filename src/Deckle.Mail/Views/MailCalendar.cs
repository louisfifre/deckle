namespace Deckle.Mail;

/// <summary>
/// The day a moment falls on, as the person lived it. Every rule takes the
/// present as an argument instead of reading a clock.
/// </summary>
internal static class MailCalendar
{
    public static DateOnly Today(DateTimeOffset now) => DayOf(now);

    // A moment's day is read in the offset it was recorded with. Converting it
    // to today's offset would move a gesture made at 00:30 in summer time to
    // the day before once the clocks go back.
    public static DateOnly DayOf(DateTimeOffset moment) => DateOnly.FromDateTime(moment.DateTime);
}
