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
// as instance properties of RecordingViewModel, which cannot be materialized
// without constructing the VM and its capture-side effects, and the page composes
// lazily on first navigation. Entries are keyed by the same LabelKey the composer
// stamps onto each card's Tag — composed cards named exactly as their manifest
// descriptors, hand-authored cards named as their x:Uid.
//
// Maintenance contract: one card added to the page — composed or bespoke — is one
// entry added here.
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

        // Maximum recording length picker (composed Choice).
        new SettingSearchEntry
        {
            LabelKey = "RecordingLimitCard",
            Keywords = ["duration", "length", "limit", "minutes", "cap", "stop"],
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

        // Listening test command (bespoke). The playback card under it is NOT
        // indexed: it stays collapsed until a take exists.
        new SettingSearchEntry
        {
            LabelKey = "RecordingPageListeningTestCard",
            Keywords = ["listen", "playback", "hear", "speakers", "headphones"],
        },

    ];

    // The microphone-telemetry opt-in, composed on Settings > Diagnostics
    // (RecordingTelemetryViewModel); registered under that page with this
    // module's assembly, where its words live.
    public static IReadOnlyList<SettingSearchEntry> TelemetryEntries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "RecordingMicrophoneTelemetryCard",
            Keywords = ["diagnostics", "metrics", "stats", "microphone", "recording"],
        },
    ];
}
