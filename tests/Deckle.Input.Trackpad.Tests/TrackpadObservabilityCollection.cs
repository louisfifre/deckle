using Xunit;

namespace Deckle.Input.Trackpad.Tests;

// The module's observability tests configure the process-wide
// OperationalLogAdmission policy and count events from a global EventSource, so
// they must not overlap with any other test class of this assembly.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TrackpadObservabilityCollection
{
    public const string Name = "TrackpadObservability";
}
