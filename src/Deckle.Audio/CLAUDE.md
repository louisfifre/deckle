# CLAUDE.md — Deckle.Audio

Module de capture audio. Le périmètre actuel est 100 % microphone : capture WASAPI via `waveInOpen`, polling sur quatre buffers circulaires de 50 ms, conversion PCM16 → float, calcul RMS en temps réel pour alimentation du HUD, télémétrie tail RMS au Stop pour détection de mic débranché ou audio bas, auto-calibration heuristique sur les N dernières sessions. Le module est aussi propriétaire de l'`AudioLevelMapper` (statiques mutables qui contrôlent la courbe dBFS → niveau perceptuel) — l'app et le HUD lisent ces statiques à chaque vsync pour rendre l'animation du chrono.

Le nom du module est volontairement plus large que son contenu actuel. Le module a été renommé `Deckle.Capture → Deckle.Audio` en mai 2026 parce que `Capture` était un faux générique qui n'avait aucune primitive partageable avec la capture vidéo (le futur module `Deckle.Vision`). Le nom `Audio` reste correct si on ajoute plus tard une capture loopback (output PC pour triggers audio dans l'ambient lighting), une sortie audio (TTS de confirmation, monitoring), ou tout autre traitement de signal audio. Les classes internes ont gardé leurs noms historiques (`CaptureSettings`, `CaptureResult`, `MicrophoneCapture`, `CaptureSettingsService`) — l'API en consommation devient `Deckle.Audio.CaptureSettings` qui se lit comme « settings de capture audio dans le module Audio ».

## Contrat avec les consommateurs

Le module expose `MicrophoneCapture` (orchestrateur de cycle de vie : `Probe()` pour pré-vol, `Record(IAudioRecordingHost, CancellationToken)` pour la séance), `IAudioRecordingHost` (contrat injecté par l'orchestrateur — Whisp typiquement — qui expose les settings live consultés à chaque entrée de `Record()`), `CaptureResult` (audio float[] + télémétrie micro + outcome), `CaptureSettings` + `CaptureSettingsService` (UI Settings → Recording page + auto-load), `AudioLevelMapper` (mappage RMS → niveau perceptuel utilisé par `Deckle.Chrono.Hud`).

Le pattern fondamental : le module ne sait pas pourquoi on capture (transcription, futur Ask-Ollama, autre). Il sait juste comment capturer proprement et comment exposer la télémétrie nécessaire pour calibrer l'expérience utilisateur. Les consommateurs implémentent `IAudioRecordingHost` pour fournir le device id, le cap durée et le toggle télémétrie, et reçoivent un `CaptureResult` complet à la sortie de `Record()`.

## Caractéristiques de la capture

Format unique non paramétrable : 16 kHz, mono, PCM16. C'est le format attendu par Whisper et il reste valide pour les futurs usages (les SLM audio comme Voxtral utilisent aussi cette résolution). Quatre buffers circulaires de 50 ms en polling pur (pas de queue managée, pas d'event-driven). La taille de la fenêtre RMS pour le mapping HUD est paramétrable côté `CaptureSettings.LevelWindow` (RMS over a sliding window of N samples) mais la cadence de polling reste 50 ms.

Le RMS de chaque sous-fenêtre est émis en event temps réel pour alimenter l'animation HUD via `AudioLevelMapper`. La courbe `dBFS → [0, 1]` est définie par trois statiques (`MinDbfs`, `MaxDbfs`, `DbfsCurveExponent`) — l'app les pousse à chaque changement de setting via `App.ApplyLevelWindow(...)`. Ces statiques sont mutables à dessein pour la calibration runtime depuis le Playground.

À la fin du `Record()`, `MicrophoneTelemetryCalculator` calcule un récap distributif (p10, p25, p50, p75, p90, peak) sur toute la session plus un tail RMS sur les 600 derniers ms (utilisé pour détecter un mic débranché ou un audio très bas). `MicrophoneCalibrationCalculator` ajuste les bornes dBFS sur les N dernières sessions (médiane des p10 → MinDbfs, médiane des p90 + 2 dB → MaxDbfs) pour que la courbe perceptuelle reste adaptée à l'environnement réel de l'utilisateur.

## Observabilité

Le module a migré sur `EventSource` à la vague 2 de la refonte observabilité ([ADR-0005](../../docs/adr/0005-adoption-eventsource-pour-l-observabilite.md)). Toutes les émissions passent par `DeckleAudioSource.Log` — provider `Deckle.Audio` exposé en singleton statique. Aucun appel à `TelemetryService.Instance` ou `LogService.Instance` ne subsiste dans le module.

Trois zones d'émission. Les jalons et anomalies de la boucle waveIn (`RecordingStarted`, `CaptureStarted`, `EmptyBufferReceived`, `LowAudioDetected`, `CaptureLagDetected`, `DurationCapReached`, `RecordingCompleted`, `CaptureCompleted`). Les anomalies d'ouverture device et de télémétrie vide (`MicrophoneOpenFailed`, `MicrophoneTelemetryEmpty`). Le récap structuré par recording (`RecordingTailSummary` pour le headline lisible, `MicrophoneTelemetryRecorded` pour le payload distributif à 14 champs aplatis depuis l'ancien `MicrophoneTelemetryPayload`). La persistance settings du module passe par les quatre events `SettingsLoaded` / `SettingsLoadComplete` / `SettingsLoadWarning` / `SettingsLoadError` qui reçoivent le message brut transmis par `JsonSettingsStore<T>` — cette zone reste paramétrée par message tant que `SettingsHost` n'a pas migré (vague 4).

Le payload `MicrophoneTelemetryPayload` continue de vivre dans `Deckle.Logging` jusqu'à la vague 6, comme POCO carrier utilisé par `MicrophoneTelemetryCalculator`, `CaptureResult` et le ring d'auto-calibration de `WhispEngine`. La `ProjectReference` vers `Deckle.Logging` dans le csproj est documentée comme type-only carry-over et disparaît au moment où le legacy `Deckle.Logging` est supprimé.

Le gating du payload est toujours fait par l'orchestrateur via `IAudioRecordingHost.MicrophoneTelemetryEnabled` (toggle « Log microphone » de Settings → Telemetry). Quand le toggle est off, `MicrophoneTelemetryRecorded` n'est tout simplement pas émis ; le payload est néanmoins calculé pour alimenter l'auto-calibration.

## Persistance

`CaptureSettingsService` est un singleton lazy qui charge et persiste les settings sous `<UserDataRoot>/modules/audio/settings.json` via `JsonSettingsStore<CaptureSettings>`. Les anciens utilisateurs qui ont leur fichier sous `modules/capture/` sont migrés automatiquement au premier boot par `SettingsBootstrap.MigrateModuleFolder("capture", "audio")` (idempotent : no-op si la cible existe déjà). Le mutex nommé pour la synchro multi-process est `Deckle-Settings-Audio-Save`. Les delegates de log injectés dans `JsonSettingsStore` pointent désormais vers `DeckleAudioSource.Log.SettingsLoaded/SettingsLoadComplete/SettingsLoadWarning/SettingsLoadError` — la source label en LogWindow devient `AUDIO`, plus `SETTINGS`, et le préfixe `[audio]` qui apparaissait au début des messages legacy disparaît parce que le tag fait déjà le travail.
