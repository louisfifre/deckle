namespace Deckle.Diagnostics.Logging;

// ── AmbientCaptureGate ─────────────────────────────────────────────────────
//
// Volatile boolean qui mémorise si une boucle de capture ambient est
// active. Remplace l'ancien `TelemetryService.SetCaptureActive(bool)`
// du legacy Deckle.Logging sans réintroduire de hub central de
// télémétrie.
//
// Consommation. La gate est consultée par les delegates injectés dans
// `LogWindowEventListener` et dans le predicate app.jsonl au boot de
// l'App. Le filter combine cette gate avec le toggle utilisateur
// `LoggingSettings.LogAmbientCaptureActivity` pour décider si une
// émission Verbose des providers Ambient / Vision / Lighting doit
// atterrir dans le journal live ou persistant. Tant que la gate est
// ouverte (capture loop active) ET le toggle off, les Verbose ambient
// sont silencés ; hors capture, tout passe.
//
// Émission. La gate elle-même n'émet aucun event EventSource — c'est
// un pur état partagé. Les transitions sont déjà logguées au niveau
// applicatif par `DeckleAmbientSource.PipelineStarted` / `Pipeline-
// Stopped`, et les surfaces UI peuvent observer ces events sans
// passer par la gate.
//
// Threading. `volatile` garantit la visibilité cross-thread sans
// nécessiter de lock — l'ambient engine flippe la gate depuis son
// thread de pilotage, et chaque emission lit la valeur sans
// synchronisation. Les races (un Verbose qui arrive juste au moment
// du flip) sont bénignes : l'événement passe ou est filtré, jamais
// corrompu.
public static class AmbientCaptureGate
{
    private static volatile bool _active;

    public static bool IsActive => _active;

    public static void SetActive(bool active) => _active = active;
}
