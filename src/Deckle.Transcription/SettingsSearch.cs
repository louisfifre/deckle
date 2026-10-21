using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Transcription;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// The module's contribution to the shell's cross-page search index: one
// SettingSearchEntry per findable card, one list per page. The composition root
// registers each list at boot beside the page's nav descriptor, and the index
// resolves each entry's text from this module's own PRI subtree without
// composing the page.
//
// The lists are DECLARED here, not derived from the manifests, on purpose. The
// composed cards live as instance properties on WhisperViewModel — reading them
// means constructing a ViewModel, which pulls in its boot side effects (settings
// load, capture-side wiring) and which the index must never do at boot. So a
// card's identity travels as its bare LabelKey, the same string the manifest
// carries and the composer stamps onto the built card's Tag. Composed and
// bespoke cards declare identically: a LabelKey whose "/Header" and
// "/Description" resolve from Resources.resw for both — the three hand-authored
// cards (Language, Initial prompt, Model) carry .resw entries under their x:Uid
// just like the composed ones, so none needs a LiteralLabel.
//
// Granularity is one entry per TOP-LEVEL card, a fold's header included but not
// its children: a child sits behind a chevron, and often masked by runtime
// state (the VAD parameters while the detector is off, the corpus children
// while the corpus opt-in is off), so a hit on one could not be reliably
// brought into view. The fold's own card lands the user in the right region;
// the buried child concepts stay findable through that card's Keywords. The
// transient WhisperSetupInfoBar is not a setting — a not-provisioned
// call-to-action — so it is absent too.
//
// Maintenance contract: one top-level card on a page, one entry here. Add a
// card — composed or hand-authored — and it stays invisible to search until it
// gets a line below; a bespoke card also needs its Tag="<LabelKey>" in the XAML
// so a hit can scroll to it.
public static class SettingsSearch
{
    // The module's General page: overlay and pasting, model and engine.
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // "Overlay and pasting" — the overlay group (fade / position / transcript
        // panel side ride under it as composed children).
        new SettingSearchEntry
        {
            LabelKey = "GeneralOverlayExpander",
            Keywords = ["hud", "indicator", "fade", "position", "transcript", "panel"],
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
    ];

    // The Decoding page: four flat sections, every card top-level.
    public static IReadOnlyList<SettingSearchEntry> DecodingEntries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "WhisperUseBeamSearchCard",
            Keywords = ["beam", "greedy", "sampling", "decoding"],
        },
        new SettingSearchEntry
        {
            LabelKey = "WhisperTemperatureCard",
            Keywords = ["randomness", "sampling", "decoding"],
        },
        new SettingSearchEntry
        {
            LabelKey = "WhisperTemperatureIncrementCard",
            Keywords = ["fallback", "retry", "step"],
        },
        new SettingSearchEntry
        {
            LabelKey = "WhisperEntropyCard",
            Keywords = ["confidence", "hallucination", "retry"],
        },
        new SettingSearchEntry
        {
            LabelKey = "WhisperLogprobCard",
            Keywords = ["confidence", "logprob", "probability", "retry"],
        },
        new SettingSearchEntry
        {
            LabelKey = "WhisperNoSpeechCard",
            Keywords = ["confidence", "silence", "hallucination"],
        },
        new SettingSearchEntry
        {
            LabelKey = "WhisperSuppressNstCard",
            Keywords = ["music", "tags", "brackets", "filter"],
        },
        new SettingSearchEntry
        {
            LabelKey = "WhisperSuppressBlankCard",
            Keywords = ["empty", "whitespace", "filter"],
        },
        new SettingSearchEntry
        {
            LabelKey = "WhisperSuppressRegexCard",
            Keywords = ["pattern", "remove", "filter"],
        },
        new SettingSearchEntry
        {
            LabelKey = "WhisperUseContextCard",
            Keywords = ["history", "conditioning", "context"],
        },
        new SettingSearchEntry
        {
            LabelKey = "WhisperMaxTokensCard",
            Keywords = ["length", "limit", "context"],
        },
    ];

    // The Streaming page: the pipeline switch, speech filtering, segmentation.
    public static IReadOnlyList<SettingSearchEntry> StreamingEntries { get; } =
    [
        // "Streaming pipeline" — the switch, first and alone (the cards under it
        // hide while streaming is off).
        new SettingSearchEntry
        {
            LabelKey = "WhisperStreamingEnabledCard",
            Keywords = ["realtime", "live", "segmenter", "utterance"],
        },

        // "Voice activity detection" — the VAD group (threshold, minimum speech,
        // minimum silence, padding ride under it; the fold hides while streaming
        // is off).
        new SettingSearchEntry
        {
            LabelKey = "WhisperVadEnabledCard",
            Keywords = ["vad", "silero", "silence", "trim", "threshold", "padding"],
        },

        // Speech level (composed field): the segmenter's loudness threshold.
        new SettingSearchEntry
        {
            LabelKey = "WhisperSegThresholdCard",
            Keywords = ["silence", "threshold", "dbfs", "level", "sensitivity", "cut"],
        },

        // "Cut curve" — the curve block with the four hangover anchors in its legend.
        new SettingSearchEntry
        {
            LabelKey = "WhisperCutCurveCard",
            Keywords = ["hangover", "pause", "cut", "ramp", "wait"],
        },
    ];

    // The dictation's dataset consents, composed on Settings > Diagnostics
    // (WhisperTelemetryViewModel); registered under that page with this
    // module's assembly, where their words live.
    public static IReadOnlyList<SettingSearchEntry> TelemetryEntries { get; } =
    [
        // Latency telemetry (composed toggle).
        new SettingSearchEntry
        {
            LabelKey = "GeneralLatencyCard",
            Keywords = ["timing", "performance", "benchmark", "dictation"],
        },

        // "Corpus" — the audio-corpus consent group (the WAV opt-in and its content
        // radio ride under it as composed children).
        new SettingSearchEntry
        {
            LabelKey = "GeneralCorpusExpander",
            Keywords = ["dataset", "recording", "training", "audio", "dictation"],
        },
    ];
}
