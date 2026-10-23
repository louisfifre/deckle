using Xunit;

namespace Deckle.Anytype.Tests;

// Runs the hot-loop count tests alone: a collection that disables parallelization
// runs after the parallel ones, so no other class's listener hears their events.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnytypeHotLoopCollection
{
    public const string Name = "anytype hot loop counts";
}
