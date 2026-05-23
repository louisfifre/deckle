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

**`Deckle.Vision` → `DeckleVisionSource`** — vague 3, capture écran DXGI Output Duplication et FrameSampler. Vingt-trois events couvrant le cycle de vie de la session de capture (`ScreenCaptureStarting`, `CaptureSessionConfigured`, `ScreenCaptureStarted`, `CaptureStartFailed`, `CaptureStartFailedDetail`, `ScreenCaptureStopped`, `ScreenCaptureStoppedDetail`), la résolution monitor (`TargetMonitorResolved`, `MonitorNotFound`), les anomalies de la boucle d'acquisition (`AccessLostRecovering`, `DeviceLost`, `AcquireFrameFailed`, `TextureQueryFailed`, `FrameConsumerThrew`, `ReleaseFrameNonZero`), la résilience de duplication (`DuplicationRecreateFailed`, `DuplicationResizeDetected`, `DuplicationRecreated`, `DuplicationRecreateAttemptFailed`, `CaptureLoopWaitFailed`), et l'échantillonneur de frames (`SamplerInitialized`, `SamplerMapFailed`, `SamplerProcessFailed`). Niveau `Informational` pour les jalons, `Verbose` pour les détails miroirs, `Warning` ou `Error` selon la gravité des anomalies. Keywords `Capture` pour la session screen, `Pipeline` pour le sampler. Le legacy `LogService.Success` est mappé sur `EventLevel.Informational` — la sémantique de réussite passe par le message ("Screen capture started").

**`Deckle.Lighting` → `DeckleLightingSource`** — vague 3, driver Hue (cloud discovery, link-button pairing CLIP v1, REST CLIP v2 entertainment, color push, identify alert). Trente-cinq events regroupés en six familles : découverte (`DiscoveryStarted`, `DiscoveryStartedDetail`, `DiscoveryFound`, `DiscoveryBridgeFound`, `DiscoveryFailed`), pairing (`PairingStarted`, `PairingStartedDetail`, `BridgePaired`, `BridgePairedDetail`, `PairingWaiting`, `PairingRejected`, `PairingTimedOut`, `BridgeUnreachable`, `PairingHttpError`), groups (`ListingGroups`, `BridgeReturnedNoGroups`, `GroupsListed`, `GroupListed`), color push (`SetColorFailed`, `PushColorOff`, `PushColor`), entertainment (`ListingEntertainmentConfigs`, `EntertainmentEmpty`, `EntertainmentV2Catalog`, `EntertainmentListed`, `EntertainmentArea`, `PlacementListed`, `ClipV2GetFailed`), identify (`IdentifyFailed`, `LightIdentified`), lights listing (`ListingLightsInGroup`, `LightsListedEmpty`, `BridgeReturnedNoLights`, `LightsListed`, `LightListed`). Keywords `Lifecycle` pour discovery et pairing, `Pipeline` pour les listings, `Push` pour les color pushes et identifies. Les deux events `PushColor` et `PushColorOff` sont les plus chauds (15 Hz sur 5+ lampes en mode ambient) — le double gate `IsEnabled(EventLevel.Verbose, Keywords.Push)` évite l'allocation des arguments quand aucun listener n'écoute.

Le module `Deckle.Core` reste sans provider à la vague 3. La doctrine du brief : « les modules silencieux (Composition, Catalog) restent sans EventSource créé jusqu'à raison réelle. » Core est silencieux — son seul site d'appel résiduel était dans `JsonSettingsStore` qui reçoit ses delegates de log par injection ; aucun appel direct à `TelemetryService` ou `LogService` n'y subsiste.

