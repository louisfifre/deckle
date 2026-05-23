# Cartographie des modules Deckle — référence 1.1

Fiche canonique de la structure modulaire issue de la passe `cartographie-cleanup` (2026-05). Décrit la répartition des modules, leurs dépendances, les conventions internes de layout, et les patterns transverses. À lire avant d'ajouter un module, d'extraire du code d'un module existant, ou de toucher à un namespace racine.

## Vue d'ensemble

Le projet est découpé en dix-sept modules de bibliothèque plus un module hôte. Chaque module est un csproj indépendant, ses dépendances sont déclarées explicitement, et le graphe est acyclique. Le module hôte `Deckle.App` est le seul point d'entrée WinUI 3 ; il référence tous les autres et compose l'application autour d'eux. Toutes les bibliothèques sont buildables et testables individuellement (aucune ne dépend du hôte).

Le binaire publié reste `Deckle.exe` malgré le module hôte renommé en `Deckle.App` — l'`<AssemblyName>` est explicitement épinglé à `Deckle` dans `Deckle.App.csproj` pour que les entries autostart, le tooltip du tray, le label taskbar et les filtres shell qui ciblent `Deckle.exe` continuent de fonctionner sans migration côté utilisateur.

## Graphe de dépendances

Feuilles d'abord. Une flèche signifie « dépend de ».

```
Deckle.Core             (standalone)
Deckle.Catalog          (standalone)
Deckle.Chrono           (standalone)
Deckle.Logging          → Core
Deckle.Audio            → Core, Logging
Deckle.Composition      → Core, Logging
Deckle.Vision           → Core, Logging, Composition
Deckle.Lighting         → Core, Logging
Deckle.Shell            → Core, Catalog, Logging
Deckle.Settings         → Core, Catalog, Logging, Audio, Shell
Deckle.Hud              → Core, Catalog, Logging, Audio, Chrono, Composition, Settings, Shell
Deckle.Llm              → Core, Logging
Deckle.Llm.Rewrite      → Core, Catalog, Logging, Llm
Deckle.Transcription    → Core, Catalog, Logging, Audio, Llm, Llm.Rewrite, Settings
Deckle.Setup            → Core, Catalog, Logging, Transcription
Deckle.Lighting.Ambient → Core, Catalog, Logging, Composition, Vision, Lighting, Settings
Deckle.Playground       → Core, Catalog, Logging, Audio, Composition, Hud, Vision, Lighting, Lighting.Ambient, Settings, Shell
Deckle.App              → tous les modules ci-dessus
```

Le graphe ci-dessus est synthétisé directement des `<ProjectReference>` des dix-huit csprojs et reflète l'état réel à la racine de la branche `refactor/cartographie-cleanup`. Une commande `Get-Content src/Deckle.*/Deckle.*.csproj | Select-String 'ProjectReference Include="\.\.\\Deckle\.[^"]*"'` reproduit le matériau brut. Si une discordance apparaît plus tard entre cette table et un csproj, le csproj est la source de vérité — ce document doit être mis à jour pour s'aligner.

## Convention de layout interne

Trois modules métier (`Deckle.Transcription`, `Deckle.Llm.Rewrite`, `Deckle.Lighting.Ambient`) suivent le même layout en sous-dossiers. Le namespace racine du module est partagé par tous les fichiers du module ; les sous-dossiers ne créent pas automatiquement de sous-namespace. Le pattern est purement organisationnel.

```
src/Deckle.<Module>/
├── <Module>Settings.cs              POCO de settings, contrat partagé Engine/Ui
├── <Module>SettingsService.cs       persistance per-module (modules/<id>/settings.json)
├── (optionnel) I<Module>EngineHost.cs  contrat host injecté par l'hôte
├── Engine/                          logique métier, état, services bas-niveau
├── Ui/                              XAML pages, sections, view-models
├── Setup/                           primitives de provisioning first-run (rare)
└── Strings/en-US/Resources.resw     PRI multi-assembly (lib propre)
```

