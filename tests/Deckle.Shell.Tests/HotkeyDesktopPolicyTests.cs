using Deckle.Shell;
using Xunit;

namespace Deckle.Shell.Tests;

// Contract: the hotkeys follow the screen saver's desktop only, and never
// past a lock — neither the secure screen saver nor the Winlogon desktop.
[Trait("Category", "unit")]
public sealed class HotkeyDesktopPolicyTests
{
    [Fact]
    public void FollowsTheNonSecureScreenSaver() =>
        Assert.True(HotkeyDesktopPolicy.Follows("Screen-saver", screenSaverSecure: false));

    [Fact]
    public void StaysOffTheSecureScreenSaver() =>
        Assert.False(HotkeyDesktopPolicy.Follows("Screen-saver", screenSaverSecure: true));

    [Theory]
    [InlineData("Default")]
    [InlineData("Winlogon")]
    [InlineData("")]
    public void StaysOnEveryOtherDesktop(string desktop) =>
        Assert.False(HotkeyDesktopPolicy.Follows(desktop, screenSaverSecure: false));
}
