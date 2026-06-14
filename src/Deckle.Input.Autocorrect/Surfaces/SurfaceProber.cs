using System.Diagnostics;
using Deckle.Core;

namespace Deckle.Input.Autocorrect;

// Resolves the focused element into a FocusedSurface via one targeted UIA
// read. Called on focus-change events only (never per keystroke) — the
// cross-process COM round-trip is the cost of knowing where we type.
public sealed class SurfaceProber : ISurfaceProber
{
    public FocusedSurface Probe()
    {
        if (!UIAutomation.TryDescribeFocusedElement(
                out bool isPassword, out bool isTextEditable, out int processId, out string probe))
            // UIA could not answer — unknown surface, but keep the reason so the
            // log says why we observe without ever correcting here.
            return FocusedSurface.Unknown with { Probe = probe };

        string process = string.Empty;
        if (processId > 0)
        {
            try { process = Process.GetProcessById(processId).ProcessName; }
            catch { /* process gone between probe and lookup — surface stays unnamed */ }
        }

        return new FocusedSurface(process, isPassword, isTextEditable, probe);
    }
}
