using System.Diagnostics;
using Deckle.Core.Interop;

namespace Deckle.Input.Autocorrect.Surfaces;

// Resolves the focused element into a FocusedSurface via one targeted UIA
// read. Called on focus-change events only (never per keystroke) — the
// cross-process COM round-trip is the cost of knowing where we type.
public sealed class SurfaceProber
{
    public FocusedSurface Probe()
    {
        if (!UIAutomation.TryDescribeFocusedElement(
                out bool isPassword, out bool isTextEditable, out int processId, out _))
            return FocusedSurface.Unknown;

        string process = string.Empty;
        if (processId > 0)
        {
            try { process = Process.GetProcessById(processId).ProcessName; }
            catch { /* process gone between probe and lookup — surface stays unnamed */ }
        }

        return new FocusedSurface(process, isPassword, isTextEditable);
    }
}
