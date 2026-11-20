using Xunit;

namespace Deckle.Shell.Tests;

// Which icon a window takes on the taskbar: the tray's mark without the
// incident dot, in the state the microphone is in.
public sealed class WindowIconTests
{
    [Theory]
    [InlineData(false, "deckle--state--rest.ico")]
    [InlineData(true,  "deckle--state--recording.ico")]
    public void TheWindowIconFollowsTheMicrophone(bool recording, string expected)
        => Assert.Equal(expected, IconAssets.WindowIconFile(recording));
}
