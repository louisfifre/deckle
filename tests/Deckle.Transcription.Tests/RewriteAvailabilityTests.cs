using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "unit")]
public sealed class RewriteAvailabilityTests
{
    [Fact]
    public void AbsenceNeutralizesRewriteWithoutReadingItsSettingsFactory()
    {
        int reads = 0;

        LlmSettings settings = RewriteAvailability.Settings(
            present: false,
            () =>
            {
                reads++;
                return new LlmSettings { Enabled = true };
            });

        Assert.Equal(0, reads);
        Assert.False(settings.Enabled);
        Assert.Empty(settings.Profiles);
    }

    [Fact]
    public void PresenceReturnsTheLiveSettingsInstance()
    {
        var live = new LlmSettings { Enabled = true };

        LlmSettings settings = RewriteAvailability.Settings(true, () => live);

        Assert.Same(live, settings);
    }
}
