using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The module's observability tests configure the process-wide
// OperationalLogAdmission policy and count events from a global EventSource, so
// they must not overlap with any other test class of this assembly.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AmbientObservabilityCollection
{
    public const string Name = "AmbientObservability";
}
