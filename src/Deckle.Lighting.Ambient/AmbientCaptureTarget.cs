using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

// The display Ambient captures, read from Windows rather than from a capture:
// the one the user picked while it is connected, else the one Windows marks
// primary, the choice ScreenCaptureService makes when it starts. The preview
// takes its frame's ratio from it before any capture runs, so the frame has
// the shape the capture will have.
internal static class AmbientCaptureTarget
{
    public static CaptureMonitor? Resolve(IReadOnlyList<CaptureMonitor> monitors, string? selectedDeviceName)
    {
        if (!string.IsNullOrEmpty(selectedDeviceName))
        {
            foreach (var monitor in monitors)
            {
                if (string.Equals(monitor.DeviceName, selectedDeviceName, StringComparison.Ordinal))
                    return monitor;
            }
        }

        foreach (var monitor in monitors)
        {
            if (monitor.IsPrimary) return monitor;
        }

        return monitors.Count > 0 ? monitors[0] : null;
    }
}
