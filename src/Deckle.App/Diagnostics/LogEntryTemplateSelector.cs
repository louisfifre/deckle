using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
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

    public DataTemplate? Dataset       { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) => Pick(item);

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => Pick(item);

    private DataTemplate Pick(object item)
    {
        if (item is not LogEntry e) return Info!;

        // Kind before level: a dataset row carries measurements, not a
        // severity, so its level must not paint it as a warning or an error.
        // LogWindowSink keeps datasets out of this window today; the slot keeps
        // such a row neutral if a route ever lets one through.
        if (e.Entry.Kind == ObservationKind.Dataset) return Dataset!;

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
}