**`Deckle.Shell` → `DeckleShellSource`** — vague 4, capacités du shell système. Quinze events couvrant le message-only host (`MessageOnlyHostCreated`), l'autostart HKCU\Run (neuf events `AutostartProbeFailed`, `AutostartEnableSkipped`, `AutostartEnableFailedAcl`, `AutostartEnabled`, `AutostartEnabledDetail`, `AutostartEnableFailed`, `AutostartDisableSkipped`, `AutostartDisabled`, `AutostartDisableFailed`), les hotkeys (`HotkeyVkResolveFailed`, `HotkeyRegistered`, `HotkeyLayoutChange`, `HotkeyReregisterFailed`), et `DispatcherQueueExtensions` (`DispatcherEnqueueRejected`). Trois LogSource legacy (`MsgHost`, `Hotkey`, `Settings` pour la branche autostart) fusionnent sous le tag `SHELL` selon la doctrine « l'observation s'attache au module qui contient l'opération ». Le paramètre `source` que `TryEnqueueOrLog` recevait jadis comme tag de log devient un champ `caller_source` dans le payload de `DispatcherEnqueueRejected` — la signature publique côté appelants reste identique.

**`Deckle.Llm` → `DeckleLlmSource`** — vague 4, réécriture via Ollama et surface Settings → LLM. Vingt-sept events couvrant la réécriture (`RewriteSkippedNoModel`, `RewriteStarted`, `RewriteStartedDetail`, `RewriteCompleted`, `RewriteCompletedDetail`, `RewriteMetrics`, `RewriteTimeout`, `RewriteUnavailable`), le polling `/api/ps` en attente prolongée (`PsProbeUnreachable`, `PsProbeEmpty`, `OllamaBusy`, `PsProbeFailed`), les failures UI de `LlmPage` (cinq events), les anomalies de `OllamaService` (`ListModelsInvalidJson`, `ShowModelInvalidJson`, `EndpointSchemeNotAllowed`, `EndpointNonLoopbackHost`), l'import GGUF (`GgufImportFailed`), et la persistance settings (`SettingsLoaded`/`Complete`/`Warning`/`Error`).

Premier provider à émettre l'event canonique `UserFeedbackEmitted(int severity, string title, string body, int role)` consommé par `HudFeedbackEventListener`. Les deux sites legacy qui passaient un `UserFeedback` inline à `_log.Warning` (timeout rewrite, rewrite unavailable) deviennent deux émissions distinctes : l'event de jalon (`RewriteTimeout` / `RewriteUnavailable`) suivi de `UserFeedbackEmitted` avec severity et role en `int` (0/1/2 pour Info/Warning/Error, 0/1 pour Replacement/Overlay). Le sink concret qui consomme côté HUD vit dans le host (`LegacyHudFeedbackSink`) et reconstruit un `UserFeedback` legacy à partir de la `FeedbackEntry` pour router vers `_hudWindow.ShowUserFeedback` ou `_overlayManager.Enqueue`. Le bridge disparaît à la vague 6.

Le legacy `_log.Narrative` est abandonné conformément à la doctrine ADR-0005 ; le seul site qui en émettait (`LlmService` ligne 85, "Rewriting the transcript with X — the Y model running in Ollama is cleaning up the raw text...") est supprimé. Le jalon `RewriteStarted` porte déjà la sémantique du jalon ; l'élaboration narrative ne survit pas à la bascule.

