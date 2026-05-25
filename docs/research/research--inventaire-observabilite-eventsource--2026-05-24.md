# Inventaire et classes d'observables EventSource (2026-05-24)

Note de recherche datée. Cartographie l'état de l'instrumentation EventSource après la vague 6 d'observabilité (mergée 2026-05-22, cf. ADR-0005), identifie les classes d'observables récurrentes par domaine, et pointe les lacunes et incohérences avant que Louis tranche une éventuelle suite. Pas de directive, pas de modification du code — matière à décision.

## Cadrage

La doctrine `deckle-logging` répond aujourd'hui à deux questions : « quoi observer dans un bout de code qu'on instrumente » et « comment l'écrire ». Le compagnon `taxonomy.md` croise des **catégories de code** (boucle temps réel, pipeline batch, driver externe, surface UI, cycle de vie) avec trois **cadres canoniques** (Four Golden Signals, USE Method, RED Method). Ce qui manque entre les deux : un set fixe d'**observables canoniques par classe** — « pour windowing on observe toujours position, taille, DPI, écran », « pour audio on observe toujours format, latence, RMS », « pour transcription on observe toujours modèle, accélérateur, durée par phase, n_tokens ». C'est cette dimension-là que la doctrine actuelle ne porte pas explicitement, et c'est elle qui rendrait reproductible l'instrumentation d'un nouveau bout de code sans repartir d'une feuille blanche.

Le besoin a émergé pendant un debug récent du tray menu : pour comprendre pourquoi le popup ne s'alignait pas avec l'icône en haut DPI, Louis a instrumenté à la main `File.AppendAllText` avec position curseur, position icône tray, taille calculée, position popup, DPI courant, scale. Exactement le type d'observables qui mériteraient de vivre dans une classe **Windowing** canonique, applicable partout où une fenêtre est positionnée.

## État de l'instrumentation actuelle

Treize providers EventSource actifs, tous héritant de `DeckleEventSource`. La distribution révèle un déséquilibre net :

- **Riche, doctrinaire** — `Deckle.Whisp` (106 events, dont la canonical log line `LatencyRecorded` à 24 champs), `Deckle.Settings` (46 events), `Deckle.Lighting` (40 events), `Deckle.Lighting.Ambient` (30 events), `Deckle` (28 events côté App, dont les crash safety nets), `Deckle.Llm` (27 events), `Deckle.Vision` (24 events), `Deckle.Audio` (16 events dont `MicrophoneTelemetryRecorded` à 14 champs).
- **Maigre, paramétré par message** — `Deckle.Shell` (15 events, mais centré hotkey/autostart/dispatch), `Deckle.Playground` (11 events dont plusieurs génériques), `Deckle.Setup` (3 events `SetupInfo`/`SetupWarning`/`SetupError` paramétrés par chaîne libre), `Deckle.Hud` (1 seul event `HudWarning` paramétré par message), `Deckle.Chrono` (1 event pilot transitoire).

Le déséquilibre n'est pas uniformément un défaut — Setup ne tourne qu'au first-run et a peu d'opérations distinctes, Chrono est pur sans matière observable au-delà du tick. Mais HUD à un seul event pour une surface qui calcule alpha, fade-in 150 ms, retract 800 ms, proximity smoothstep, DPI awareness, repositionnement work-area, state machine à six états, c'est une cécité réelle dont je parle plus loin.

Côté infrastructure consommatrice : `LogWindowEventListener` consomme tous les `Deckle.*` et alimente le viewer ; `JsonlEventListener` persiste vers `app.jsonl` filtrable par prédicat ; `RoutedJsonlEventListener` route vers une arborescence dynamique (corpus brut bucketé par tier, corpus rewrite bucketé par profil) ; `HudFeedbackEventListener` filtre exclusivement sur l'event canonique `UserFeedbackEmitted` au contrat fixe `(severity, title, body, role)`. La centralisation est bien tenue — aucun `File.AppendAllText`, `Console.WriteLine` ou `LogService` résiduel actif dans `src/` en dehors de Diagnostics qui en a légitimement besoin.

## Classes d'observables récurrentes — proposition de cadrage

Je propose ci-dessous neuf classes que je crois suffisantes pour couvrir le code Deckle existant et futur. Chacune correspond à un **type de situation** qu'on instrumente, et porte un **set de paramètres canoniques** qu'on viserait par défaut quand on touche un bout de code de cette nature. Ce n'est pas exclusif — un site peut relever de deux classes (par exemple une opération de boot qui charge un modèle relève de Lifecycle ET de Pipeline batch).

### 1. Lifecycle et boot

