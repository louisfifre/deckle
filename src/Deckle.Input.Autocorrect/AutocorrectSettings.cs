using System.Text.Json.Serialization;

namespace Deckle.Input.Autocorrect;

// Module settings. Enablement is per app (CONTEXT.md § Autocorrect — the
// activation gate): an app the user has never met is never corrected, but a
// would-be correction there can offer enrollment; an app set to false is
// declined and left entirely alone; true means corrections run there.
public sealed class AutocorrectSettings : IJsonOnDeserialized
{
    public bool Enabled { get; set; } = true;

    // Process name (no extension, matched case-insensitively) → corrections on.
    //   absent = never encountered — a candidate for the enrollment prompt.
    //   true   = enabled here.   false = declined (never prompt, never correct).
    public Dictionary<string, bool> Apps { get; set; } =
        new(StringComparer.OrdinalIgnoreCase) { ["notepad"] = true };

    // Legacy v1 allow-list. Read once and folded into Apps, then never written
    // again — the one-way migration off the flat list.
    [JsonPropertyName("enrolledProcesses")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? EnrolledProcesses { get; set; }

    // System.Text.Json rebuilds Apps with the default ordinal comparer, so
    // restore the case-insensitive one; then fold any legacy allow-list in (a
    // listed app becomes enabled) and drop it so it is never written back.
    public void OnDeserialized()
    {
        Apps = new Dictionary<string, bool>(Apps, StringComparer.OrdinalIgnoreCase);
        if (EnrolledProcesses is { Count: > 0 })
            foreach (string process in EnrolledProcesses)
                if (process.Length > 0)
                    Apps[process] = true;
        EnrolledProcesses = null;
    }

    // Pure transforms of the per-app decision map, beside the map they act on.
    // AutocorrectSettingsService calls these under its write lock; both return
    // a NEW case-insensitive map so the engine — which reads Apps live on its
    // input thread — only ever observes a complete old-or-new reference, never
    // a half-built one. The OrdinalIgnoreCase comparer is preserved on purpose:
    // process names are matched without regard to case everywhere.
    public static Dictionary<string, bool> WithDecision(
        IReadOnlyDictionary<string, bool> apps, string process, bool enabled) =>
        new(apps, StringComparer.OrdinalIgnoreCase) { [process] = enabled };

    public static Dictionary<string, bool> WithoutDecision(
        IReadOnlyDictionary<string, bool> apps, string process)
    {
        var next = new Dictionary<string, bool>(apps, StringComparer.OrdinalIgnoreCase);
        next.Remove(process);
        return next;
    }
}
