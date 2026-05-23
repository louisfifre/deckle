using System.Diagnostics.Tracing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App.Diagnostics;

// Route chaque LogEntry vers le DataTemplate adapté. Deux familles de
// décisions :
//
//   1. Quelques EventName spécifiques portent une présentation
//      compacte tertiary text (Latency / Corpus / Microphone). Ils
//      passent par leur template dédié indépendamment du niveau.
//   2. Sinon, on retombe sur la couleur sémantique par EventLevel
//      (Verbose / Informational / Warning / Error / Critical / LogAlways).
//
// Instancié deux fois dans les ressources XAML (NoWrapSelector et
// WrapSelector) ; la bascule Word-wrap échange la collection complète.
// Tous les slots doivent être renseignés au load XAML — un slot vide
// fait crasher la première row de ce niveau au realization.
public sealed class LogEntryTemplateSelector : DataTemplateSelector
{
    // Couleurs sémantiques alignées sur les EventLevel BCL.
    public DataTemplate? Verbose       { get; set; }
    public DataTemplate? Info          { get; set; }
    public DataTemplate? Warning       { get; set; }
    public DataTemplate? Error         { get; set; }

    // Présentation compacte tertiary pour les rows télémétrie pure.
    public DataTemplate? Latency       { get; set; }
    public DataTemplate? Corpus        { get; set; }
    public DataTemplate? Microphone    { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) => Pick(item);

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => Pick(item);

    private DataTemplate Pick(object item)
    {
        if (item is LogEntry e)
        {
            // Routage par nom d'event d'abord pour capter les rows
            // télémétrie spécialisées avant la dispatch par niveau.
            switch (e.EventName)
            {
                case "LatencyRecorded":             return Latency!;
                case "CorpusRecorded":              return Corpus!;
                case "MicrophoneTelemetryRecorded": return Microphone!;
            }

            return e.Level switch
            {
                EventLevel.Verbose       => Verbose!,
                EventLevel.Informational => Info!,
                EventLevel.Warning       => Warning!,
                // Critical et Error partagent le template Error
                // (rouge critical) — la distinction BCL ne porte pas
                // de différence visuelle dans cette surface.
                EventLevel.Critical      => Error!,
                EventLevel.Error         => Error!,
                // LogAlways = events qu'un EventListener reçoit
                // toujours, peu importe le niveau demandé. Sans
                // sémantique de gravité propre — on rend en Info.
                EventLevel.LogAlways     => Info!,
                _                        => Info!,
            };
        }
        return Info!;
    }
}