Démarrage process, init paths, warmup ressources, chargement module, transitions d'app (idle → recording → transcribing → done), shutdown amorcé, restart post-build, crash safety nets. Cadre dominant Four Golden Signals dégradés (durée des étapes, erreurs au démarrage) ; les opérations sont uniques par cycle.

**Set canonique proposé** : nom de l'étape, durée `<name>_ms`, outcome (succès, ignoré, échec), backend ou variant actif quand pertinent (`backend=Vulkan`, `model=ggml-large-v3.bin`), version du composant si charge réseau ou disque, motif de transition pour les state changes (`reason=hotkey`, `reason=tray`, `reason=auto-shutdown`).

État actuel : très bien instrumenté côté `Deckle` (App), `Deckle.Whisp` (warmup boot), `Deckle.Audio` (capture lifecycle), `Deckle.Vision` (ScreenCaptureStarted/Stopped). Pattern `PathsInitialized` + `PathsDetail` (jalon Info + miroir Verbose) est l'archétype propre.

### 2. Pipeline batch

Transcription d'un blob audio, réécriture LLM, calibration appareil, push ambient sur un frame complet. Opération discrète début → fin → résultat. Cadres dominants RED et Four Golden Signals.

**Set canonique proposé** : identifiant d'opération (`transcription_id`), durée totale et par phase clé (`hotkey_to_capture_ms`, `record_drain_ms`, `whisper_init_ms`, `whisper_ms`, `llm_ms`, …), métriques d'entrée (`audio_sec`, `text_chars`, `prompt_tok`), métriques de sortie (`n_segments`, `text_words`, `tok_s`), outcome enum (`outcome=ok|repetition_loop|llm_failed|user_cancelled`), profil ou stratégie active (`strategy=`, `profile=`), flag binaire d'effet de bord (`pasted=true`).

