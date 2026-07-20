using Deckle.Input;
using Xunit;

namespace Deckle.Input.Tests;

// Behavioral contract for the native focus boundary: one Windows transition
// reaches consumers once, while a genuinely different target remains visible.
[Trait("Category", "unit")]
public sealed class FocusEventCoalescerTests
{
    private static readonly IntPtr Window = new(42);

    [Fact]
    public void ForegroundAndObjectFocusForOneWindowBothPublish()
    {
        var events = new FocusEventCoalescer();

        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_SYSTEM_FOREGROUND, Window, objectId: 0, childId: 0, timestamp: 100));
        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: -4, childId: 7, timestamp: 101));
    }

    [Fact]
    public void DifferentEventTypesWithOneNativeTargetBothPublish()
    {
        var events = new FocusEventCoalescer();

        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_SYSTEM_FOREGROUND, Window, objectId: 0, childId: 0, timestamp: 100));
        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: 0, childId: 0, timestamp: 101));
    }

    [Fact]
    public void RepeatedObjectFocusForOneTargetPublishesOnce()
    {
        var events = new FocusEventCoalescer();

        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: -4, childId: 7, timestamp: 100));
        Assert.False(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: -4, childId: 7, timestamp: 102));
    }

    [Fact]
    public void DifferentFocusedObjectsInOneWindowBothPublish()
    {
        var events = new FocusEventCoalescer();

        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: -4, childId: 7, timestamp: 100));
        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: -4, childId: 8, timestamp: 101));
    }

    [Fact]
    public void ObjectAfterTheForegroundPairStillPublishes()
    {
        var events = new FocusEventCoalescer();

        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_SYSTEM_FOREGROUND, Window, objectId: 0, childId: 0, timestamp: 100));
        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: -4, childId: 7, timestamp: 101));
        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: -4, childId: 8, timestamp: 102));
    }

    [Fact]
    public void ForegroundAfterObjectFocusStartsANewTransition()
    {
        var events = new FocusEventCoalescer();

        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: -4, childId: 7, timestamp: 100));
        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_SYSTEM_FOREGROUND, Window, objectId: 0, childId: 0, timestamp: 101));
    }

    [Fact]
    public void SameTargetAfterTheBurstPublishesAgain()
    {
        var events = new FocusEventCoalescer();

        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: -4, childId: 7, timestamp: 100));
        Assert.True(events.ShouldPublish(
            WinEventInterop.EVENT_OBJECT_FOCUS, Window, objectId: -4, childId: 7,
            timestamp: 100 + FocusEventCoalescer.WindowMilliseconds + 1));
    }
}
