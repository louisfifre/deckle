using Xunit;

namespace Deckle.Anytype.Tests;

// Runs the tests that read the process-wide user incident registry alone: a
// collection that disables parallelization runs after the parallel ones, so no
// other class's supervisor opens or closes the incident they watch.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnytypeUserIncidentCollection
{
    public const string Name = "anytype user incident";
}
