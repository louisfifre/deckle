namespace Deckle.Shell.Taskbar;

/// <summary>
/// One value the restore gives back to a tray icon's subkey: the DWORD to
/// write, or null to delete the value so explorer applies its own default.
/// </summary>
public sealed record TrayIconRestoreStep(string Subkey, uint? Value);

// What "show all tray icons" writes, decided without touching the registry.
// Its input is the state of HKCU\Control Panel\NotifyIconSettings as one map,
// subkey name -> IsPromoted (null when the value is absent); its outputs are
// the writes. TrayIconPromoter reads the key, asks here, then writes.
//
// Promotion writes 1 only where the value is not already 1. That is what keeps
// the watcher from looping: Deckle's own write signals the watcher, the next
// plan finds every value at 1 and writes nothing (brief-csharp-socle B10).
//
// Restore gives each subkey the value the snapshot taken at enable recorded.
// A subkey the snapshot does not know appeared after enable; its IsPromoted is
// deleted, which hands it back to Windows's default (hidden in the overflow).
// Louis chose that over leaving such icons visible (brief question D7). A
// subkey the snapshot knows but the key no longer holds is an icon Windows
// dropped since: nothing to give back.
public static class TrayIconPromotion
{
    /// <summary>The DWORD value explorer reads to show an icon on the bar.</summary>
    public const string ValueName = "IsPromoted";

    /// <summary>IsPromoted for an icon shown on the bar.</summary>
    public const uint Promoted = 1;

    /// <summary>The subkeys whose IsPromoted is not already 1.</summary>
    public static IReadOnlyList<string> SubkeysToPromote(IReadOnlyDictionary<string, uint?> current) =>
        current
            .Where(pair => pair.Value != Promoted)
            .Select(pair => pair.Key)
            .OrderBy(subkey => subkey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// The writes that give the key back the state <paramref name="snapshot"/>
    /// recorded, skipping every subkey that already holds its target.
    /// </summary>
    public static IReadOnlyList<TrayIconRestoreStep> PlanRestore(
        IReadOnlyDictionary<string, uint?> snapshot,
        IReadOnlyDictionary<string, uint?> current)
    {
        var known = new Dictionary<string, uint?>(snapshot, StringComparer.OrdinalIgnoreCase);
        var steps = new List<TrayIconRestoreStep>();
        foreach ((string subkey, uint? value) in current.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            uint? target = known.TryGetValue(subkey, out uint? prior) ? prior : null;
            if (value != target) steps.Add(new TrayIconRestoreStep(subkey, target));
        }
        return steps;
    }
}
