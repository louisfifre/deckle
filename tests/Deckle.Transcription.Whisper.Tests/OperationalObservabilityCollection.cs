using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// Operational-log admission and the Deckle-Whisp provider are process-wide, so
// the tests that switch the one or listen to the other run alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OperationalObservabilityCollection
{
    public const string Name = "Whisper operational observability";
}
