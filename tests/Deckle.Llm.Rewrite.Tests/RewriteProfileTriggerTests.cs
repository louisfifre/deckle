using System.Linq;
using System.Text.Json;
using Deckle.Core;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// What starts a profile, the length ladder and a profile's shortcut, survives
// the settings file.
[Trait("Category", "unit")]
public sealed class RewriteProfileTriggerTests
{
    // The options LlmSettingsService writes the file with.
    private static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static LlmSettings RoundTrip(LlmSettings settings) =>
        JsonSerializer.Deserialize<LlmSettings>(JsonSerializer.Serialize(settings, FileOptions), FileOptions)!;

    [Fact]
    public void LadderAndShortcutRoundTripThroughTheSettingsFile()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.Apply(settings);
        settings.LengthSwitch.Enabled = true;
        settings.LengthSwitch.Steps.Add(new LengthStep { FromChars = 3000, ProfileId = settings.Profiles[1].Id });
        settings.Profiles[0].HotkeyEnabled = true;
        settings.Profiles[0].Hotkey = new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Win, 0x29);

        LlmSettings read = RoundTrip(settings);

        Assert.True(read.LengthSwitch.Enabled);
        LengthStep step = Assert.Single(read.LengthSwitch.Steps);
        Assert.Equal(3000, step.FromChars);
        Assert.Equal(settings.Profiles[1].Id, step.ProfileId);
        Assert.True(read.Profiles[0].HasActiveHotkey);
        Assert.Equal(settings.Profiles[0].Hotkey, read.Profiles[0].Hotkey);
        Assert.False(read.Profiles[1].HasActiveHotkey);
    }

    // A length typed in the field is any value on its grain, not one of a
    // few presets: the next load keeps it as it is.
    [Fact]
    public void ATypedLengthSurvivesTheNextLoad()
    {
        var ladder = new LengthSwitchSettings
        {
            Steps =
            [
                new LengthStep { FromChars = 2300, ProfileId = "a" },
                new LengthStep { FromChars = 2400, ProfileId = "b" },
                new LengthStep { FromChars = 17500, ProfileId = "c" },
            ],
        };

        Assert.False(LlmSettingsNormalizer.NormalizeLadder(ladder));

        Assert.Equal([2300, 2400, 17500], ladder.Steps.Select(step => step.FromChars));
    }

    // The grain grows with the length: fine on a short dictation, coarse on
    // a long one, where a hundred characters change nothing.
    [Theory]
    [InlineData(2345, 2300)]
    [InlineData(3960, 4000)]
    [InlineData(4321, 4400)]
    [InlineData(12345, 12500)]
    [InlineData(23456, 23000)]
    [InlineData(40, LengthSwitchSettings.MinChars)]
    [InlineData(99999, LengthSwitchSettings.MaxChars)]
    public void ALengthSnapsToTheGrainOfItsSize(int typed, int expected)
    {
        Assert.Equal(expected, LengthSwitchSettings.Snap(typed));
    }

    // A snapped length snaps to itself, across the band edges too, and its
    // neighbours on either side are snapped lengths as well: what the field
    // shows is what the next load reads.
    [Fact]
    public void SnappingIsStableAcrossTheWholeRange()
    {
        for (int chars = 0; chars <= LengthSwitchSettings.MaxChars + 1000; chars += 7)
        {
            int snapped = LengthSwitchSettings.Snap(chars);
            Assert.Equal(snapped, LengthSwitchSettings.Snap(snapped));
            if (snapped < LengthSwitchSettings.MaxChars)
                Assert.Equal(LengthSwitchSettings.Above(snapped), LengthSwitchSettings.Snap(LengthSwitchSettings.Above(snapped)));
            if (snapped > LengthSwitchSettings.MinChars)
                Assert.Equal(LengthSwitchSettings.Below(snapped), LengthSwitchSettings.Snap(LengthSwitchSettings.Below(snapped)));
        }
    }

    // Two thresholds on one value: the second moves just above the first
    // instead of losing its step.
    [Fact]
    public void TwoThresholdsOnOneValueBothKeepAStep()
    {
        var ladder = new LengthSwitchSettings
        {
            Steps =
            [
                new LengthStep { FromChars = 2000, ProfileId = "first" },
                new LengthStep { FromChars = 2040, ProfileId = "second" },
            ],
        };

        LlmSettingsNormalizer.NormalizeLadder(ladder);

        Assert.Collection(ladder.Steps,
            step => { Assert.Equal(2000, step.FromChars); Assert.Equal("first", step.ProfileId); },
            step => { Assert.Equal(2100, step.FromChars); Assert.Equal("second", step.ProfileId); });
    }

    [Fact]
    public void TheLadderIsKeptOnTheGrainWithinRangeStrictlyIncreasing()
    {
        var ladder = new LengthSwitchSettings
        {
            Steps =
            [
                new LengthStep { FromChars = 10240, ProfileId = "x" },
                new LengthStep { FromChars = 1950, ProfileId = "y" },
                new LengthStep { FromChars = 40, ProfileId = "z" },
                new LengthStep { FromChars = 99999, ProfileId = "w" },
            ],
        };

        Assert.True(LlmSettingsNormalizer.NormalizeLadder(ladder));

        Assert.Collection(ladder.Steps,
            step => { Assert.Equal(LengthSwitchSettings.MinChars, step.FromChars); Assert.Equal("z", step.ProfileId); },
            step => { Assert.Equal(2000, step.FromChars); Assert.Equal("y", step.ProfileId); },
            step => { Assert.Equal(10000, step.FromChars); Assert.Equal("x", step.ProfileId); },
            step => { Assert.Equal(LengthSwitchSettings.MaxChars, step.FromChars); Assert.Equal("w", step.ProfileId); });
    }

    // Only a step pushed past the maximum by the one below it is lost.
    [Fact]
    public void AStepPushedPastTheMaximumIsDropped()
    {
        var ladder = new LengthSwitchSettings
        {
            Steps =
            [
                new LengthStep { FromChars = LengthSwitchSettings.MaxChars, ProfileId = "top" },
                new LengthStep { FromChars = 40000, ProfileId = "over" },
            ],
        };

        LlmSettingsNormalizer.NormalizeLadder(ladder);

        LengthStep kept = Assert.Single(ladder.Steps);
        Assert.Equal("top", kept.ProfileId);
    }
}
