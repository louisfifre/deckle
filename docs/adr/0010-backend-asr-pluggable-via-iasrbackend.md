# ADR-0010 — Backend ASR pluggable via IAsrBackend

**Status** — accepted le 2026-05-23

## Contexte

La transcription vocale dans Deckle a vécu sa première année exclusivement sur whisper.cpp. Le moteur `WhispEngine` (2 158 lignes) portait simultanément trois responsabilités enchevêtrées : l'orchestration métier (state machine du hotkey, coordination de la capture audio, déclenchement de la réécriture LLM, paste UIA), la mécanique de la pipeline (callbacks natifs, parsing des logs whisper.cpp, détection de répétitions), et l'invocation P/Invoke vers `libwhisper.dll` (params, structs, gestion du contexte natif).

L'arrivée probable de [Voxtral](./0007-rester-sur-whisper-cpp-surveiller-voxtral.md) comme moteur ASR alternatif (meilleure qualité française, modèles Mistral) force à clarifier la frontière. Une comparaison Whisper ↔ Voxtral en usage réel demande de pouvoir basculer entre les deux backends sans dupliquer la pipeline complète. Et le fichier `WhispEngine.cs` dépasse largement le seuil de vigilance modularité (~500 lignes) — la doctrine `deckle-modularite` impose d'examiner la responsabilité quand un fichier devient inconfortable.

## Options considérées

- **A. Garder un seul module et un seul moteur, brancher Voxtral par drapeau dans `WhispEngine`.** Conservation maximale du code existant. Mais : le fichier déjà monolithique double de taille, le P/Invoke whisper.cpp et le client HTTP/Python Voxtral cohabitent dans le même fichier, et chaque ajout futur (Mistral autre, ONNX Runtime, etc.) demande une nouvelle branche. Aucune frontière exploitable.

- **B. Deux moteurs parallèles `WhispEngine` et `VoxtralEngine`, l'app choisit lequel instancier.** Sépare les implémentations. Mais : tout le code d'orchestration (state machine, capture, LLM rewrite, paste, télémétrie) est dupliqué. Toute évolution du pipeline doit être propagée aux deux moteurs — coût de maintenance qui croît linéairement avec le nombre de backends.

- **C. Orchestrateur unique dans `Deckle.Transcription`, contrat `IAsrBackend`, implémentations dans des modules enfants.** Le parent porte l'orchestration backend-agnostique (state machine, capture, rewrite, paste, télémétrie). Une interface étroite (`LoadModelAsync`, `UnloadModel`, `TranscribeAsync`, `Dispose` + trois propriétés) capture ce qu'un backend doit fournir. Chaque backend vit dans son propre module enfant (`Deckle.Transcription.Whisper`, plus tard `Deckle.Transcription.Voxtral`). L'app hôte injecte le backend choisi dans l'orchestrateur. Conforme au pattern parent/enfants déjà établi par [ADR-0006](./0006-structure-diagnostics-parent-logging-telemetry-enfants.md) pour Diagnostics.

## Décision

Option C retenue. La transcription s'organise en parent + un module enfant par backend. Le parent `Deckle.Transcription` porte l'orchestrateur `TranscriptionEngine` (anciennement `WhispEngine`), le contrat `IAsrBackend`, les DTOs (`TranscriptionResult`, `TranscriptionSegment`, `ModelLoadResult`), le POCO `TranscriptionSettings` (anciennement `WhispSettings`), l'UI Settings, le bridge `ITranscriptionEngineHost`, et le provider EventSource `DeckleWhispSource`. Le child `Deckle.Transcription.Whisper` porte `WhisperBackend` qui implémente `IAsrBackend`, plus toute la machinerie native (P/Invoke, structs, callbacks, parsing logs whisper.cpp, catalogues `SpeechModels` + `NativeRuntime`).

Le suffixe `Backend` n'est pas dans le vocabulaire fermé `deckle-nomenclature`. L'extension est actée par cet ADR : la responsabilité d'« implémentation interchangeable de l'inférence ASR (chargement modèle, inférence, libération) » est nommable en une phrase, et le mot « backend » est l'idiome établi dans le monde ML/AI (llama.cpp, whisper.cpp, transformers parlent tous de "backends"). Alternative considérée : `IAsrService` ou `ISpeechRecognizer` — les deux ont été écartés. `Service` est trop générique (orchestre, alors qu'ici le backend répond à des requêtes du backend). `Recognizer` est correct en isolation mais ne capture pas la dimension « pluggable, swappable » qui justifie tout le chantier.

## Conséquences

Devient plus facile : ajouter un second backend (Voxtral, ONNX Runtime, futur) est un nouveau module enfant qui implémente `IAsrBackend` ; l'orchestrateur, l'UI, les settings, le bridge App ne bougent pas. Le découpage modulaire force la séparation des préoccupations — l'orchestrateur ne touche jamais P/Invoke, le backend ne connaît rien du paste UIA ou de la réécriture LLM. Le fichier `TranscriptionEngine.cs` passe sous la barre des ~1 800 lignes (vs 2 158 avant) ; les ~500 lignes whisper-spécifiques (P/Invoke, params mapper, callbacks, log parsing) vivent maintenant dans le child. Les renommages cascade vers une nomenclature cohérente : `WhispEngine` → `TranscriptionEngine`, `WhispSettings` → `TranscriptionSettings`, `IWhispEngineHost` → `ITranscriptionEngineHost`, le sous-POCO `TranscriptionSettings` (collision) → `EngineSettings`.

Devient plus difficile : deux modules à maintenir au lieu d'un, plus la cérémonie d'interface (`IAsrBackend` doit être stable). Surcoût administratif réel — `csproj` + `CLAUDE.md` supplémentaires, `ProjectReference` depuis `Deckle.App` et `Deckle.Setup` vers le child. Compensé par la lisibilité du graphe et par la capacité future à pivoter de backend sans toucher au reste.

Devient impossible : un consommateur de l'orchestrateur ne peut plus appeler P/Invoke whisper.cpp en direct — il doit passer par le backend. Le bridge `ITranscriptionEngineHost` empêche tout couplage circulaire app ↔ engine. Le child ne peut pas être référencé par le parent ; toute évolution qui demanderait que le parent connaisse un type spécifique au backend signale un sale design à revoir.

Le mapping concret. **`Deckle.Transcription`** porte `TranscriptionEngine`, `IAsrBackend`, `TranscriptionResult`, `TranscriptionSegment`, `ModelLoadResult`, `TranscriptionSettings`, `EngineSettings`, `TranscriptionSettingsService` (avec migration disque `modules/whisp/` → `modules/transcription/`), `ITranscriptionEngineHost`, l'UI `WhisperPage`, le provider EventSource `DeckleWhispSource` (nom ETW `Deckle.Whisp` conservé pour ne pas casser les listeners JSONL), et les helpers backend-agnostiques (`TextMetrics`, `Downloader`, `ModelEntry`). **`Deckle.Transcription.Whisper`** porte `WhisperBackend`, `WhisperPInvoke`, `WhisperStructs`, `WhisperParamsMapper`, `RepetitionDetector`, `SpeechModels` (catalogue Whisper + Silero VAD), `NativeRuntime` (provisioning `libwhisper.dll`). Dépend uniquement de `Deckle.Core` et `Deckle.Transcription`.

L'app hôte (`Deckle.App`) instancie `WhisperBackend` et l'injecte dans `TranscriptionEngine` au boot. Quand un second backend ship, la composition root devient un switch sur un setting `Engine = Whisper | Voxtral` qui choisit lequel instancier — l'orchestrateur reste inchangé.
