using Deckle.Shell.Foreground;
using Xunit;

namespace Deckle.Shell.Foreground.Tests;

// Behaviour of FullscreenNotificationState: only the answers that can belong
// to the foreground window read as fullscreen.
[Trait("Category", "unit")]
public class FullscreenNotificationStateTests
{
    [Theory]
    [InlineData(FullscreenNotificationState.QUNS_RUNNING_D3D_FULL_SCREEN)]
    [InlineData(FullscreenNotificationState.QUNS_PRESENTATION_MODE)]
    public void An_exclusive_app_or_presentation_mode_reads_as_fullscreen(int quns)
    {
        Assert.True(FullscreenNotificationState.ReadsAsFullscreen(quns));
    }

    // QUNS_BUSY says a fullscreen app runs somewhere in the session, not
    // that the foreground window is one.
    [Fact]
    public void A_busy_session_alone_does_not_read_as_fullscreen()
    {
        Assert.False(FullscreenNotificationState.ReadsAsFullscreen(FullscreenNotificationState.QUNS_BUSY));
    }

    // 1 is the screen saver or the lock screen, 5 an ordinary session,
    // 6 quiet time.
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    public void Every_other_answer_reads_as_windowed(int quns)
    {
        Assert.False(FullscreenNotificationState.ReadsAsFullscreen(quns));
    }
}
