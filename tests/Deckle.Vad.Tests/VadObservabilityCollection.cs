using Xunit;

namespace Deckle.Vad.Tests;

// Tests that read the Deckle-Vad provider count its events, so two of them must not
// run at once: one would see the other's lines. The tests of this assembly that
// listen to the provider join this collection and run one after the other.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VadObservabilityCollection
{
    public const string Name = "Vad observability";
}
