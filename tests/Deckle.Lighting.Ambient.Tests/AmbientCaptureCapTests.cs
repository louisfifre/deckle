using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The capture rate is 60 frames a second, whatever the display refreshes at:
// no setting and no screen lookup stand between the constant and the capture.
[Trait("Category", "unit")]
public sealed class AmbientCaptureCapTests
{
    [Fact]
    public void TheCaptureRateIs60FramesASecond()
        => Assert.Equal(60, AmbientCaptureCap.CaptureHz);
}
