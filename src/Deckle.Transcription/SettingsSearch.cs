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
// The list is declared here, not derived from the manifests: those are instance
// members of WhisperViewModel, and the index is built without constructing the
// page or its ViewModel. A card's identity is its bare LabelKey, the string the
// manifest carries and the composer stamps onto the built card's Tag. The three
// hand-authored cards (Language, Initial prompt, Model) carry the same "/Header"
// and "/Description" .resw entries under their x:Uid, so none needs a LiteralLabel.
//
// One entry per top-level card, fold headers included, never their nested
// children: a child sits in a collapsed fold, often masked by runtime state
// (streaming off, corpus opt-in off), so a hit on it could not be brought into
// view. The fold card's Keywords carry the child concepts instead. The transient
// WhisperSetupInfoBar is not a setting, so it is absent too.
//
// Maintenance contract: one top-level card on the page, one entry here. Add a card —
// composed or hand-authored — and it stays invisible to search until it gets a line
// below; a bespoke card also needs its Tag="<LabelKey>" in WhisperPage.xaml so a hit
// can scroll to it.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "GeneralOverlayExpander",
            Keywords = ["hud", "indicator", "fade", "position", "transcript", "panel"],
        },

        new SettingSearchEntry
        {
            LabelKey = "GeneralAutoPasteCard",
            Keywords = ["clipboard", "insert"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperLanguageCard",
            Keywords = ["locale"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperInitialPromptCard",
            Keywords = ["hint", "vocabulary", "bias"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperUseGpuCard",
            Keywords = ["vulkan", "hardware"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperModelCard",
            Keywords = ["weights", "size"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperModelsDirCard",
            Keywords = ["directory", "path", "storage"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperVadEnabledCard",
            Keywords = ["vad", "silero", "silence", "trim"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperSuppressNstCard",
            Keywords = ["music", "tags", "brackets"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperSuppressBlankCard",
            Keywords = ["empty", "whitespace"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperSuppressRegexCard",
            Keywords = ["pattern", "remove"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperUseContextCard",
            Keywords = ["history", "conditioning"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperMaxTokensCard",
            Keywords = ["length", "limit"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperDecodingExpander",
            Keywords = ["temperature", "beam", "greedy", "sampling"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperConfidenceExpander",
            Keywords = ["entropy", "logprob", "hallucination"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperStreamingEnabledCard",
            Keywords = ["realtime", "live", "segmenter", "utterance"],
        },

        new SettingSearchEntry
        {
            LabelKey = "WhisperCutPresetCard",
            Keywords = ["preset", "patient", "balanced", "quick", "custom", "hangover", "curve"],
        },

        new SettingSearchEntry
        {
            LabelKey = "GeneralLatencyCard",
            Keywords = ["timing", "performance", "benchmark"],
        },

        new SettingSearchEntry
        {
            LabelKey = "GeneralCorpusExpander",
            Keywords = ["dataset", "recording", "training", "audio"],
        },
    ];
}