Les modules qui ne portent pas cette structure (Core, Catalog, Chrono, Composition, Audio, Logging, Shell, Vision, Lighting, Hud, Llm, Setup, Settings, Playground) n'en ont pas besoin : soit ils sont monolithiques (une seule responsabilité, pas de séparation Engine/Ui), soit ils sont eux-mêmes la couche UI (Settings, Setup, Playground), soit ils gèrent leur structure interne autrement (Composition a `Core/` + `Primitives/`, Audio a `Internal/` + `Telemetry/`).

## Convention de namespace

Chaque module exporte ses types sous son namespace racine, qui matche le nom du csproj. Les sous-namespaces ne sont introduits que pour les clusters internes qui le justifient — par exemple `Deckle.Core.Interop` pour le cluster Win32 dans Core, `Deckle.Transcription.Pinvoke` pour les structs whisper.cpp, `Deckle.Audio.Telemetry` pour la télémétrie micro. Le namespace bare `Deckle` n'existe pas, et aucun type n'utilise de namespace qui contredit l'assembly qui le contient (la convention « tous les PInvoke dans `Deckle.Interop` indépendamment de l'assembly » a été retirée pendant la passe).

## Catalogue par module

### Couches fondations (sans dépendance applicative)

**`Deckle.Core`** — Foundations sans UI. Expose `AppPaths` (résolution `<UserDataRoot>` + chemins dérivés), `JsonSettingsStore<T>` (générique pour la persistance JSON), et `Deckle.Core.Interop` (le cluster Win32 : `NativeMethods`, `Structs`, `UIAutomation`, `Win32Util`). Toutes les bibliothèques au-dessus partent de là.

**`Deckle.Logging`** — Hub télémétrie unique. `TelemetryService.Instance` + sinks (JsonlFileSink, LogWindowSink, HudFeedbackSink). Tous les modules métier remontent leurs events via `LogService.Instance` (façade au-dessus de TelemetryService). Le module porte aussi `TelemetrySettingsService` et `CorpusPaths`, plus le contrat `ITelemetryGates` consommé par chaque sink pour gater l'écriture selon les consents utilisateur.

**`Deckle.Catalog`** — Référentiel des ressources UI nommées par clé sémantique. `Loc.cs` (façade ResourceLoader WinAppSDK pour les `x:Uid`), `Themes/Icons.xaml` + `Glyphs.cs` (~51 clés sémantiques Fluent Icons consommées en XAML via `{StaticResource Icon.X}` et en code-behind via `Glyphs.X`).

**`Deckle.Chrono`** — Timer pur sans UI. `ChronoTimer` (wrapper Stopwatch managé), `ChronoFormatter`. Réutilisable par n'importe quel sous-système qui veut une lecture « time since trigger » sans le visuel.

**`Deckle.Composition`** — Primitives Direct2D et Composition partagées (`ColorSpace`, easing, animateurs). Sous-dossiers `Core/` et `Primitives/` pour séparer le bas niveau (mathématique couleurs, courbes) des primitives consommables.

### Capteurs et drivers (acquisition + sortie)

