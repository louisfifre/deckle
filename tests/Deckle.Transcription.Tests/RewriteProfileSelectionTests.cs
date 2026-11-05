using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "unit")]
public sealed class RewriteProfileSelectionTests
{
    [Fact]
    public void PlainDictationWithoutActiveProfileKeepsTheTranscript()
    {
        var settings = new LlmSettings
        {
            Enabled = true,
            Profiles = [new RewriteProfile { Id = "a1", Name = "Clean up" }],
            AutoRewriteRules =
            [
                new AutoRewriteRule
                {
                    MinDurationSeconds = 0,
                    ProfileName = "Clean up",
                },
            ],
        };

        Assert.Null(RewriteProfileSelection.ForDictation(settings));
    }

    [Fact]
    public void ActiveProfileResolvesById()
    {
        var profile = new RewriteProfile { Id = "a1", Name = "Clean up" };
        var settings = new LlmSettings
        {
            Enabled = true,
            Profiles = [new RewriteProfile { Id = "b2", Name = "Other" }, profile],
            ActiveProfileId = "a1",
            ActiveProfileName = "Stale name",
        };

        Assert.Same(profile, RewriteProfileSelection.ForDictation(settings));
    }

    [Fact]
    public void ActiveProfileFallsBackToItsNameWhenTheIdIsUnknown()
    {
        var profile = new RewriteProfile { Id = "a1", Name = "Clean up" };
        var settings = new LlmSettings
        {
            Enabled = true,
            Profiles = [profile],
            ActiveProfileId = "gone",
            ActiveProfileName = "clean UP",
        };

        Assert.Same(profile, RewriteProfileSelection.ForDictation(settings));
    }

    private static LlmSettings Ladder(string? activeId = null) => new()
    {
        Enabled = true,
        ActiveProfileId = activeId,
        Profiles =
        [
            new RewriteProfile { Id = "base", Name = "Base" },
            new RewriteProfile { Id = "short", Name = "Short", AutoSelect = true, AutoSelectFromMinutes = 3 },
            new RewriteProfile { Id = "long", Name = "Long", AutoSelect = true, AutoSelectFromMinutes = 15 },
            new RewriteProfile { Id = "off", Name = "Off", AutoSelect = false, AutoSelectFromMinutes = 1 },
        ],
    };

    [Theory]
    [InlineData(3 * 60, "short")]
    [InlineData(14 * 60 + 59, "short")]
    [InlineData(15 * 60, "long")]
    [InlineData(90 * 60, "long")]
    public void TheHighestThresholdReachedChoosesTheProfile(double seconds, string expectedId)
    {
        Assert.Equal(expectedId, RewriteProfileSelection.ForDictation(Ladder("base"), seconds)?.Id);
    }

    [Fact]
    public void UnderEveryThresholdTheActiveProfileApplies()
    {
        Assert.Equal("base", RewriteProfileSelection.ForDictation(Ladder("base"), 2 * 60)?.Id);
    }

    [Fact]
    public void UnderEveryThresholdWithoutActiveProfileTheTranscriptStaysRaw()
    {
        Assert.Null(RewriteProfileSelection.ForDictation(Ladder(), 2 * 60));
    }

    [Fact]
    public void AProfileWithAutoSelectOffIsNeverChosenByLength()
    {
        Assert.Null(RewriteProfileSelection.ForDictation(Ladder(), 2 * 60));
        Assert.NotEqual("off", RewriteProfileSelection.ForDictation(Ladder(), 60 * 60)?.Id);
    }

    [Fact]
    public void OnATieTheFirstProfileInTheListWins()
    {
        var settings = Ladder();
        settings.Profiles[3].AutoSelect = true;
        settings.Profiles[3].AutoSelectFromMinutes = 15;

        Assert.Equal("long", RewriteProfileSelection.ForDictation(settings, 20 * 60)?.Id);
    }

    [Fact]
    public void AnUnknownLengthIgnoresTheThresholds()
    {
        Assert.Equal("base", RewriteProfileSelection.ForDictation(Ladder("base"), sentAudioSec: null)?.Id);
        Assert.Null(RewriteProfileSelection.ForDictation(Ladder(), sentAudioSec: null));
    }

    [Fact]
    public void AProfileShortcutWinsOverLengthAndHoldsWithRewritingOff()
    {
        var settings = Ladder("base");
        Assert.Equal("off", RewriteProfileSelection.ForDictation(settings, 60 * 60, "off")?.Id);

        settings.Enabled = false;
        Assert.Equal("off", RewriteProfileSelection.ForDictation(settings, 60 * 60, "off")?.Id);
        Assert.Null(RewriteProfileSelection.ForDictation(settings, 60 * 60));
    }

    [Fact]
    public void AShortcutToADeletedProfileFallsBackToTheOrdinaryChoice()
    {
        Assert.Equal("long", RewriteProfileSelection.ForDictation(Ladder("base"), 20 * 60, "gone")?.Id);
    }

    [Fact]
    public void DisabledRewritingKeepsTheTranscript()
    {
        var settings = new LlmSettings
        {
            Enabled = false,
            Profiles = [new RewriteProfile { Id = "a1", Name = "Clean up" }],
            ActiveProfileId = "a1",
            ActiveProfileName = "Clean up",
        };

        Assert.Null(RewriteProfileSelection.ForDictation(settings));
    }
}
