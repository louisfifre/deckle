using Deckle.Input;
using Xunit;

namespace Deckle.Input.Tests;

[Trait("Category", "unit")]
public class LowLevelMouseHookInteropTests
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
}
