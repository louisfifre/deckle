# CLAUDE.md — Deckle.Transcription.Whisper

Backend ASR whisper.cpp pour le module `Deckle.Transcription`. Implémente `IAsrBackend` derrière la classe `WhisperBackend`, encapsule toute la machinerie P/Invoke vers `libwhisper.dll`, et expose les catalogues de provisioning (`SpeechModels`, `NativeRuntime`) consommés par le first-run wizard.

Vit comme module enfant de `Deckle.Transcription` selon le pattern parent/enfants déjà établi par `Deckle.Diagnostics` → `Deckle.Diagnostics.Logging` + `Deckle.Diagnostics.Telemetry`. Le parent porte le contrat `IAsrBackend`, le DTO `TranscriptionResult`, et le provider EventSource `DeckleWhispSource` ; le child porte l'implémentation Whisper. Aucune référence inverse — le parent ne voit jamais ce module ; c'est `Deckle.App` (composition root) qui instancie `WhisperBackend` et l'injecte dans `TranscriptionEngine`. Décision tracée dans [ADR 0010](../../docs/adr/0010-backend-asr-pluggable-via-iasrbackend.md).

## Surface publique

`WhisperBackend(ITranscriptionEngineHost host)` est le seul constructeur. Le backend lit ses settings via `host.Transcription.Engine` (model, useGpu, language, initialPrompt) et résout le chemin du modèle via `host.ResolveModelsDirectory()`. Quatre méthodes implémentent `IAsrBackend` : `LoadModelAsync` (synchrone en pratique pour Whisper — `whisper_init` est bloquant), `UnloadModel`, `TranscribeAsync`, `Dispose`. Trois propriétés : `Name = "whisper"`, `IsModelLoaded`, `DetectedAccelerator` (`"CPU" | "Vulkan" | "CUDA" | "Metal"`).

Le namespace `Deckle.Transcription.Whisper.Setup` expose deux types consommés par `Deckle.Setup` (le wizard first-run) : `NativeRuntime` (provisioning de `libwhisper.dll` + backends ggml + runtime MinGW) et `SpeechModels` (catalogue des `.bin` Whisper + Silero VAD téléchargeables). Le namespace `Deckle.Transcription.Whisper.Pinvoke` est privé d'usage externe — il ne sort jamais du backend.

## Pipeline d'inférence

`TranscribeAsync(pcmSamples, segmentSink, ct)` enchaîne : reset des accumulateurs (segments locaux, VAD parsing state), mapping des `TranscriptionSettings` → `WhisperFullParams` natifs via `WhisperParamsMapper`, branchement des deux callbacks (`new_segment_callback`, `abort_callback`), appel à `whisper_full` sous lock `_transcribeLock`, force-arrêt des stopwatches VAD/init si bail anticipé, free des allocations natives, assemblage du `TranscriptionResult` (segments, full text, timings).

Le callback `OnNewSegment` produit un `TranscriptionSegment` (Text, T0Cs, T1Cs, Confidence, NoSpeechProb), le pousse dans `_segmentsLocal`, le passe à la `RepetitionDetector` (qui peut lever `_abortRequested`), invoque `segmentSink?.Invoke(segment)` pour le streaming vers l'orchestrateur, et émet le log Verbose détaillé (`p̄`, `min`, `dur`, `gap`).

## Compaction des logs natifs au model load

