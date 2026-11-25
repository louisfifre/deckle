using Deckle.Catalog;

namespace Deckle.App;

// ── DictationSettingsPages ─────────────────────────────────────────────────────
//
// The pages of the Dictation node of the settings rail, in the order the rail
// shows them: General (the transcription module's own page), Microphone (the
// audio module's page, which nests itself under the node on the module id) and
// Lexicon. Kept apart from the startup wiring so a test registers exactly what
// the resident app registers and reads the order back, with no App instance.
//
// The caller registers inside its transcription-present block: the node exists
// only with the module, and Microphone and Lexicon name it as their parent, so
// the parent registers first.
internal static class DictationSettingsPages
{
    // Gaps between the values leave room for a later page between two.
    internal const int GeneralOrder = 100;
    internal const int MicrophoneOrder = 105;
    internal const int LexiconOrder = 110;

    internal static void Register(
        Action<SettingsModuleDescriptor, IReadOnlyList<SettingSearchEntry>> register)
    {
        register(
            Transcription.Views.TranscriptionSettingsModule.Describe(GeneralOrder),
            Transcription.Views.SettingsSearch.Entries);
        register(
            Audio.Views.RecordingSettingsModule.Describe(MicrophoneOrder),
            Audio.Views.SettingsSearch.Entries);
        register(
            Transcription.Views.TranscriptionSettingsModule.DescribeLexicon(LexiconOrder),
            Transcription.Views.SettingsSearch.LexiconEntries);
    }
}
