# Reference — Convention EventSource (1.0)

Fiche canonique de la convention d'observabilité Deckle après la bascule vers `System.Diagnostics.Tracing.EventSource`. La fiche couvre la procédure de migration appliquée site par site, la table de mapping symboles legacy vers symboles EventSource, l'inventaire des providers par module avec leurs events, leurs niveaux, leurs keywords et le schéma JSONL attendu sur disque.

À lire avant chaque vague de migration. Met à jour au fil de l'avancement des vagues — la version `1.0` couvre l'état au moment de l'introduction de la pipeline ; les versions ultérieures (`1.1`, `1.2`…) refléteront l'évolution incrémentale de l'inventaire.

## Procédure de migration site par site

La migration d'un module se déroule en cinq passes ordonnées, chacune validée par un build et un grep zéro avant de passer à la suivante.

**Passe 1 — Provider du module.** Créer un fichier `Deckle<Module>Source.cs` dans le module, qui hérite de `DeckleEventSource` et porte un singleton statique `public static readonly Log = new()`. Décorer la classe avec `[EventSource(Name = "Deckle.<Module>")]`. Le nom suit la hiérarchie pointée et reflète le nom de namespace du module sans le suffixe `Source`.

**Passe 2 — Inventaire des sites d'appel.** Lister toutes les occurrences de `TelemetryService.Instance.*(...)` et `LogService.Instance.*(...)` du module. Pour chacune, décider du nom de méthode `[Event]` à créer sur le provider (verbe au passé pour les jalons : `RecordingStarted`, `ProfileLoaded` ; suffixe `Recorded` pour les payloads structurés : `LatencyRecorded`, `MicrophoneTelemetryRecorded`). Plusieurs sites qui partagent la même opération sémantique reçoivent le même nom d'event ; un site qui porte une opération distincte reçoit le sien.

**Passe 3 — Déclaration des `[Event]`.** Pour chaque nom retenu, ajouter une méthode `[Event(id, Level = ..., Keywords = ..., Message = "...")] public void <Name>(<paramètres snake_case>)` sur le provider. Le corps est `if (IsEnabled()) WriteEvent(id, paramètres...)` quand l'event est simple, ou `if (IsEnabled(level, keywords)) { /* construction */ ; WriteEvent(...) }` quand l'event coûte une construction (concat, allocation). Les ids sont séquentiels à partir de 1 sur chaque provider et publics dans le manifest ETW — ne pas réutiliser un id après suppression.

**Passe 4 — Bascule des sites d'appel.** Remplacer chaque `TelemetryService.Instance.Log(source, message, level, feedback)` par `Deckle<Module>Source.Log.<EventName>(<paramètres>)`. Un site qui voulait un `UserFeedback` ajoute en parallèle `Deckle<Module>Source.Log.UserFeedbackEmitted(severity, title, body, role)` — ce n'est pas une substitution, c'est une émission supplémentaire.

**Passe 5 — Validation.** Build via `scripts/lib/build-run.ps1 -NoRun`. Grep zéro sur les symboles legacy dans le périmètre du module migré : `TelemetryService.Instance`, `LogService.Instance`, `JsonlFileSink`, `ITelemetrySink`, `TelemetryGates`. Lancer l'app, exercer le scénario typique du module, vérifier visuellement la LogWindow et les fichiers JSONL sous `<telemetry>/validation/`. Comparer le schéma JSON ligne à ligne avec un échantillon de l'ancienne sortie — les clés et leur sérialisation doivent être identiques.

## Table de mapping legacy → EventSource

La table couvre les patterns d'appel les plus fréquents observés dans le code legacy.

`LogService.Instance.Info(LogSource.<X>, "<message>")` devient `Deckle<X>Source.Log.<Milestone>()` ou `Deckle<X>Source.Log.<Milestone>(<paramètres>)` selon que le message portait des données dynamiques. Le `LogSource.<X>` n'a plus de pendant explicite — l'appartenance au provider remplace la constante de catégorie.

`LogService.Instance.Verbose(LogSource.<X>, "<action> | k1=v1 | k2=v2")` devient `Deckle<X>Source.Log.<ActionRecorded>(<v1>, <v2>)` typé. Le format `k1=v1 | k2=v2` du legacy n'est plus formaté au site d'appel — il est reconstruit côté listener à partir du nom des paramètres et de leur valeur si une surface humaine en a besoin.

`LogService.Instance.Warning(LogSource.<X>, "<message>")` devient `Deckle<X>Source.Log.<Anomaly>()` ou typé selon le contexte. Le niveau `EventLevel.Warning` est porté par l'attribut `[Event]`.

