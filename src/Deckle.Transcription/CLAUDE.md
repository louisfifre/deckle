# CLAUDE.md — Deckle.Transcription

Module de transcription vocale via whisper.cpp. Couvre tout le pipeline du hotkey à l'écriture clipboard final : ouverture du runtime natif, chargement du modèle Whisper, capture audio (déléguée à `Deckle.Audio`), VAD, transcription monobloc avec callback par segment, filtrage de répétitions, optionnellement réécriture LLM (déléguée à `Deckle.Llm.Rewrite` pour le moteur de réécriture et à `Deckle.Llm` pour la disponibilité Ollama), écriture clipboard, paste optionnel. Le module possède aussi son UI Settings (`WhisperPage.xaml`) et son setup first-run (téléchargement des natives et des modèles).

Le contrat avec l'app hôte passe par `IWhispEngineHost` — interface bridge qui expose les settings utiles côté engine sans coupler `Deckle.Transcription` à `Deckle.Settings`. L'app implémente `AppWhispEngineHost` dans `src/Deckle.App/Engine/`. La transcription en elle-même est invoquée via `_engine.RequestToggle(...)` depuis le handler de hotkey.

## Pipeline transcription monobloc

Le pipeline tourne en un seul appel `whisper_full()` avec `new_segment_callback` qui pousse chaque segment au fil de l'eau dans `OnNewSegment`. Pas de chunking externe : whisper.cpp gère sa fenêtre interne (30 s + seek dynamique), le VAD coupe les silences en amont, et nous récupérons les segments dès qu'ils sont prêts plutôt que de les attendre tous. `Record()` accumule tout l'audio capturé dans un unique `List<byte>` et retourne un `float[]` au Stop ; `Transcribe(float[])` fait **un seul** appel `whisper_full()` et Whisper gère la propagation de contexte inter-fenêtres via tokens.

La récupération progressive passe par `new_segment_callback` (binding du champ `WhisperFullParams.new_segment_callback` via `Marshal.GetFunctionPointerForDelegate` ; délégué stocké en champ d'instance `_newSegmentCallback` pour échapper au GC pendant l'appel natif). Chaque segment est poussé sous lock dans `List<TranscribedSegment> _segments` (`Text` / `T0` / `T1` / `NoSpeechProb`) depuis le thread d'inférence whisper.cpp. Le texte final est assemblé à partir de cette liste — garantit qu'un segment loggé est exactement un segment du texte produit. Un seul thread worker `Record → Transcribe` ; plus de `BlockingCollection`, plus de `MatchHallucination`, plus de mémoire `initial_prompt` chunk-par-chunk.

### Instrumentation par segment

Logs `Verbose` enrichis avec `p̄`, `min`, `dur`, `gap` filtrés sur les seuls tokens texte (via `whisper_token_beg`). Patterns d'hallucinations identifiables visuellement : **boucle** quand `dur=3,0s gap=+0,0s` métronomique avec texte identique répété ; **hallucination de silence** quand gros `gap` + `p̄ < 0,5` sur le 1ᵉʳ segment ; **saut Whisper** quand gros `gap` isolé. `nsp` inutile sur dictation (toujours 0 %). `min` seul inutilisable comme discriminant — il chute aussi sur parole saine.

### Defaults whisper.cpp et piège `entropy_thold`

