using Xunit;

namespace Deckle.Autocorrect.Tests;

// Every class that listens to the global Deckle-Autocorrect EventSource, or
// configures the process-wide OperationalLogAdmission policy, joins this
// collection so no other test class of the assembly runs while it counts
// events. The assembly itself runs in parallel: only the ONNX judge tests
// (OnnxJudgeSerialCollection) and this collection are serialized.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AutocorrectObservabilityCollection
{
    public const string Name = "AutocorrectObservability";
}
