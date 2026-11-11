using System.Globalization;
using System.Resources;

namespace Deckle.Mail;

/// <summary>
/// The English words of Deckle Mail that the core composes: dates, the result
/// of a gesture, the framed labels, the summary of the day. Each word lives
/// under a key of Strings/MailStrings.resx; this class only assembles them.
/// </summary>
public static class MailWording
{
    private static readonly ResourceManager s_strings =
        new("Deckle.Mail.Strings.MailStrings", typeof(MailWording).Assembly);

    // The interface is in English whatever the language of the machine, so
    // dates take their weekday and month names from the invariant culture,
    // whose names are the English ones, and never from the current culture.
    private static readonly CultureInfo s_english = CultureInfo.InvariantCulture;

    public static string Inbox => Text("Inbox");

    public static string NothingWaiting => Text("NothingWaiting");

    public static string SenderNotVerified => Text("SenderNotVerified");

    public static string BackInTriage => Text("BackInTriage");

    /// <summary>
    /// A date, written one way everywhere: the weekday and the day (« Fri 13 »),
    /// with the month when it is not the current one (« Tue 1 Dec »). A day of
    /// the current month in another year also shows its month; no date shows a
    /// year, which the spec never writes.
    /// </summary>
    public static string Day(DateOnly day, DateOnly today)
    {
        var sameMonth = day.Year == today.Year && day.Month == today.Month;
        return day.ToString(Text(sameMonth ? "DayFormat" : "DayWithMonthFormat"), s_english);
    }

    /// <summary>What a treated row or a line of Activity says happened: « Task for Fri 13 », « Task for today », « Moved to trash ».</summary>
    public static string Result(TreatmentResult result, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Kind switch
        {
            TreatmentKind.Task when result.TaskDay is null => Text("TaskWithoutDay"),
            TreatmentKind.Task when result.TaskDay == today => Text("TaskForToday"),
            TreatmentKind.Task => Format("TaskForDay", Day(result.TaskDay!.Value, today)),
            TreatmentKind.Trash => Text("MovedToTrash"),
            TreatmentKind.Dismissal => Text("Dismissed"),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    /// <summary>The label of the task control: « Add task », or « Add task · Fri 13 » with the proposed day.</summary>
    public static string AddTask(DateOnly? proposedDay, DateOnly today) =>
        proposedDay is { } day ? Format("AddTaskForDay", Day(day, today)) : Text("AddTask");

    /// <summary>The framed label of a mail the sorting could not read.</summary>
    public static string Label(UnreadableReason reason) => reason switch
    {
        UnreadableReason.ImagesOnly => Text("UnreadableImagesOnly"),
        UnreadableReason.Encrypted => Text("UnreadableEncrypted"),
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>The framed word of a line of the sorting's part of Activity.</summary>
    public static string Label(SetAsideReason reason) => reason switch
    {
        SetAsideReason.Newsletter => Text("ReasonNewsletter"),
        SetAsideReason.Promotion => Text("ReasonPromotion"),
        SetAsideReason.Receipt => Text("ReasonReceipt"),
        SetAsideReason.NoActionNeeded => Text("ReasonNoActionNeeded"),
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>
    /// The summary of the day: « Today · 2 tasks · 2 moved to trash · 3 dismissed ».
    /// A count of zero leaves its part out, so the summary names only what happened.
    /// </summary>
    public static string Summary(DaySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var parts = new List<string> { Text("SummaryToday") };
        if (summary.Tasks > 0)
        {
            parts.Add(Format(summary.Tasks == 1 ? "SummaryTaskOne" : "SummaryTaskMany", summary.Tasks));
        }

        if (summary.MovedToTrash > 0)
        {
            parts.Add(Format("SummaryMovedToTrash", summary.MovedToTrash));
        }

        if (summary.Dismissed > 0)
        {
            parts.Add(Format("SummaryDismissed", summary.Dismissed));
        }

        return string.Join(Text("SummarySeparator"), parts);
    }

    private static string Text(string key) =>
        s_strings.GetString(key, s_english)
        ?? throw new MissingManifestResourceException($"Deckle Mail has no text under the key '{key}'.");

    private static string Format(string key, object argument) =>
        string.Format(s_english, Text(key), argument);
}
