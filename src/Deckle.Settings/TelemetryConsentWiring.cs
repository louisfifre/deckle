namespace Deckle.Settings;

// ── TelemetryConsentWiring ────────────────────────────────────────────────────
//
// Fills the Catalog.TelemetryConsent slots with the consent dialogs. The dialog
// classes stay internal to Deckle.Settings: module settings pages reach consent
// through the Catalog slots, never the dialog types.
public static class TelemetryConsentWiring
{
    public static void Wire()
    {
        Catalog.TelemetryConsent.ApplicationLog       = ApplicationLogConsentDialog.ShowAsync;
        Catalog.TelemetryConsent.Microphone           = MicrophoneTelemetryConsentDialog.ShowAsync;
        Catalog.TelemetryConsent.Corpus               = CorpusConsentDialog.ShowAsync;
        Catalog.TelemetryConsent.AudioCorpus          = AudioCorpusConsentDialog.ShowAsync;
        Catalog.TelemetryConsent.AutocorrectDecisions = AutocorrectDecisionsConsentDialog.ShowAsync;
        Catalog.TelemetryConsent.AutocorrectText      = AutocorrectTextConsentDialog.ShowAsync;
    }
}
