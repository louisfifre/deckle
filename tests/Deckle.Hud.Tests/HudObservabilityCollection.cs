using Xunit;

namespace Deckle.Hud.Tests;

// The Hud observability tests configure the process-wide OperationalLogAdmission
// policy and count events from the Deckle-Hud source, so they run one after the
// other. No class outside this collection configures that policy or emits on
// that source, so the collection keeps running beside the rest.
[CollectionDefinition(Name)]
public sealed class HudObservabilityCollection
{
    public const string Name = "Hud operational observability";
}
