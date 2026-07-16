using Xunit;

namespace Deckle.Transcription.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OperationalObservabilityCollection
{
    public const string Name = "Transcription operational observability";
}
