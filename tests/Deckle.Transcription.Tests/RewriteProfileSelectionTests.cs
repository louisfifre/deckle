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
            new RewriteProfile { Id = "short", Name = "Short" },
            new RewriteProfile { Id = "long", Name = "Long" },
            new RewriteProfile { Id = "other", Name = "Other" },
        ],
        LengthSwitch = new LengthSwitchSettings
        {
            Enabled = true,
            Steps =
            [
                new LengthStep { FromChars = 2000, ProfileId = "short" },
                new LengthStep { FromChars = 10000, ProfileId = "long" },
            ],
        },
    };

    [Theory]
    [InlineData(2000, "short")]
    [InlineData(9999, "short")]
    [InlineData(10000, "long")]
    [InlineData(60000, "long")]
    public void TheHighestStepReachedChoosesTheProfile(int chars, string expectedId)
    {
        Assert.Equal(expectedId, RewriteProfileSelection.ForDictation(Ladder("base"), chars)?.Id);
    }

    [Fact]
    public void BelowTheFirstStepTheActiveProfileApplies()
    {
        Assert.Equal("base", RewriteProfileSelection.ForDictation(Ladder("base"), 1999)?.Id);
    }

    [Fact]
    public void BelowTheFirstStepWithoutActiveProfileTheTranscriptStaysRaw()
    {
        Assert.Null(RewriteProfileSelection.ForDictation(Ladder(), 1999));
    }

    [Fact]
    public void ALadderSwitchedOffNeverChooses()
    {
        var settings = Ladder("base");
        settings.LengthSwitch.Enabled = false;

        Assert.Equal("base", RewriteProfileSelection.ForDictation(settings, 60000)?.Id);
    }

    [Fact]
    public void AStepWhoseProfileWasDeletedHandsOverToTheStepBelow()
    {
        var settings = Ladder("base");
        settings.Profiles.RemoveAll(p => p.Id == "long");

        Assert.Equal("short", RewriteProfileSelection.ForDictation(settings, 60000)?.Id);
    }

    [Fact]
    public void AnUnknownLengthIgnoresTheLadder()
    {
        Assert.Equal("base", RewriteProfileSelection.ForDictation(Ladder("base"), rawChars: null)?.Id);
        Assert.Null(RewriteProfileSelection.ForDictation(Ladder(), rawChars: null));
    }

    [Fact]
    public void AProfileShortcutWinsOverLengthAndHoldsWithRewritingOff()
    {
        var settings = Ladder("base");
        Assert.Equal("other", RewriteProfileSelection.ForDictation(settings, 60000, "other")?.Id);

        settings.Enabled = false;
        Assert.Equal("other", RewriteProfileSelection.ForDictation(settings, 60000, "other")?.Id);
        Assert.Null(RewriteProfileSelection.ForDictation(settings, 60000));
    }

    [Fact]
    public void AShortcutToADeletedProfileFallsBackToTheOrdinaryChoice()
    {
        Assert.Equal("long", RewriteProfileSelection.ForDictation(Ladder("base"), 20000, "gone")?.Id);
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
