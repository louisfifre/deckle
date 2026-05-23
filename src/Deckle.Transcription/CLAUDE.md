# CLAUDE.md — Deckle.Transcription

Orchestrateur de transcription vocale. Couvre tout le pipeline du hotkey à l'écriture clipboard final : capture audio (déléguée à `Deckle.Audio`), invocation d'un backend ASR via l'interface `IAsrBackend`, filtrage des résultats, optionnellement réécriture LLM (déléguée à `Deckle.Llm.Rewrite` pour le moteur de réécriture et à `Deckle.Llm` pour la disponibilité Ollama), écriture clipboard, paste optionnel. Le module possède aussi son UI Settings (`WhisperPage.xaml`).

Le module est **backend-agnostique**. L'implémentation ASR vit dans un module enfant (`Deckle.Transcription.Whisper` aujourd'hui ; `Deckle.Transcription.Voxtral` planifié). Le pattern suit celui établi par `Deckle.Diagnostics` → `Deckle.Diagnostics.Logging` + `Deckle.Diagnostics.Telemetry` : le parent porte les contrats et l'orchestration, les enfants portent les implémentations spécifiques. La décision est tracée dans [ADR 0010](../../docs/adr/0010-backend-asr-pluggable-via-iasrbackend.md).

Le contrat avec l'app hôte passe par `ITranscriptionEngineHost` — interface bridge qui expose les settings utiles côté engine sans coupler `Deckle.Transcription` à `Deckle.Settings`. L'app implémente `AppTranscriptionEngineHost` dans `src/Deckle.App/Engine/`, et compose le moteur avec un `IAsrBackend` concret (`WhisperBackend` aujourd'hui). La transcription est invoquée via `_engine.RequestToggle(...)` depuis le handler de hotkey.

## Contrat IAsrBackend

`IAsrBackend` est l'interface que tout backend ASR implémente. Quatre méthodes : `LoadModelAsync`, `UnloadModel`, `TranscribeAsync`, `Dispose`. Trois propriétés : `Name` (identifiant stable pour la télémétrie), `IsModelLoaded`, `DetectedAccelerator` (vocabulaire backend-défini : `CPU`, `Vulkan`, `CUDA`, `Metal`, etc.).

`TranscribeAsync(pcmSamples, segmentSink, ct)` prend un buffer PCM mono 16 kHz, un callback synchrone pour streamer les segments au fil de l'inférence (le HUD/LogWindow s'y abonnent via `TranscriptionEngine.NewSegment`), un token de cancellation. Retourne un `TranscriptionResult` qui agrège les segments produits, le texte assemblé, et les timings phase-par-phase (init pré-VAD, VAD, total). Le suffixe `Backend` n'est pas dans le vocabulaire fermé Deckle mais c'est l'idiome établi dans le monde ML/AI (llama.cpp, whisper.cpp, transformers parlent tous de "backends") — extension du vocabulaire actée par ADR 0010.

L'orchestrateur ne touche jamais P/Invoke, native callbacks, ou structs C — toute cette mécanique vit dans le backend. Conséquence : ajouter un second backend (Voxtral via Python+Transformers, par exemple) est un nouveau module enfant qui implémente `IAsrBackend` ; l'orchestrateur reste inchangé, l'app injecte le bon backend selon un setting `Engine = Whisper | Voxtral`.

## Pipeline transcription monobloc

Le pipeline tourne en un seul appel `_backend.TranscribeAsync(...)` qui retourne dès que le backend a fini. Pour Whisper aujourd'hui c'est un wrapper synchrone autour de `whisper_full()` ; pour un backend HTTP (Voxtral), ce serait un vrai `await`. Pas de chunking externe : le backend gère sa fenêtre interne (30 s + seek dynamique chez Whisper, équivalent côté Voxtral), le VAD coupe les silences en amont, et les segments arrivent au fil de l'eau via le `segmentSink`.

`Record()` accumule tout l'audio capturé dans un unique `List<byte>` et retourne un `float[]` au Stop ; `Transcribe(float[])` fait un seul appel `_backend.TranscribeAsync(...)` et le backend gère la propagation de contexte inter-fenêtres en interne. Le texte final assemblé arrive dans `TranscriptionResult.FullText`.

### Initial prompt Whisper

