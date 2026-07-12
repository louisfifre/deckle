namespace Deckle.Settings;

// ── TelemetryConsentWiring ────────────────────────────────────────────────────
//
// Fills the Catalog.TelemetryConsent registry with the shell's consent dialogs.
// Called once by the App at boot. This shim is the only public surface that
// crosses the assembly boundary — the ContentDialog classes themselves stay
// internal to Deckle.Settings; module settings pages reach their consent through
// the Catalog delegate slots, never the dialog types.
//
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
