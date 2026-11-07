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
//
// The same pass checks that each event's parameters reach WriteEvent as the
// types they declare (WidenedBinding). That rule has no list: a breach is
// always fixed in the signature.
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

                string? widened = WidenedBinding(method);
                if (widened is not null)
                    failures.Add($"binding: {key} {widened}");
            }
        }

        foreach (string key in knownBreaches.Keys)
        {
            if (!seen.Contains(key))
                failures.Add($"stale: {key} matches no event, remove it from the list");
        }

        return failures;
    }

    // An event method passes its parameters straight to WriteEvent. When they
    // match none of its typed overloads exactly but a widening conversion makes
    // one fit, the compiler picks that one: (int, long) binds to (long, long),
    // (string, uint) to (string, long). The bytes written are then not the
    // types the event's metadata declares from the method. An in-process
    // listener reads a uint written that way as text; an out-of-process ETW
    // reader misplaces every field after a widened int. With no typed overload
    // in reach, the call takes the params overload, which writes each value as
    // its own type, so only the widened case is a breach. The fix is to declare
    // the wider type, so the signature matches the overload it binds to.
    //
    // Returns null when the binding is faithful, else the overload it widens to.
    public static string? WidenedBinding(MethodInfo method)
    {
        Type[] declared = method.GetParameters().Select(p => p.ParameterType).ToArray();
        Type[][] typed = TypedWriteEventOverloads.Value;

        if (typed.Any(o => o.SequenceEqual(declared))) return null;

        Type[]? widened = typed.FirstOrDefault(o =>
            o.Length == declared.Length
            && o.Zip(declared).All(pair => WidensTo(pair.Second, pair.First)));
        if (widened is null) return null;

        return $"({string.Join(", ", declared.Select(t => t.Name))}) binds to WriteEvent's "
             + $"({string.Join(", ", widened.Select(t => t.Name))}) overload; declare those types";
    }

    // The typed overloads read from EventSource itself, so a runtime that adds
    // one is followed without editing this list: every protected WriteEvent
    // after the event id, except the params array forms.
    private static readonly Lazy<Type[][]> TypedWriteEventOverloads = new(() =>
        typeof(EventSource)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(m => m.Name == "WriteEvent" && (m.IsFamily || m.IsFamilyOrAssembly))
            .Select(m => m.GetParameters())
            .Where(ps => ps.Length >= 1 && ps[0].ParameterType == typeof(int)
                      && !ps.Any(p => p.IsDefined(typeof(ParamArrayAttribute))))
            .Select(ps => ps.Skip(1).Select(p => p.ParameterType).ToArray())
            .ToArray());

    // C#'s implicit numeric conversions, the only ones that can carry a
    // primitive to another typed overload's parameter.
    private static bool WidensTo(Type from, Type to)
    {
        if (from == to) return true;
        if (to == typeof(int))
            return from == typeof(sbyte) || from == typeof(byte) || from == typeof(short)
                || from == typeof(ushort) || from == typeof(char);
        if (to == typeof(long))
            return from == typeof(sbyte) || from == typeof(byte) || from == typeof(short)
                || from == typeof(ushort) || from == typeof(char) || from == typeof(int)
                || from == typeof(uint);
        return false;
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
