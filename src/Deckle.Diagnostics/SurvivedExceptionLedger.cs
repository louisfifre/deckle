using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace Deckle.Diagnostics;

// Where an exception that the process survived was caught. The words are what a
// person filters on, so they are spelled here and nowhere else.
public static class SurvivedExceptionOrigin
{
    // The UI framework's unhandled-exception net, which marked it handled.
    public const string UiThread = "ui_thread";

    // The task scheduler's net for a faulted task nobody awaited, which marked it observed.
    public const string UnobservedTask = "unobserved_task";
}

// What the ledger knows about one exception it was told of.
public readonly record struct SurvivedException(
    string Origin,
    string Type,
    string Site,
    string Message,
    int Occurrences)
{
    // The first time this exception type came out of this site from this origin:
    // the one that opens the incident.
    public bool OpensIncident => Occurrences == 1;
}

// Counts the exceptions a net caught and let the process go on from, so that a
// fault that fires on every frame or every tick is one incident in the journal and
// not one line each. An incident is an exception type thrown at one site and
// caught by one origin; the message is not part of it, because a message that
// carries a number or a name would make every occurrence look new.
//
// It only counts and describes; what to write for the first and for the repeats is
// the caller's, which owns the log provider.
public sealed class SurvivedExceptionLedger
{
    // Long enough for a sentence of cause, short enough that a message embedding a
    // payload cannot turn one line into a page.
    internal const int MaxMessageLength = 300;

    private readonly ConcurrentDictionary<string, int> _occurrences = new(StringComparer.Ordinal);

    public SurvivedException Note(string origin, Exception exception)
    {
        ArgumentException.ThrowIfNullOrEmpty(origin);
        ArgumentNullException.ThrowIfNull(exception);

        string type = exception.GetType().Name;
        string site = SiteOf(exception);
        int occurrences = _occurrences.AddOrUpdate(
            origin + "|" + type + "|" + site,
            static _ => 1,
            static (_, seen) => seen + 1);
        return new SurvivedException(origin, type, site, OneLine(exception.Message), occurrences);
    }

    // The method that threw, as Type.Method. The compiler turns an async method
    // into a state machine class and a lambda into a method of a helper class; both
    // are named back after the method the author wrote, so a site reads the same
    // as the code does.
    internal static string SiteOf(Exception exception)
    {
        MethodBase? method = exception.TargetSite;
        if (method is null) return "(unknown)";

        Type? type = method.DeclaringType;
        string name = AuthoredName(type?.Name) ?? AuthoredName(method.Name) ?? method.Name;
        while (type is not null && type.Name.StartsWith('<'))
            type = type.DeclaringType;

        return type is null ? name : type.Name + "." + name;
    }

    // "<LoadAsync>d__5" and "<Load>b__0_1" carry the author's name between the
    // angle brackets; "<>c" and plain names carry none.
    private static string? AuthoredName(string? generated)
    {
        if (generated is null || generated.Length < 3 || generated[0] != '<') return null;
        int end = generated.IndexOf('>');
        return end > 1 ? generated[1..end] : null;
    }

    // The cause on one line: a message can span several, and the journal reads one
    // event per line.
    internal static string OneLine(string message)
    {
        var line = new StringBuilder(Math.Min(message.Length, MaxMessageLength + 3));
        bool pendingSpace = false;
        foreach (char c in message)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                pendingSpace = line.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                line.Append(' ');
                pendingSpace = false;
            }

            if (line.Length >= MaxMessageLength)
            {
                line.Append("...");
                break;
            }

            line.Append(c);
        }

        return line.ToString();
    }
}
