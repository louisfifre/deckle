using System.Diagnostics.Tracing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App.Diagnostics;

// Routes each LogEntry to the appropriate DataTemplate. Two decision families:
//
//   1. A few specific EventName values carry compact tertiary text
//      presentation (Latency / Corpus / Microphone). They go through their
//      dedicated template regardless of level.
//   2. Otherwise, fall back to semantic color by EventLevel
//      (Verbose / Informational / Warning / Error / Critical / LogAlways).
//
// Instantiated twice in XAML resources (NoWrapSelector and WrapSelector);
// the Word-wrap toggle swaps the whole collection. Every slot must be set at
// XAML load time; an empty slot crashes the first row of that level at
// realization.
public sealed class LogEntryTemplateSelector : DataTemplateSelector
{
    // Semantic colors aligned with BCL EventLevel values.
    public DataTemplate? Verbose       { get; set; }
    public DataTemplate? Info          { get; set; }
    public DataTemplate? Warning       { get; set; }
    public DataTemplate? Error         { get; set; }

    // Compact tertiary presentation for pure telemetry rows.
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
            // Route by event name first to catch specialized telemetry rows
            // before dispatching by level.
            switch (e.EventName)
            {
                case "LatencyRecorded":             return Latency!;
                // The two normalized corpus events share the same template;
                // see ADR-0006 for ASR/rewrite separation on disk, which is
                // not meant to be reflected in live presentation.
                case "CorpusAsrRecorded":           return Corpus!;
                case "CorpusRewriteRecorded":       return Corpus!;
                case "MicrophoneTelemetryRecorded": return Microphone!;
            }

            return e.Level switch
            {
                EventLevel.Verbose       => Verbose!,
                EventLevel.Informational => Info!,
                EventLevel.Warning       => Warning!,
                // Critical and Error share the Error template (critical red);
                // the BCL distinction carries no visual difference here.
                EventLevel.Critical      => Error!,
                EventLevel.Error         => Error!,
                // LogAlways = events an EventListener always receives,
                // regardless of the requested level. No intrinsic severity
                // semantics; render as Info.
                EventLevel.LogAlways     => Info!,
                _                        => Info!,
            };
        }
        return Info!;
    }
}
