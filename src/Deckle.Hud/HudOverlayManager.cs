using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Settings;
using Deckle.Shell;

namespace Deckle.Hud;

// Owns the stack of transient overlay cards displayed alongside the main
// HudWindow. Each enqueued UserFeedback with Role=Overlay creates one
// HudOverlayWindow, placed at the next free slot away from the main HUD.
// Per-card timers expire based on severity (see UserFeedbackDurations).
//
// ─── Invariant — no gap ──────────────────────────────────────────────────────
// Slot indices span 0..N-1 contiguously, with 0 adjacent to the main HUD and
// N-1 farthest from it. After any mutation (enqueue, expiry, main HUD show /
// hide) Recompact() reassigns slot indices from 0 in list order — oldest
// closest to HUD, newest farthest — and slides each card whose slot changed.
// A single primitive covers expiry-at-bottom, expiry-at-top, expiry-in-middle,
// and HUD visibility flips — no branching per case.
//
// ─── Position direction ──────────────────────────────────────────────────────
// Read from Settings.Overlay.Position:
//   Bottom*  → stack grows upward from main HUD (away into empty screen).
//   Top*     → stack grows downward.
// When the main HUD hides, slot 0 takes over its exact position; all other
// slots shift one stride toward the HUD's old location.
public sealed class HudOverlayManager : IDisposable
{
    private const int GapDip = 12;

    private readonly HudWindow _mainHud;
    private readonly DispatcherQueue _dispatcher;
    private readonly CursorMovementSignal _cursorSignal;
    private readonly List<OverlayEntry> _entries = new();

    private bool _mainHudVisible;
    private bool _disposed;

    public HudOverlayManager(HudWindow mainHud, DispatcherQueue dispatcher, CursorMovementSignal cursorSignal)
    {
        _mainHud        = mainHud;
        _dispatcher     = dispatcher;
        _cursorSignal   = cursorSignal;
        _mainHudVisible = mainHud.IsMainHudShown;
        mainHud.MainHudVisibilityChanged += OnMainHudVisibilityChanged;
    }

    // Sub-wave 6b: signature in primitives rather than a `UserFeedback`
    // record. Severity convention 0=Info, 1=Warning, 2+=Error, aligned with
    // the `UserFeedbackEmitted(severity, ...)` EventSource event.
    public void Enqueue(int severity, string title, string body)
    {
        if (_disposed) return;

        if (!_dispatcher.HasThreadAccess)
        {
            // Threading: overlay enqueue from a non-UI thread
            // (HudFeedbackEventListener on the Diagnostics side, or any
            // UserFeedbackEmitted emitter from a worker engine). An abnormal
            // wait_ms here indicates a saturated UI thread delaying overlay
            // display.
            _dispatcher.TryEnqueueObserved(
                "feedback-display", "overlay-manager",
                () => Enqueue(severity, title, body),
                "HUD", "overlay enqueue");
            return;
        }

        // Symmetric with HudWindow.SetState: Overlay disabled in Settings → no-op.
        if (!SettingsService.Instance.Current.Overlay.Enabled)
            return;

        var window = new HudOverlayWindow(_cursorSignal);
        var hwnd = window.Hwnd;

        // Append to the list first so ComputeSlotPositionPx sees the final
        // count. The new card sits at the top of the stack (slot = N-1,
        // farthest from the main HUD).
        int newSlot = _entries.Count;
        var (xPx, yPx) = ComputeSlotPositionPx(newSlot);

        window.ApplyPayload(severity, title, body);
        window.ShowAt(xPx, yPx);

        // Windowing: overlay specialization emitted after ShowAt (which
        // already emits the common trunk with window="hud-overlay"). The slot
        // is only known here in the manager, not in the window itself. Also
        // emitted on each slot reassignment in Recompact below when an overlay
        // changes position after a neighbor expires or the main HUD
        // appears/disappears.
        WindowingProbe.EmitOverlaySlotAssigned(hwnd, newSlot);

        var slide = new WindowSlideAnimator(hwnd, _dispatcher, xPx, yPx);

        var lifeTimer = _dispatcher.CreateTimer();
        lifeTimer.Interval = HudWindow.FeedbackDuration(severity);
        lifeTimer.IsRepeating = false;

        var entry = new OverlayEntry(window, slide, lifeTimer, newSlot);
        lifeTimer.Tick += (_, _) => OnLifeTimerTick(entry);

        // Window owns its fade animator so proximity updates and fade-in /
        // fade-out share a single alpha source of truth.
        window.FadeIn();
        lifeTimer.Start();

        _entries.Add(entry);

        // No-op in the common append case (only the new entry changed slots,
        // and it's already at the right position). Still called for uniformity
        // — every mutation ends with Recompact.
        Recompact();
    }

