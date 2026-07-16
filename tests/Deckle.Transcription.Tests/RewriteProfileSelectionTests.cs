using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "unit")]
public sealed class RewriteProfileSelectionTests
{
    [Fact]
    public void PlainTranscriptionNeverSelectsAnAutomaticRule()
    {
        var settings = new LlmSettings
        {
            Enabled = true,
            Profiles = [new RewriteProfile { Name = "Clean up" }],
            AutoRewriteRules =
            [
                new AutoRewriteRule
                {
                    MinDurationSeconds = 0,
                    ProfileName = "Clean up",
                },
            ],
        };

        RewriteProfile? selected = RewriteProfileSelection.ForHotkey(
            settings,
            requestedProfileName: null);

        Assert.Null(selected);
    }

    [Fact]
    public void ExplicitRewriteHotkeySelectsItsProfile()
    {
        var profile = new RewriteProfile { Name = "Clean up" };
        var settings = new LlmSettings
        {
            Enabled = true,
            Profiles = [profile],
        };

        RewriteProfile? selected = RewriteProfileSelection.ForHotkey(
            settings,
            requestedProfileName: "clean UP");

        Assert.Same(profile, selected);
    }
}
