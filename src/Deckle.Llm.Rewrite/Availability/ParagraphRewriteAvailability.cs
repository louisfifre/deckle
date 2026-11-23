namespace Deckle.Llm.Rewrite;

// ─── What the paragraph rewrite needs on this PC ─────────────────────────────
//
// Paragraph rewriting runs only on the local Ollama and the one pinned model
// (ParagraphRewrite.ProviderId and .Model), decided with Louis on 2026-11-23:
// without them the keyboard and mouse host is not started at all, so a PC
// that does not have them pays nothing for a feature it cannot offer.
public enum ParagraphRewriteAvailability
{
    /// <summary>Not probed yet. Counts as unavailable.</summary>
    Unknown,

    /// <summary>Ollama answers and lists the pinned model.</summary>
    Available,

    /// <summary>The local Ollama provider is missing from the list, switched
    /// off, or not an Ollama provider.</summary>
    ProviderOff,

    /// <summary>The provider's address does not answer: Ollama is not
    /// installed, not started, or listens elsewhere.</summary>
    OllamaUnreachable,

    /// <summary>Ollama answers but does not hold the pinned model.</summary>
    ModelMissing,
}

public static class ParagraphRewriteAvailabilityNames
{
    // The fixed words the log carries; a log filter spells them one way.
    public static string Name(this ParagraphRewriteAvailability availability) => availability switch
    {
        ParagraphRewriteAvailability.Available         => "available",
        ParagraphRewriteAvailability.ProviderOff       => "provider_off",
        ParagraphRewriteAvailability.OllamaUnreachable => "ollama_unreachable",
        ParagraphRewriteAvailability.ModelMissing      => "model_missing",
        _                                              => "unknown",
    };
}
