using Xunit;

namespace Deckle.Input.PrecisionScroll.Tests;

// Tests that read the Deckle-PrecisionScroll provider count its events, so two of
// them must not run at once: one would see the other's lines. The tests of this
// assembly that listen to the provider join this collection and run one after the
// other.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PrecisionScrollObservabilityCollection
{
    public const string Name = "Precision scroll observability";
}
