namespace Deckle.Diagnostics.Telemetry;

// User-facing consent toggles that gate the structured telemetry
// listeners. POCO with per-module persistence — loaded / saved by
// TelemetrySettingsService.
//
// Defaults are cautious: every consent-bearing toggle ships off and
// only flips when the user explicitly opts in via a Settings page
// dialog. Reproduces the legacy Deckle.Logging.TelemetrySettings
// posture.
public sealed class TelemetrySettings
{
    public bool LatencyEnabled       { get; set; } = false;
    public bool MicrophoneTelemetry  { get; set; } = false;
    public bool CorpusEnabled        { get; set; } = false;
    public bool RecordAudioCorpus    { get; set; } = false;

    // ApplicationLogToDisk gate l'écriture du journal applicatif sur
    // disque (app.jsonl). StorageDirectory est l'override optionnel
    // du dossier racine des fichiers JSONL ; vide = défaut résolu par
    // AppPaths.
    public bool   ApplicationLogToDisk { get; set; } = false;
    public string StorageDirectory     { get; set; } = "";
}
