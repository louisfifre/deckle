using Xunit;

namespace Deckle.Diagnostics.Tests;

// Deckle-Diagnostics is one process-wide provider: a test that provokes a sink
// incident writes it to every listener on that provider, including the one a
// parallel test is counting events with. Classes that provoke or count those
// incidents share this collection so they run one after the other.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DiagnosticsProviderCollection
{
    public const string Name = "diagnostics provider singleton";
}
