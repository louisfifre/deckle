using Deckle.Shell;
using Xunit;

namespace Deckle.Shell.Tests;

// ── ResizeGestureTests ──────────────────────────────────────────────────────
//
// Exercises the Win32-free state machine behind ResizeCoalescer: the gesture
// boundary (ENTER … EXIT), the per-frame coalescing of WM_SIZE, and the
// safety net for a WM_SIZE that arrives outside any gesture (maximize / snap /
// programmatic resize, none of which enter the modal move/size loop). The native
// subclass shell is not under test here — only the decision it drives.
[Trait("Category", "unit")]
public class ResizeGestureTests
{
    [Fact]
    public void EnterSizeMove_RaisesIsResizing()
    {
        var g = new ResizeGesture();
        Assert.False(g.IsResizing);

        g.EnterSizeMove();

        Assert.True(g.IsResizing);
    }

    [Fact]
    public void Gesture_CoalescesEverySizeIntoOneSettlementOnExit()
    {
        var g = new ResizeGesture();
        g.EnterSizeMove();

        // Every WM_SIZE during the gesture is swallowed — no settlement yet.
        Assert.Null(g.Size());
        Assert.Null(g.Size());
        Assert.Null(g.Size());

        var settled = g.ExitSizeMove();

        Assert.NotNull(settled);
        Assert.Equal(ResizeTrigger.Gesture, settled!.Value.Trigger);
        Assert.Equal(3, settled.Value.Frames);   // the 3 coalesced WM_SIZE
        Assert.False(g.IsResizing);
    }

    [Fact]
    public void MoveOnlyGesture_DoesNotSettle()
    {
        // Dragging the title bar without resizing: ENTER then EXIT, no WM_SIZE.
        var g = new ResizeGesture();
        g.EnterSizeMove();

        var settled = g.ExitSizeMove();

        Assert.Null(settled);          // no recompute for a pure move
        Assert.False(g.IsResizing);
    }

    [Fact]
    public void SizeOutsideGesture_SettlesImmediatelyAsDirect()
    {
        // The safety net: maximize / snap / programmatic SetWindowPos emit
        // WM_SIZE without entering the modal loop, so there is no EXIT to wait
        // for. It must settle on the spot and never flip IsResizing.
        var g = new ResizeGesture();

        var settled = g.Size();

        Assert.NotNull(settled);
        Assert.Equal(ResizeTrigger.Direct, settled!.Value.Trigger);
        Assert.Equal(1, settled.Value.Frames);
        Assert.False(g.IsResizing);
    }

    [Fact]
    public void ExitWithoutEnter_IsIgnored()
    {
        var g = new ResizeGesture();

        Assert.Null(g.ExitSizeMove());
        Assert.False(g.IsResizing);
    }

    [Fact]
    public void FrameCount_ResetsBetweenGestures()
    {
        var g = new ResizeGesture();

        g.EnterSizeMove();
        g.Size();
        g.Size();
        Assert.Equal(2, g.ExitSizeMove()!.Value.Frames);

        g.EnterSizeMove();
        g.Size();
        Assert.Equal(1, g.ExitSizeMove()!.Value.Frames);
    }

    [Fact]
    public void DirectSize_StillWorksAfterAGesture()
    {
        // State must fully reset so a later programmatic resize is not mistaken
        // for part of the previous gesture.
        var g = new ResizeGesture();
        g.EnterSizeMove();
        g.Size();
        g.ExitSizeMove();

        var settled = g.Size();

        Assert.NotNull(settled);
        Assert.Equal(ResizeTrigger.Direct, settled!.Value.Trigger);
        Assert.False(g.IsResizing);
    }
}
