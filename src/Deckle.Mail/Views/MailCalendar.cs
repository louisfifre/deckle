namespace Deckle.Mail;

/// <summary>
/// The day a moment falls on, as the person lives it. Every rule takes the
/// present as an argument and reads the day in its offset, so a mail received
/// at 23:30 in another time zone falls on the person's day, not the sender's.
/// </summary>
internal static class MailCalendar
{
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.DateTime);

    public static DateOnly DayOf(DateTimeOffset moment, DateTimeOffset now) =>
        DateOnly.FromDateTime(moment.ToOffset(now.Offset).DateTime);
}
