using System.Text.Json.Serialization;

namespace Deckle.Orchestration;

// What Deckle changes while an entry runs.
public sealed class AppRules : IJsonOnDeserialized
{
    // GDI device name ("\\.\DISPLAY1") → rule. A monitor absent from the map is
    // left alone, the same as HdrRule.Leave.
    public Dictionary<string, HdrRule> HdrByMonitor { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // System.Text.Json rebuilds the map with the default ordinal comparer;
    // device names are compared without regard to case everywhere.
    public void OnDeserialized() =>
        HdrByMonitor = new Dictionary<string, HdrRule>(HdrByMonitor, StringComparer.OrdinalIgnoreCase);

    internal AppRules Clone() => new()
    {
        HdrByMonitor = new Dictionary<string, HdrRule>(HdrByMonitor, StringComparer.OrdinalIgnoreCase),
    };
}