`LogService.Instance.Error(LogSource.<X>, "<message>")` devient `Deckle<X>Source.Log.<Failure>()` ou typé. Niveau `EventLevel.Error`.

`LogService.Instance.Success(...)` est remappé en `EventLevel.Informational` — le legacy `Success` est abandonné, la sémantique de réussite passe par le message.

`LogService.Instance.Narrative(...)` est abandonné. Si le besoin était d'adresser un texte UX à l'utilisateur, il passe par `UserFeedbackEmitted` (HUD) ou par une string `.resw` (surface UI).

`TelemetryService.Instance.Latency(payload)` devient `DeckleWhispSource.Log.LatencyRecorded(audio_sec, hotkey_to_capture_ms, /* … 22 autres champs … */)`. La signature flat avec paramètres typés primitifs remplace le passage de POCO record. Niveau `EventLevel.Verbose`, keywords `Keywords.Heartbeat`.

`TelemetryService.Instance.Microphone(payload)` devient `DeckleAudioSource.Log.MicrophoneTelemetryRecorded(duration_seconds, samples, /* … 13 autres champs … */)`. Mêmes contraintes.

`TelemetryService.Instance.Corpus(payload)` devient `DeckleWhispSource.Log.CorpusRecorded(profile, profile_id, slug, /* … champs whisper + raw + metrics inlinés … */)`. Les sub-sections (WhisperSection, RawSection, CorpusMetricsSection) du legacy sont aplaties — EventSource n'accepte pas de types complexes en paramètres.

`new UserFeedback(title, body, severity, role)` passé en argument au sink HUD legacy devient une émission supplémentaire `Deckle<Module>Source.Log.UserFeedbackEmitted((int)severity, title, body, (int)role)` à côté de l'event de jalon principal. Le `HudFeedbackEventListener` filtre exclusivement sur le nom d'event canonique `UserFeedbackEmitted`.

## Inventaire des providers

Section étoffée vague par vague. Les providers à introduire par les vagues suivantes, dans l'ordre du brief : `DeckleCoreSource` + `DeckleVisionSource` + `DeckleLightingSource` (vague 3), `DeckleShellSource` + `DeckleLlmSource` + `DeckleSettingsSource` (vague 4), `DeckleWhispSource` + `DeckleAmbientSource` + `DecklePlaygroundSource` + `DeckleAppSource` (vague 5). La suppression finale du pilier legacy se fait en vague 6.

**`Deckle.Chrono` → `DeckleChronoSource`** — pilote vague 1, un seul event `PilotEmitted(string note)` ; niveau `Informational`, keyword `Lifecycle`. L'event est émis une fois au boot par `App.OnLaunched` pour exercer la pipeline complète. Sera remplacé en vague suivante par les vrais jalons du chrono quand des sites d'appel applicatifs migreront. Schéma JSONL :

```json
{"timestamp":"2026-05-22T18:35:00.0000000+02:00","kind":"log","session":"2026-05-22-a1b2","payload":{"note":"wave 1 boot"}}
```

**`Deckle.Audio` → `DeckleAudioSource`** — vague 2, premier provider applicatif réel. Seize events couvrant la boucle de capture microphone, les anomalies, le récap télémétrie structuré et la persistance settings du module. Les events de jalon sont en `Informational` avec keyword `Capture` (`RecordingStarted`, `CaptureStarted`, `RecordingCompleted`, `RecordingTailSummary`). Les anomalies sont en `Warning` ou `Error` selon la gravité (`EmptyBufferReceived`, `LowAudioDetected`, `CaptureLagDetected`, `DurationCapReached`, `MicrophoneTelemetryEmpty` en Warning ; `MicrophoneOpenFailed` en Error ; `DurationCapReached` combine `Capture | Lifecycle` parce que c'est aussi une transition d'état du recording). Le récap structuré `MicrophoneTelemetryRecorded` est en `Verbose` avec keyword `Heartbeat` et porte 14 paramètres aplatis depuis le legacy `MicrophoneTelemetryPayload` :

```
duration_seconds, samples,
min_dbfs, p10_dbfs, p25_dbfs, p50_dbfs, p75_dbfs, p90_dbfs, max_dbfs,
mean_rms, mean_dbfs, tail_rms, tail_dbfs, tail_state
```

Schéma JSONL miroir du legacy `microphone.jsonl` (sous-dossier `validation/` pendant la coexistence) :

```json
{"timestamp":"<iso>","kind":"microphone","session":"<id>","payload":{"duration_seconds":2.4,"samples":48,"min_dbfs":-72.3,"p10_dbfs":-61.4,"p25_dbfs":-55.9,"p50_dbfs":-48.2,"p75_dbfs":-41.6,"p90_dbfs":-36.7,"max_dbfs":-22.4,"mean_rms":0.0048,"mean_dbfs":-46.4,"tail_rms":0.0021,"tail_dbfs":-53.6,"tail_state":"silence"}}
```

