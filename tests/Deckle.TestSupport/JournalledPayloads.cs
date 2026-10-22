using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.TestSupport;

// ── JournalledPayloads ────────────────────────────────────────────────────────
//
// Answers "which events of this provider declare a payload with one of these
// names, and do they travel in the journal?" straight from the [Event]
// attributes. It backs the rule that an application or process name stays on a
// Verbose detail event and never rides a Warning, an Error or a journalled
// milestone: the journal outlives the session, and a name read there says which
// program the person was using.
public static class JournalledPayloads
{
    // The payload names under which the providers carry the name of an
    // application or of a process. A new provider that names an app under
    // another word adds it here.
    public static readonly IReadOnlyList<string> AppNameFields =
    [
        "process",
        "process_name",
        "display_name",
        "foreground_descriptor",
        "app_name",
        "exe_name",
    ];

    // The journal keeps every Warning and Error and every event tagged Journal.
    // This is the same predicate the app's journal sink applies.
    public static bool Travels(ProviderManifest.DeclaredEvent declared)
        => declared.Level <= EventLevel.Warning || ObservationTags.IsJournal(declared.Tags);

    // "EventName.field" for each declared event that carries one of the fields,
    // with whether the event travels in the journal.
    public static IReadOnlyList<(string Payload, ProviderManifest.DeclaredEvent Event)> Carrying(
        Type sourceType, IEnumerable<string> fields)
    {
        string[] wanted = [.. fields];
        var found = new List<(string, ProviderManifest.DeclaredEvent)>();
        foreach (ProviderManifest.DeclaredEvent declared in ProviderManifest.EventsOf(sourceType))
        {
            System.Reflection.MethodInfo method = sourceType.GetMethod(declared.Name)!;
            foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
            {
                if (wanted.Contains(parameter.Name, StringComparer.Ordinal))
                    found.Add(($"{declared.Name}.{parameter.Name}", declared));
            }
        }
        return found;
    }

    // The payloads that break the rule: an app or process name on an event that
    // travels in the journal. Empty when the provider keeps to it.
    public static IReadOnlyList<string> AppNamesInTheJournal(Type sourceType)
        => [.. Carrying(sourceType, AppNameFields)
            .Where(c => Travels(c.Event))
            .Select(c => c.Payload)];
}
