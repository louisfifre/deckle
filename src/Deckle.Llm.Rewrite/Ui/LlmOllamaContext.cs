using System;
using System.Collections.Generic;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ─── Shared Ollama state across LlmPage subsections ─────────────────────────
//
// Instantiated by the host (LlmPage), filled by RefreshOllamaStateAsync, passed
// to dependent sections (Profiles, Models) through Initialize().
//
// Sections subscribe to StateChanged to rebuild when model list or availability
// changes. Sections that do not depend on Ollama (General, ManualShortcut,
// Rules) do not touch the context.
//
// This context is NOT a ViewModel: it does not observe properties and does not
// notify field-by-field. It only shares runtime state (service, models,
// availability) and coordinates refreshes through a single coarse event.

internal sealed class LlmOllamaContext
{
    public OllamaService? Service { get; set; }

    public IReadOnlyList<OllamaModel> Models { get; set; } = Array.Empty<OllamaModel>();

    public bool Available { get; set; }

    public event EventHandler? StateChanged;

    public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
