using Deckle.Input.Trackpad;
using Xunit;

namespace Deckle.Input.Trackpad.Tests;

// TrackpadEngine copies the person's press guards (delay and distance) into
// its recognizer before every frame and every deadline. The engine itself
// needs a live Raw Input host, so these tests drive the copy it performs,
// which is where the values are read, converted and bounded.
[Trait("Category", "unit")]
public class TrackpadEnginePressDelayTests
{
    // Mirrors TrackpadEngine.BaseScale: one finger unit moves the cursor this
    // many pixels at drag speed 1.
    private const double BaseScale = 0.25;

    [Fact]
    public void ChangedSettingReachesTheRecognizerOnTheNextApply()
    {
        var recognizer = new ThreeFingerDragRecognizer();
        var settings = new TrackpadSettings();

        TrackpadEngine.ApplyPressGuards(recognizer, settings);
        Assert.Equal(50, recognizer.PressDelayMs);

        settings.PressDelayMs = 80;
        TrackpadEngine.ApplyPressGuards(recognizer, settings);
        Assert.Equal(80, recognizer.PressDelayMs);
    }

    [Theory]
    [InlineData(-10, TrackpadSettings.MinPressDelayMs)]
    [InlineData(5000, TrackpadSettings.MaxPressDelayMs)]
    public void HandEditedValueOutsideThePageRangeIsBounded(double stored, double applied)
    {
        var recognizer = new ThreeFingerDragRecognizer();

        TrackpadEngine.ApplyPressGuards(recognizer, new TrackpadSettings { PressDelayMs = stored });

        Assert.Equal(applied, recognizer.PressDelayMs);
    }

    // The distance is set in cursor pixels; a faster drag covers them with
    // less finger travel, so the recognizer's threshold shrinks with speed.
    [Theory]
    [InlineData(5, 1.0, 20)]
    [InlineData(5, 2.0, 10)]
    [InlineData(0, 1.0, 0)]
    public void DistanceInPixelsBecomesFingerUnitsThroughTheDragSpeed(double px, double speed, double units)
    {
        var recognizer = new ThreeFingerDragRecognizer();

        TrackpadEngine.ApplyPressGuards(recognizer,
            new TrackpadSettings { PressDistancePx = px, DragSpeed = speed });

        Assert.Equal(units, recognizer.PressDistanceUnits, precision: 6);
    }

    [Theory]
    [InlineData(-3, TrackpadSettings.MinPressDistancePx)]
    [InlineData(900, TrackpadSettings.MaxPressDistancePx)]
    public void HandEditedDistanceOutsideThePageRangeIsBounded(double stored, double appliedPx)
    {
        var recognizer = new ThreeFingerDragRecognizer();

        TrackpadEngine.ApplyPressGuards(recognizer,
            new TrackpadSettings { PressDistancePx = stored, DragSpeed = 1.0 });

        Assert.Equal(appliedPx / BaseScale, recognizer.PressDistanceUnits, precision: 6);
    }
}
