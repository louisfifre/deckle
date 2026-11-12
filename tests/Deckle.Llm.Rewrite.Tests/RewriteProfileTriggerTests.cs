using System.Linq;
using System.Text.Json;
using Deckle.Core;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// What starts a profile, the dictation steps and a profile's shortcut,
// survives the settings file.
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
    public void StepsAndShortcutRoundTripThroughTheSettingsFile()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.Apply(settings);
        settings.Dictation.Enabled = true;
        settings.Dictation.RewriteWhileSpeaking = true;
        settings.Dictation.Steps.Add(new RewriteStep { FromChars = 3000, ProfileId = settings.Profiles[1].Id, BlockChars = 6000 });
        settings.Profiles[0].HotkeyEnabled = true;
        settings.Profiles[0].Hotkey = new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Win, 0x29);

        LlmSettings read = RoundTrip(settings);

        Assert.True(read.Dictation.Enabled);
        Assert.True(read.Dictation.RewriteWhileSpeaking);
        Assert.Equal(2, read.Dictation.Steps.Count);
        RewriteStep step = read.Dictation.Steps[1];
        Assert.Equal((3000, settings.Profiles[1].Id, 6000), (step.FromChars, step.ProfileId, step.BlockChars));
        Assert.True(read.Profiles[0].HasActiveHotkey);
        Assert.Equal(settings.Profiles[0].Hotkey, read.Profiles[0].Hotkey);
        Assert.False(read.Profiles[1].HasActiveHotkey);
    }

    // The fields from before the steps are read to migrate, never written.
    [Fact]
    public void TheFieldsFromBeforeTheStepsAreNotWritten()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.Apply(settings);

        string json = JsonSerializer.Serialize(settings, FileOptions);

        Assert.DoesNotContain("activeProfileId", json);
        Assert.DoesNotContain("lengthSwitch", json);
        Assert.DoesNotContain("batchRewriting", json);
        Assert.DoesNotContain("startAtLength", json);
    }

    // A length typed in the field is any value on its grain, not one of a
    // few presets: the next load keeps it as it is.
    [Fact]
    public void ATypedLengthSurvivesTheNextLoad()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.Apply(settings);
        settings.Dictation.Steps =
        [
            new RewriteStep { FromChars = 1000, ProfileId = "a", BlockChars = 600 },
            new RewriteStep { FromChars = 2400, ProfileId = "b", BlockChars = 600 },
            new RewriteStep { FromChars = 17500, ProfileId = "c", BlockChars = 600 },
        ];

        Assert.False(LlmSettingsNormalizer.NormalizeDictation(settings));

        Assert.Equal([1000, 2400, 17500], settings.Dictation.Steps.Select(step => step.FromChars));
    }

    // The grain grows with the length: fine on a short dictation, coarse on
    // a long one, where a hundred characters change nothing.
    [Theory]
    [InlineData(2345, 2300)]
    [InlineData(3960, 4000)]
    [InlineData(4321, 4400)]
    [InlineData(12345, 12500)]
    [InlineData(23456, 23000)]
    [InlineData(40, 0)]
    [InlineData(60, DictationRewriteSettings.MinChars)]
    [InlineData(99999, DictationRewriteSettings.MaxChars)]
    public void ALengthSnapsToTheGrainOfItsSize(int typed, int expected)
    {
        Assert.Equal(expected, DictationRewriteSettings.Snap(typed));
    }

    // A snapped length snaps to itself, across the band edges too, and its
    // neighbours on either side are snapped lengths as well: what the field
    // shows is what the next load reads.
    [Fact]
    public void SnappingIsStableAcrossTheWholeRange()
    {
        for (int chars = 0; chars <= DictationRewriteSettings.MaxChars + 1000; chars += 7)
        {
            int snapped = DictationRewriteSettings.Snap(chars);
            Assert.Equal(snapped, DictationRewriteSettings.Snap(snapped));
            if (snapped < DictationRewriteSettings.MaxChars)
                Assert.Equal(DictationRewriteSettings.Above(snapped), DictationRewriteSettings.Snap(DictationRewriteSettings.Above(snapped)));
            if (snapped > DictationRewriteSettings.MinChars)
                Assert.Equal(DictationRewriteSettings.Below(snapped), DictationRewriteSettings.Snap(DictationRewriteSettings.Below(snapped)));
        }
    }

    // Two later steps on one value: the second moves just above the first
    // instead of losing its step.
    [Fact]
    public void TwoStepsOnOneValueBothKeepAStep()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.Apply(settings);
        settings.Dictation.Steps =
        [
            new RewriteStep { ProfileId = "base" },
            new RewriteStep { FromChars = 2000, ProfileId = "first" },
            new RewriteStep { FromChars = 2040, ProfileId = "second" },
        ];

        LlmSettingsNormalizer.NormalizeDictation(settings);

        Assert.Equal(
            [("first", 2000), ("second", 2100)],
            settings.Dictation.Steps.Skip(1).Select(step => (step.ProfileId, step.FromChars)));
    }
}
