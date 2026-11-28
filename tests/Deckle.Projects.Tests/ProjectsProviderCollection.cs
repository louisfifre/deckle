using Xunit;

namespace Deckle.Projects.Tests;

// The readers, the store, the runner and the launchers emit on the process-wide
// Deckle-Projects source that the events tests count, so every class that calls
// them shares this collection and runs one after the other. No class outside it
// emits on that source, so the collection keeps running beside the rest.
[CollectionDefinition(Name)]
public sealed class ProjectsProviderCollection
{
    public const string Name = "projects provider singleton";
}
