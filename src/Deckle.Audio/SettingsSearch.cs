using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Audio;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// RecordingPage's contribution to the shell's cross-page search index: one
// SettingSearchEntry per findable card on the page. The composition root reads
// this list at boot and registers it against the module's nav descriptor, so a
// query can reach a recording setting without the search box knowing this page.
//
// Declared here as a plain static list rather than derived from the page or the
// ViewModel because neither is available at indexing time: the composed cards live
// as instance properties of RecordingViewModel (PreprocessingSettings, ...), which
// cannot be materialized without constructing the VM and its capture-side effects,
// and the page composes lazily on first navigation. So the searchable surface is
// spelled out declaratively, keyed by the same LabelKey the composer stamps onto
// each card's Tag — composed cards named exactly as their manifest descriptors, the
// two hand-authored cards named as their x:Uid. The index resolves the display text
// from this module's PRI subtree; the Literal* escapes stay unused here since every
// card carries a "<LabelKey>/Header" entry.
//
// Maintenance contract: one card added to the page — composed or bespoke — is one
// entry added here. The individual voice-level sliders are deliberately absent: they
// belong to the GeneralVoiceLevelExpander group and stay collapsed while
// auto-calibration is on (the default), so a hit on one could not be brought into
// view; the group card is the reliable, single search target for the whole window.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // Microphone device picker (bespoke — runtime waveIn enumeration).
        new SettingSearchEntry
        {
            LabelKey = "GeneralMicrophoneCard",
            Keywords = ["mic", "source"],
        },

        // Transcription pre-processing toggle (composed).
        new SettingSearchEntry
        {
            LabelKey = "RecordingPagePreprocessingCard",
            Keywords = ["dsp", "noise", "gain", "boost"],
        },

        // Microphone level check command + advice (bespoke — command and InfoBars).
        new SettingSearchEntry
        {
            LabelKey = "RecordingPageMicCheckCard",
            Keywords = ["test", "meter", "calibrate", "volume"],
        },

        // Voice-level window group (composed Group — the sliders live under it).
        new SettingSearchEntry
        {
            LabelKey = "GeneralVoiceLevelExpander",
            Keywords = ["gain", "sensitivity", "loudness", "threshold"],
        },

        // Microphone-telemetry opt-in (composed).
        new SettingSearchEntry
        {
            LabelKey = "RecordingMicrophoneTelemetryCard",
            Keywords = ["diagnostics", "metrics", "stats"],
        },
    ];
}