Whisper n'est pas instruction-tuned. L'`initial_prompt` (champ `TranscriptionSettings.Engine.InitialPrompt`, lu par le backend Whisper) est un **échantillon stylistique à imiter**, pas une consigne. Les phrases méta (« voici une transcription », « avec ponctuation soignée ») sont au mieux neutres, au pire polluantes et favorisent le leak du prompt dans la sortie (cf. [openai/whisper#1150](https://github.com/openai/whisper/discussions/1150)). Cible de prompt : prose continue 80-150 mots, registre neutre, vocabulaire personnel ancré, ponctuation française correcte, zéro artefact oral, un seul bloc sans structure. Le prompt doit être dérivé d'un corpus réel, pas deviné.

Avant toute retouche du prompt, vérifier les paramètres connexes : `language` forcé à `fr` côté `TranscriptionSettings.Engine.Language`, `condition_on_previous_text` au défaut, `suppress_tokens` (peut supprimer les caractères typo français `« » — '` si mal réglé), `prepend_punctuations` / `append_punctuations`, et la limite de 224 tokens du prompt. Ne **jamais** mettre d'exemple `oral brut → propre` dans le prompt : Whisper produit un seul texte, le prompt montre à quoi ressemble une sortie propre. La correction d'oralité brute est du ressort du LLM aval, pas de Whisper.

### Defaults whisper.cpp et piège `entropy_thold`

Le fallback natif whisper.cpp est désormais actif : `temperature=0,0 / temperature_inc=0,2 / logprob_thold=-1,0 / entropy_thold=2,4`. Le décodeur re-décode automatiquement les segments ratés à température croissante jusqu'à ≤ 1,0.

**`entropy_thold` est contre-intuitif** : le test interne est `entropy < seuil`, donc seuil HAUT = STRICT (déclenche fallback plus souvent), seuil BAS = PERMISSIF. Documenté en commentaire dans le mapper côté backend Whisper. Toute proposition de retoucher les seuils doit relire ce paragraphe avant de toucher au code.

### Hot-reload via SettingsService

Le backend reconstruit ses params à chaque appel de `TranscribeAsync` — snapshot `TranscriptionSettingsService.Instance.Current` lu en début d'appel pour hot-reload gratuit, sans re-init modèle.

## Règles UX non négociables

### Clipboard — 2 états maximum par transcription

Le clipboard porte au plus deux contenus successifs sur la durée d'une transcription : la transcription brute, puis le texte réécrit par le LLM si un profil est actif. **Jamais d'accumulation token par token, jamais d'incréments mot par mot.** L'historique du presse-papier système doit rester propre. Conséquence pour un éventuel streaming LLM : on remplace l'objet clipboard en place, pas d'append. La granularité acceptable est la phrase entière (sur détection de point) ou un intervalle régulier d'environ 5 s, jamais token par token.

## Paste — doctrine UI Automation au Stop

Le paste automatique est désactivé par défaut côté settings — le HUD montre toujours `Copied to clipboard` en fallback quand l'utilisateur n'a pas explicitement opté pour le paste. Quand le paste est activé, la politique est **clipboard sûr par défaut, paste seulement si UIA confirme un champ texte**. Plus rien n'est capté au Start : pas de cible HWND, pas de focus volatile. On fait confiance à l'état du système au moment du Stop — l'utilisateur a eu tout le temps de l'enregistrement + transcription + réécriture pour placer son curseur.

`PasteFromClipboard` applique quatre checks ordonnés. Tous refusent en clipboard-seul si faux. (1) `GetForegroundWindow()` ≠ 0. (2) Le foreground n'appartient pas au process Deckle. (3) `UIAutomation.IsFocusedElementTextEditable(out diag)` renvoie `true` — la probe lit `CUIAutomation.GetFocusedElement()` puis `IUIAutomationElement.GetCurrentPropertyValue(UIA_ControlTypePropertyId)` et ne valide que `Edit` (50004) ou `Document` (50030). (4) `SendInput` complet (4 events : `VK_CONTROL↓ VK_V↓ VK_V↑ VK_CONTROL↑`).

UIA est l'API canonique d'accessibilité Windows et répond à la bonne question : *cet élément accepte-t-il de la saisie ?* Elle fonctionne à travers Win32 classique, WinForms, WPF, WinUI, Chromium (`input` HTML, `contenteditable`), Qt, Electron, UWP. Un match sur `class name` rate les frameworks modernes — toute proposition de revenir à un match `class name` est à refuser.

Juste avant `PasteFromClipboard`, `OnReadyToPaste` est invoqué synchronement et câblé à `HudWindow.HideSync()`. Le HUD est caché de façon bloquante (marshal `DispatcherQueue` + `ManualResetEventSlim`) avant que `SendInput` parte.

## Persistance settings

`TranscriptionSettingsService` charge et persiste sous `<UserDataRoot>/modules/transcription/settings.json` via `JsonSettingsStore<T>`.

## Structure interne

`TranscriptionSettings.cs` est le POCO racine du module (sept sections imbriquées : engine, speech detection, confidence, output filters, decoding, context, models directory). La classe interne `EngineSettings` porte les paramètres de bootstrap du backend (model, useGpu, language, initialPrompt) — son nom évite la collision avec le nom du module et reflète le rôle (config du moteur ASR actif).

`TranscriptionSettingsService.cs` est le singleton lazy qui charge et persiste les settings + opère la migration disque. `ITranscriptionEngineHost.cs` est l'interface bridge exposée aux consommateurs (l'app implémente `AppTranscriptionEngineHost`). `WhisperPage.xaml(.cs)` et `ViewModels/WhisperViewModel.cs` portent l'UI Settings du module aujourd'hui — la page est encore whisper-centrée (model picker, VAD settings, beam search) ; une page agnostique générique avec un sélecteur de backend viendra quand un second backend sera prêt.

Le dossier `Engine/` héberge l'orchestrateur (`TranscriptionEngine.cs`) et ses helpers backend-agnostiques (`TextMetrics.cs`). Le contrat `IAsrBackend.cs` et le DTO `TranscriptionResult.cs` y vivent aussi — surface publique consommée par les modules enfants. Le dossier `Setup/` ne porte plus que les éléments génériques (`Downloader.cs`, `ModelEntry.cs`) ; les catalogues whisper-spécifiques (`SpeechModels`, `NativeRuntime`) ont migré vers `Deckle.Transcription.Whisper.Setup`. Le dossier `Strings/en-US/` porte les ressources `.resw` pour les `x:Uid` de `WhisperPage`. Le provider EventSource `DeckleWhispSource` reste dans le parent — son nom ETW `Deckle.Whisp` est conservé pour ne pas casser les listeners JSONL existants.
