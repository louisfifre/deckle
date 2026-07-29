using System.Runtime.InteropServices;
using Deckle.Input;
using Xunit;

namespace Deckle.Input.Tests;

[Trait("Category", "unit")]
public sealed class SyntheticTouchpadFrameBuilderTests
{
    [Fact]
    public void InteropLayoutsMatchWinuserOnX64()
    {
        Assert.Equal(40, Marshal.SizeOf<SyntheticTouchpadInterop.SyntheticDeviceCreationParams>());
        Assert.Equal(44, Marshal.SizeOf<TouchpadParametersInterop.TouchpadParameters>());
        Assert.Equal(96, Marshal.SizeOf<SyntheticTouchpadInterop.PointerInfo>());
        Assert.Equal(144, Marshal.SizeOf<SyntheticTouchpadInterop.PointerTouchInfo>());
        Assert.Equal(152, Marshal.SizeOf<SyntheticTouchpadInterop.PointerTypeInfo>());
    }

    [Fact]
    public void GestureFramesCarryBothContactsAcrossDownMoveAndLift()
    {
        var builder = new SyntheticTouchpadFrameBuilder();

        var down = builder.Begin(new(3_000, 5_000), new(7_000, 5_000)).ToArray();
        var move = builder.Move(new(3_000, 4_000), new(7_000, 4_000), elapsedMs: 10).ToArray();
        var lift = builder.End(elapsedMs: 10).ToArray();

        Assert.True(builder.IsContactActive is false);
        Assert.Equal([0u, 1u], down.Select(ContactId));
        Assert.All(down, contact => Assert.Equal(0u, contact.TouchInfo.PointerInfo.PointerType));
        Assert.All(down, contact => Assert.Equal(1u, ContactTime(contact)));
        Assert.All(move, contact => Assert.Equal(11u, ContactTime(contact)));
        Assert.All(lift, contact => Assert.Equal(21u, ContactTime(contact)));
        Assert.Equal([4_000, 4_000], lift.Select(ContactY));
        Assert.All(down, contact => Assert.True(IsInContact(contact)));
        Assert.All(move, contact => Assert.True(IsInContact(contact)));
        Assert.All(lift, contact => Assert.False(IsInContact(contact)));
    }

    [Fact]
    public void MoveBeforeBeginIsRejected()
    {
        var builder = new SyntheticTouchpadFrameBuilder();

        Assert.Throws<InvalidOperationException>(() =>
            builder.Move(new(3_000, 4_000), new(7_000, 4_000), elapsedMs: 10));
    }

    private static uint ContactId(SyntheticTouchpadInterop.PointerTypeInfo info) =>
        info.TouchInfo.PointerInfo.PointerId;

    private static uint ContactTime(SyntheticTouchpadInterop.PointerTypeInfo info) =>
        info.TouchInfo.PointerInfo.Time;

    private static int ContactY(SyntheticTouchpadInterop.PointerTypeInfo info) =>
        info.TouchInfo.PointerInfo.HimetricLocation.Y;

    private static bool IsInContact(SyntheticTouchpadInterop.PointerTypeInfo info) =>
        (info.TouchInfo.PointerInfo.PointerFlags
            & SyntheticTouchpadInterop.POINTER_FLAG_INCONTACT) != 0;
}
