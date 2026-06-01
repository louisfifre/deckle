# Reference — Convention EventSource (1.2)

Fiche canonique de la convention d'observabilité Deckle après la bascule vers `System.Diagnostics.Tracing.EventSource`. La fiche couvre la procédure de migration appliquée site par site, la table de mapping symboles legacy vers symboles EventSource, l'inventaire des providers par module avec leurs events, leurs niveaux, leurs keywords et le schéma JSONL attendu sur disque. La révision `1.2` ouvre une nouvelle famille de providers — les **sub-providers transverses** sous le nom ETW `Deckle.Diagnostics.<X>` — pour absorber les primitives techniques non-métier consommées par plusieurs modules avec exactement le même set de paramètres. Elle ajoute aussi cinq nouveaux keywords transverses (`Windowing`, `Threading`, `Theme`, `Resource`, `Network`) qui élargissent la table des bits 0-4 aux bits 0-9.

À lire avant chaque vague de migration ou avant toute extension d'instrumentation. Les versions `1.0` et `1.1` restent en place pour la valeur historique. Les versions ultérieures (`1.3`, `1.4`…) refléteront l'évolution incrémentale.

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

Le legacy `_log.Narrative` est abandonné conformément à la doctrine ADR-0003 ; le seul site qui en émettait (`LlmService` ligne 85, "Rewriting the transcript with X — the Y model running in Ollama is cleaning up the raw text...") est supprimé. Le jalon `RewriteStarted` porte déjà la sémantique du jalon ; l'élaboration narrative ne survit pas à la bascule.

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

## Sub-providers transverses

Famille de providers introduite par cette `1.2`. Distincts des providers par-module (`DeckleAudioSource`, `DeckleHudSource`, etc.) qui attribuent une observation au module qui contient l'opération, les **sub-providers transverses** absorbent les primitives techniques **non rattachées à un module métier** et **consommées par plusieurs modules avec exactement le même set de paramètres**. Sans cette famille, une primitive comme « positionner une fenêtre » se dupliquerait dans chaque provider de module qui ouvre une fenêtre — `DeckleHudSource.HudPositioned(...)`, `DeckleSettingsSource.SettingsWindowPositioned(...)`, `DeckleAppSource.LogWindowPositioned(...)` — avec à chaque fois la même signature, le même set d'inquiétudes (DPI, monitor, cursor), et aucun moyen de filtrer côté listener un événement transverse « toute fenêtre positionnée ».

### Convention de nommage

- **Nom ETW** : `Deckle.Diagnostics.<X>` où `<X>` est le nom du domaine technique en PascalCase singulier (`Windowing`, `Threading`, `Theme`, `Resource`, `Cancellation`, `Network`).
- **Classe C#** : `Deckle<X>Source` — le mot « Diagnostics » n'apparaît **pas** dans le nom de classe, c'est le namespace qui le porte. Exemple : `DeckleWindowingSource`, pas `DeckleDiagnosticsWindowingSource`.
- **Namespace** : `Deckle.Diagnostics`.
- **Fichier physique** : `src/Deckle.Diagnostics/Deckle<X>Source.cs`, à plat dans le module Diagnostics (pas de sous-dossier `Transverse/` — les providers transverses sont la nature secondaire du module et restent visibles dans la racine).
- **Tag LogWindow** : règle existante préservée (dernier segment uppercase). `Deckle.Diagnostics.Windowing` → `[WINDOWING]`, `Deckle.Diagnostics.Network` → `[NETWORK]`.

### Critère de promotion à deux clauses

Une primitive devient sub-provider transverse si **les deux clauses sont satisfaites cumulativement** :

