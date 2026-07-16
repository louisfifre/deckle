using Xunit;

namespace Deckle.Lighting.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LightingObservabilityCollection
{
    public const string Name = "LightingObservability";
}
