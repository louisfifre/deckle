using Deckle.Input;
using Xunit;

namespace Deckle.Input.Tests;

[Trait("Category", "unit")]
public sealed class WheelObservationBufferTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HookAndRawInputPublishOneDeviceAwareTransition(bool hookFirst)
    {
        var buffer = new WheelObservationBuffer();
        MouseWheelEvent hook = Wheel(WheelEventSource.MessageHook, 100, IntPtr.Zero);
        MouseWheelEvent raw = Wheel(WheelEventSource.RawInput, 102, new IntPtr(42));

        MouseWheelEvent first = hookFirst ? hook : raw;
        MouseWheelEvent second = hookFirst ? raw : hook;

        Assert.False(buffer.Observe(in first, out _));
        Assert.True(buffer.Observe(in second, out MouseWheelEvent publish));
        Assert.Equal(WheelEventSource.RawInput, publish.Source);
        Assert.Equal(new IntPtr(42), publish.Device);
        Assert.False(buffer.HasPending);
    }

    [Fact]
    public void HookOnlyTransitionPublishesAfterTheCorrelationWindow()
    {
        var buffer = new WheelObservationBuffer();
        MouseWheelEvent hook = Wheel(WheelEventSource.MessageHook, 100, IntPtr.Zero);

        Assert.False(buffer.Observe(in hook, out _));
        Assert.False(buffer.TryDequeueExpired(139, out _));
        Assert.True(buffer.TryDequeueExpired(140, out MouseWheelEvent publish));
        Assert.Equal(WheelEventSource.MessageHook, publish.Source);
    }

    [Fact]
    public void DifferentPhysicalTransitionsRemainIndependent()
    {
        var buffer = new WheelObservationBuffer();
        MouseWheelEvent hook = Wheel(WheelEventSource.MessageHook, 100, IntPtr.Zero);
        MouseWheelEvent raw = Wheel(WheelEventSource.RawInput, 102, new IntPtr(42)) with
        {
            Delta = -120,
        };

        Assert.False(buffer.Observe(in hook, out _));
        Assert.False(buffer.Observe(in raw, out _));
        Assert.True(buffer.TryDequeueExpired(145, out MouseWheelEvent first));
        Assert.True(buffer.TryDequeueExpired(145, out MouseWheelEvent second));
        Assert.NotEqual(first.Source, second.Source);
    }

    private static MouseWheelEvent Wheel(
        WheelEventSource source,
        double timestampMs,
        IntPtr device) =>
        new(
            WheelAxis.Vertical,
            Delta: 120,
            timestampMs,
            device,
            source);
}
