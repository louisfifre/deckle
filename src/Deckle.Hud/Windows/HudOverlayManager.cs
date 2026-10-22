using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Settings;
using Deckle.Shell;

namespace Deckle.Hud;

// Owns the stack of transient overlay cards displayed alongside the main
// HudWindow. Each Enqueue creates one HudOverlayWindow, placed at the next
// free slot away from the main HUD. Per-card timers expire by severity
// (HudWindow.FeedbackDuration).
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
        SettingsService.Instance.OverlayAnimationsChanged += OnOverlayAnimationsChanged;
    }

    // Severity convention 0=Info, 1=Warning, 2+=Error, aligned with the
    // `UserFeedbackEmitted(severity, ...)` EventSource event.
    public void Enqueue(int severity, string title, string body)
    {
        if (_disposed) return;

        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueueObserved(
                "feedback-display", "overlay-manager",
                () => Enqueue(severity, title, body),
                "HUD", "overlay enqueue");
            return;
        }

        // Symmetric with HudWindow.SetState: Overlay disabled in Settings → no-op.
        if (!SettingsService.Instance.Current.Overlay.Enabled)
        {
            DeckleHudSource.Log.HudDisplaySuppressed(
                "overlay", "Message", severity, "overlay_disabled");
            return;
        }

        var window = new HudOverlayWindow(_cursorSignal);
        var hwnd = window.Hwnd;

        // The new card takes the top of the stack (slot N-1 once added,
        // farthest from the main HUD).
        int newSlot = _entries.Count;
        var (xPx, yPx) = ComputeSlotPositionPx(newSlot);

        window.ApplyPayload(severity, title, body);
        window.ShowAt(xPx, yPx);

        // The slot is known only here, so the manager emits the overlay
        // specialization after ShowAt's common trunk; Recompact emits it again
        // on each slot change.
        WindowingProbe.EmitOverlaySlotAssigned(hwnd, newSlot);

        var slide = new WindowSlideAnimator(
            hwnd,
            _dispatcher,
            xPx,
            yPx,
            SettingsService.Instance.Current.Overlay.Animations);

        var lifeTimer = _dispatcher.CreateTimer();
        lifeTimer.Interval = HudWindow.FeedbackDuration(severity);
        lifeTimer.IsRepeating = false;

        var entry = new OverlayEntry(window, slide, lifeTimer, newSlot, severity);
        lifeTimer.Tick += (_, _) => OnLifeTimerTick(entry);

        // Window owns its fade animator so proximity updates and fade-in /
        // fade-out share a single alpha source of truth.
        window.FadeIn();
        lifeTimer.Start();

        _entries.Add(entry);
        DeckleHudSource.Log.OverlayCardChanged("shown", newSlot, severity, _entries.Count);

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

    private void OnOverlayAnimationsChanged(bool enabled)
    {
        if (_disposed) return;

        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueueObserved(
                "ui-update", "overlay-manager",
                () => OnOverlayAnimationsChanged(enabled),
                "HUD", "animation preference change");
            return;
        }

        foreach (var entry in _entries)
            entry.Slide.SetAnimationsEnabled(enabled);
    }

    private void OnLifeTimerTick(OverlayEntry entry)
    {
        if (_disposed) return;
        if (entry.IsExpiring) return;

        entry.IsExpiring = true;
        entry.LifeTimer.Stop();

        entry.Window.FadeOut(onComplete: () =>
        {
            entry.Slide.Cancel();
            CloseCard(entry, "overlay-expiry");
            _entries.Remove(entry);
            DeckleHudSource.Log.OverlayCardChanged(
                "closed", entry.SlotIndex, entry.Severity, _entries.Count);
            Recompact();
        });
    }

    // A card the window already closed throws here; teardown goes on either
    // way, but the throw stays visible in the detail log.
    private static void CloseCard(OverlayEntry entry, string what)
    {
        try { entry.Window.ForceClose(); }
        catch (Exception ex)
        {
            DeckleHudSource.Log.CleanupFailed(what, ex.GetType().Name, ex.Message);
        }
    }

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

                // The probe captures the pre-slide rect: the trace means "slot N
                // assigned", not "animation completed".
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
        double scale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_mainHud.Hwnd));

        int gapPx = Dpi.ToPixels(GapDip, scale);
        int stride = gapPx + mainRect.Height;

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
        SettingsService.Instance.OverlayAnimationsChanged -= OnOverlayAnimationsChanged;

        int remaining = _entries.Count;
        foreach (var entry in _entries)
        {
            entry.LifeTimer.Stop();
            entry.Slide.Cancel();
            CloseCard(entry, "overlay-shutdown");
            DeckleHudSource.Log.OverlayCardChanged(
                "closed", entry.SlotIndex, entry.Severity, --remaining);
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

        // Kept for the card's closing trace only; the payload text is never
        // retained.
        public int                  Severity  { get; }

        public OverlayEntry(
            HudOverlayWindow window,
            WindowSlideAnimator slide,
            DispatcherQueueTimer lifeTimer,
            int slotIndex,
            int severity)
        {
            Window    = window;
            Slide     = slide;
            LifeTimer = lifeTimer;
            SlotIndex = slotIndex;
            Severity  = severity;
        }
    }
}
