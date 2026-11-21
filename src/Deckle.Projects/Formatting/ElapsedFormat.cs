using System.Globalization;

namespace Deckle.Projects;

// The two ways a duration reads on a project's page: how long an action has
// been running ("1 min 12 s") and how long ago it ended ("4 min ago"). English
// literals, since Deckle ships English strings only, and pure, so a test can
// hold them to their boundaries; Deckle Folders keeps EntryFormat in its engine for
// the same reason.
public static class ElapsedFormat
{
    // "45 s", "1 min 12 s", "1 h 02 min". Seconds are dropped once an hour has
    // passed: nobody watches a build for its seconds after the first hour.
    public static string Running(TimeSpan elapsed)
    {
        long seconds = Math.Max(0, (long)elapsed.TotalSeconds);
        if (seconds < 60) return string.Create(CultureInfo.InvariantCulture, $"{seconds} s");

        long minutes = seconds / 60;
        if (minutes < 60) return string.Create(CultureInfo.InvariantCulture, $"{minutes} min {seconds % 60:00} s");

        return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} h {minutes % 60:00} min");
    }

    // "just now", "4 min ago", "2 h ago", "3 days ago".
    public static string Ago(TimeSpan elapsed)
    {
        long seconds = Math.Max(0, (long)elapsed.TotalSeconds);
        if (seconds < 60) return "just now";

        long minutes = seconds / 60;
        if (minutes < 60) return string.Create(CultureInfo.InvariantCulture, $"{minutes} min ago");

        long hours = minutes / 60;
        if (hours < 24) return string.Create(CultureInfo.InvariantCulture, $"{hours} h ago");

        long days = hours / 24;
        return string.Create(CultureInfo.InvariantCulture, $"{days} {(days == 1 ? "day" : "days")} ago");
    }
}
