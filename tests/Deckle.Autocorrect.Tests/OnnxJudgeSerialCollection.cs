using Xunit;

namespace Deckle.Autocorrect.Tests;

// Every test that creates a REAL ONNX Runtime GenAI session joins this
// collection. Measured on the full suite (2026-07-14): loading the DirectML
// judge while other tests run in parallel fails in Model construction with
// "Specified provider is not supported", and the same load is clean when the
// suite runs with parallelism off — the provider registration inside
// onnxruntime-genai 0.13 does not survive concurrent process activity. The
// live app is unaffected (it initializes the judge alone, sequentially, at
// boot); only the test host executes it under parallel load, so the fix
// belongs here: DisableParallelization runs this collection with nothing
// else in flight.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OnnxJudgeSerialCollection
{
    public const string Name = "onnx-judge-serial";
}
