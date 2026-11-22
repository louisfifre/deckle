using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Transcription.Views;

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
// carries and the composer stamps onto the built card's Tag: a LabelKey whose
// "/Header" and "/Description" resolve from Resources.resw.
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
// gets a line below; a hand-authored card also needs its Tag="<LabelKey>" in
// the XAML so a hit can scroll to it.
public static class SettingsSearch
{
    // The module's General page: overlay, engine and models folder.
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // "Overlay" — the overlay group (fade / position / transcript
        // panel side ride under it as composed children).
        new SettingSearchEntry
        {
            LabelKey = "GeneralOverlayExpander",
            Keywords = ["hud", "indicator", "fade", "position", "transcript", "panel"],
        },

        // "Speech engine" — the engine fold (the Parakeet model folder, the state
        // of its files and the encoder precision ride under it).
        new SettingSearchEntry
        {
            LabelKey = "WhisperSpeechEngineFold",
            Keywords = ["parakeet", "engine", "nvidia", "model", "folder",
                        "encoder", "precision", "fp16", "fp32"],
        },

        // GPU acceleration (composed toggle).
        new SettingSearchEntry
        {
            LabelKey = "WhisperUseGpuCard",
            Keywords = ["directml", "hardware"],
        },

        // Models folder (composed Path).
        new SettingSearchEntry
        {
            LabelKey = "WhisperModelsDirCard",
            Keywords = ["directory", "path", "storage"],
        },
    ];

    // The Lexicon page: the person's words and the switch of the boost.
    public static IReadOnlyList<SettingSearchEntry> LexiconEntries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "LexiconAddCard",
            Keywords = ["vocabulary", "words", "names", "proper nouns", "dictionary",
                        "capital", "case", "hotwords"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LexiconBoostCard",
            Keywords = ["boost", "bias", "favor"],
        },
    ];

    // The Streaming page: the pipeline switch, segmentation, silence and noise
    // filtering.
    public static IReadOnlyList<SettingSearchEntry> StreamingEntries { get; } =
    [
        // "Streaming pipeline" — the switch, first and alone (the cards under it
        // show whatever it says: files and recovered takes always use them).
        new SettingSearchEntry
        {
            LabelKey = "WhisperStreamingEnabledCard",
            Keywords = ["realtime", "live", "segmenter", "utterance"],
        },

        // "Pause before cutting" — the curve block: the four hangover anchors,
        // and the cut margin and the minimum speech in its pause strip, so the
        // words that found those two cards find the card that now holds them.
        new SettingSearchEntry
        {
            LabelKey = "WhisperCutCurveCard",
            Keywords = ["hangover", "pause", "cut", "ramp", "wait",
                        "margin", "silence", "end", "clip", "trailing",
                        "minimum", "speech", "blip", "noise", "short", "word", "drop", "cough"],
        },

        // "Silence and noise filtering" — the Silero trim fold (its switch, then
        // threshold, minimum silence and padding ride under it).
        new SettingSearchEntry
        {
            LabelKey = "WhisperSilenceGuardsFold",
            Keywords = ["silero", "vad", "voice activity", "silence", "noise", "trim",
                        "threshold", "padding"],
        },
    ];

    // The dictation corpus, composed in the Corpus section of Settings >
    // Diagnostics (WhisperTelemetryViewModel); registered under that page with
    // this module's assembly, where its words live. The WAV opt-in and its
    // content radio ride under it as composed children.
    public static IReadOnlyList<SettingSearchEntry> TelemetryEntries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "GeneralCorpusExpander",
            Keywords = ["corpus", "dataset", "recording", "training", "audio", "dictation", "transcription"],
        },
    ];
}
