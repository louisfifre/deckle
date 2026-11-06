using System.Linq;
using System.Text.Json;
using Deckle.Catalog;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// What starts a profile, the length ladder and a profile's shortcut, survives
// the settings file, and the per-profile thresholds of v0.77 to v0.79 move
// into the ladder once.
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
        LlmSettingsMigrations.Apply(settings);
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

    [Fact]
    public void AFileWrittenBeforeTheLadderLeavesItOffAndEmpty()
    {
        LlmSettings read = JsonSerializer.Deserialize<LlmSettings>(
            """{ "profiles": [ { "id": "a", "name": "Old" } ] }""", FileOptions)!;
        LlmSettingsMigrations.Apply(read);

        Assert.False(read.LengthSwitch.Enabled);
        Assert.Empty(read.LengthSwitch.Steps);
        Assert.False(read.Profiles[0].HasActiveHotkey);
    }

    [Fact]
    public void PerProfileThresholdsMoveIntoTheLadderOnce()
    {
        LlmSettings read = JsonSerializer.Deserialize<LlmSettings>(
            """
            { "profiles": [
                { "id": "a", "name": "Base" },
                { "id": "b", "name": "Long", "autoSelect": true, "autoSelectFromMinutes": 15 },
                { "id": "c", "name": "Short", "autoSelect": true, "autoSelectFromMinutes": 3 } ] }
            """, FileOptions)!;

        Assert.True(LlmSettingsMigrations.Apply(read));

        Assert.True(read.LengthSwitch.Enabled);
        Assert.Collection(read.LengthSwitch.Steps,
            step => { Assert.Equal(3 * 700, step.FromChars); Assert.Equal("c", step.ProfileId); },
            step => { Assert.Equal(15 * 700, step.FromChars); Assert.Equal("b", step.ProfileId); });
        Assert.All(read.Profiles, profile => Assert.False(profile.AutoSelect));

        Assert.False(LlmSettingsMigrations.Apply(read));
        Assert.Equal(2, read.LengthSwitch.Steps.Count);
    }

    [Fact]
    public void AnExistingChordKeepsWorkingAfterTheSwitchArrives()
    {
        // A v0.77 file: a chord, and no switch (HotkeyEnabled absent → null).
        var written = new LlmSettings
        {
            Profiles =
            [
                new RewriteProfile { Id = "a", Name = "Bound",
                    Hotkey = new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Win, 0x29) },
                new RewriteProfile { Id = "b", Name = "Unbound" },
            ],
        };
        LlmSettings read = RoundTrip(written);
        Assert.Null(read.Profiles[0].HotkeyEnabled);

        LlmSettingsMigrations.Apply(read);

        Assert.True(read.Profiles[0].HotkeyEnabled);
        Assert.False(read.Profiles[1].HotkeyEnabled);
    }

    // A length typed in the field is any hundred, not one of a few presets:
    // the next load keeps it as it is.
    [Fact]
    public void ATypedLengthSurvivesTheNextLoad()
    {
        var ladder = new LengthSwitchSettings
        {
            Steps =
            [
                new LengthStep { FromChars = 2300, ProfileId = "a" },
                new LengthStep { FromChars = 2400, ProfileId = "b" },
                new LengthStep { FromChars = 17800, ProfileId = "c" },
            ],
        };

        Assert.False(LlmSettingsMigrations.NormalizeLadder(ladder));

        Assert.Equal([2300, 2400, 17800], ladder.Steps.Select(step => step.FromChars));
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

        LlmSettingsMigrations.NormalizeLadder(ladder);

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
                new LengthStep { FromChars = 10149, ProfileId = "x" },
                new LengthStep { FromChars = 1950, ProfileId = "y" },
                new LengthStep { FromChars = 40, ProfileId = "z" },
                new LengthStep { FromChars = 99999, ProfileId = "w" },
            ],
        };

        Assert.True(LlmSettingsMigrations.NormalizeLadder(ladder));

        Assert.Collection(ladder.Steps,
            step => { Assert.Equal(LengthSwitchSettings.MinChars, step.FromChars); Assert.Equal("z", step.ProfileId); },
            step => { Assert.Equal(2000, step.FromChars); Assert.Equal("y", step.ProfileId); },
            step => { Assert.Equal(10100, step.FromChars); Assert.Equal("x", step.ProfileId); },
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

        LlmSettingsMigrations.NormalizeLadder(ladder);

        LengthStep kept = Assert.Single(ladder.Steps);
        Assert.Equal("top", kept.ProfileId);
    }
}