    private void OnMainHudVisibilityChanged(object? sender, bool visible)
    {
        if (_disposed) return;

        if (!_dispatcher.HasThreadAccess)
        {
            // Threading: propagation of the main HUD visibility event to the
            // overlay stack. Cross-thread when HudWindow emits from a non-UI
            // path (rare in practice, but the path is wired). Instrumentation
            // identical to other overlay sites.
            _dispatcher.TryEnqueueObserved(
                "ui-update", "overlay-manager",
                () => OnMainHudVisibilityChanged(sender, visible),
                "HUD", "main HUD visibility change");
            return;
        }

        if (_mainHudVisible == visible) return;
        _mainHudVisible = visible;
        Recompact();
    }

    private void OnLifeTimerTick(OverlayEntry entry)
    {
        if (_disposed) return;
        if (entry.IsExpiring) return;

        entry.IsExpiring = true;
        entry.LifeTimer.Stop();

        // Fade out, then close the HWND and recompact the stack so remaining
        // cards slide into the freed slot(s). Recompact is what guarantees the
        // "no gap" invariant — regardless of where the expiring card sat
        // (bottom, middle, top), the surviving cards end up contiguous.
        entry.Window.FadeOut(onComplete: () =>
        {
            entry.Slide.Cancel();
            try { entry.Window.ForceClose(); } catch { /* already closed */ }
            _entries.Remove(entry);
            Recompact();
        });
    }

    // The one primitive — walks entries in list order (0 = oldest), reassigns
    // SlotIndex to match list position, and slides any card whose target
    // pixel position differs from its current one. Called from every mutation:
    // enqueue, expiry, main HUD visibility flip.
    private void Recompact()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry.IsExpiring) continue;

            int newSlot = i;
            var (xPx, yPx) = ComputeSlotPositionPx(newSlot);

            if (entry.SlotIndex != newSlot ||
                entry.Slide.CurrentX != xPx ||
                entry.Slide.CurrentY != yPx)
            {
                bool slotChanged = entry.SlotIndex != newSlot;
                entry.SlotIndex = newSlot;
                entry.Slide.SlideTo(xPx, yPx);

                // Windowing: slot reassignment. Emitted only when the slot
                // changes (not on simple in-slot repositioning caused by a DPI
                // or work area change). The post-slide rect is asynchronous on
                // the animator side; we capture the current rect, which
                // reflects the pre-animation position. The trace says "slot N
                // assigned to this window", not "animation completed".
                if (slotChanged)
                {
                    WindowingProbe.EmitOverlaySlotAssigned(entry.Window.Hwnd, newSlot);
                }
            }
        }
    }

    private (int X, int Y) ComputeSlotPositionPx(int slot)
    {
        var mainRect = _mainHud.GetRectPx();
        uint dpi = NativeMethods.GetDpiForWindow(_mainHud.Hwnd);
        double scale = dpi / 96.0;

        int gapPx = (int)Math.Round(GapDip * scale);
        int stride = gapPx + mainRect.Height;

        // When the main HUD is visible, slot 0 sits one stride away (leaving
        // room for the HUD itself + the 24 dip gap). When it's hidden, slot 0
        // drops onto the HUD's exact position.
        int offset = slot + (_mainHudVisible ? 1 : 0);

        string position = SettingsService.Instance.Current.Overlay.Position ?? "";
        bool isTop = position.StartsWith("Top", StringComparison.Ordinal);

        int y = isTop
            ? mainRect.Y + offset * stride
            : mainRect.Y - offset * stride;

        return (mainRect.X, y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mainHud.MainHudVisibilityChanged -= OnMainHudVisibilityChanged;

        foreach (var entry in _entries)
        {
            entry.LifeTimer.Stop();
            entry.Slide.Cancel();
            try { entry.Window.ForceClose(); } catch { /* shutting down */ }
        }
        _entries.Clear();
    }

    private sealed class OverlayEntry
    {
        public HudOverlayWindow     Window    { get; }
        public WindowSlideAnimator  Slide     { get; }
        public DispatcherQueueTimer LifeTimer { get; }
        public int                  SlotIndex { get; set; }
        public bool                 IsExpiring { get; set; }

        public OverlayEntry(
            HudOverlayWindow window,
            WindowSlideAnimator slide,
            DispatcherQueueTimer lifeTimer,
            int slotIndex)
        {
            Window    = window;
            Slide     = slide;
            LifeTimer = lifeTimer;
            SlotIndex = slotIndex;
        }
    }
}
