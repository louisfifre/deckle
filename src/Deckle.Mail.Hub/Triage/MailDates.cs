using System.Globalization;

namespace Deckle.Mail.Hub;

// A date is written one way everywhere: the weekday and the day (« Fri 13 »),
// with the month when it is not the current one (« Tue 1 Dec »). The
// interface is in English, so the names are English whatever the system's
// culture.
public static class MailDates
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static string Day(DateOnly day, DateOnly today)
        => day.Month == today.Month && day.Year == today.Year
            ? day.ToString("ddd d", English)
            : day.ToString("ddd d MMM", English);

    // The time of a line in Activity, and of a mail's reception: 24-hour
    // clock, as the frames draw it.
    public static string Time(DateTimeOffset at) => at.ToString("HH:mm", English);

    // « Sat 7, 10:15 »: the reception of a mail on an opened subject.
    public static string DayAndTime(DateTimeOffset at, DateOnly today)
        => $"{Day(DateOnly.FromDateTime(at.DateTime), today)}, {Time(at)}";

    // The day heading of Activity: Today, Yesterday, then the day itself.
    public static DayHeading Heading(DateOnly day, DateOnly today)
        => day == today ? DayHeading.Today
            : day == today.AddDays(-1) ? DayHeading.Yesterday
            : DayHeading.Other;

    // « 96 KB »: the size of an attached file.
    public static string Size(long bytes)
        => bytes < 1024 ? $"{bytes} B"
            : bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} KB"
            : $"{bytes / (1024.0 * 1024.0):0.#} MB".Replace(',', '.');
}

public enum DayHeading
{
    Today,
    Yesterday,
    Other,
}
