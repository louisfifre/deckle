using Xunit;

namespace Deckle.Autocorrect.Tests;

// Every test that creates a REAL ONNX Runtime GenAI session joins this
// collection. Loading the DirectML judge while other tests run in parallel
// fails in Model construction with "Specified provider is not supported": the
// provider registration inside onnxruntime-genai 0.13 does not survive
// concurrent process activity. The live app initializes the judge alone at
// boot, so only the test host needs DisableParallelization.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OnnxJudgeSerialCollection
{
    public const string Name = "onnx-judge-serial";
}