Les quatre events de persistance settings (`SettingsLoaded`, `SettingsLoadComplete`, `SettingsLoadWarning`, `SettingsLoadError`) acceptent un message brut sous keyword `Lifecycle`. Cette zone est temporairement paramétrée par message — la doctrine strict-typed par opération est tenue ailleurs sur le provider mais relâchée ici parce que les delegates de `JsonSettingsStore<T>` dans `Deckle.Core` sont `Action<string>` et ne distinguent pas l'opération à l'appel. La refonte propre vient à la vague 4 quand `SettingsHost` migre lui-même sur EventSource. Le préfixe `[audio]` qui ouvrait les messages legacy disparaît : la source label LogWindow vient désormais du nom du provider (`Deckle.Audio` → `AUDIO`).

Le payload `MicrophoneTelemetryPayload` reste un POCO carrier dans `Deckle.Logging` jusqu'à la vague 6 — utilisé par `MicrophoneTelemetryCalculator`, `CaptureResult` et le ring d'auto-calibration de `WhispEngine`. `Deckle.Audio.csproj` garde une `ProjectReference` type-only vers `Deckle.Logging` documentée comme dette transitoire ; à la vague 6 le payload migre dans `Deckle.Audio`.

## Schéma JSONL — contrat machine

Le schéma émis par `JsonlEventListener` est identique au schéma legacy `JsonlFileSink` sur la structure d'enveloppe et sur les clés de payload. Une ligne JSON par event, séparateur `\n`, encodage UTF-8 sans BOM. Structure :

```json
{
  "timestamp": "<ISO 8601 avec offset local>",
  "kind": "<label de canal>",
  "session": "YYYY-MM-DD-XXXX",
  "payload": { "<paramètre snake_case>": <valeur typée>, … }
}
```

Le `kind` prend les valeurs `"log"` (canal général, dans `app.jsonl`), `"latency"` (canal latency, dans `latency.jsonl`), `"microphone"` (canal microphone, dans `microphone.jsonl`), `"corpus"` (canal corpus, dans `corpus.jsonl` ou `<profile>/corpus.jsonl` selon le contexte). Le label `"log"` est conservé tel quel pour la compatibilité avec les outils de benchmark existants ; les autres labels également.

Les valeurs primitives sont sérialisées par leur type natif (int → JSON number sans guillemets, string → JSON string, bool → true/false). Les `DateTime` et `DateTimeOffset` passent par leur représentation `"o"` (round-trip ISO 8601). Les `Guid` passent par leur représentation `"D"` (segments uppercase, dashes). Tout autre type est stringifié — en pratique ce cas ne survient pas, EventSource interdisant les types complexes en paramètres `[Event]`.

## Endroit où écrire pendant la migration

Pendant les vagues 1 à 5, les `JsonlEventListener` écrivent sous `<TelemetryDirectory>/validation/` pour éviter de mélanger avec les fichiers que le `JsonlFileSink` legacy possède encore. Le sous-dossier est explicite, séparé, et permet la comparaison ligne à ligne du schéma. À la vague 6, quand le legacy disparaît, `TelemetryListenerBootstrap.Configure(...)` est appelé avec `validationSubdirectory: false` et les fichiers reprennent leur emplacement canonique.

## Tests d'observabilité

`EventSource` est testable nativement via `EventListener`. Le pattern canonique de test est :

```csharp
sealed class TestEventListener : EventListener
{
    public readonly List<EventEntry> Events = new();
    protected override void OnEventSourceCreated(EventSource src)
    {
        if (src.Name == "Deckle.Audio")
            EnableEvents(src, EventLevel.LogAlways, EventKeywords.All);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Events.Add(LogWindowEventListener.BuildEntry(e));
    }
}
```

Le test instancie le listener, exécute le code sous test, assert sur la séquence collectée dans `Events`. Pas de mock de pipeline, pas d'inscription dans un hub maison — le contrat de test est natif au framework.

## Évolution de la fiche

La fiche est versionnée. Une refonte de fond (changement de la pipeline, nouvelle base class, abandon d'un concept majeur) crée une `2.0` qui supersède la `1.0`. Une évolution incrémentale (nouveau provider, nouvelle convention de paramètre, élargissement de l'inventaire) crée une `1.1`, `1.2`… Les versions précédentes restent en place pour la valeur historique — un changement de version n'écrase pas l'ancien fichier, il en crée un nouveau à côté.
