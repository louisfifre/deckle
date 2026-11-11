using Xunit;

namespace Deckle.Shell.Tests;

// Which icon a window takes on the taskbar: the tile made for the Windows
// mode, in the state the microphone is in.
public sealed class WindowIconTests
{
    [Theory]
    [InlineData(false, false, "deckle--state--rest--mode--dark.ico")]
    [InlineData(false, true,  "deckle--state--rest--mode--light.ico")]
    [InlineData(true,  false, "deckle--state--recording--mode--dark.ico")]
    [InlineData(true,  true,  "deckle--state--recording--mode--light.ico")]
    public void TheWindowIconFollowsTheMicrophoneAndTheWindowsMode(bool recording, bool lightMode, string expected)
        => Assert.Equal(expected, IconAssets.WindowIconFile(recording, lightMode));

    [Fact]
    public void AWindowsModeSetToLightReadsAsLight()
        => Assert.True(WindowsMode.IsLight(1));

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    [InlineData("1")]
    public void AnythingElseReadsAsDark(object? registryValue)
        => Assert.False(WindowsMode.IsLight(registryValue));

    [Theory]
    [InlineData("ImmersiveColorSet", true)]
    [InlineData("Environment", false)]
    [InlineData(null, false)]
    public void OnlyAColourSettingCountsAsAModeChange(string? changedSetting, bool expected)
        => Assert.Equal(expected, WindowsMode.IsModeChange(changedSetting));
}
