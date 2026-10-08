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
