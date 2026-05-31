using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App.Diagnostics;

// Wrapper UI-side autour d'un EventEntry produit par le listener
// Deckle.Diagnostics. LogWindow consomme exclusivement des LogEntry —
// le wrap précompute le texte affiché (`HH:mm:ss.fff [SOURCE] message`)
// pour que la virtualisation ListView ne reformatte pas à chaque
// realization de ligne.
//
// Le mapping `Provider` → label source ("Deckle.Whisp" → "WHISP",
// "Deckle.App" → "APP") suit la convention courte uppercase héritée
// du legacy LogSource. Il vit dans Deckle.Diagnostics.LogLineFormatter
// pour que la LogWindow et app.jsonl produisent la même ligne rendue.
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
        Text = LogLineFormatter.Format(entry);
    }
}
