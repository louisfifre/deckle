using System.Diagnostics.Tracing;
using System.Globalization;
using Deckle.Diagnostics;

namespace Deckle.Diagnostics;

// Wrapper UI-side autour d'un EventEntry produit par le listener
// Deckle.Diagnostics. LogWindow consomme exclusivement des LogEntry —
// le wrap précompute le texte affiché (`HH:mm:ss.fff [SOURCE] message`)
// pour que la virtualisation ListView ne reformatte pas à chaque
// realization de ligne.
//
// Le mapping `Provider` → label source ("Deckle.Whisp" → "WHISP",
// "Deckle" → "APP") suit la convention courte uppercase héritée du
// legacy LogSource ; il vivait dans LegacyLogWindowSink.MapSource et
// migre ici puisque LogWindow est désormais le seul consommateur.
//
// `EventName` et `Level` sont exposés en proxy parce que le
// DataTemplateSelector route ses templates sur ces deux propriétés —
// par nom d'event pour les rows télémétrie spécialisées (Latency /
// Corpus / Microphone), par EventLevel BCL pour le reste.
public sealed class LogEntry
{
    public EventEntry Entry { get; }
    public string Text { get; }
    public string EventName => Entry.EventName;
    public EventLevel Level => Entry.Level;

    public LogEntry(EventEntry entry)
    {
        Entry = entry;
        string source = MapSource(entry.Provider);
        string message = entry.FormattedMessage ?? entry.EventName;
        Text = string.Format(
            CultureInfo.InvariantCulture,
            "{0:HH:mm:ss.fff} [{1}] {2}",
            entry.Timestamp, source, message);
    }

    private static string MapSource(string providerName)
    {
        // "Deckle" tout court → "APP" pour rester aligné avec
        // l'ancienne constante LogSource.App. Les providers nommés
        // "Deckle.<Module>" perdent le préfixe et passent en uppercase
        // pour donner le tag court attendu en tête de ligne.
        const string prefix = "Deckle.";
        if (string.Equals(providerName, "Deckle", System.StringComparison.Ordinal))
            return "APP";
        if (providerName.StartsWith(prefix, System.StringComparison.Ordinal))
            return providerName.Substring(prefix.Length).ToUpperInvariant();
        return providerName.ToUpperInvariant();
    }
}
