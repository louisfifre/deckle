using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // ── Cursor signal ─────────────────────────────────────────────────────

    // The single EVENT_OBJECT_LOCATIONCHANGE hook carries two signals across
    // every process: the cursor (hwnd == NULL + OBJID_CURSOR) drives the
    // reveal-zone machine; a geometry change on the *foreground* window
    // (OBJID_WINDOW, hwnd == _foregroundHwnd) is the in-place F11 toggle,
    // which raises no foreground event. Runs at input cadence — everything
    // here is a few comparisons, plus one GetCursorPos on the cursor path.
    private void OnLocationChange(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (_systemSuspended || !_layoutKnown) return;

        // The foreground window resized in place (F11): re-evaluate
        // suppression and, while visible, climb back over the taskbar.
        if (idObject == OBJID_WINDOW && idChild == 0
            && hwnd != IntPtr.Zero && hwnd == _foregroundHwnd)
        {
            EvaluateAppSuppressed();
            if (_coverVisible) ReassertTopmost();
            return;
        }

        // Cursor moves: the reveal-zone state machine.
        if (idObject != OBJID_CURSOR || hwnd != IntPtr.Zero) return;
        if (!NativeMethods.GetCursorPos(out var p)) return;

        bool inZone = CoverGeometry.Contains(_zone, p);
        if (inZone)
        {
            // Back in the zone before the re-cover delay expired: stay revealed.
            if (_recoverTimerArmed)
            {
                KillTimer(_hwnd, TIMER_RECOVER);
                _recoverTimerArmed = false;
            }
            if (!_cursorInRevealZone)
            {
                _cursorInRevealZone = true;
                UpdateCover("zone_enter");
            }
        }
        else if (_cursorInRevealZone && !_recoverTimerArmed)
        {
            // Zone exit: arm the one-shot once; the flag keeps every
            // further movement from re-arming it. On SetTimer failure the
            // flag stays false so the next movement retries — only the
            // first failure is logged, this path runs at input cadence.
            if (SetTimer(_hwnd, TIMER_RECOVER, RecoverDelayMs, IntPtr.Zero) != UIntPtr.Zero)
            {
                if (_recoverArmFailureLogged)
                    DeckleShellTaskbarCoverSource.Log.TimerArmRecovered();
                _recoverTimerArmed = true;
                _recoverArmFailureLogged = false;
            }
            else if (!_recoverArmFailureLogged)
            {
                _recoverArmFailureLogged = true;
                int error = Marshal.GetLastWin32Error(); // before any WriteEvent clobbers it
                DeckleShellTaskbarCoverSource.Log.TimerArmFailed();
                DeckleShellTaskbarCoverSource.Log.TimerArmFailedDetail("recover", error);
            }
        }
    }

    private void OnRecoverTimer()
    {
        KillTimer(_hwnd, TIMER_RECOVER); // SetTimer repeats by default
        _recoverTimerArmed = false;

        // Defensive re-check: the queue could deliver the timer after the
        // cursor came back without the hook event being processed yet —
        // never re-cover under a cursor sitting in the zone.
        if (NativeMethods.GetCursorPos(out var p) && CoverGeometry.Contains(_zone, p)) return;

        _cursorInRevealZone = false;
        UpdateCover("zone_exit_delay");
    }

}
