using Xunit;

namespace Deckle.Shell.Tests;

// Which mark the tray shows for what the app is doing.
public sealed class TrayMarkTests
{
    [Theory]
    [InlineData(false, false, MarkState.Rest)]
    [InlineData(true,  false, MarkState.Recording)]
    [InlineData(false, true,  MarkState.Rewriting)]
    public void TheMarkFollowsTheWorkInProgress(bool recording, bool rewriting, MarkState expected)
        => Assert.Equal(expected, TrayIconManager.MarkFor(recording, rewriting));

    [Fact]
    public void AnOpenMicrophoneShowsOverARewriteThatOverlapsIt()
        => Assert.Equal(MarkState.Recording, TrayIconManager.MarkFor(recording: true, rewriting: true));
}
