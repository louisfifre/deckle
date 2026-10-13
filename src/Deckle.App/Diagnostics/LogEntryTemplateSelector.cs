using System.Diagnostics.Tracing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App;

// Every slot must be set in XAML: an empty slot crashes the first row it routes
// at realization.
public sealed class LogEntryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Verbose       { get; set; }
    public DataTemplate? Info          { get; set; }
    public DataTemplate? Warning       { get; set; }
    public DataTemplate? Error         { get; set; }

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
            switch (e.EventName)
            {
                case "LatencyRecorded":             return Latency!;
                case "CorpusAsrRecorded":           return Corpus!;
                case "CorpusRewriteRecorded":       return Corpus!;
                case "MicrophoneTelemetryRecorded": return Microphone!;
            }

            return e.Level switch
            {
                EventLevel.Verbose       => Verbose!,
                EventLevel.Informational => Info!,
                EventLevel.Warning       => Warning!,
                EventLevel.Critical      => Error!,
                EventLevel.Error         => Error!,
                // LogAlways carries no severity: a listener receives it at any level.
                EventLevel.LogAlways     => Info!,
                _                        => Info!,
            };
        }
        return Info!;
    }
}
