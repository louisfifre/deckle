using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // ── Visibility — the sole ShowWindow site, idempotent ─────────────────

    private void UpdateCover(string reason)
    {
        bool shouldBeVisible = _layoutKnown && !_appSuppressed
                            && !_systemSuspended && _coverAlpha > 0;
        if (shouldBeVisible == _coverVisible) return;

        _coverVisible = shouldBeVisible;
        if (shouldBeVisible)
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            ReassertTopmost();
            // Z-order witness: ShowWindow and the topmost assert can both
            // succeed while the band still sits below Shell_TrayWnd. CoverShown
            // only proves the ShowWindow call; this records what occludes the
            // band right after the assert, and the foreground at that instant.
            WindowingProbe.EmitWindowZOrderState(_hwnd, "taskbar-cover", "after_show_topmost");
            DeckleShellTaskbarCoverSource.Log.CoverShown(reason);
        }
        else
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            DeckleShellTaskbarCoverSource.Log.CoverHidden(reason);
        }
    }

    // The taskbar is topmost too and among topmost windows the most recently
    // positioned wins, so the band re-asserts its place on show and, while
    // visible, on every foreground change, every layout message (even with an
    // unchanged rect) and every return to full opacity.
    private void ReassertTopmost() =>
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
}
