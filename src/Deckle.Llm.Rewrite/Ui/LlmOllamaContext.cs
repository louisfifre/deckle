using System;
using System.Collections.Generic;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ─── Shared Ollama state across LlmPage subsections ─────────────────────────
//
// Filled by LlmPage.RefreshOllamaStateAsync and handed to the Models section
// through Initialize(). Not a ViewModel: it does not notify field by field. It
// shares runtime state (service, models, availability) and signals a refresh
// through a single coarse event.

internal sealed class LlmOllamaContext
{
    public OllamaService? Service { get; set; }

    public IReadOnlyList<OllamaModel> Models { get; set; } = Array.Empty<OllamaModel>();

    public bool Available { get; set; }

    public event EventHandler? StateChanged;

    public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