1. **Au moins deux modules métier consomment la primitive avec exactement le même set de paramètres.** Si un seul module l'utilise, ou si plusieurs modules l'utilisent mais avec des paramètres différents, la primitive reste locale à son module (ou aux modules concernés, avec un signal d'audit pour identifier la convergence éventuelle).
2. **La primitive est de nature technique non-métier** — un wiring de plateforme (windowing, threading, theme, ressources, réseau, annulation, HTTP, storage) plutôt qu'une étape de pipeline métier. Une opération métier consommée par deux modules ne se promeut pas — elle se factorise en service partagé ou reste duplicée.

Les deux clauses fonctionnent comme un garde-fou. La première évite les sub-providers anémiques (un seul consommateur, déguisé en transverse). La seconde évite la fusion artificielle de providers métier sous un parapluie générique qui dilue la lisibilité de la cartographie.

### Liste actuelle des sub-providers

Six sub-providers **actifs** introduits par la vague d'instrumentation transverse (mai 2026) :

- **`DeckleWindowingSource`** (`Deckle.Diagnostics.Windowing`) — positionnement et dimensionnement de toute fenêtre WinUI 3 ou Win32 (HUD, HudOverlay, tray popup, SettingsWindow, LogWindow, SetupWindow, FolderPicker). Trois events : `WindowPositioned` (tronc commun), `OverlaySlotAssigned` (overlays empilés), `PopupAnchored` (popups ancrés à un contrôle parent). Keyword : `Windowing`.
- **`DeckleThreadingSource`** (`Deckle.Diagnostics.Threading`) — marshalling dispatcher (`DispatcherQueue.TryEnqueue`) significatif (App, HUD, LogWindow, SettingsWindow). Trois events : `MarshalQueued`, `MarshalCompleted`, `MarshalTimeout`. Hérite aussi de l'event historique `DispatcherEnqueueRejected` migré depuis `DeckleShellSource`. Keyword : `Threading`.
- **`DeckleThemeSource`** (`Deckle.Diagnostics.Theme`) — transitions de thème (`ActualThemeChanged` sur HUD, Settings, Log, Setup, tray). Un event : `ThemeChanged(surface, from, to, source)`. Keyword : `Theme`.
- **`DeckleResourceSource`** (`Deckle.Diagnostics.Resource`) — cycle de vie des ressources natives non-managées (textures D3D11 côté Vision, visuals Composition côté HUD ; Whisper natif différé). Trois events : `ResourceAcquired`, `ResourceReleased`, `ResourceLeakSuspect`. Keyword : `Resource`.
- **`DeckleCancellationSource`** (`Deckle.Diagnostics.Cancellation`) — `OperationCanceledException` captées sur les sites où l'annulation est sémantiquement intéressante (moteur Whisper, capture Vision, rewrite Llm). Un event : `OperationCancelled(operation, reason, age_ms)`. Keyword : aucun keyword transverse dédié — `Lifecycle` ou un keyword local convient (la nature « cancellation » est portée par le provider).
- **`DeckleNetworkSource`** (`Deckle.Diagnostics.Network`) — transitions de l'état réseau de la machine (présence/absence de connectivité, profil, comptes NIC). Un event : `NetworkStatusChanged(connected, profile, ipv4_count, ipv6_count)`, émis par un émetteur unique abonné à `NetworkInformation.NetworkStatusChanged` au boot de `App.OnLaunched`. Keyword : `Network`.

Deux sub-providers **reportés**, identifiés comme candidats mais non activés cette vague :

- **`DeckleHttpSource`** (`Deckle.Diagnostics.Http`) — pattern HTTP générique `HttpRequestCompleted(verb, endpoint, status, rtt_ms, retry_count)` qui factoriserait les requêtes externes (Hue REST, Ollama, discovery cloud, futurs drivers WLED ou services LLM tiers). Aujourd'hui les requêtes sont observées via events métier spécifiques (`PushColor`, `BridgePaired`, `OllamaBusy`) ; pas de squelette transverse réutilisable. Reporté parce que la lacune n'est pas critique tant qu'il n'y a que deux clients HTTP (Hue + Ollama) — la promotion se déclenche dès qu'un troisième client émerge. Lacune identifiée dans la 1.1 §*Lacunes identifiées*.
- **`DeckleStorageSource`** (`Deckle.Diagnostics.Storage`) — opérations de filesystem partagées entre modules (`JsonSettingsStore<T>` par module, `WavCorpusWriter`, résolution `CorpusPaths`, import GGUF, dump/restore settings). Pattern candidat : `FileWritten(path, bytes, duration_ms, outcome)` + `DirectoryEnsured(path, created)`. Reporté parce que la refonte Storage est tributaire de la sous-vague 6a (relocalisation des POCOs vers les modules métier) et de la convergence `SettingsHost` — instrumenter avant cette refonte risquerait d'attacher des events à des sites qui vont bouger.

Lorsqu'une nouvelle primitive transverse émerge dans le projet, l'instruction est d'auditer les deux clauses **avant** de créer le sub-provider — pas de provider transverse spéculatif. Inversement, lorsque trois ou quatre modules émettent un event au même schéma sous leur provider local, c'est le signal de promouvoir le pattern en sub-provider transverse.

### Exemple complet — `DeckleWindowingSource`

Provider canonique illustrant la convention complète. Tous les events sont en `Verbose` parce qu'ils portent des identifiants (handle moniteur, coordonnées) — par construction du contrat « tout event qui porte un ID est Verbose » du `CLAUDE.md` Deckle.Diagnostics. Le set canonique de paramètres (`hmon`, `dpi`, `anchor`, `pos`, `size`, `slot`, `parent_rect`) est défini dans la 1.1 §*Classe 6 — Windowing*. La convention de coordonnées est **pixels écran absolus** (les calculs internes peuvent partir de DIP, les events portent toujours du pixel pour permettre la reverse via `dpi`).

```csharp
namespace Deckle.Diagnostics;

[EventSource(Name = "Deckle.Diagnostics.Windowing")]
public sealed class DeckleWindowingSource : DeckleEventSource
{
    public static readonly DeckleWindowingSource Log = new();

    // Tronc commun — émis par tout site qui positionne ou redimensionne
    // une fenêtre. `window` est un nom logique court ("hud", "settings",
    // "log", "setup", "tray-popup", "folder-picker"). Les overlays et
    // popups émettent CET event en plus de leur event spécialisé.
    [Event(1, Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "window positioned | window={0} | hmon=0x{1:X} | dpi={2} | anchor={3} | pos={4},{5} size={6},{7}")]
    public void WindowPositioned(
        string window, long hmon, int dpi, string anchor,
        int pos_x, int pos_y, int size_w, int size_h)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(1, window, hmon, dpi, anchor, pos_x, pos_y, size_w, size_h);
    }

    // Spécialisation overlays empilés — slot=0 pour le premier, slot=1
    // pour le suivant, etc. `WindowPositioned` est aussi émis avec
    // window="hud-overlay" pour conserver le déterminisme du tronc.
    [Event(2, Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "overlay slot | slot={0} | hmon=0x{1:X} | pos={2},{3} size={4},{5}")]
    public void OverlaySlotAssigned(
        int slot, long hmon,
        int pos_x, int pos_y, int size_w, int size_h)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(2, slot, hmon, pos_x, pos_y, size_w, size_h);
    }

    // Spécialisation popups ancrés — `parent_rect` est le rectangle du
    // contrôle ancré (ex. icône tray, bouton FolderPicker) en pixels
    // écran absolus, sérialisé en string "x,y,w,h" pour tenir dans 6
    // paramètres. `WindowPositioned` est aussi émis avec
    // window="tray-popup" ou "folder-picker" pour le tronc.
    [Event(3, Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "popup anchored | popup={0} | parent_rect={1} | pos={2},{3} size={4},{5}")]
    public void PopupAnchored(
        string popup, string parent_rect,
        int pos_x, int pos_y, int size_w, int size_h)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(3, popup, parent_rect, pos_x, pos_y, size_w, size_h);
    }
}
```

**Pattern « tronc commun + events spécialisés ».** Plutôt que des sentinelles (`slot=-1` quand non applicable, `parent_rect=""` quand pas un popup), chaque cas a son event distinct avec son schéma propre. Le déterminisme cross-site est porté par le contrat « tout site qui positionne une fenêtre émet `WindowPositioned` avec ce schéma exact, en plus de l'event spécialisé si applicable ». Cette approche garde les events lisibles individuellement (pas de champs vides), grep-able par `EventName` côté listener, et permet à un consommateur de s'abonner uniquement à `OverlaySlotAssigned` sans recevoir le bruit des fenêtres app.

**Convention pour les autres sub-providers.** Le pattern est dupliqué — pas de sentinelles dans le tronc commun, events spécialisés pour les cas particuliers. `DeckleThreadingSource.MarshalQueued` est le tronc, `MarshalTimeout` est l'event spécialisé du cas timeout (pas un champ `outcome` du tronc). `DeckleResourceSource.ResourceAcquired` / `ResourceReleased` sont les deux versants normaux, `ResourceLeakSuspect` est l'event spécialisé du cas anormal (release manqué détecté à finalization).

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

La dette legacy résiduelle se résume aux commentaires historiques qui mentionnent les noms `LogService`, `TelemetryService`, `LegacyHudFeedbackSink`, etc. dans des explications de doctrine ou de migration — ces traces ne nuisent pas et documentent la trajectoire pour les sessions futures.

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

## Classes d'observables canoniques

Cette section (nouvelle en `1.1`) répond à une question qui n'était pas traitée en `1.0` : quand on instrumente un bout de code, **quels paramètres viser par défaut**. La `1.0` répondait *où et comment écrire* l'event (provider, niveau, keyword, format). La `taxonomy.md` du skill `deckle-logging` répond *quelles catégories de code donnent quels cadres canoniques* (USE / RED / Four Golden Signals). Cette section couche entre les deux : pour chaque classe de situation rencontrée chez Deckle, un set canonique de paramètres et un état actuel de la couverture.

Neuf classes suffisent à couvrir le code Deckle existant et futur. Un site peut relever de deux classes simultanément — par exemple une étape de boot qui charge un modèle relève de *Lifecycle* ET de *Pipeline batch*.

### Classe 1 — Lifecycle et boot

Démarrage process, init paths, warmup ressources, chargement module, transitions d'état d'app (`idle → recording → transcribing → done`), shutdown amorcé, restart post-build, crash safety nets. Opérations uniques par cycle, jalons attendus en `Informational` avec keyword `Lifecycle`, miroirs en `Verbose` quand des paramètres techniques justifient un détail séparé.

**Set canonique** : nom de l'étape, durée `<name>_ms`, outcome (`succeeded` / `skipped` / `failed`), backend ou variant actif quand pertinent (`backend=Vulkan`, `model=ggml-large-v3.bin`), version du composant si charge réseau ou disque, motif de transition pour les state changes (`reason=hotkey`, `reason=tray`, `reason=auto-shutdown`).

**État actuel** : très bien instrumenté côté `Deckle` (App boot, status transitions, shutdown/restart), `Deckle.Whisp` (warmup boot, model load), `Deckle.Audio` (capture lifecycle), `Deckle.Vision` (`ScreenCaptureStarted`/`Stopped`). Pattern `PathsInitialized` + `PathsDetail` (jalon Info + miroir Verbose) est l'archétype propre.

### Classe 2 — Pipeline batch

Transcription d'un blob audio, réécriture LLM, calibration appareil, push ambient sur un frame complet. Opération discrète début → fin → résultat. Cadres dominants RED et Four Golden Signals.

**Set canonique** : identifiant d'opération (`transcription_id` si pertinent), durée totale et par phase clé (`hotkey_to_capture_ms`, `record_drain_ms`, `whisper_init_ms`, `whisper_ms`, `llm_ms`, …), métriques d'entrée (`audio_sec`, `text_chars`, `prompt_tok`), métriques de sortie (`n_segments`, `text_words`, `tok_s`), outcome enum (`outcome=ok|repetition_loop|llm_failed|user_cancelled`), profil ou stratégie active (`strategy=`, `profile=`), flag binaire d'effet de bord (`pasted=true`).

**État actuel** : `LatencyRecorded` à 24 champs (cf. inventaire `Deckle.Whisp` plus haut) est l'exemple canonique réussi — *canonical log line* au sens industrie, colocalise toutes les mesures clés en une ligne par invocation. `CorpusAsrRecorded` (14 champs) et `CorpusRewriteRecorded` (12 champs) suivent le même pattern pour la persistance dataset (cf. ADR-0006). Le pattern est mature côté transcription ; il n'est pas systématisé ailleurs (par exemple le push ambient pourrait avoir son canonical heartbeat plus riche que l'actuel `Heartbeat` à 7 champs).

### Classe 3 — Boucle temps réel haute fréquence

Capture audio polling 50 ms, capture écran DXGI à ~15 Hz, push lumière à 10-15 Hz, raw input curseur ~125 Hz pour fade proximité HUD. Opérations nombreuses, brèves, l'enjeu est la stabilité du débit. Cadres dominants USE et Four Golden Signals côté flux sortant.

**Set canonique** : sur fenêtre glissante (1 s typique) — `fps` ou `ticks/s` observés, `drops` (frames acquis mais non traités), latence intra-tick `p50_ms` / `p95_ms`, saturation de file (`queue_depth` ou `pending_frames`), erreurs intra-fenêtre (`acquire_fail=N`). Pattern dit *rollup* — une ligne périodique qui résume N ticks, plutôt qu'une ligne par tick qui noierait l'observation.

**État actuel** : la `Heartbeat` de `Deckle.Lighting.Ambient` est l'incarnation actuelle de ce pattern (7 champs, périodique). `Deckle.Vision` n'a pas d'équivalent — la boucle de capture émet par incident (anomalies, recovery) mais pas une trace régulière du débit. `Deckle.Audio` émet le RMS tick sur un event UI direct (alimentation HUD), explicitement *non* loggué selon la doctrine « heartbeats haute fréquence < 1 s ne sont pas loggués », mais le récap distributif `MicrophoneTelemetryRecorded` à 14 champs en fin de session compense.

### Classe 4 — Driver matériel et intégration externe

Pilote micro (WASAPI), client HTTP Hue REST, client HTTP Ollama, EventStream SSE, P/Invoke whisper.cpp natif. Frontière entre code interne et système externe sur lequel on a peu de contrôle. Cadres dominants RED (durée aller-retour, taux d'erreur, taux d'appel) + USE sur ressources internes consommées.

**Set canonique** : événements de cycle de vie de la connexion (`discovery`, `pairing`, `session_opened`, `session_closed`, `signal_lost`, `reconnected`) ; codes de retour natifs avec notation canonique stable (`hr=0x{hex}` HRESULT, `result=<int>` mmsys, `status=<int>` HTTP, `mmsys=<int>` waveIn) ; identifiants tronqués ou masqués pour les secrets (`username=eDOvxk-...`, `clientkey=[redacted]`) ; latence aller-retour (`rtt_ms`) ; ressources consommées (`http_clients`, `socket_pool`).

**État actuel** : `Deckle.Lighting` (40 events) couvre bien tout le cycle Hue — discovery, pairing, control, EventStream, identify, color push. La discipline de masquage des secrets (clientkey jamais en clair, username tronqué) est tenue. `Deckle.Llm` instrumente les états Ollama (`OllamaBusy`, polling `/api/ps`). `Deckle.Audio` couvre les anomalies waveIn par codes `mmsys`. Une normalisation transverse manque — il n'y a pas de pattern uniforme `HttpRequestCompleted(verb, endpoint, status, rtt_ms, retry_count)` réutilisable, c'est implicite dans chaque event spécifique. Lacune traitée plus bas (« Pattern HTTP générique »).

### Classe 5 — Surface UI et navigation

Page settings ouverte, dialog confirmé, formulaire validé, navigation NavView, ViewModel setter qui change une valeur, page chargée prête, page failed to init. Cadres dominants Four Golden Signals adaptés (latence perçue, taux d'actions par session, erreurs visibles) + RED sur opérations déclenchées utilisateur.

**Set canonique** : transitions d'état UI en jalons concis (`Page loaded`, `Dialog opened`, `Form validated`) ; détails techniques en Verbose miroir (`page=Llm | duration_ms=120 | items=5`) ; UserFeedback adressé à l'utilisateur via le canal canonique séparé (`UserFeedbackEmitted` au contrat strict `(severity, title, body, role)`).

**État actuel** : `Deckle.Settings` est l'exemple riche — 46 events couvrent navigation NavView, ViewModel setters, backup/restore, folder picker, setup wizard. L'event générique paramétré `SettingChanged(string, string, string)` est l'entorse acceptée à la discipline strict-typed (un setter générique du MVVM ne sait pas distinguer 30 setters distincts).

### Classe 6 — Windowing

Nouvelle classe transverse introduite par cette `1.1` parce qu'elle est absente du code instrumenté à ce jour. Concerne le **positionnement et le dimensionnement de toute fenêtre WinUI 3 ou Win32** — `HudWindow` (320×64 bas-centre), `HudOverlayWindow`, `HudMessage` hybrid bleed (400×160 puis retract 272×78), `SettingsWindow`, `LogWindow`, `SetupWindow`, popup tray menu, popup folder picker. Tous ces sites calculent à la main une position en DIP, multiplient par `GetDpiForWindow(hwnd) / 96.0`, choisissent un `DisplayArea` ou un `MonitorFromPoint`, gèrent le multi-écran.

**Set canonique** :
- `hmon=0x{hex}` — handle moniteur retourné par `MonitorFromPoint` ou `GetMonitorInfo`.
- `dpi=192` — entier, résultat `GetDpiForWindow`.
- `scale=2.0` — une décimale, dérivé `dpi/96`.
- `work_area=2560,40,2520,1392` — rect en pixels écran absolus (x, y, w, h).
- `cursor=1240,860` — pixels écran absolus, retour `GetCursorPos`.
- `anchor=BottomCenter` — ancrage choisi côté settings.
- `pos=1100,820 size=320,64` — rect calculé en **pixels écran absolus** (convention fixée par cette fiche pour permettre la reverse via `dpi`).
- Pour les overlays empilés : `slot=0` ou `slot=1`.
- Pour les popups : `parent_rect=x,y,w,h` du contrôle ancré.

Convention de coordonnées : **pixels écran absolus partout**. Les calculs internes peuvent partir de DIP, mais les events émis pour observation portent les valeurs en pixels (cohérent avec ce que retournent `GetCursorPos`, `GetWindowRect`, `GetMonitorInfo`, et permet de reverse vers DIP via `dpi`).

**État actuel** : **non observé**. Le HUD a un seul `HudWarning(string)` paramétré par message libre. `SettingsWindow`, `LogWindow`, `SetupWindow` n'émettent rien sur leur positionnement. `TrayIconManager` ne loggue ni position icône ni position popup. Quand un bug arrive (« le HUD est mal placé en DPI 200% sur le second écran »), l'instrumentation est faite à la main avec `File.AppendAllText` — exactement le type de chemin parallèle que la doctrine de centralisation veut éviter. La classe est à câbler progressivement sur les sites de positionnement existants (chantier par module, cf. *Lacunes identifiées*).

### Classe 7 — Activité utilisateur

Hotkey pressé, entrée tray cliquée, toggle settings changé, page settings ouverte manuellement. Cadre dominant RED sur opérations déclenchées.

**Set canonique** : déclencheur (`trigger=hotkey:WinTilde | tray:Quit | settings:OllamaModel`), résultat (`outcome=triggered|ignored:busy|ignored:not-configured`), valeur avant et après pour un toggle (`before=true after=false`).

**État actuel** : `Deckle.Shell` couvre les hotkeys (`HotkeyRegistered`, `HotkeyToggleIgnored`). `Deckle` (App) couvre `HotkeyStart`, `HotkeyStop`, `HotkeyNoProfile`. `Deckle.Settings` couvre les setters via `SettingChanged` générique. Cohérent mais éclaté entre trois providers (Shell pour la primitive, App pour l'orchestration, Settings pour la modification de valeur) — c'est correct doctrinairement (« l'observation s'attache au module qui contient l'opération »), un peu lourd à recoller mentalement quand on lit la LogWindow.

### Classe 8 — Persistance settings per-module

Chaque module qui a des settings (`Audio`, `Transcription`, `Llm`, `Lighting.Ambient`, …) charge et persiste via `JsonSettingsStore<T>` sous `<UserDataRoot>/modules/<name>/settings.json`. Quatre events transitoires partagent le pattern : `SettingsLoaded` / `SettingsLoadComplete` / `SettingsLoadWarning` / `SettingsLoadError`, tous paramétrés par message string libre.

**Set canonique cible** (post-refonte vague 4) : `module=<name>`, `path=<abs>`, `outcome=loaded|defaulted|migrated|failed`, `size_bytes=<n>`, `version=<schema>`, durée `load_ms=<n>`, raison si échec (`reason=missing|corrupt|migration_failed`).

**État actuel** : entorse documentée dans `DeckleAudioSource` et `DeckleWhispSource` — le delegate `Action<string>` de `JsonSettingsStore` ne sait pas distinguer au site d'appel entre « Settings loaded », « Settings initialized (defaults) » et « Settings reloaded from disk ». La discipline strict-typed est temporairement échangée contre un typage par niveau et keyword. La doctrine prévoit que `SettingsHost` / `JsonSettingsStore` basculent eux-mêmes sur un contrat EventSource direct en vague 4. C'est de la dette identifiée et planifiée, pas une dérive.

### Classe 9 — Crash et safety nets

`Application.UnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`. Trois filets posés au constructeur de `App`. Capture exception type, message, stack trace, contexte (handler invoqué, thread).

**Set canonique** : `source=app|appdomain|task-scheduler`, `ex_type=System.Foo.Bar`, `ex_message=<short>`, `stack=<multi-line ou indiqué via event séparé>`, `thread_id=<n>`, `terminating=true|false` (pour AppDomain).

**État actuel** : `Deckle` (App) porte les 4 events `CrashUnhandled`, `CrashAppDomain`, `CrashTaskScheduler`, `CrashStackTrace`. Pattern bien tenu — la stack trace est sur un event séparé pour ne pas exploser la signature primaire.

## Lacunes identifiées

Cette section (nouvelle en `1.1`) pointe les zones structurellement sous-instrumentées ou non instrumentées. Chacune est un chantier candidat. Les actes d'instrumentation eux-mêmes vivent dans les `CLAUDE.md` des modules concernés une fois lancés ; cette fiche ne fait que les inventorier.

**Windowing absent partout.** Aucun site de positionnement ne loggue son arithmétique. La classe canonique est définie ci-dessus (`Classe 6`), reste à la câbler sur `HudWindow`, `HudOverlayManager`, `HudOverlayWindow`, `TrayIconManager`, `SettingsWindow`, `LogWindow`, `SetupWindow`, popups `FolderPickerCard`. Deux options de câblage envisageables : (a) un mini sub-provider `DeckleWindowingSource` partagé, (b) un set d'events ajouté au provider du module concerné. L'option (b) est plus alignée sur la doctrine « l'observation s'attache au module qui contient l'opération » mais demande de tenir le set canonique en transverse pour que les events soient comparables.

**HUD interne sous-instrumenté.** Le module porte une mécanique d'affichage très riche — state machine à six états, fade-in 150 ms cubic ease-out, retract 800 ms après ombre attenuated, proximity smoothstep entre `NEAR_RADIUS_DIP=10` et `FAR_RADIUS_DIP=128`, hybrid bleed 400×160 → 272×78, warm pass invisible au boot via layered alpha. Un seul event `HudWarning(string)` couvre tout ça (`DeckleHudSource`). Quand « le HUD ne s'efface pas avant le paste » ou « le HUD flashe brièvement au boot », il n'y a aucune trace. Les events candidats Verbose miroir des transitions (`SetState | from=Recording to=Transcribing alpha=255 dpi=192`), du fade-in (`fade_in_start | duration_ms=150 from=0 to=255`), du retract (`message_retract | from=400x160 to=272x78`), du warm pass (`warm_pass_complete | took_ms=42`), de la proximity (`proximity_alpha | cursor_dist_dip=37 alpha=183`) sont attendus mais absents. Chantier qui se chevauche avec Windowing — DPI, position, taille sont communs.

**Capture vidéo sans rollup périodique fps.** `Deckle.Vision` instrumente les jalons et les anomalies mais pas un heartbeat régulier équivalent au `Heartbeat` de `Deckle.Lighting.Ambient`. Quand on diagnostique « la capture est lente » ou « les frames arrivent saccadés », pas de mesure continue. À ajouter dans la classe `Boucle temps réel haute fréquence` — un event `Heartbeat(period_sec, frames_acquired, frames_dropped, p50_acquire_ms, p95_acquire_ms, p50_sample_ms, p95_sample_ms)` émis chaque seconde, gated par `IsEnabled(Verbose, Heartbeat)`.

**Pas de pattern HTTP générique.** Les requêtes externes (Hue REST, Ollama, discovery cloud) sont observées via events spécifiques métier (`PushColor`, `BridgePaired`, `OllamaBusy`). Pas de squelette transverse `HttpRequestCompleted(verb, endpoint, status, rtt_ms, retry_count)`. Pas critique aujourd'hui ; deviendra utile dès qu'un troisième client HTTP émerge (driver WLED, services LLM tiers en remplacement d'Ollama).

**Provider unique par module ne couvre pas les sous-domaines.** `Deckle.Whisp` à 106 events est lisible parce que l'auteur a organisé les EventIds par zone (Warmup 1-16, Model 17-29, WhisperLog 30-33, Hotkey 35-36, etc.). Mais le **filtrage côté LogWindow** se fait par provider (SelectorBar par module), pas par sous-zone. Pour debug un sous-domaine spécifique (le seul Clipboard de Whisp, par exemple), l'utilisateur grep le texte. Acceptable, mais une dimension supplémentaire « sous-keyword par zone » serait possible si la lecture devient inconfortable. Non chiffré comme chantier — à laisser émerger.

**Pattern `SetupInfo`/`Warning`/`Error` paramétré par message** dans `Deckle.Setup`. Trois events génériques typés sur le niveau, payload string libre. Contredit la doctrine strict-typed per opération. Acceptable comme phase transitoire (le module Setup est jeune et son périmètre va évoluer) mais à reclasser en events distincts au prochain passage substantiel sur le module.

**Préfixes module obsolètes dans les `Message` Settings/Audio.** Pattern legacy « `[audio] Settings loaded` » qui apparaît encore parce que `JsonSettingsStore` est appelé avec un `prefix` paramétré. La nouvelle architecture met le tag de source (`AUDIO`) en colonne LogWindow et le préfixe `[audio]` devient redondant. À nettoyer en même temps que la refonte `SettingsHost` (vague 4 de la `Classe 8`).

## Évolution de la fiche

La fiche est versionnée. Une refonte de fond (changement de la pipeline, nouvelle base class, abandon d'un concept majeur) crée une `2.0` qui supersède la `1.x`. Une évolution incrémentale (nouveau provider, nouvelle convention de paramètre, élargissement de l'inventaire, ajout de doctrine transverse comme la `1.1` qui introduit les classes d'observables et les lacunes, ou la `1.2` qui ouvre les sub-providers transverses) crée une `1.3`, `1.4`… Les versions précédentes restent en place pour la valeur historique — un changement de version n'écrase pas l'ancien fichier, il en crée un nouveau à côté.

Delta `1.0` → `1.1` : ajout des sections *Classes d'observables canoniques* (neuf classes, set canonique de paramètres par classe, état actuel de la couverture) et *Lacunes identifiées* (Windowing absent partout, HUD interne sous-instrumenté, pas de rollup fps Vision, pas de pattern HTTP générique, plus trois lacunes mineures déjà connues). Le reste du contenu est reconduit tel quel.

Delta `1.1` → `1.2` : ouverture de la famille **sub-providers transverses** sous le nom ETW `Deckle.Diagnostics.<X>`, avec convention de nommage formalisée, critère de promotion à deux clauses, liste des six sub-providers actifs (`Windowing`, `Threading`, `Theme`, `Resource`, `Cancellation`, `Network`) et des deux reportés (`Http`, `Storage`), et exemple complet `DeckleWindowingSource` avec le pattern « tronc commun + events spécialisés » (pas de sentinelles). Extension de la table `Keywords` transverse des bits 0-4 aux bits 0-9 par ajout de `Windowing=0x20`, `Threading=0x40`, `Theme=0x80`, `Resource=0x100`, `Network=0x200`. Les modules métier qui consomment ces sub-providers transverses (HUD pour les state machine internes, Vision pour le heartbeat de capture, etc.) sont instrumentés en parallèle dans la même vague et apparaissent à l'inventaire des providers du module concerné — la 1.2 fixe la convention transverse mais ne rejoue pas l'inventaire des providers par-module qui évolue indépendamment.
