namespace Deckle.Llm;

// The longest wait any caller grants a generation. Generous enough for slow
// CPU-only local setups on long transcripts, but still guards against a stuck
// worker. Lives beside the seam because the Ollama busy-poll log line quotes
// it next to the elapsed wait, and the rewrite service arms its timeout with
// it.
public static class GenerationDeadline
{
    public static readonly TimeSpan HardCap = TimeSpan.FromMinutes(15);
}
