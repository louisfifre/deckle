using Xunit;

namespace Deckle.Vision.Tests;

// The capture incidents and the source tests count events from the process-wide
// Deckle-Vision source, so they run one after the other. No class outside this
// collection emits on that source (the sampler tests read its pure helpers
// only), so the collection keeps running beside the rest.
[CollectionDefinition(Name)]
public sealed class VisionObservabilityCollection
{
    public const string Name = "Vision observability";
}
