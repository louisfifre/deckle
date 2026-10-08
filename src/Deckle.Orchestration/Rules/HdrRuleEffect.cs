namespace Deckle.Orchestration;

// HDR on or off per monitor while an app runs. Rules are keyed by GDI device
// name; the monitors are read once per launch, because adapter and target ids
// change when the display configuration does. A monitor that is absent or not
// HDR-capable is skipped, not an error. In clone mode one device name covers
// several targets, and the rule switches each of them.
public sealed class HdrRuleEffect : IRuleEffect
{
    public string Name => "hdr";

    public bool AppliesTo(AppEntry entry) =>
        entry.Rules.HdrByMonitor.Values.Any(rule => rule != HdrRule.Leave);

    public Action? Apply(AppEntry entry)
    {
        IReadOnlyList<DisplayTarget> targets = DisplayTargets.Enumerate();
        var undo = new List<(string Device, bool Prior)>();

        foreach ((string device, HdrRule rule) in entry.Rules.HdrByMonitor)
        {
            if (rule == HdrRule.Leave) continue;
            bool wanted = rule == HdrRule.On;

            var matches = targets.Where(t => SameDevice(t, device)).ToList();
            if (matches.Count == 0)
            {
                DeckleOrchestrationSource.Log.HdrRuleSkipped(device, "absent");
                continue;
            }

            foreach (DisplayTarget target in matches)
            {
                if (!target.HdrSupported)
                {
                    DeckleOrchestrationSource.Log.HdrRuleSkipped(device, "not_hdr_capable");
                    continue;
                }
                if (!HdrStateSwitch.TryRead(target.AdapterId, target.TargetId, out _, out bool current))
                {
                    DeckleOrchestrationSource.Log.HdrRuleSkipped(device, "unreadable");
                    continue;
                }
                if (current == wanted) continue;

                if (!Switch(target, current, wanted))
                {
                    // The transaction only undoes effects that returned; this
                    // one undoes its own monitors before failing.
                    Undo(undo);
                    throw new InvalidOperationException($"HDR could not be switched on {device}.");
                }
                undo.Add((device, current));
            }
        }

        if (undo.Count == 0) return null;
        return () =>
        {
            if (Undo(undo) is { } failed)
                throw new InvalidOperationException($"HDR could not be restored on {failed}.");
        };
    }

    // Puts each changed monitor back, last first. The monitors are read again:
    // the configuration may have changed while the app ran. Returns the
    // devices that could not be restored, or null when all were.
    private static string? Undo(List<(string Device, bool Prior)> changes)
    {
        IReadOnlyList<DisplayTarget> targets = DisplayTargets.Enumerate();
        var failed = new List<string>();

        for (int i = changes.Count - 1; i >= 0; i--)
        {
            (string device, bool prior) = changes[i];
            var matches = targets.Where(t => SameDevice(t, device) && t.HdrSupported).ToList();
            if (matches.Count == 0)
            {
                DeckleOrchestrationSource.Log.HdrRuleSkipped(device, "absent");
                continue;
            }
            foreach (DisplayTarget target in matches)
            {
                if (!HdrStateSwitch.TryRead(target.AdapterId, target.TargetId, out _, out bool current))
                {
                    failed.Add(device);
                    continue;
                }
                if (current != prior && !Switch(target, current, prior))
                    failed.Add(device);
            }
        }
        return failed.Count == 0 ? null : string.Join(", ", failed.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool Switch(DisplayTarget target, bool from, bool to)
    {
        if (HdrStateSwitch.TrySet(target.AdapterId, target.TargetId, to, out int error))
        {
            DeckleOrchestrationSource.Log.HdrStateChanged(target.GdiDeviceName, from, to);
            return true;
        }
        DeckleOrchestrationSource.Log.HdrSetFailed();
        DeckleOrchestrationSource.Log.HdrSetFailedDetail(target.GdiDeviceName, error);
        return false;
    }

    private static bool SameDevice(DisplayTarget target, string device) =>
        string.Equals(target.GdiDeviceName, device, StringComparison.OrdinalIgnoreCase);
}
