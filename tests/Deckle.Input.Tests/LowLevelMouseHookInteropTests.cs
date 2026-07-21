using Deckle.Input;
using Xunit;

namespace Deckle.Input.Tests;

[Trait("Category", "unit")]
public sealed class LowLevelMouseHookInteropTests
{
    [Theory]
    [InlineData(0x00780000u, 120)]
    [InlineData(0xFF880000u, -120)]
    [InlineData(0x00010000u, 1)]
    [InlineData(0xFFFF0000u, -1)]
    public void GetWheelDeltaReturnsSignedHighWord(uint mouseData, short expected)
    {
        Assert.Equal(expected, LowLevelMouseHookInterop.GetWheelDelta(mouseData));
    }

    [Theory]
    [InlineData(LowLevelMouseHookInterop.WM_LBUTTONDOWN)]
    [InlineData(LowLevelMouseHookInterop.WM_RBUTTONDOWN)]
    [InlineData(LowLevelMouseHookInterop.WM_MBUTTONDOWN)]
    [InlineData(LowLevelMouseHookInterop.WM_XBUTTONDOWN)]
    public void EveryButtonDownMessageIsAPointerInteraction(int message)
    {
        Assert.True(LowLevelMouseHookInterop.IsButtonDown(message));
    }

    [Theory]
    [InlineData(LowLevelMouseHookInterop.WM_MOUSEWHEEL)]
    [InlineData(LowLevelMouseHookInterop.WM_MOUSEHWHEEL)]
    [InlineData(0x0200)] // WM_MOUSEMOVE
    public void MovementAndWheelMessagesAreNotPointerInteractions(int message)
    {
        Assert.False(LowLevelMouseHookInterop.IsButtonDown(message));
    }
}
