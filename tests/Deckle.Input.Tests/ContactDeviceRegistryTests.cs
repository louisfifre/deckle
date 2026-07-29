using Deckle.Input;
using Xunit;

namespace Deckle.Input.Tests;

[Trait("Category", "unit")]
public sealed class ContactDeviceRegistryTests
{
    [Fact]
    public void DeviceSeenBetweenSnapshotAndArmBelongsToTheSession()
    {
        var registry = new ContactDeviceRegistry();
        TouchpadDevice arrived = Device(handle: 42, productId: 7);

        Assert.False(registry.Observe(
            arrived,
            preservePreviousIdentity: false,
            out _));
        registry.StartSession([]);

        Assert.True(registry.TryGet(arrived.Handle, out RegisteredTouchpad registered));
        Assert.Equal(0, registered.Index);
        Assert.Equal(arrived.Capabilities, registered.Capabilities);
    }

    [Fact]
    public void ReusedHandleGetsANewIdentityAfterCaptureHasStarted()
    {
        var registry = new ContactDeviceRegistry();
        TouchpadDevice original = Device(handle: 42, productId: 7);
        TouchpadDevice replacement = Device(handle: 42, productId: 8);
        registry.StartSession([original]);

        Assert.True(registry.Observe(
            replacement,
            preservePreviousIdentity: true,
            out RegisteredTouchpad registered));

        Assert.Equal(1, registered.Index);
        Assert.Equal((uint)8, registered.Capabilities.ProductId);
        Assert.True(registry.TryGet(replacement.Handle, out RegisteredTouchpad current));
        Assert.Equal(registered, current);
    }

    [Fact]
    public void ReusedHandleUpdatesInPlaceBeforeTheFirstFrame()
    {
        var registry = new ContactDeviceRegistry();
        TouchpadDevice original = Device(handle: 42, productId: 7);
        TouchpadDevice replacement = Device(handle: 42, productId: 8);
        registry.StartSession([original]);

        Assert.False(registry.Observe(
            replacement,
            preservePreviousIdentity: false,
            out RegisteredTouchpad registered));

        Assert.Equal(0, registered.Index);
        Assert.Equal((uint)8, registered.Capabilities.ProductId);
    }

    private static TouchpadDevice Device(long handle, uint productId) =>
        new(
            new IntPtr(handle),
            new TouchpadCapabilities(
                DeviceName: $"device-{productId}",
                VendorId: 1,
                ProductId: productId,
                XMin: 0,
                XMax: 1_000,
                YMin: 0,
                YMax: 1_000,
                ContactSlots: 5,
                ReportByteLength: 64));
}
