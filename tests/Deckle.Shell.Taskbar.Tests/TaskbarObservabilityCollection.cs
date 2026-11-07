using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The test classes that assert on the Deckle-Taskbar provider's milestones.
// A milestone carries no payload to tell one test's from another's, so these
// classes run alone, with no other class of the suite emitting meanwhile.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TaskbarObservabilityCollection
{
    public const string Name = "Taskbar observability";
}