Les overrides historiques `entropy_thold=1,9` et `no_speech_thold=0,7` ont été supprimés (héritage chunking, plus d'actualité). Le fallback natif est désormais actif : `temperature=0,0 / temperature_inc=0,2 / logprob_thold=-1,0 / entropy_thold=2,4`. Le décodeur re-décode automatiquement les segments ratés à température croissante jusqu'à ≤ 1,0.

**`entropy_thold` est contre-intuitif** : le test interne est `entropy < seuil`, donc seuil HAUT = STRICT (déclenche fallback plus souvent), seuil BAS = PERMISSIF. L'ancien override 1,9 était donc **plus permissif** que le défaut 2,4 — à l'inverse de ce qu'on croyait initialement. Documenté en commentaire dans `Transcribe()`. Toute proposition de retoucher les seuils doit relire ce paragraphe avant de toucher au code.

### Hot-reload via SettingsService

`Transcribe()` reconstruit ses `WhisperFullParams` à chaque appel via `whisper_full_default_params_by_ref` — snapshot `WhispSettingsService.Instance.Current` en début d'appel pour hot-reload gratuit, sans re-init modèle. L'approche snapshot immutable au début de `Transcribe` suffit comme garantie de thread safety.

### Tâches ouvertes connues

- **Paste fantôme intermittent** — bug en investigation : sur une fraction des transcriptions le `SendInput Ctrl+V` ne déclenche aucun paste visible alors que tous les checks UIA et HUD passent. Pas de pattern de reproduction stable identifié.
- **Filtrage par segment vs filtrage textuel** — plus de filtrage textuel par patterns ; on s'appuie sur `entropy_thold=2,4` (défaut) et les seuils natifs. À valider en usage réel. Si insuffisant, brancher un filtre par segment basé sur `no_speech_prob` (déjà accessible via `_segments`) plutôt que rejeter tout le texte.
- **Bugs résiduels** — hallucinations sur silences longs ou musique en fond (`no_speech_thold`, `suppress_blank`) ; ponctuation manquante si stop net (~300 ms de silence PCM en fin de buffer) ; screensaver casse l'enregistrement (`SetThreadExecutionState`).
- **VAD Silero** — intégration possible si `libwhisper.dll` a été compilée avec support VAD.

## Native runtime

Le module dépend de `libwhisper.dll` et des backends ggml (Vulkan en priorité, CPU en fallback). Les DLLs ne sont pas embarquées dans le repo — elles sont téléchargées au first-run depuis la release GitHub `native-vX.Y.Z` du repo Deckle ou recompilées localement par le maintainer quand un upgrade upstream est nécessaire. La recette de recompilation, les chemins, et l'inventaire des fichiers attendus vivent dans [docs/reference/reference--native-runtime--1.0.md](../../docs/reference/reference--native-runtime--1.0.md). Le code de bootstrap est dans `Setup/NativeRuntime.cs`.

## Règles UX non négociables

### Clipboard — 2 états maximum par transcription

Le clipboard porte au plus deux contenus successifs sur la durée d'une transcription : la transcription brute Whisper, puis le texte réécrit par le LLM si un profil est actif. **Jamais d'accumulation token par token, jamais d'incréments mot par mot.** L'historique du presse-papier système doit rester propre — un utilisateur qui ouvre l'historique clipboard après une transcription voit `raw` et `rewrite`, pas `raw1`, `raw1+w2`, `raw1+w2+w3`. Conséquence pour un éventuel streaming LLM : si on stream, on remplace l'objet clipboard en place (ou on le supprime puis on ré-ajoute), pas d'append. La granularité acceptable est la phrase entière (sur détection de point) ou un intervalle régulier d'environ 5 s, jamais token par token. Cette règle prime sur le gain de latence perçue.

### Pré-chargement VAD au hotkey (idée notée, pas implémentée)

Le VAD de whisper.cpp prend environ 5 % du temps audio quel que soit le backend GPU ou CPU — confirmé sur plus de 700 runs de télémétrie. Une piste plausible serait de pré-charger le contexte VAD dès la réception du hotkey (avant même le `waveInStart`) et de libérer au stop. À tester avec mesure avant et après une fois l'instrumentation correcte en place. Pas implémenté à ce jour, noté ici pour ne pas l'oublier.

## Paste — doctrine UI Automation au Stop

Le paste automatique est désactivé par défaut côté settings — la valeur par défaut est `false`, le HUD montre toujours `Copied to clipboard` en fallback quand l'utilisateur n'a pas explicitement opté pour le paste. Quand le paste est activé, la politique est **clipboard sûr par défaut, paste seulement si UIA confirme un champ texte**. Plus rien n'est capté au Start : pas de cible HWND, pas de focus volatile, pas de filet anti-drift. On fait confiance à l'état du système au moment du Stop — l'utilisateur a eu tout le temps de l'enregistrement + de la transcription + de la réécriture LLM pour placer son curseur où il veut.

`PasteFromClipboard` (dans `WhispEngine.cs`) applique quatre checks ordonnés. Tous refusent en clipboard-seul si faux. (1) `GetForegroundWindow()` ≠ 0. (2) Le foreground n'appartient pas au process Deckle (filet contre le faux positif « collé dans nos propres logs »). (3) `UIAutomation.IsFocusedElementTextEditable(out diag)` renvoie `true` — la probe lit `CUIAutomation.GetFocusedElement()` puis `IUIAutomationElement.GetCurrentPropertyValue(UIA_ControlTypePropertyId)` et ne valide que `Edit` (50004) ou `Document` (50030). Toute autre issue (UIA refuse, exception COM, ControlType différent, process protégé) est traitée comme « pas sûr ». (4) `SendInput` complet (4 events : `VK_CONTROL↓ VK_V↓ VK_V↑ VK_CONTROL↑`). Si tout passe, HUD `ShowPasted()` (flash vert 500 ms). Sinon HUD `ShowCopied()` (3 s, `Copied to clipboard — Ctrl+V where you want it`).

UIA est l'API canonique d'accessibilité Windows et répond à la bonne question : *cet élément accepte-t-il de la saisie ?* Elle fonctionne à travers Win32 classique, WinForms, WPF, WinUI, Chromium (`input` HTML, `contenteditable`), Qt, Electron, UWP. Un match sur `class name` (`Edit`, `RichEdit50W`, `Chrome_RenderWidgetHostHWND`, etc.) rate les frameworks modernes et produit des faux positifs sur des contrôles non éditables qui réutilisent une classe Edit. Toute proposition de revenir à un match `class name` est à refuser — UIA est le bon niveau d'abstraction.

Juste avant `PasteFromClipboard`, `OnReadyToPaste` est invoqué synchronement et câblé à `HudWindow.HideSync()`. Le HUD est caché de façon **bloquante** (marshal `DispatcherQueue` + `ManualResetEventSlim`) avant que `SendInput` parte. Sans ce verrou, le `Hide` async pouvait redistribuer l'activation pendant que Ctrl+V était encore en vol dans la queue du thread cible. Rien dans Deckle ne touche à l'activation entre le `Hide` effectif et la délivrance des frappes. `PasteFromClipboard` tourne sur le worker thread de l'engine (MTA par défaut sous .NET). UIA client supporte MTA depuis Windows 7 — pas d'init COM explicite. L'instance `IUIAutomation` est lazy-instanciée et réutilisée (cache global thread-safe dans `Deckle.Core/Interop/UIAutomation.cs`).

Doctrine retirée 2026-04-15 — à ne pas réintroduire. Jusqu'à cette date la logique captait au Start le HWND foreground et le HWND focus précis (`GUITHREADINFO.hwndFocus`), stockait ces deux volatiles (`_pasteTarget`, `_pasteFocusHwnd`), puis au Stop forçait `SetForegroundWindow(_pasteTarget)` + `Sleep(50)` + vérif foreground, et comparait sub-window focus. Même avec ces filets, le paste pouvait atterrir dans une fenêtre voisine, et la restauration `SetForegroundWindow` était intrusive — elle ramenait au premier plan une fenêtre que l'utilisateur avait peut-être volontairement laissée en arrière-plan. Tout ce dispositif a été démantelé : paramètres `pasteTarget`/`pasteFocusHwnd` de `StartRecording`, `SetForegroundWindow` + sleep + vérif, check sub-window, helpers `Win32Util.GetFocusedClass`/`GetFocusedHwnd`, et le hook `AltMenuSuppressor` qui tentait de neutraliser le menu Alt avant le SendInput.

## Structure interne

Le module suit le pattern canonique des modules Deckle.

`WhispSettings.cs` est le POCO de configuration (sept sections imbriquées : transcription, speech detection, prompt, paste, autorewrite rules, models directory, level window). `WhispSettingsService.cs` est le singleton lazy qui charge et persiste les settings sous `<UserDataRoot>/modules/whisp/settings.json` via `JsonSettingsStore<T>`. `IWhispEngineHost.cs` est l'interface bridge exposée aux consommateurs (l'app implémente `AppWhispEngineHost`). `WhisperPage.xaml(.cs)` et `ViewModels/WhisperViewModel.cs` portent l'UI Settings du module (rendu via `Type.GetType` depuis le NavView de `SettingsWindow`). Le dossier `Engine/` héberge le moteur principal (`WhispEngine.cs`) et ses helpers (`RepetitionDetector.cs`, `WhisperParamsMapper.cs`). Le dossier `Pinvoke/` contient les wrappers `[LibraryImport]` autour de whisper.cpp natif (`WhisperPInvoke.cs`, `WhisperStructs.cs`). Le dossier `Setup/` héberge le first-run provisioning (`NativeRuntime.cs`, `SpeechModels.cs`, `Downloader.cs`, `SetupContext.cs`). Le dossier `Strings/en-US/` porte les ressources `.resw` pour les `x:Uid` de `WhisperPage`.
