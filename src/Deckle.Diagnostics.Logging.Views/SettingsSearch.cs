using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Diagnostics.Logging.Views;

// The diagnostics page's contribution to the shell's cross-page search index: one
// SettingSearchEntry per findable card, resolved from this module's own PRI
// subtree without composing the page.
//
// The list is declared, not derived from the manifest: the composed cards are
// instance properties of DiagnosticsViewModel, and the index must never construct
// a ViewModel at boot. A card's identity is its bare LabelKey, the string the
// composer stamps onto the built card's Tag.
//
// Maintenance contract: one card on the page, one entry here. Add a card (composed
// or hand-authored) and it stays invisible to search until it gets a line below; a
// bespoke card also needs its Tag="<LabelKey>" in DiagnosticsPage.xaml so a hit can
// scroll to it. The words of a former label stay as keywords, so a person who
// learned the old name still finds the card.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // "Logs" section — the Detailed logs fold and its children.
        new SettingSearchEntry
        {
            LabelKey = "LoggingDetailedCard",
            Keywords = ["verbosity", "verbose", "debug", "details", "summary", "troubleshoot"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingAmbientCard",
            Keywords = ["lighting", "capture", "hue"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingTranscriptionCard",
            Keywords = ["transcription", "speech", "file"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingDictationTimingsCard",
            Keywords = ["latency", "timing", "performance", "benchmark", "telemetry"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingMicrophoneLevelCard",
            Keywords = ["microphone", "rms", "volume", "telemetry"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingLlmCard",
            Keywords = ["llm", "tokens", "usage", "cost", "ollama", "claude", "openai", "rewrite"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingAutocorrectCard",
            Keywords = ["typing", "correction", "learning"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingInputCard",
            Keywords = ["raw input", "trackpad", "gesture", "input"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingWindowingCard",
            Keywords = ["windowing", "monitor", "display", "placement", "dpi"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingTouchpadFramesCard",
            Keywords = ["trackpad", "contact", "frames", "raw", "record", "telemetry"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingWheelEventsCard",
            Keywords = ["mouse", "scroll", "wheel", "record", "telemetry"],
        },

        // The copy on disk.
        new SettingSearchEntry
        {
            LabelKey = "GeneralAppLogCard",
            Keywords = ["trace", "application log", "debug", "troubleshoot", "retention", "archive", "disk", "history"],
        },

        // "Folders" section, last on the page: the two composed folder folds
        // (FoldersSettings), the dictation audio folder then the diagnostics
        // folder.
        new SettingSearchEntry
        {
            LabelKey = "GeneralStorageFolderCard",
            Keywords = ["directory", "location", "path", "telemetry", "corpus", "wav"],
        },
        new SettingSearchEntry
        {
            LabelKey = "DiagnosticsFilesCard",
            Keywords = ["crash", "troubleshoot", "debug", "logs"],
        },
    ];
}
