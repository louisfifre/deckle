using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Transcription;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// WhisperPage's contribution to the shell's cross-page search index: one
// SettingSearchEntry per findable card. The composition root registers this list
// at boot alongside the module's nav descriptor, and the index resolves each
// entry's text from this module's own PRI subtree without composing the page.
//
// The list is DECLARED here, not derived from the page or the manifests, on
// purpose. The composed cards live as instance properties on WhisperViewModel
// (BehaviourSettings, VadSettings, DecodingSettingsManifest, StreamingSettings, …) —
// reading them means constructing a ViewModel, which pulls in its boot side effects
// (settings load, capture-side wiring) and which the index must never do at boot.
// So a card's identity travels as its bare LabelKey, the same string the manifest
// carries and the composer stamps onto the built card's Tag. Composed and bespoke
// cards declare identically: a LabelKey whose "/Header" and "/Description" resolve
// from Resources.resw for both — the three hand-authored cards (Language, Initial
// prompt, Model) carry .resw entries under their x:Uid just like the composed ones,
// so none needs a LiteralLabel.
//
// Granularity is one entry per TOP-LEVEL card, fold headers included, but NOT their
// nested children. WhisperPage is the heaviest page, its parameters stacked in folds
// (the VAD group, the energy-segmenter group, the Decoding and Confidence sections,
// the overlay and corpus groups); a child lives inside a fold that is collapsed
// behind its chevron — and often masked outright by runtime state (the whole Speech-
// filtering fold and the segmenter children hide while streaming is off, the corpus
// children while the corpus opt-in is off) — so a hit on one could not be reliably
// brought into view. The fold's own card is the single reliable target that lands the
// user in the right region; the buried child concepts stay findable through that
// card's Keywords ("temperature"/"beam" reaching Decoding, "hangover"/"utterance"
// reaching Streaming, "silero"/"silence" reaching VAD). The transient
// WhisperSetupInfoBar is not a setting — a not-provisioned call-to-action — so it is
// absent too.
//
// Maintenance contract: one top-level card on the page, one entry here. Add a card —
// composed or hand-authored — and it stays invisible to search until it gets a line
// below; a bespoke card also needs its Tag="<LabelKey>" in WhisperPage.xaml so a hit
// can scroll to it.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // "Overlay and pasting" — the overlay group (fade / animations / position
        // ride under it as composed children).
        new SettingSearchEntry
        {
            LabelKey = "GeneralOverlayExpander",
            Keywords = ["hud", "indicator", "fade", "position"],
        },

        // Auto-paste toggle (composed).
        new SettingSearchEntry
        {
            LabelKey = "GeneralAutoPasteCard",
            Keywords = ["clipboard", "insert"],
        },

        // Language selector (bespoke — editable ComboBox, Tag'd in the XAML).
        new SettingSearchEntry
        {
            LabelKey = "WhisperLanguageCard",
            Keywords = ["locale"],
        },

        // Initial prompt (bespoke expander, Tag'd in the XAML).
        new SettingSearchEntry
        {
            LabelKey = "WhisperInitialPromptCard",
            Keywords = ["hint", "vocabulary", "bias"],
        },

        // GPU acceleration (composed toggle).
        new SettingSearchEntry
        {
            LabelKey = "WhisperUseGpuCard",
            Keywords = ["vulkan", "hardware"],
        },

        // Whisper model selector (bespoke — disk-enumerated AutoSuggestBox, Tag'd in
        // the XAML).
        new SettingSearchEntry
        {
            LabelKey = "WhisperModelCard",
            Keywords = ["weights", "size"],
        },

        // Models folder (composed Path).
        new SettingSearchEntry
        {
            LabelKey = "WhisperModelsDirCard",
            Keywords = ["directory", "path", "storage"],
        },

        // "Speech filtering" — the VAD group (the four Silero parameters ride under
        // it; the whole fold hides while streaming is off).
        new SettingSearchEntry
        {
            LabelKey = "WhisperVadEnabledCard",
            Keywords = ["vad", "silero", "silence", "trim"],
        },

        // Suppress non-speech tokens (composed toggle).
        new SettingSearchEntry
        {
            LabelKey = "WhisperSuppressNstCard",
            Keywords = ["music", "tags", "brackets"],
        },

        // Suppress blank segments (composed toggle).
        new SettingSearchEntry
        {
            LabelKey = "WhisperSuppressBlankCard",
            Keywords = ["empty", "whitespace"],
        },

        // Custom regex filter (composed text field).
        new SettingSearchEntry
        {
            LabelKey = "WhisperSuppressRegexCard",
            Keywords = ["pattern", "remove"],
        },

        // Carry previous context (composed toggle).
        new SettingSearchEntry
        {
            LabelKey = "WhisperUseContextCard",
            Keywords = ["history", "conditioning"],
        },

        // Maximum tokens per segment (composed number).
        new SettingSearchEntry
        {
            LabelKey = "WhisperMaxTokensCard",
            Keywords = ["length", "limit"],
        },

        // "Decoding" — the master-less section (beam search, beam size, temperature,
        // fallback step ride under it as composed children).
        new SettingSearchEntry
        {
            LabelKey = "WhisperDecodingExpander",
            Keywords = ["temperature", "beam", "greedy", "sampling"],
        },

        // "Confidence thresholds" — the master-less section (entropy, log-probability,
        // no-speech ride under it as composed children).
        new SettingSearchEntry
        {
            LabelKey = "WhisperConfidenceExpander",
            Keywords = ["entropy", "logprob", "hallucination"],
        },

        // "Streaming pipeline" — the streaming group (the seven energy-segmenter
        // parameters ride under it; children hide while streaming is off).
        new SettingSearchEntry
        {
            LabelKey = "WhisperStreamingEnabledCard",
            Keywords = ["realtime", "live", "segmenter", "hangover", "utterance"],
        },

        // Latency telemetry (composed toggle).
        new SettingSearchEntry
        {
            LabelKey = "GeneralLatencyCard",
            Keywords = ["timing", "performance", "benchmark"],
        },

        // "Corpus" — the audio-corpus consent group (the WAV opt-in and its content
        // radio ride under it as composed children).
        new SettingSearchEntry
        {
            LabelKey = "GeneralCorpusExpander",
            Keywords = ["dataset", "recording", "training", "audio"],
        },
    ];
}
