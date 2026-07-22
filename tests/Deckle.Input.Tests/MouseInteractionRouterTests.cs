using Deckle.Input;
using Xunit;

namespace Deckle.Input.Tests;

[Trait("Category", "unit")]
public sealed class MouseInteractionRouterTests
{
    [Fact]
    public void HookButtonWaitsForTheInputPumpBeforePublishing()
    {
        int queued = 0;
        int pointers = 0;
        var router = CreateRouter(
            queuePointer: () => queued++,
            publishPointer: () => pointers++);

        bool intercepted = router.ObserveHookMessage(
            LowLevelMouseHookInterop.WM_LBUTTONDOWN, mouseData: 0);

        Assert.False(intercepted);
        Assert.Equal(1, queued);
        Assert.Equal(0, pointers);

        router.PublishQueuedButtonDown();

        Assert.Equal(1, pointers);
    }

    [Fact]
    public void RawInputPublishesWhenTheHookIsUnavailable()
    {
        int pointers = 0;
        var router = CreateRouter(publishPointer: () => pointers++);

        router.ObserveRawButtonDown(hookInstalled: false);

        Assert.Equal(1, pointers);
    }

    [Fact]
    public void HookAndRawInputForOneClickPublishOnce()
    {
        int queued = 0;
        int pointers = 0;
        var router = CreateRouter(
            queuePointer: () => queued++,
            publishPointer: () => pointers++);

        router.ObserveHookMessage(LowLevelMouseHookInterop.WM_LBUTTONDOWN, mouseData: 0);
        router.ObserveRawButtonDown(hookInstalled: true);
        router.PublishQueuedButtonDown();

        Assert.Equal(1, queued);
        Assert.Equal(1, pointers);
    }

    [Theory]
    [InlineData(LowLevelMouseHookInterop.WM_MOUSEWHEEL, WheelAxis.Vertical)]
    [InlineData(LowLevelMouseHookInterop.WM_MOUSEHWHEEL, WheelAxis.Horizontal)]
    public void WheelMessagePublishesOnlyItsWheelAxis(int message, WheelAxis expectedAxis)
    {
        int queued = 0;
        int pointers = 0;
        var wheels = new List<(WheelAxis Axis, short Delta)>();
        var router = new MouseInteractionRouter(
            () => queued++,
            () => pointers++,
            (axis, delta, _) =>
            {
                wheels.Add((axis, delta));
                return false;
            });

        bool intercepted = router.ObserveHookMessage(message, 0x00780000u);

        Assert.False(intercepted);
        Assert.Equal(0, queued);
        Assert.Equal(0, pointers);
        Assert.Equal([(expectedAxis, (short)120)], wheels);
    }

    [Fact]
    public void WheelPolicyCanConsumeTheHookMessage()
    {
        uint observedFlags = 0;
        var router = new MouseInteractionRouter(
            () => { },
            () => { },
            (_, _, flags) =>
            {
                observedFlags = flags;
                return true;
            });

        bool intercepted = router.ObserveHookMessage(
            LowLevelMouseHookInterop.WM_MOUSEWHEEL,
            0xFF880000u,
            LowLevelMouseHookInterop.LLMHF_INJECTED);

        Assert.True(intercepted);
        Assert.Equal(LowLevelMouseHookInterop.LLMHF_INJECTED, observedFlags);
    }

    private static MouseInteractionRouter CreateRouter(
        Action? queuePointer = null,
        Action? publishPointer = null) =>
        new(
            queuePointer ?? (() => { }),
            publishPointer ?? (() => { }),
            (_, _, _) => false);
}
