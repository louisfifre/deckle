using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Tests;

// An incident opener is a Warning or an Error, which the journal keeps. Its closer
// has to travel there too, or a journal read later shows an incident that never
// ended. The rule is checked on the declared events, so a closer added later is
// covered without a line here.
[Trait("Category", "observability")]
public sealed class JournalClosersTests
{
    [Fact]
    public void DeckleWhispSourceRecoveryClosersTravelInTheJournal()
    {
        IReadOnlyList<string> outside = ProviderManifest.ClosersOutsideTheJournal(typeof(DeckleWhispSource));

        Assert.Empty(outside);
    }

    [Fact]
    public void DeckleLlmSourceRecoveryClosersTravelInTheJournal()
    {
        IReadOnlyList<string> outside = ProviderManifest.ClosersOutsideTheJournal(typeof(Deckle.Llm.DeckleLlmSource));

        Assert.Empty(outside);
    }
}