Le hook `whisper_log_set` (installé une fois à la construction du backend, jamais désinstallé — c'est un callback process-global) intercepte chaque ligne émise par whisper.cpp. Il route trois flux :

1. **Détection backend** — le premier prefix `ggml_vulkan:` / `ggml_cuda:` / `ggml_metal:` rencontré sticke dans `_detectedBackend`. Pas de match = `CPU`.
2. **Parsing VAD** — les lignes `whisper_vad*` émises pendant `_vadCapturing` sont silencées et leurs valeurs (durée parole, segments détectés, % réduction, inference ms, points de mapping) accumulées. À la sentinelle `"Reduced audio from"` (marqueur de fin du module VAD) ou au bail no-speech, un unique event `VadParsed` consolidé est émis.
3. **Compaction des phases d'init** — quatre prefixes accumulent leurs lignes respectives jusqu'à ce qu'un prefix différent (ou non-trackable) arrive, qui flush la phase courante en un unique event. `whisper_init_with_params_no_state:` → `WhisperInitParamsParsed` (IDs 101). `whisper_model_load:` → `WhisperModelLoadParsed` (102). `whisper_backend_init_gpu:` → `WhisperBackendInitParsed` (103). `whisper_init_state:` → `WhisperInitStateParsed` (104). Une ligne non-phase (le standard `whisper_init_from_file_with_params_no_state:` notamment) flush d'abord la phase pending avant de passer au niveau switch normal.

Les lignes orphelines passent par un switch sur `ggml_log_level` : ERROR (4) → `WhisperLogError`, WARN (3) → `WhisperLogWarning`, le reste → `WhisperLogVerbose`. Cas spécial : `whisper_backend_init_gpu: no GPU found` (émis par la création du contexte VAD secondaire qui hardcode `use_gpu=false`) est dégradé en Verbose plutôt que Warn — bénin mais alarmant sinon.

## Repetition guard

`RepetitionDetector` est un classifieur binaire dédié au cas observé : N segments consécutifs identiques (case- et whitespace-insensitive) sur audio long avec silence trailing ambigu — le décodeur greedy entre dans une boucle où `logprob_thold` et `entropy_thold` ne mordent pas (`p̂ ≈ 0,99`). Le détecteur lève `_abortRequested`, l'`abort_callback` retourne `true` au prochain probe interne de whisper, `whisper_full` retourne `0` avec les segments produits avant le bail.

Le détecteur est whisper-spécifique (failure mode tuné pour whisper.cpp). Vit donc dans ce module et pas dans le parent — un futur backend Voxtral aura ses propres caractéristiques et son propre détecteur si nécessaire.

## Native runtime

Le module dépend de `libwhisper.dll` et des backends ggml (Vulkan en priorité, CPU en fallback). Les DLLs ne sont pas embarquées dans le repo — elles sont téléchargées au first-run depuis la release GitHub `native-vX.Y.Z` du repo Deckle ou recompilées localement par le maintainer quand un upgrade upstream est nécessaire. La recette de recompilation, les chemins et l'inventaire vivent dans [docs/reference/reference--native-runtime--1.0.md](../../docs/reference/reference--native-runtime--1.0.md). Le code de bootstrap est dans `Setup/NativeRuntime.cs`.

`WhisperPInvoke.cs` installe un `NativeLibrary.SetDllImportResolver` qui charge `libwhisper.dll` depuis `<UserDataRoot>\native\` plutôt que depuis le répertoire de l'exe. La constante `EntryDll` doit rester synchronisée avec la chaîne littérale dans chaque `[DllImport("libwhisper")]` — C# exige une constante littérale dans l'attribut, donc la duplication est inévitable.

## Pièges connus

**Defaults whisper.cpp et piège `entropy_thold`** — le test interne est `entropy < seuil`, donc seuil HAUT = STRICT (déclenche fallback plus souvent), seuil BAS = PERMISSIF. Documenté dans `WhisperParamsMapper`. Toute proposition de retoucher les seuils doit relire le commentaire avant d'agir.

**`ggml_log_level` mapping** — l'énumération `ggml_log_level` côté whisper.cpp / ggml suit l'ordre `NONE=0, DEBUG=1, INFO=2, WARN=3, ERROR=4, CONT=5`. Piège récurrent : l'intuition `1=Info, 2=Warn, 3=Error` est fausse — toutes les lignes `whisper_vad_*` et `whisper_full: *` sont émises en INFO (2). Le hook route ERROR (4) → erreur, WARN (3) → warning, INFO/DEBUG (1-2) → verbose.

**GC des delegates natifs** — chaque delegate passé à whisper.cpp via `Marshal.GetFunctionPointerForDelegate` doit être maintenu rooted dans un champ d'instance pendant toute la durée où le natif détient le pointeur. Sans cela, le GC peut collecter le thunk entre deux invocations et un crash natif suit. `_logCallback`, `_segmentCallback`, `_abortCallback` sont stockés en champs pour cette raison.