État actuel : `LatencyRecorded` à 24 champs est l'exemple canonique réussi — *canonical log line* au sens industrie, colocalise toutes les mesures clés en une ligne par invocation. `CorpusAsrRecorded` (14 champs) et `CorpusRewriteRecorded` (12 champs) suivent le même pattern pour la persistance dataset (ADR-0011). Le pattern est mature côté transcription ; il n'est pas systématisé ailleurs (par exemple le push ambient pourrait avoir son canonical heartbeat richer que l'actuel `Heartbeat` à 7 champs).

### 3. Boucle temps réel haute fréquence

Capture audio polling 50 ms, capture écran DXGI à ~15 Hz, push lumière à 10-15 Hz, raw input curseur ~125 Hz pour fade proximité HUD. Opérations nombreuses, brèves, l'enjeu est la stabilité du débit. Cadres dominants USE et Four Golden Signals côté flux sortant.

**Set canonique proposé** : sur fenêtre glissante (1 s typique) — `fps` ou ticks/s observés, `drops` (frames acquis mais non traités), latence intra-tick `p50_ms` / `p95_ms`, saturation de file (`queue_depth` ou `pending_frames`), erreurs intra-fenêtre (`acquire_fail=N`). Pattern dit *rollup* — une ligne périodique qui résume N ticks, plutôt qu'une ligne par tick qui noierait l'observation.

État actuel : la `Heartbeat` de `Deckle.Lighting.Ambient` est l'incarnation actuelle de ce pattern (7 champs, périodique). `Deckle.Vision` n'a pas d'équivalent — la boucle de capture émet par incident (anomalies, recovery) mais pas une trace régulière du débit. `Deckle.Audio` émet le RMS tick sur un event UI direct (alimentation HUD), explicitement *non* loggué selon la doctrine « heartbeats haute fréquence < 1 s ne sont pas loggués », mais le récap distributif `MicrophoneTelemetryRecorded` à 14 champs en fin de session compense.

### 4. Driver matériel et intégration externe

Pilote micro (WASAPI), client HTTP Hue REST, client HTTP Ollama, EventStream SSE, P/Invoke whisper.cpp natif. Frontière entre code interne et système externe sur lequel on a peu de contrôle. Cadres dominants RED (durée aller-retour, taux d'erreur, taux d'appel) + USE sur ressources internes consommées.

**Set canonique proposé** : événements de cycle de vie de la connexion (discovery, pairing, ouverture session, fermeture propre, perte signal, reconnexion) ; codes de retour natifs avec notation canonique stable (`hr=0x{hex}` HRESULT, `result=<int>` mmsys, `status=<int>` HTTP, `mmsys=<int>` waveIn) ; identifiants tronqués ou masqués pour les secrets (`username=eDOvxk-...`, `clientkey=[redacted]`) ; latence aller-retour (`rtt_ms`) ; ressources consommées (`http_clients`, `socket_pool`).

État actuel : `Deckle.Lighting` (40 events) couvre bien tout le cycle Hue — discovery, pairing, control, EventStream, identify, color push. La discipline de masquage des secrets (clientkey jamais en clair, username tronqué) est tenue. `Deckle.Llm` instrumente les états Ollama (`OllamaBusy`, polling `/api/ps`). `Deckle.Audio` couvre les anomalies waveIn par codes `mmsys`. Une normalisation transverse manque — il n'y a pas de pattern uniforme « toute requête HTTP émet `<verb> <endpoint> | status=<n> | rtt_ms=<n>` », c'est implicite dans chaque event spécifique. Pour le futur (driver WLED, DMX, HomeAssist mentionnés dans le CLAUDE.md de Lighting), un sub-pattern réutilisable aiderait.

### 5. Surface UI et navigation

Page settings ouverte, dialog confirmé, formulaire validé, navigation NavView, ViewModel setter qui change une valeur, page chargée prête, page failed to init. Cadres dominants Four Golden Signals adaptés (latence perçue, taux d'actions par session, erreurs visibles) + RED sur opérations déclenchées utilisateur.

**Set canonique proposé** : transitions d'état UI en jalons concis (`Page loaded`, `Dialog opened`, `Form validated`) ; détails techniques en Verbose miroir (`page=Llm | duration_ms=120 | items=5`) ; UserFeedback adressé à l'utilisateur via le canal canonique séparé (`UserFeedbackEmitted` au contrat strict).

État actuel : `Deckle.Settings` est l'exemple riche — 46 events couvrent navigation NavView, ViewModel setters, backup/restore, folder picker, setup wizard. L'event générique paramétré `SettingChanged(string, string, string)` est l'entorse acceptée à la discipline strict-typed (un setter générique du MVVM ne sait pas distinguer 30 setters distincts).

### 6. Windowing (classe absente)

C'est la classe que Louis a évoquée et qui n'existe pas aujourd'hui. Concerne le **positionnement et le dimensionnement de toute fenêtre WinUI 3 ou Win32** — HudWindow (320×64 bas-centre), HudOverlayWindow, HudMessage hybrid bleed (400×160 puis retract 272×78), SettingsWindow, LogWindow, SetupWindow, popup tray menu, popup folder picker. Tous ces sites calculent à la main une position en DIP, multiplient par `GetDpiForWindow(hwnd) / 96.0`, choisissent un `DisplayArea` ou un `MonitorFromPoint`, gèrent le multi-écran.

**Set canonique proposé** : `hmon=0x{hex}` (handle moniteur retourné par `MonitorFromPoint` ou `GetMonitorInfo`), `dpi=192` entier (résultat `GetDpiForWindow`), `scale=2.0` 1 décimale (dérivé `dpi/96`), `work_area=2560,40,2520,1392` (rect en pixels), `cursor=1240,860` (pixels écran absolus, retour `GetCursorPos`), `anchor=BottomCenter` (ancrage choisi côté settings), `pos=1100,820 size=320,64` (rect calculé, DIP ou pixels — choisir et tenir une convention), pour les overlays empilés `slot=0` ou `slot=1`, pour les popups `parent_rect=x,y,w,h` du contrôle ancré.

État actuel : **non observé**. Le HUD a un seul `HudWarning(string)` paramétré par message libre. Le SettingsWindow, LogWindow, SetupWindow n'émettent rien sur leur positionnement. Le TrayIconManager ne loggue ni position icône ni position popup. Quand un bug arrive (« le HUD est mal placé en DPI 200% sur le second écran »), l'instrumentation est faite à la main avec `File.AppendAllText`, exactement le type de chemin parallèle que la doctrine de centralisation veut éviter. Une fois la doctrine d'observation à chaud écrite, l'événement temporaire devient inutile.

### 7. Activité utilisateur

Hotkey pressé, entrée tray cliquée, toggle settings changé, page settings ouverte manuellement. Cadres : RED sur opérations déclenchées.

**Set canonique proposé** : déclencheur (`trigger=hotkey:WinTilde | tray:Quit | settings:OllamaModel`), résultat (`outcome=triggered|ignored:busy|ignored:not-configured`), valeur avant et après pour un toggle (`before=true after=false`).

État actuel : `Deckle.Shell` couvre les hotkeys (`HotkeyRegistered`, `HotkeyToggleIgnored`). `Deckle` (App) couvre `HotkeyStart`, `HotkeyStop`, `HotkeyNoProfile`. `Deckle.Settings` couvre les setters via `SettingChanged` générique. Cohérent mais éclaté entre trois providers (Shell pour la primitive, App pour l'orchestration, Settings pour la modification de valeur) — c'est correct doctrinairement (« l'observation s'attache au module qui contient l'opération »), un peu lourd à recoller mentalement quand on lit la LogWindow.

### 8. Persistance settings per-module

Chaque module qui a des settings (`Audio`, `Transcription`, `Llm`, `Lighting.Ambient`, …) charge et persiste via `JsonSettingsStore<T>` sous `<UserDataRoot>/modules/<name>/settings.json`. Quatre events transitoires partagent le pattern : `SettingsLoaded` / `SettingsLoadComplete` / `SettingsLoadWarning` / `SettingsLoadError`, tous paramétrés par message string libre.

**Set canonique proposé une fois la refonte vague 4 faite** : `module=<name>`, `path=<abs>`, `outcome=loaded|defaulted|migrated|failed`, `size_bytes=<n>`, `version=<schema>`, durée `load_ms=<n>`, raison si échec (`reason=missing|corrupt|migration_failed`).

État actuel : entorse documentée dans `DeckleAudioSource` et `DeckleWhispSource` — le delegate `Action<string>` de `JsonSettingsStore` ne sait pas distinguer au site d'appel entre « Settings loaded », « Settings initialized (defaults) » et « Settings reloaded from disk ». La discipline strict-typed est temporairement échangée contre une typage par niveau et keyword. La doctrine prévoit que `SettingsHost` / `JsonSettingsStore` basculent eux-mêmes sur un contrat EventSource direct en vague 4. C'est de la dette identifiée et planifiée, pas une dérive.

### 9. Crash et safety nets

`Application.UnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`. Trois filets posés au constructeur de `App`. Capture exception type, message, stack trace, contexte (handler invoqué, thread).

**Set canonique proposé** : `source=app|appdomain|task-scheduler`, `ex_type=System.Foo.Bar`, `ex_message=<short>`, `stack=<multi-line ou indiqué via event séparé>`, `thread_id=<n>`, `terminating=true|false` (pour AppDomain).

État actuel : `Deckle` (App) porte les 4 events `CrashUnhandled`, `CrashAppDomain`, `CrashTaskScheduler`, `CrashStackTrace`. Pattern bien tenu — la stack trace est sur un event séparé pour ne pas exploser la signature primaire.

## Lacunes identifiées

**Windowing absent**, traité ci-dessus. C'est la lacune la plus saillante au regard de l'exemple Louis (debug tray DPI). Une classe d'observables à doctriner et à câbler progressivement sur les sites de positionnement existants (HudWindow, HudOverlayManager, TrayIconManager, SettingsWindow, LogWindow, SetupWindow, popups).

**HUD interne sous-instrumenté**. Le module porte une mécanique d'affichage très riche — state machine à six états, fade-in 150 ms cubic ease-out, retract 800 ms après ombre attenuated, proximity smoothstep entre `NEAR_RADIUS_DIP=10` et `FAR_RADIUS_DIP=128`, hybrid bleed 400×160 → 272×78, warm pass invisible au boot via layered alpha. Un seul event `HudWarning(string)` couvre tout ça. Quand « le HUD ne s'efface pas avant le paste » ou « le HUD flashe brièvement au boot », il n'y a aucune trace. Les events candidats Verbose miroir des transitions (`SetState | from=Recording to=Transcribing alpha=255 dpi=192`), du fade-in (`fade_in_start | duration_ms=150 from=0 to=255`), du retract (`message_retract | from=400x160 to=272x78`), du warm pass (`warm_pass_complete | took_ms=42`), de la proximity (`proximity_alpha | cursor_dist_dip=37 alpha=183`) sont attendus mais absents.

**Capture vidéo sans rollup périodique fps**. `Deckle.Vision` instrumente les jalons et les anomalies mais pas un heartbeat régulier équivalent au `Heartbeat` Ambient. Quand on diagnostique « la capture est lente » ou « les frames arrivent saccadés », pas de mesure continue. À ajouter dans la classe « Boucle temps réel haute fréquence ».

**Pas de pattern HTTP générique**. Les requêtes externes (Hue REST, Ollama, discovery cloud) sont observées via events spécifiques métier (`PushColor`, `BridgePaired`, `OllamaBusy`). Pas de squelette transverse `HttpRequestCompleted(verb, endpoint, status, rtt_ms, retry_count)`. Pas critique aujourd'hui ; deviendra utile dès qu'un troisième client HTTP émerge (driver WLED, services LLM tiers en remplacement d'Ollama).

**Provider unique par module ne couvre pas les sous-domaines**. `Deckle.Whisp` à 106 events est lisible parce que l'auteur a organisé les EventIds par zone (Warmup 1-16, Model 17-29, WhisperLog 30-33, Hotkey 35-36, etc.). Mais le **filtrage côté LogWindow** se fait par provider (SelectorBar par module), pas par sous-zone. Pour debug un sous-domaine spécifique (le seul Clipboard de Whisp, par exemple), l'utilisateur grep le texte. Acceptable, mais une dimension supplémentaire « sous-keyword par zone » serait possible si la lecture devient inconfortable.

## Incohérences et dette doctrinale

**Note de mise à jour (2026-05-25)** — la matière de ce rapport (9 classes d'observables + lacunes identifiées) a été transférée dans `docs/reference/reference--eventsource-convention--1.1.md`. Le présent fichier reste comme trace de l'investigation initiale ; la doctrine vit désormais dans la fiche 1.1. L'assertion initiale de cette section qui qualifiait la fiche `1.0` d'« annoncée mais inexistante » était fausse — la fiche existait sur disque mais était untracked git pendant la fenêtre de recherche. Cf. mémoire `feedback_doc_existence_check.md`.

**Pattern `SetupInfo`/`Warning`/`Error` paramétré par message** dans `Deckle.Setup`. Trois events génériques typés sur le niveau, payload string libre. Contredit la doctrine strict-typed per opération. Acceptable comme phase transitoire (le module Setup est jeune et son périmètre va évoluer) mais à reclasser en events distincts au prochain passage.

**Le `Message` template des events Settings et Audio loggue des préfixes module obsolètes**. Pattern legacy « `[audio] Settings loaded` » qui apparaît encore parce que `JsonSettingsStore` est appelé avec un `prefix` paramétré. La nouvelle architecture met le tag de source (`AUDIO`) en colonne LogWindow et le préfixe `[audio]` devient redondant. À nettoyer en même temps que la refonte `SettingsHost` (vague 4).

**Pas de discipline doctrinale sur les paramètres `pos`/`size`/`rect`**. Le code émet des positions et tailles en plusieurs endroits (chez Lighting `xy=`, chez Audio `bytes=`, chez Vision `size=WxH`), mais aucun event Windowing ne pose la convention pixels vs DIP. Sans norme, deux instrumentations futures divergeront.

## Décision à prendre

Trois questions ouvertes sur lesquelles la suite dépend.

**(1) Une fiche `reference--observables-canoniques--1.0.md` doit-elle naître ?** Elle figerait par classe d'observable le set canonique de paramètres (nom, unité, format), avec un exemple d'event de chaque famille, comme le fait déjà `src/Deckle.Diagnostics/CLAUDE.md` pour le vocabulaire de mesures mais étendu aux **classes** plutôt qu'aux **unités**. Avantage : doctrine reproductible pour toute nouvelle instrumentation. Inconvénient : une fiche stable de plus à maintenir, et le risque qu'elle dérive du code si la discipline d'update n'est pas tenue. Alternative légère : enrichir `taxonomy.md` du skill `deckle-logging` avec un set canonique par catégorie de code, plutôt que de créer une fiche stable séparée. Compromis : la doctrine reste dans le skill, la cartographie ponctuelle de l'inventaire reste dans ce research datée.

**(2) La classe Windowing mérite-t-elle une vague d'instrumentation à part entière ?** Si oui, périmètre proposé — HudWindow, HudOverlayManager, HudOverlayWindow, TrayIconManager, SettingsWindow, LogWindow, SetupWindow, popups FolderPicker — avec un mini sub-provider `DeckleWindowingSource` partagé ou un set d'events ajouté au provider de chaque module concerné. Le second est plus aligné avec la doctrine « l'observation s'attache au module qui contient l'opération » mais demande de doctriner le set canonique en transverse pour que les events soient comparables d'un module à l'autre.

**(3) Quel est le bon moment pour combler le HUD interne ?** Soit en parallèle de la vague Windowing (les deux se chevauchent — DPI, position, taille sont communs), soit en autonomie comme passe Hud-specific qui aurait son intérêt propre (state transitions, fade, retract, proximity). Une troisième option est d'attendre qu'un bug HUD émerge et d'instrumenter sur incident — c'est ce qui s'est fait jusqu'ici par défaut, et c'est exactement ce que la doctrine de couverture maximale veut éviter.

Le rapport s'arrête ici. Matière prête pour discussion ou pour transformation en plan de chantier si une suite est décidée.
