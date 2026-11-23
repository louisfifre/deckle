using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── Paragraph rewrite availability ──────────────────────────────────

    // state and previous: available, provider_off, ollama_unreachable,
    // model_missing, unknown. One line per change, never per probe: a PC
    // without Ollama is probed every thirty seconds and says this once.
    [Event(EvtParagraphRewriteAvailabilityChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Paragraph rewriting availability changed")]
    public void ParagraphRewriteAvailabilityChanged(string state, string previous)
    {
        if (IsEnabled()) WriteEvent(EvtParagraphRewriteAvailabilityChanged, state, previous);
    }
}
