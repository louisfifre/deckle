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
                            && !_cursorInRevealZone && !_systemSuspended;
        if (shouldBeVisible == _coverVisible) return;

        _coverVisible = shouldBeVisible;
        if (shouldBeVisible)
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            ReassertTopmost();
            // Z-order witness: ShowWindow + the topmost assert can both succeed
            // while the band still sits below Shell_TrayWnd (the taskbar is
            // topmost too, last-positioned wins). CoverShown only proves the
            // ShowWindow call; this captures the native result — what occludes
            // the band right after the assert, and the foreground at that
            // instant — to settle the boot "covers but stays under the taskbar"
            // case the visibility log can't see.
            WindowingProbe.EmitWindowZOrderState(_hwnd, "taskbar-cover", "after_show_topmost");
            DeckleShellTaskbarCoverSource.Log.CoverShown(reason);
        }
        else
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            DeckleShellTaskbarCoverSource.Log.CoverHidden(reason);
        }
    }

    // Climb to the top of the topmost band. The taskbar is topmost too and
    // among topmost windows the most recently positioned wins, so this is how
    // the band stays above it — re-asserted when shown, on every foreground
    // change, and on the suppression poll as a fallback.
    private void ReassertTopmost() =>
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
}
