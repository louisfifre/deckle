using Deckle.Input.Trackpad;
using Xunit;

namespace Deckle.Input.Trackpad.Tests;

// TrackpadEngine copies the person's press delay into its recognizer before
// every frame and every deadline. The engine itself needs a live Raw Input
// host, so these tests drive the copy it performs, which is where the value
// is read and bounded.
[Trait("Category", "unit")]
public class TrackpadEnginePressDelayTests
{
    [Fact]
    public void ChangedSettingReachesTheRecognizerOnTheNextApply()
    {
        var recognizer = new ThreeFingerDragRecognizer();
        var settings = new TrackpadSettings();

        TrackpadEngine.ApplyPressDelay(recognizer, settings);
        Assert.Equal(50, recognizer.PressDelayMs);

        settings.PressDelayMs = 120;
        TrackpadEngine.ApplyPressDelay(recognizer, settings);
        Assert.Equal(120, recognizer.PressDelayMs);
    }

    [Theory]
    [InlineData(-10, TrackpadSettings.MinPressDelayMs)]
    [InlineData(5000, TrackpadSettings.MaxPressDelayMs)]
    public void HandEditedValueOutsideThePageRangeIsBounded(double stored, double applied)
    {
        var recognizer = new ThreeFingerDragRecognizer();

        TrackpadEngine.ApplyPressDelay(recognizer, new TrackpadSettings { PressDelayMs = stored });

        Assert.Equal(applied, recognizer.PressDelayMs);
    }
}
