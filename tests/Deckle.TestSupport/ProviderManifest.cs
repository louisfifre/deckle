using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.Diagnostics;

namespace Deckle.TestSupport;

// ── ProviderManifest ──────────────────────────────────────────────────────────
//
// Reads what a provider declares (event names, levels, tags) straight from its
// [Event] attributes, so a test can assert on the shape of the provider without
// raising any event. It exists for rules that hold across a whole provider, such
// as "every closing event travels in the journal", where listing the events one
// by one in a test would let a new event slip through unchecked.
public static class ProviderManifest
{
    // The suffix every recovery closer carries; the journal doctrine requires
    // each of them, since the incident opener (a Warning or an Error) already
    // travels there and an unclosed incident reads as one that never ended.
    public const string RecoveredSuffix = "Recovered";

    public sealed record DeclaredEvent(string Name, int Id, EventLevel Level, EventTags Tags);

    public static IReadOnlyList<DeclaredEvent> EventsOf(Type sourceType)
    {
        var events = new List<DeclaredEvent>();
        foreach (MethodInfo method in sourceType.GetMethods(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            EventAttribute? attribute = method.GetCustomAttribute<EventAttribute>();
            if (attribute is null) continue;
            events.Add(new DeclaredEvent(method.Name, attribute.EventId, attribute.Level, attribute.Tags));
        }
        return events;
    }

    // Every payload name a provider declares, as "EventName.parameter". Payload
    // names are the method's parameter names, so a rule on what a line may carry
    // (no credential, no raw path) can be checked here without raising an event.
    public static IReadOnlyList<string> PayloadNamesOf(Type sourceType)
    {
        var names = new List<string>();
        foreach (MethodInfo method in sourceType.GetMethods(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<EventAttribute>() is null) continue;
            foreach (ParameterInfo parameter in method.GetParameters())
                names.Add($"{method.Name}.{parameter.Name}");
        }
        return names;
    }

    // Names of the closing events that do not carry the Journal tag: every event
    // named *Recovered, plus the provider's other closers listed by name. A
    // listed name that matches no event is reported too, so the list cannot
    // outlive a renamed event.
    public static IReadOnlyList<string> ClosersOutsideTheJournal(
        Type sourceType, params string[] otherClosers)
    {
        IReadOnlyList<DeclaredEvent> events = EventsOf(sourceType);
        var missing = new List<string>();

        foreach (DeclaredEvent declared in events)
        {
            bool isCloser = declared.Name.EndsWith(RecoveredSuffix, StringComparison.Ordinal)
                || otherClosers.Contains(declared.Name);
            if (isCloser && !ObservationTags.IsJournal(declared.Tags))
                missing.Add(declared.Name);
        }

        foreach (string name in otherClosers)
        {
            if (!events.Any(declared => declared.Name == name))
                missing.Add($"{name} (no such event)");
        }

        return missing;
    }
}