**`Deckle.Audio`** — Capture audio microphone via WASAPI. Expose `MicrophoneCapture` (orchestrateur `Probe()` + `Record(IAudioRecordingHost, CancellationToken)`), `IAudioRecordingHost` (contrat injecté par l'orchestrateur), `CaptureResult`, `CaptureSettings` + `CaptureSettingsService`, `AudioLevelMapper`. Sous-dossiers `Internal/` (RMS, buffers circulaires) et `Telemetry/` (métriques micro).

**`Deckle.Vision`** — Capture écran via DXGI Output Duplication. `ScreenCaptureService`, `FrameSampler`, `CapturedFrame`, `SampledFrame`, `IFrameAnalyzer` + `FrameAnalysisHint` (scaffold posé pour un module d'analyse partageable Vision↔Audio).

**`Deckle.Lighting`** — Driver LED abstraction. `ILightOutput`, `LightDescriptor`, `LightColor`. Sous-dossier `Hue/` pour l'implémentation Philips Hue Entertainment API v2 en direct UDP (pas de NuGet tiers, pas de relais cloud).

### Couches transverses (utilitaires shell + persistance + résolution UI)

**`Deckle.Shell`** — Shell système. `AutostartService` (HKCU Run key), `HotkeyManager` (`RegisterHotKey`), `IconAssets`, `MessageOnlyHost` (HWND_MESSAGE pour le tray + hotkeys + subclass Win32), `TrayIconManager`, `DispatcherQueueExtensions`. Le module est intentionnellement bas-niveau — pas de connaissance applicative au-delà du tray + hotkey + autostart.

**`Deckle.Settings`** — Shell UI Settings. `SettingsWindow` (NavigationView Auto adaptatif + Frame), pages owned (`GeneralPage`, `RecordingPage`, `DiagnosticsPage`), dialogs de consentement, racine de persistance (`SettingsService`, `AppSettings`, `SettingsBootstrap`, `SettingsBackupService`), et le registry de delegates `SettingsHost` (théme broadcast, level window propagation, restart, accès parent-window pour les dialogs cross-module, ouverture wizard setup). Les pages modulaires (`WhisperPage`, `LlmPage`, `AmbientPage`) ne vivent pas ici — elles sont possédées par leur module et résolues via `Type.GetType(tag)` à partir du `Tag` assembly-qualified du `NavigationViewItem`.

### Couches métier (subsystems)

**`Deckle.Hud`** — HUD applicatif. `HudWindow` (HUD principal bas-centre, ~320×64, OverlappedPresenter non resizable), `HudOverlayWindow` (cartes transient empilées), `HudOverlayManager` (gestion du stack avec invariant « pas de gap »), `HudChrono` (UserControl chronomètre avec coloration progressive et stroke Composition), `HudState` (enum visual states), `HudMessage` + `HudPalette` + `MessageKind` (UI primitives internes). Issu de la fusion de `Deckle.Chrono.Hud` (dissous) et de l'extraction du code HUD-side qui vivait dans `Deckle.App` pré-cleanup.

**`Deckle.Llm`** — Wrapper HTTP Ollama bas-niveau (administration des modèles installés, health-check). Un seul fichier : `OllamaService`. Réutilisable par n'importe quel consommateur LLM (AskOllama futur, …), pas de WinUI, pas de Settings, pas de Catalog.

**`Deckle.Llm.Rewrite`** — Consommateur réécriture. Au root le contrat de settings (`LlmSettings`, `LlmSettingsService`, `LlmSettingsMigrations`). Dans `Engine/` le moteur de réécriture (`LlmService` sur `/api/generate` en raw mode avec templates par famille de modèle, `PromptTemplates`). Dans `Ui/` la `LlmPage` Settings + 5 sections + `ProfileViewModel` + `LlmOllamaContext`. Le runtime LLM passe par les types `RewriteResult` + `RewriteProfile` + `AutoRewriteRule` exposés au root.

**`Deckle.Transcription`** — Pipeline transcription Whisper. Au root les settings (`WhispSettings` + ses sous-sections, `WhispSettingsService`), le contrat host (`IWhispEngineHost`), et la `WhisperPage` Settings. Dans `Engine/` le moteur (`WhispEngine`, state machine, segment callback, filtrage répétitions, fallback rewrite), `WhisperParamsMapper`. Dans `Pinvoke/` la surface `[DllImport]` (`WhisperPInvoke`) et les structs whisper.cpp (`WhisperStructs`, namespace `Deckle.Transcription.Pinvoke`). Dans `Setup/` les primitives first-run (`NativeRuntime`, `SpeechModels`, `SetupContext`) que le module `Deckle.Setup` orchestre. Dans `ViewModels/` le VM de la WhisperPage.

**`Deckle.Setup`** — Wizard first-run. `SetupWindow` (shell trois-rows : header + Frame + footer Cancel/Back/Next), `ChoicesPage`, `InstallingPage`, `SummaryPage`. Le module ne porte aucune primitive de provisioning — il orchestre les primitives exposées par `Deckle.Transcription.Setup` (`NativeRuntime`, `SpeechModels`). Détaché de `Deckle.App` en cartographie-cleanup pour que la `Settings.GeneralPage` puisse rouvrir le wizard sans que le hôte traîne le XAML.

**`Deckle.Lighting.Ambient`** — Consumer gaming `Vision + Lighting → Hue`. Au root les settings (`AmbientSettings`, `AmbientSettingsService`) et le contrat host (`IAmbientEngineHost`). Dans `Engine/` le moteur (`AmbientEngine`, state machine, `HuePairingService`, `AmbientModePresets`, `LightZone`, `LightZoneSuggester`). Dans `Ui/` la `AmbientPage` Settings + `Controls/` (par exemple `BrightnessCurveCanvas`).

### Modules utilitaires de surface

**`Deckle.Playground`** — Outil dev. `PlaygroundWindow` + pages frame-navigées (`HomePage`, `HudPage`, `AmbientPage`) pour le tuning live des courbes, des couleurs, des zones d'éclairage. Ouvrable via le tray (lazy creation, jamais détruit une fois créé).

### Module hôte

**`Deckle.App`** — App host WinUI 3. `App.xaml.cs` est le point d'entrée et l'orchestrateur. Possède les fenêtres longue vie (`HudWindow`, `LogWindow`, `SettingsWindow`, `PlaygroundWindow`) et la séquence `OnLaunched` (migration settings, bootstrap télémétrie, first-run gate, instanciation engines, wiring tray + hotkeys + message host, broadcast theme, ouverture conditionnelle Settings). Au root les fenêtres applicatives (`App.xaml`, `LogWindow.xaml`). Dans `Engine/` les bridges host (`AppWhispEngineHost`, `AppAmbientEngineHost`) qui implémentent les contrats `I*EngineHost` exposés par les modules métier. Dans `Logging/` les sinks app-side et `AppTelemetryGates`. `AssemblyName` épinglé à `Deckle` (cf. note d'ouverture).

## Patterns transverses

**Settings POCO + Service per-module.** Chaque module qui porte ses propres knobs utilisateur expose au root un POCO `<Module>Settings` (data) et un singleton `<Module>SettingsService.Instance` (persistance JSON débouncée + accès `Current` non-cachant pour lecture live). Le fichier sur disque vit sous `<UserDataRoot>/modules/<id>/settings.json`. Le pattern remplace l'ancien `AppSettings` monolithique migré pendant slice C2b — le module `Deckle.Settings` ne référence aucun `*Settings` POCO modulaire, chaque module charge le sien indépendamment. Le contrat est lu côté engine via une interface `IXxxEngineHost` injectée à l'instanciation, ce qui découple le module engine du `SettingsService` et permet le test isolé.

**`SettingsHost` delegate registry.** Les modules métier consomment des actions côté shell App (theme broadcast, restart, accès parent-window pour dialogs cross-module, ouverture wizard) via un registry de statics `Action<...>?` / `Func<...>?` dans `Deckle.Settings.SettingsHost`. Le hôte wire les delegates dans `OnLaunched` ; les call sites les invoquent en `?.Invoke(...)` et dégradent silencieusement en no-op quand rien n'est wiré (la lib reste buildable et testable en isolation). Pattern aligné sur `HudChrono.MaxRecordingDurationSecondsProvider`.

**Multi-assembly PRI pattern.** Chaque module qui ship du XAML avec `x:Uid` génère son propre PRI via `<EnableMsixTooling>true</EnableMsixTooling>` et copie le sous-ensemble pertinent du `.resw` sous `Strings/en-US/Resources.resw`. Au runtime, `Loc.Get` (façade `ResourceLoader` du module `Deckle.Catalog`) résout les clés contre l'instance de ResourceLoader bound au manifest courant. Modules concernés : `Settings`, `Transcription`, `Llm.Rewrite`, `Lighting.Ambient`, `Setup`, `Playground`.

**`<AssemblyName>` découplé de `<RootNamespace>`.** Convention introduite pour `Deckle.App` : le csproj porte `<RootNamespace>Deckle.App` (cohérence cartographique) et `<AssemblyName>Deckle</AssemblyName>` (stabilité user-facing du binaire). Aucun autre module n'a besoin de cette dissociation aujourd'hui.

**Pas de back-référence module-vers-host.** Aucun module bibliothèque ne référence `Deckle.App`. Les besoins de remontée vers le hôte passent par les contrats `IXxxEngineHost` (settings + actions de cycle de vie) et `SettingsHost` (delegates statiques). Le graphe acyclique est strict.

**Doctrine de modularité Settings.** La page Settings qui configure un domaine vit dans le module qui possède ce domaine, et son service de persistance aussi. Doctrine déjà appliquée pour `WhisperPage` (dans `Deckle.Transcription`), `LlmPage` (dans `Deckle.Llm.Rewrite`), `AmbientPage` (dans `Deckle.Lighting.Ambient`), et leurs `*SettingsService` respectifs. Pour les pages encore mal placées — `RecordingPage` aujourd'hui dans `Deckle.Settings` alors qu'elle configure le pipeline de capture, `DiagnosticsPage` idem — la migration est planifiée sous le code-nom Move H (cf. section ci-dessous).

## Taxonomie des modules

Le repo se range en **quatre catégories structurelles** qui se lisent en deux temps. On identifie d'abord à quelle catégorie un module appartient, ensuite on applique les règles propres à cette catégorie.

**Librairie support** — code passif sans état runtime propre. Code statique, structs, primitives, ressources. Référencé largement, ne référence quasi-rien. Pas de singleton actif, pas de boucle, pas d'écoute d'événements Windows. Modules concernés à ce jour : `Deckle.Core`, `Deckle.Catalog`, `Deckle.Composition`, `Deckle.Chrono`.

**Module métier** — porte un domaine, un état runtime actif, et souvent un store Settings + une page Settings. Singleton actif qui tient un état pendant la vie de l'app et agit sur le système (écoute des events Windows, lit un device, mute le clipboard, tient un buffer). Modules concernés : `Deckle.Diagnostics` (et ses enfants `.Logging` / `.Telemetry`), `Deckle.Audio`, `Deckle.Vision`, `Deckle.Lighting`, `Deckle.Shell`, `Deckle.Hud`, `Deckle.Llm`, `Deckle.Llm.Rewrite`, `Deckle.Transcription`, `Deckle.Lighting.Ambient`.

**Shell** — coquille de présentation qui reçoit et n'expose rien aux modules métier ; ne référence pas les domaines, agrège dynamiquement via registry. `Deckle.Settings` est le seul du genre aujourd'hui (NavigationView Settings qui résout `Type.GetType(tag)` depuis le `Tag` du `NavigationViewItem` pour charger les pages possédées par les modules métier).

**Host** — référence largement, agrège, sert un usage différencié. Trois hosts identifiés : `Deckle.App` (host de production, point d'entrée EXE, composition root qui voit tous les modules), `Deckle.Setup` (host first-run wizard, transient), `Deckle.Playground` (host dev/tuning, persistant). **Les hosts sont explicitement exemptés de la doctrine de modularité** — leur rôle *est* d'agréger. `Playground` n'est pas une exception ad-hoc ; c'est une instance d'une catégorie qui inclut aussi `App` et `Setup`.

**Critère discriminant librairie support vs module métier (K3).** Le module porte-t-il un singleton actif qui tient un état pendant la vie de l'app et agit sur le système ? Si oui, module métier. Sinon, librairie support. Critère stable structurellement — il ne dépend pas de l'apparition future d'un Settings POCO et capture la vraie différence (passif utilitaire vs actif agissant). À reconfirmer pour `Deckle.Chrono` quand sa surface se stabilise : si `ChronoTimer` reste un wrapper stateless sur `Stopwatch`, c'est support ; s'il gagne un état métier (cumul de sessions, timeline persistée), il bascule en module métier.

## Move H — décidé, pas appliqué

Migration complète des résidus historiques d'`AppSettings` vers la persistance per-module. Slice C2b avait déjà déplacé les services per-module (`CaptureSettingsService` dans `Audio`, `WhispSettingsService` dans `Transcription`, etc.). Move H finalise côté POCO et page. `Overlay` migre vers `Hud` (POCO `OverlaySettings` + nouvelle `OverlayPage`). `Paste` migre vers `Transcription` (sibling de `WhispSettings`, type `TranscriptionPasteSettings`). `Startup.WarmupOnLaunch` migre vers `Transcription` (le warmup *est* une transcription muette ; le shell n'a pas à porter cette sémantique). `RecordingPage` migre vers `Audio`, `DiagnosticsPage` vers `Diagnostics.Logging` — leurs services y sont déjà depuis C2b. Restent côté shell `AppearanceSettings.Theme` (consommé par toutes les fenêtres, aucun domaine ne le possède) et `PathsSettings.BackupDirectory` (concerne le backup de `settings.json` lui-même, donc le module qui possède `settings.json`). `GeneralPage` devient minimaliste après migration : Theme + Backup + ReRunSetup + OpenDataFolder.

Plusieurs branches non creusées à reprendre au moment de la réalisation. Le footer Logs de la NavigationView Settings n'est pas une nav-Page mais une action (ouverture de `LogWindow` via callback) ; soit le registry est étendu pour porter deux types d'entrées (Page + Action), soit Logs reste un cas spécial XAML. Les dialogs partagés (`ApplicationLogConsentDialog`, `AudioCorpusConsentDialog`, `MicrophoneTelemetryConsentDialog`) vivent aujourd'hui dans `Deckle.Settings` mais sont invoqués depuis plusieurs pages dans plusieurs modules — trois options à arbitrer (migration stricte avec dep correspondante côté pages invoquantes, rester dans Settings comme dialogs shared cross-module, ou extraction dans un `Deckle.Settings.Dialogs` léger). `InternalsVisibleTo` pour les hosts comme conséquence directe de la taxonomie : si `App`, `Setup`, `Playground` sont hosts au sens fort, on peut leur ouvrir `InternalsVisibleTo("Deckle.App")` sur les modules métier pour ne pas surexposer la surface publique. Réactivité HUD ← `OverlaySettings` post-Move H : le HUD lit aujourd'hui `Settings.SettingsService.Instance.Current.Overlay.X` à plusieurs endroits en lazy-read ; après Move H ça devient `OverlaySettingsService.Instance.Current.X`, le pattern lazy-read continue de fonctionner sans plomberie event-driven — à reconfirmer en réalisation, surtout pour `WindowSlideAnimator`.

## Historique

Cette structure est l'aboutissement de la passe `refactor/cartographie-cleanup` (mai 2026), elle-même précédée par la passe modularité C1/C2/C2b (avril 2026, déplacement des `*Page` modulaires hors de `Deckle.Settings`, persistance per-module). Les moves clés du cleanup : Move A (suppression du Playground stub), Move B (rename `Deckle` → `Deckle.App` avec `AssemblyName` pin), Move C (extraction `Deckle.Setup` du hôte), Move D+F (extraction `Deckle.Hud` + dissolution `Deckle.Chrono.Hud`), Move (b) (split `Deckle.Llm` en engine + `Deckle.Llm.Rewrite`), Move G (normalisation Engine/Ui dans Llm.Rewrite et Lighting.Ambient), Move I (homogénéisation des namespaces de Deckle.Core sous `Deckle.Core.*`). Le journal détaillé des commits vit dans `git log refactor/cartographie-cleanup`.
