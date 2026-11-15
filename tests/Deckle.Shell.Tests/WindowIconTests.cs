using Xunit;

namespace Deckle.Shell.Tests;

// Which icon a window takes on the taskbar: the normal form, in the state the
// microphone is in.
public sealed class WindowIconTests
{
    [Theory]
    [InlineData(false, "deckle--state--rest--form--normal.ico")]
    [InlineData(true,  "deckle--state--recording--form--normal.ico")]
    public void TheWindowIconFollowsTheMicrophone(bool recording, string expected)
        => Assert.Equal(expected, IconAssets.WindowIconFile(recording));
}