**`Deckle.Settings` → `DeckleSettingsSource`** — vague 4, le module le plus dense (~70 sites d'appel répartis sur dix fichiers). Quarante-six events regroupés en six familles : migration legacy → per-module (`SettingsBootstrap` — quinze events), backup et restore (`SettingsBackupService` — sept events), folder pickers (`FolderPickerCard` / `FolderPickerEditableCard` — un event partagé), setup wizard depuis Settings (`GeneralPage` — quatre events), navigation `NavigationView` (`SettingsWindow` — douze events couvrant selection, navigation success/failure, stack trace, item invocation, footer Logs), et la zone setter des ViewModels.

Zone setter paramétrée. Les ~30 setters des trois ViewModels (`GeneralViewModel`, `RecordingViewModel`, `DiagnosticsViewModel`) suivent un pattern homogène « Property ← value » — chaque setter logue son changement à Informational ou Verbose. La doctrine strict-typed se dégrade ici en `SettingChanged(string property, string value)` + `SettingChangedDetail(...)` (pour les sliders haute fréquence en Verbose) + `SectionReset(string section)`. Niveau et keyword fixes ; seuls (name, value) varient. Justifiable parce que l'opération est elle-même générique par construction (un setter qui logue), et que multiplier les events typés dédiés (`ThemeChanged`, `OverlayEnabledChanged`, etc.) sans gain sémantique créerait du bruit sans bénéfice. La doctrine sera ré-imposée si un setter mérite un jour des keywords distincts ou des paramètres typés supplémentaires.

`Deckle.Settings.csproj` garde une `ProjectReference` type-only vers `Deckle.Logging` parce que `DiagnosticsViewModel` consomme encore `LoggingSettingsService` et `TelemetrySettingsService` du legacy pour bridger les toggles utilisateur. La `ProjectReference` disparaît à la vague 6 quand ces POCO migrent dans `Deckle.Diagnostics.Logging` / `Deckle.Diagnostics.Telemetry`.

Le host (`Deckle/Diagnostics/`) reçoit en vague 4 le bridge `LegacyHudFeedbackSink` (`IHudFeedbackSink` → `UserFeedback` legacy → callbacks HUD existants) et la méthode `AppDiagnosticsBootstrap.AttachHudFeedbackSink(IHudFeedbackSink)` appelée depuis `App.OnLaunched` après la création du `HudWindow` / `HudOverlayManager`. Ce wiring permet aux émissions `UserFeedbackEmitted` des modules migrés de remonter au HUD pendant la coexistence avec le legacy `HudFeedbackSink`.

**`Deckle.Whisp` → `DeckleWhispSource`** — vague 5, le moteur de transcription monolithique et toutes ses surfaces périphériques. Quatre-vingt-six events couvrant le warmup boot (clip loader, mic gate, ollama gate, transcribe boot), le cycle de vie du modèle Whisper natif (chargement paresseux + idle unload), la redirection des logs whisper.cpp, le hotkey gating, la transcription (start détaillé, params, prompt, segments, complétion, repetition loop), le clipboard (alloc/open/setdata/verify/copy), le paste (foreground, UIA probe, SendInput, refus), la complétion de pipeline (`PipelineCompleted` + trois lignes humaines `PipelineTimings`/`LlmMetrics`/`Outputs`), le dispose, et la page `WhisperPage` + ViewModel (24 setters via `SettingChanged`).

Heartbeats structurés canoniques JSONL : `LatencyRecorded` (24 paramètres aplatis depuis le legacy `LatencyPayload` — audio_sec, model_load_ms, hotkey_to_capture_ms, record_drain_ms, stop_to_pipeline_ms, whisper_init_ms, vad_ms, vad_inference_ms, whisper_ms, llm_ms, ollama_load_ms, llm_prompt_eval_ms, llm_eval_ms, llm_prompt_tokens, llm_eval_tokens, clipboard_ms, paste_ms, strategy, n_segments, text_chars, text_words, profile, pasted, outcome) et `CorpusRecorded` (13 paramètres aplatis depuis le legacy `CorpusPayload` avec sections `Whisper`/`Raw`/`Metrics` linéarisées — profile, profile_id, slug, duration_seconds, model, language, elapsed_ms, initial_prompt, raw_text, raw_words, raw_chars, words_per_second, audio_file). Les deux events portent un `Message` mono-ligne pour LogWindow ; le payload complet sérialise via EtwSelfDescribingEventFormat. Les listeners `latency.jsonl` et `corpus.jsonl` filtrent strictement sur les noms d'event (`LatencyRecorded` / `CorpusRecorded`), pas sur le keyword.

Le legacy `_log.Narrative` est définitivement abandonné. Les huit narratifs résiduels (`Looking for speech...`, `No speech detected...`, `Captured X s of audio...`, `Whisper transcribed...`, `The transcription is now on the clipboard...`, `Final text pasted into...`, `Recording hit the cap...`, `Rewrite complete...` / `Rewrite failed...`) sont supprimés. Le helper `RaiseNarrative` lui-même est retiré du moteur ; seuls les jalons `Informational` (Transcribing, TranscriptionComplete, ClipboardCopied, PipelineCompleted, PasteSucceeded) et les `Verbose` structurés associés survivent. La perte d'élaboration narrative est compensée par la richesse des verbose structurés et par la lisibilité directe des messages typés au niveau `Informational`.

Les 23 sites legacy qui passaient un `UserFeedback` inline à `_log.Error/Warning` (model load failed, recording probe failed, capture mic error, warmup flag model/ollama KO, pipeline crashed, transcribe failed, clipboard alloc/open/setdata/verify failed, low-audio detected) deviennent chacun une émission de l'event typé + une émission `UserFeedbackEmitted`. Le helper privé `EmitUserFeedback(UserFeedback fb)` centralise la conversion enum → int au niveau du moteur. Le bridge HUD continue de fonctionner via `HudFeedbackEventListener` côté host.

**`Deckle.Lighting.Ambient` → `DeckleAmbientSource`** — vague 5, l'orchestrateur de l'ambient lighting et le pairing Hue côté consumer. Vingt-six events couvrant le cycle de vie du pipeline (`PipelineStarted`/`Detail`, `PipelineStartFailed`, `PipelineStopped`/`Detail`, `PushLoopCrashed`), les anomalies de la boucle multi-light (`StateChangedSubscriberThrew`, `MultiLightFallbackNoLights`, `MultiLightDriverIncompat`), les push ticks group et multi-light (`PushGroup`, `PushGroupFailed`, `PushMulti`, `PushMultiFailed`), le heartbeat agrégé par fenêtre (`Heartbeat`), le pairing service Hue (`BridgeAutoRestoreFailed`, `BridgePairingStored`, `BridgeRestoreSkipped`, `BridgeRestoredFromSettings`, `BridgeForgotten`), les anomalies de la surface Settings (`AmbientPagePairFailed`, `AmbientPageListGroupsFailed`), et la persistance settings du module.

Provider Name = `Deckle.Ambient` plutôt que `Deckle.Lighting.Ambient` pour un tag LogWindow court et lisible (`[AMBIENT]` après strip prefix par `LegacyLogWindowSink`). Le legacy distinguait `[HUE]` (pairing) et `[AMBIENT]` (pipeline) sous le même module — la migration les fusionne sous un seul tag selon la doctrine un-provider-par-module, le tag `[HUE]` survit dans `Deckle.Lighting` (vague 3) qui couvre le driver bas-niveau de Hue. Heartbeat structuré : un event `Heartbeat(mode, period_sec, ticks, pushed, dropped, unmapped_lights, http_stats_suffix)` émis chaque seconde quand le pipeline tourne, gated par `IsEnabled(Verbose, Heartbeat)` pour zero-alloc quand aucun listener n'écoute.

**`Deckle.Playground` → `DecklePlaygroundSource`** — vague 5, surface de tuning et de diagnostic. Onze events organisés en six familles : navigation (`NavWarning`, `NavError`), setters du `AmbientViewModel` (`SettingChanged`, `SettingChangedDetail`), screen capture playground (`ScreenCaptureVerbose`, `ScreenCaptureWarning`), interactions Hue (`HueWarning`, `HueInfo`, `HueVerbose`), interactions Ambient (`AmbientVerbose`, `AmbientInfo`). Le Playground est une surface dev-only ; la doctrine strict-typed se relâche en plusieurs events génériques per-canal qui acceptent un message libre. Cette entorse vaut aussi pour `DeckleSetupSource` (vague 5 host) — la prose technique d'interactions diagnostic ne mérite pas un event typé par phrase.

**`Deckle` (host) → `DeckleAppSource`** — vague 5, l'app hôte elle-même. Vingt-huit events couvrant les filets de crash (`CrashUnhandled`, `CrashAppDomain`, `CrashTaskScheduler`, `CrashStackTrace`), `ProcessExit`, le boot (`PathsInitialized`, `PathsDetail`, `StartupMilestones`), les transitions de status engine (`StatusChanged`), le shutdown / restart (`ShutdownRequested`, `ShutdownWarning`, `RestartRequested`, `RestartFromTrayRequested`, `RestartSpawnNewProcess`, `PostBuildRestartRequested`, `PostBuildShellExecute`, `PostBuildRelaunchFailed`), le command-line (`CmdLineSettingsFlag`, `CmdLinePostBuildFlag`), l'observer Ambient master (`AmbientPipelineState`, `AmbientMasterForcedOff`, `AmbientStartFailed`), les hotkey dispatchés au niveau host (`HotkeyStart`, `HotkeyStop`, `HotkeyNoProfile`), les surfaces HUD et LogWindow (`HudWarning`, `LogWindowWarning`), et `UserFeedbackEmitted` pour les notifications utilisateur émises depuis le host (notamment la hotkey registration failure).

Provider Name = `Deckle` (sans suffixe). Le bridge `LegacyLogWindowSink` a une règle spéciale pour ce nom : il mappe sur le tag `APP` au lieu de stripper le préfixe `Deckle.` (qui produirait une chaîne vide). Le legacy `LogSource.Status` qui produisait un tag distinct `[STATUS]` n'est pas préservé — toutes les transitions de status convergent maintenant sous `[APP]` via `StatusChanged`. Régression visuelle assumée, cohérente avec la doctrine un-provider-par-module.

**`Deckle.Setup` → `DeckleSetupSource`** — vague 5, le wizard first-run. Trois events génériques (`SetupInfo`, `SetupWarning`, `SetupError`) qui acceptent un message libre. Le wizard est une zone de prose technique d'interactions utilisateur (browse native folder, download progress, summary recap) — la doctrine strict-typed se relâche en niveau-par-niveau plutôt qu'en opération-par-opération, comme pour le Playground. Provider distinct de `Deckle` parce que le tag `[SETUP]` reste désirable pour filtrer le wizard first-run hors du flux principal de l'app.

## Vague 6 — Convergence et suppression `Deckle.Logging`

La vague 6 retire entièrement le module legacy `Deckle.Logging` après cinq vagues de migration site-par-site. Le chantier est découpé en huit sous-vagues atomiques, chacune validée par un build clean et un commit dédié, pour préserver la bisectabilité.

**Sous-vague 6a — Relocalisation des POCOs métier.** Quatre types qui vivaient encore dans `Deckle.Logging` parce qu'ils portaient de la sémantique métier (et non observabilité) migrent vers leur module naturel : `MicrophoneTelemetryPayload` part dans `Deckle.Audio.Telemetry` (aux côtés de `MicrophoneTelemetryCalculator` qui le produit), `TextMetrics` part dans `Deckle.Whisp.Engine` (consommateur unique : le comptage `text_words` pour `LatencyRecorded`), `CorpusPaths` part dans `Deckle.Core` (helper de paths consommé par les 4 dialogs `Deckle.Settings` et `WavCorpusWriter` — `Deckle.Core` est le seul module accessible aux deux sans introduire un cycle `Settings → Whisp`), et `WavCorpusWriter` part dans `Deckle.Whisp.Corpus`. Pattern d'inversion de dépendance pour `CorpusPaths` : le helper expose `ConfigureStorageDirectoryOverride(Func<string?>)` que l'App câble au boot, ce qui supprime la dep dure sur `TelemetryGates`.

**Sous-vague 6b — Démantèlement `UserFeedback`.** Les surfaces HUD acceptent désormais les paramètres primitifs `(int severity, string title, string body)` plutôt que le record `UserFeedback`. `HudWindow.ShowUserFeedback`, `HudOverlayManager.Enqueue`, `HudOverlayWindow.ApplyPayload` réécrivent leur signature. `UserFeedbackDurations` disparaît au profit de deux constantes locales à `HudWindow` (`SuccessDuration`, `FeedbackDuration(int)`). Le bridge App-side bascule de `HudFeedbackSink` (legacy via `TelemetryService`) + `LegacyHudFeedbackSink` (qui faisait FeedbackEntry → UserFeedback → callbacks) vers un seul `AppHudFeedbackSink` qui consomme `FeedbackEntry` directement. `WhispEngine` inline ses 14 sites `new UserFeedback(...)` en `EmitUserFeedback(FB_<SEV>, title, body, FB_<ROLE>)` avec des constantes nommées au niveau classe.

**Sous-vague 6c — `LogWindow` consomme EventSource direct.** Le viewer live abandonne l'interface `ITelemetrySink` et passe à `ILogWindowSink`. Création d'un wrapper local `LogEntry` qui précompute le `Text` formaté (`HH:mm:ss.fff [SOURCE] message`) avec la règle de mapping `Provider → tag uppercase` (`Deckle` → `APP`, `Deckle.Whisp` → `WHISP`). Le `LogEntryTemplateSelector` route sur `EventName` d'abord (`LatencyRecorded`/`CorpusRecorded`/`MicrophoneTelemetryRecorded` en tertiary text) puis sur `EventLevel` BCL. Les niveaux legacy `Success` et `Narrative` disparaissent. Le `LogWindowEventListener` (Deckle.Diagnostics) gagne un pattern buffer ring de capacité 5000 + multi-sink avec `AttachSink`/`DetachSink` : remplace `TelemetryService._history` et `Replay(sink)`, le LogWindow s'attache à sa première ouverture lazy et reçoit l'historique boot en replay. Suppression de `LegacyLogWindowSink`.

**Sous-vague 6d — Gates utilisateur sur les listeners.** Les `JsonlEventListener` consultent désormais une gate live à chaque émission, via un délégué injecté dans `TelemetryListenerBootstrap.ConfigureGates(Func<string, bool>)`. L'App câble le délégué sur la source de vérité retenue. Côté `LogWindowEventListener`, ajout d'un drop filter optionnel `ConfigureDropFilter(Func<EventEntry, bool>)` consulté avant insertion dans le buffer (les events filtrés ne sont ni rejoués ni broadcastés). Création de `AmbientCaptureGate` dans `Deckle.Diagnostics.Logging` — `volatile bool` lu par le filter, écrit par `AmbientEngine` autour de sa boucle de capture (remplace `TelemetryService.SetCaptureActive`). Le filter App-side compose la gate + `LoggingSettings.LogAmbientCaptureActivity` + provider check (Ambient/Vision/Lighting) pour drop les Verbose pendant la capture quand le toggle est off. Création du scaffold `TelemetrySettingsService` côté `Deckle.Diagnostics.Telemetry` (non instancié au boot — activation en 6g).

**Sous-vague 6e — Bascule path canonique + retrait `JsonlFileSink`.** Le flag `validationSubdirectory: true` passe à `false` dans `AppDiagnosticsBootstrap.Initialize` : les `JsonlEventListener` écrivent désormais directement aux paths canoniques `<TelemetryDirectory>/{app,latency,microphone,corpus}.jsonl`. Suppression de `TelemetryService.Instance.AddSink(new JsonlFileSink())` côté App. Suppression du fichier `JsonlFileSink.cs` et de son dossier parent vide. Le legacy `TelemetryService` survit avec des méthodes `Log`/`Latency`/`Corpus`/`SetCaptureActive` désormais sans aucun call site applicatif — pure dette transitoire jusqu'en 6g.

**Sous-vague 6f — Cleanup des `using Deckle.Logging` morts.** Audit des 18 fichiers qui portaient encore un `using Deckle.Logging;`. Quatorze sont nettoyés parce qu'aucun type Deckle.Logging n'est plus consommé dans le fichier (les call sites ont migré vers les EventSource providers en 6a-6e). Quatre fichiers gardent le using parce qu'ils consomment encore `LoggingSettingsService`, `TelemetrySettingsService`, `TelemetryGates`, `AppTelemetryGates` — survivants attendus jusqu'à la 6g qui les bascule sur les nouveaux services. Pas de modification de csproj à cette sous-vague (les `<ProjectReference>` legacy partent en 6g de manière coordonnée avec la suppression du module).

**Sous-vague 6g — Suppression du module `Deckle.Logging`.** Activation des deux nouveaux services côté Diagnostics : `Deckle.Diagnostics.Telemetry.TelemetrySettingsService` repointe son path sur `modules/telemetry/settings.json` (même que le legacy) pour préserver les settings utilisateur, et `Deckle.Diagnostics.Logging.LoggingSettingsService` est créé avec un path `modules/logging/settings.json` également aligné sur le legacy. Le conflit `ApplicationLogToDisk` (présent dans LoggingSettings ET TelemetrySettings) est résolu en faveur de Telemetry — c'est une gate de persistance disk, qui est la responsabilité Telemetry. Bascule des quatre consommateurs survivants : `IWhispEngineHost`/`AppWhispEngineHost` pointent sur `Deckle.Diagnostics.Telemetry.TelemetrySettings`, `App.xaml.cs` lit directement les nouveaux services pour `ConfigureGates` et `ConfigureLogWindowDropFilter` (plus de bridge `AppTelemetryGates`), `DiagnosticsViewModel` change ses usings. Suppression du dossier `src/Deckle.Logging/` entier (15 fichiers, ~1000 lignes) et de `src/Deckle/Logging/` (AppTelemetryGates + résidu orphelin TelemetryEventTemplateSelector). Retrait des six `<ProjectReference Include="..\Deckle.Logging\Deckle.Logging.csproj">` dans les csprojs consommateurs. Build clean à 18 modules.

**Sous-vague 6h — Validation finale et documentation.** Build clean confirmé. Grep zéro sur `Deckle.Logging` dans le code source (les seules occurrences résiduelles sont des commentaires historiques explicites et les artefacts `obj/Debug/` qui se régénèrent au prochain build Debug). Extension de cette fiche pour acter la convergence. Mise à jour des `CLAUDE.md` des modules concernés et du `CLAUDE.md` racine pour refléter la nouvelle topologie modulaire (`Deckle.Diagnostics` + ses deux enfants remplacent `Deckle.Logging` dans la liste).

### Bilan post-vague 6

L'inventaire complet des providers EventSource après 6h, dans l'ordre de la migration : `DeckleChronoSource` (vague 1 pilote), `DeckleAudioSource` (vague 2), `DeckleVisionSource` + `DeckleLightingSource` (vague 3, `Deckle.Core` reste sans provider, silencieux), `DeckleShellSource` + `DeckleLlmSource` + `DeckleSettingsSource` (vague 4), `DeckleWhispSource` + `DeckleAmbientSource` + `DecklePlaygroundSource` + `DeckleAppSource` + `DeckleSetupSource` (vague 5). Dix providers actifs au total. Aucun nouveau provider en vague 6 — la vague 6 est exclusivement structurelle.

La pipeline d'écoute consiste en quatre `JsonlEventListener` (un par fichier de destination), un `LogWindowEventListener` (avec buffer ring + multi-sink), un `HudFeedbackEventListener` (filter event-name + sink unique). Tous instanciés au boot, persistent pour la vie du process.

Les sources de configuration utilisateur post-vague 6 :
- `Deckle.Diagnostics.Logging.LoggingSettingsService` → `<UserDataRoot>/modules/logging/settings.json` → toggle `LogAmbientCaptureActivity`.
- `Deckle.Diagnostics.Telemetry.TelemetrySettingsService` → `<UserDataRoot>/modules/telemetry/settings.json` → gates `LatencyEnabled`, `MicrophoneTelemetry`, `CorpusEnabled`, `RecordAudioCorpus`, `ApplicationLogToDisk`, `StorageDirectory`.

La dette legacy résiduelle se résume aux commentaires historiques qui mentionnent les noms `LogService`, `TelemetryService`, `LegacyLogWindowSink`, etc. dans des explications de doctrine ou de migration — ces traces ne nuisent pas et documentent la trajectoire pour les sessions futures.

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
