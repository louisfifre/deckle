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
            step => { Assert.Equal(2000, step.FromChars); Assert.Equal("c", step.ProfileId); },
            step => { Assert.Equal(10000, step.FromChars); Assert.Equal("b", step.ProfileId); });
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

    // 4 and 5 minutes both snap to 3,000 characters: the second moves up a
    // stop instead of losing its step.
    [Fact]
    public void TwoThresholdsOnOneStopBothKeepAStep()
    {
        var ladder = new LengthSwitchSettings
        {
            Steps =
            [
                new LengthStep { FromChars = 4 * 700, ProfileId = "four" },
                new LengthStep { FromChars = 5 * 700, ProfileId = "five" },
            ],
        };

        LlmSettingsMigrations.NormalizeLadder(ladder);

        Assert.Collection(ladder.Steps,
            step => { Assert.Equal(3000, step.FromChars); Assert.Equal("four", step.ProfileId); },
            step => { Assert.Equal(4000, step.FromChars); Assert.Equal("five", step.ProfileId); });
    }

    [Fact]
    public void TheLadderIsKeptOnStopsStrictlyIncreasing()
    {
        var ladder = new LengthSwitchSettings
        {
            Steps =
            [
                new LengthStep { FromChars = 10100, ProfileId = "x" },
                new LengthStep { FromChars = 1900, ProfileId = "y" },
                new LengthStep { FromChars = 2100, ProfileId = "z" },
            ],
        };

        Assert.True(LlmSettingsMigrations.NormalizeLadder(ladder));

        Assert.Collection(ladder.Steps,
            step => { Assert.Equal(2000, step.FromChars); Assert.Equal("y", step.ProfileId); },
            step => { Assert.Equal(3000, step.FromChars); Assert.Equal("z", step.ProfileId); },
            step => { Assert.Equal(10000, step.FromChars); Assert.Equal("x", step.ProfileId); });
    }
}
