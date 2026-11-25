using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// The suite's observability tests count events from the process-wide
// Deckle-Llm source, which every rewrite, monitor and coordinator of the other
// test classes also writes to, so they must not overlap with any other test
// class of this assembly.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LlmRewriteObservabilityCollection
{
    public const string Name = "LlmRewriteObservability";
}
