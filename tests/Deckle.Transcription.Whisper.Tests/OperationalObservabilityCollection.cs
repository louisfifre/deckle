using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// Operational-log admission and the Deckle-Whisp and Deckle-Whisper providers
// are process-wide, so the tests that switch the one or listen to the others
// run alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OperationalObservabilityCollection
{
    public const string Name = "Whisper operational observability";
}
