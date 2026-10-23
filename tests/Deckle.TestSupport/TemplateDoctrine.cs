using System.Diagnostics.Tracing;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Deckle.TestSupport;

// ── TemplateDoctrine ──────────────────────────────────────────────────────────
//
// Checks the shape of every [Event] Message against the level it is written at,
// so that a line reads the same way whichever provider wrote it:
//
//   Informational   a sentence alone, with no "|" and no "name={n}" pair
//   Verbose         "name={n}" pairs only, separated by " | ", no sentence
//   Warning, Error, a sentence, then a newline and the pairs; the sentence
//   Critical        alone when the event carries no payload
//
// The values travel in the payload whatever the template says, so an
// Informational sentence loses nothing by showing none of them.
//
// A template that cannot follow the doctrine is listed by its suite with the
// reason it cannot, keyed "SourceType.EventName". The check fails on a template
// that breaks the doctrine and is not listed, on a listed one that now follows it
// (the entry has gone stale), and on a listed name that matches no event.
public static partial class TemplateDoctrine
{
    // What an unlisted breach asks for when it is reported as a line for the
    // list: the reason is written by hand, or the template is reshaped.
    public const string ReasonToWrite = "why this template cannot follow the doctrine";

    public static string? Breach(EventLevel level, string? message, int payloadCount)
    {
        if (message is null) return "no Message";

        switch (level)
        {
            case EventLevel.Informational:
                return IsSentence(message) ? null : "an Informational template is a sentence alone";

            case EventLevel.Verbose:
                if (payloadCount == 0) return "a Verbose event without a payload has no pair to show";
                return IsPairs(message) ? null : "a Verbose template is name={n} pairs only";

            default:
                if (payloadCount == 0)
                {
                    return IsSentence(message)
                        ? null
                        : "a Warning or Error template without a payload is a sentence alone";
                }

                int newline = message.IndexOf('\n');
                bool shaped = newline > 0
                    && IsSentence(message[..newline])
                    && IsPairs(message[(newline + 1)..]);
                return shaped ? null : "a Warning or Error template is a sentence, a newline, then name={n} pairs";
        }
    }

    // The failures of one suite's providers against its list of known breaches.
    // Each unlisted breach comes back as a line ready to paste into that list.
    public static IReadOnlyList<string> Violations(
        IEnumerable<Type> sourceTypes, IReadOnlyDictionary<string, string> knownBreaches)
    {
        var failures = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Type sourceType in sourceTypes)
        {
            foreach (MethodInfo method in sourceType.GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                EventAttribute? attribute = method.GetCustomAttribute<EventAttribute>();
                if (attribute is null) continue;

                string key = $"{sourceType.Name}.{method.Name}";
                seen.Add(key);
                int payloadCount = method.GetParameters().Length;
                string? breach = Breach(attribute.Level, attribute.Message, payloadCount);
                bool listed = knownBreaches.ContainsKey(key);

                if (breach is not null && !listed)
                    failures.Add($"{{ \"{key}\", \"{ReasonToWrite}\" }}, // {attribute.Level}: {breach}");
                else if (breach is null && listed)
                {
                    failures.Add($"stale: {key} now follows the doctrine, remove it from the list");
                }
            }
        }

        foreach (string key in knownBreaches.Keys)
        {
            if (!seen.Contains(key))
                failures.Add($"stale: {key} matches no event, remove it from the list");
        }

        return failures;
    }

    // A sentence: some words, with no pair separator, no line break and no
    // placeholder.
    private static bool IsSentence(string text)
        => text.Any(char.IsLetter)
            && !text.Contains('|')
            && !text.Contains('\n')
            && !Placeholder().IsMatch(text);

    // One or more name={n} pairs separated by " | ". A value is one token
    // without spaces that holds a placeholder, so a unit may follow it
    // ("{0}ms") but a literal value may not stand in for one.
    private static bool IsPairs(string text)
        => text.Split(" | ").All(pair => Pair().IsMatch(pair));

    [GeneratedRegex(@"\{\d+")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*=[^\s|]*\{\d+(,[^}]*)?(:[^}]*)?\}[^\s|]*$")]
    private static partial Regex Pair();
}
