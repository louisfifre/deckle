using System.Globalization;
using Deckle.Catalog;

namespace Deckle.Mail.Hub;

// The window's words read from code, from the module's own Resources.resw
// (Loc.GetFrom: no mirror in Deckle.App is needed). Every wording the spec
// leaves open (Open 12, 23, 59) is a key of that file, so the maintainer
// changes it there and nowhere else.
internal static class MailText
{
    private const string Library = "Deckle.Mail.Hub";

    public static string Get(string key) => Loc.GetFrom(Library, key);

    public static string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    // « Add task », « Add task · today », « Add task · Fri 13 » (Open 12: the
    // proposal on a row, 19B).
    public static string AddTask(DateOnly? proposed, DateOnly today)
        => proposed is not { } day ? Get("Task_Add")
            : day == today ? Format("Task_AddFor_Format", Get("Task_Today"))
            : Format("Task_AddFor_Format", NoBreak(MailDates.Day(day, today)));

    // What happened, in words: « Task for today », « Task for Fri 13 »,
    // « Moved to trash ». A task with no day reads « Task, no day » (Open 23,
    // frame 28U).
    public static string Result(GestureResult result, DateOnly today)
        => result.Kind == GestureKind.Trash ? Get("Result_MovedToTrash")
            : result.TaskDay is not { } day ? Get("Result_TaskNoDay")
            : day == today ? Format("Result_TaskFor_Format", Get("Task_Today"))
            : Format("Result_TaskFor_Format", NoBreak(MailDates.Day(day, today)));

    public static string Reason(SetAsideReason reason) => reason switch
    {
        SetAsideReason.Newsletter => Get("Reason_Newsletter"),
        SetAsideReason.Promotion => Get("Reason_Promotion"),
        SetAsideReason.Receipt => Get("Reason_Receipt"),
        _ => Get("Reason_NoActionNeeded"),
    };

    public static string Mark(SubjectMark mark) => mark switch
    {
        SubjectMark.Unreadable => Get("Mark_Unreadable"),
        SubjectMark.SenderNotVerified => Get("Mark_SenderNotVerified"),
        _ => "",
    };

    public static string Heading(DateOnly day, DateOnly today) => MailDates.Heading(day, today) switch
    {
        DayHeading.Today => Get("Day_Today"),
        DayHeading.Yesterday => Get("Day_Yesterday"),
        _ => MailDates.Day(day, today),
    };

    // « Sat 7, 10:15 »: the reception of a mail on an opened subject. The
    // separator is the resource's, like every word of the window.
    public static string DayAndTime(DateTimeOffset at, DateOnly today)
        => Format("Date_DayAndTime_Format", MailDates.Day(DateOnly.FromDateTime(at.DateTime), today), MailDates.Time(at));

    // « 96 KB »: the size of an attached file, its unit from the resources.
    // The number is written in English, as every date and number of the window.
    public static string Size(long bytes)
        => bytes < 1024 ? Format("Size_Bytes_Format", bytes)
            : bytes < 1024 * 1024 ? Format("Size_Kilobytes_Format", Math.Max(1, bytes / 1024))
            : Format("Size_Megabytes_Format", (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.GetCultureInfo("en-US")));

    // A day never breaks across two lines: « Fri 13 » stays whole in a
    // narrow column.
    public static string NoBreak(string text) => text.Replace(' ', ' ');
}
