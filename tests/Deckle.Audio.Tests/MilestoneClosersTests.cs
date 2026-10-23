using Deckle.TestSupport;
using Xunit;

namespace Deckle.Audio.Tests;

// An incident opener is a Warning or an Error, which the log keeps. Its closer
// has to travel there too, or a log read later shows an incident that never
// ended. The rule is checked on the declared events, so a closer added later is
// covered without a line here.
[Trait("Category", "observability")]
public sealed class MilestoneClosersTests
{
    [Fact]
    public void DeckleAudioSourceRecoveryClosersTravelInTheLog()
    {
        IReadOnlyList<string> outside = ProviderManifest.ClosersOutsideTheLog(typeof(DeckleAudioSource));

        Assert.Empty(outside);
    }
}
