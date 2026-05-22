# Cartographie modulaire Deckle — récap session 2026-05-22

## Statut

Document de travail. Produit en clôture d'une session de cartographie / grilling sur le découpage modulaire de Deckle. À fusionner avec le récap d'une session parallèle qui porte la refonte logging EventSource. Pas encore promu en référence stable — c'est de la matière pour la décision, pas la décision elle-même.

## Contexte et but

La session est partie d'une demande de cartographie réelle des modules `src/Deckle.*` (qu'est-ce qui vit où vraiment, où sont les frontières fausses, qu'est-ce qu'on propose comme cible). Elle a glissé en grilling structurant sur le cadre de découpage lui-même — que veut-on faire de la modularité, pour quoi, jusqu'où. Le présent document capture les deux choses : l'inventaire factuel observé dans le repo au 22 mai 2026, et le cadre cible qui s'est cristallisé pendant la discussion.

Le récap est rédigé pour être lisible cold par une autre session — Louis le fusionnera avec la session refonte logging EventSource pour repartir sur une base commune.

## Principes cadres cristallisés pendant la session

**Quatre familles de modules, pas une typologie uniforme.** Tous les modules de Deckle ne sont pas de la même nature. Les forcer dans un même shape « normalisé » est une fausse symétrie qui coûte plus qu'elle ne rapporte. Les quatre familles : fondations techniques transverses, services de capability, modules métier (application), host. La standardisation porte uniquement sur la famille métier.

**Scission par consumer uniquement.** Trois axes de scission existent en théorie : par consumer (un parent fournit la capacité, plusieurs enfants l'appliquent — pattern `Lighting` + `Lighting.Ambient` + futur `Lighting.Informative`), par layer (parent fournit la primitive, enfant fournit la présentation lourde — pattern `Chrono` + `Chrono.Hud`), et par moteur/UI (scission interne moteur vs interface visuelle). Louis tranche : seul l'axe par consumer est légitime. L'axe par layer est rejeté (le pattern `Chrono.Hud` actuel sera dissous, voir section mouvements). L'axe moteur/UI est rejeté également (un module garde son UI à l'intérieur, on ne scinde pas pour rien).

**Standardisation par convention de dossier, pas par scission.** Chaque module métier suit la même convention de dossier interne. Le squelette canonique : un dossier `Engine/` pour le code moteur, `Ui/` pour le XAML + .xaml.cs + ViewModels, `Setup/` pour le provisioning first-run si pertinent, `Strings/<locale>/Resources.resw` pour la localisation, un `CLAUDE.md` à la racine du module qui documente la doctrine spécifique. Les dossiers ne sont matérialisés sur le disque que s'ils contiennent du contenu — mais leur liste exhaustive de dossiers possibles est documentée dans un skill dédié pour que tout ajout futur soit guidé.

**Rename `Deckle` → `Deckle.App`.** Le module sans suffixe est jugé incohérent dans le tree `src/`. Il devient `Deckle.App`. Le `.exe` produit reste `Deckle.exe` (via `AssemblyName=Deckle` dans le csproj).

**Refonte Settings dynamique acceptée.** Chaque module métier déclare ses metadata de page Settings (titre, icône, sous-titre, type de page, poids d'ordre) que le shell `Deckle.Settings` ingère dynamiquement à l'instar d'une découverte plugin. Le shell ne référence plus les pages modulaires en dur dans son XAML. Les trois objections soulevées (coût/bénéfice mince, perte de garanties statiques, ordre d'affichage) sont rejetées explicitement : Louis prévoit beaucoup de modules à terme, le testing couvrira les casses statiques, et un paramètre de poids d'ordre dans les metadata règle l'affichage. UX cible : modules avec dropdown qui déroule sous-modules quand il y en a, paramètres généraux puis spécifiques.

**Documentation canonique et vérifiable.** Chaque module porte un `CLAUDE.md` qui documente sa doctrine. Les README généraux (racine du repo, racine de `src/`) font inventaire des modules. La règle d'écriture : ne mettre dans la documentation que ce qui est canonique et vérifiable — ce qui n'a pas vocation à devenir obsolète. Tout ce qui est volatil (état d'avancement, todos, hypothèses transitoires) vit ailleurs (mémoire, chantier en cours, plan). Idée à creuser : un README de l'inventaire des modules sous `src/` qui se génère dynamiquement (à valider plus tard).

## Cartographie réelle au 22 mai 2026

### Famille 1 — Fondations techniques transverses

Quatre modules. Toolbox consommée par tous, sans UI propre, sans page Settings, sans cycle de vie applicatif.

`Deckle.Core` héberge la résolution des chemins (`Paths/AppPaths.cs`), le store JSON générique (`JsonSettingsStore<T>`), et l'interop Win32 sous `Interop/` (`NativeMethods` avec ~60 P/Invoke user32/dwmapi/shell32/winmm/kernel32/comctl32, `Structs`, `Win32Util`, `UIAutomation`). Trois namespaces différents coexistent (`Deckle` pour `AppPaths`, `Deckle.Core` pour `JsonSettingsStore`, `Deckle.Interop` pour les fichiers Interop) alors que `RootNamespace=Deckle.Core` — incohérence cosmétique à corriger lors du chantier. `UIAutomation` n'a qu'un seul consommateur (`WhispEngine.PasteFromClipboard`) — primitive réutilisable, mais à surveiller : si elle reste unique-consumer dans six mois, déménagement vers `Deckle.Whisp` à envisager.

`Deckle.Logging` héberge le hub télémétrie unique : `TelemetryService`, façade `LogService`, sinks (`JsonlFileSink`), payloads (`TelemetryEvent`, `LatencyPayload`, `CorpusPayload`, `MicrophonePayload`, `UserFeedback`), gates (`ITelemetryGates`, `TelemetryGates`), settings de télémétrie (`TelemetrySettings` + service), `WavCorpusWriter`. Coexistence de `LoggingSettings` et `TelemetrySettings` (deux POCO + deux services) à clarifier — soit scission saine, soit duplication. La refonte EventSource en cours dans la session parallèle reverra probablement la structure interne.

`Deckle.Catalog` héberge `Loc.cs` (façade ResourceLoader pour `x:Uid`), `Glyphs.cs` (~51 codepoints Segoe Fluent Icons accessibles côté code), `Themes/Icons.xaml` (mêmes glyphes accessibles côté XAML). Module minuscule, conforme à sa doctrine documentaire. Sera consommé par tout module qui a une UI.

`Deckle.Composition` héberge `Primitives/ColorSpace.cs` (conversions OKLCh / sRGB / linear), `Primitives/Easing.cs`, `Primitives/SwipeWaveAnimator.cs`, et `Core/HudComposition.cs` + `Core/ProcessingVariant.cs` (factories Win2D du HUD stroke). Le module mélange des primitives réutilisables et des factories spécifiques au HUD chrono — chargé. Une fois les modules métier stabilisés, l'allègement de `Composition` pourra être réexaminé (déplacer les factories `HudComposition` vers le module HUD applicatif si le module Composition devient « lourd pour rien »).

### Famille 2 — Services de capability

Cinq modules. Fournisseurs d'une capacité technique, sans UI propre (sauf petits UserControls réutilisables au besoin), sans page Settings dédiée — la page Settings consommée par ces services vit dans le shell `Deckle.Settings` parce qu'elle est jugée transverse (cas typique : `RecordingPage` qui pilote `CaptureSettingsService` du module `Deckle.Audio`).

`Deckle.Shell` héberge `TrayIconManager`, `HotkeyManager`, `MessageOnlyHost`, `AutostartService`, `IconAssets`, `DispatcherQueueExtensions`. Le module abstrait l'intégration système (tray, hotkeys globaux, autostart) sans porter de logique métier. Asymétrie noter : `Deckle.Settings` (famille 3 / shell) dépend de `Deckle.Shell` (pour `AutostartService` + `IconAssets`) — Shell n'est pas un consommateur du shell Settings, c'est l'inverse.

`Deckle.Audio` héberge la capture micro WASAPI (`MicrophoneCapture`, `Internal/WaveInLoop`, `Internal/PcmConversion`), la calibration RMS (`MicrophoneCalibrationCalculator`, `MicrophoneTelemetryCalculator`), le mappeur RMS → niveau perceptuel (`AudioLevelMapper`), le contrat consumer (`IAudioRecordingHost`), les POCO résultat (`CaptureResult`, `ProbeResult`), settings (`CaptureSettings` + service). Le nom est volontairement plus large que le contenu actuel (rename `Capture → Audio` en mai 2026, anticipe loopback / TTS / monitoring futurs).

`Deckle.Vision` héberge la capture écran (`ScreenCaptureService`, `ScreenCaptureInterop`, `CapturedFrame`, `SampledFrame`), l'échantillonneur GPU (`FrameSampler`), et le scaffold du futur module d'analyse (`IFrameAnalyzer`, `FrameAnalysisHint`). Frontière propre.

`Deckle.Chrono` est un timer pur : `ChronoTimer` (wrapper Stopwatch) + `ChronoFormatter`. Zéro `ProjectReference`. Zéro WinUI. Réutilisable hors contexte Deckle.

`Deckle.Lighting` héberge les drivers Hue (`Hue/HueBridge`, `Hue/HueDiscovery`, `Hue/HueGroup`, `Hue/HueLight`, `Hue/HueEntertainmentArea`, `Hue/HueBridgeClient`, `Hue/HueRestLightOutput`, `Hue/HueColorMath`) + l'abstraction `ILightOutput` / `IMultiLightOutput` + les POCO `LightColor`, `LightDescriptor`. Pas de persistance, pas d'UI, pas de notion d'« ambient ». Pure couche driver.

### Famille 3 — Modules métier

Trois modules métier aujourd'hui, plus des cas borderline et des extractions identifiées.

`Deckle.Whisp` est le pipeline transcription complet — `Engine/WhispEngine.cs` + helpers (`RepetitionDetector`, `WhisperParamsMapper`), `Pinvoke/WhisperPInvoke.cs` + `WhisperStructs.cs`, `Setup/NativeRuntime.cs` + `SpeechModels.cs` + `Downloader.cs` + `SetupContext.cs`, settings (`WhispSettings`, `WhispSettingsService`), contrat host (`IWhispEngineHost`), UI (`WhisperPage.xaml(.cs)` à la racine, `ViewModels/WhisperViewModel.cs`). Squelette le plus complet des trois. Détail UI à corriger : page Settings à la racine du module au lieu de `Ui/`.

`Deckle.Llm` est la réécriture LLM via Ollama — `LlmService.cs`, `OllamaService.cs`, `PromptTemplates.cs`, `LlmSettings` + migrations + service, `LlmOllamaContext.cs`, `ProfileViewModel.cs`, plus une UI Settings éclatée en sept fichiers .xaml.cs (`LlmPage`, `LlmGeneralSection`, `LlmModelsSection`, `LlmProfilesSection`, `LlmRulesSection`, `LlmShortcutSlotsSection`, `GgufImport/GgufImportView` + `GgufImportDialog`). Pas de dossier `Engine/` (le moteur est à la racine du module), pas de `IXxxEngineHost`, pas de `Setup/`. Squelette moins complet que Whisp. `GgufImport` est une grosse surface UI ajoutée tard, qui pourrait être supprimée ou simplifiée car peu utilisée (à voir).

`Deckle.Lighting.Ambient` est le consumer Hue temps réel — `AmbientEngine.cs` (~1338 lignes, orchestrateur capture → analyse → push couleur, deux pipelines group/multi, tone-mapping HDR, EMA temporelle, heartbeat télémétrie), `AmbientSettings` + service, `AmbientEngineState`, `AmbientModePresets`, contrat host (`IAmbientEngineHost`), `HuePairingService.cs` (singleton process-wide qui possède `HueBridgeClient` et persiste dans `AmbientSettings`), `LightZone`, `LightZoneSuggester`, UI (`AmbientPage.xaml(.cs)` à la racine, `Controls/BrightnessCurveCanvas.xaml(.cs)`). Pas de dossier `Engine/`, le moteur est à la racine. Squelette intermédiaire.

`Deckle.Settings` est le shell Settings — fenêtre `SettingsWindow.xaml(.cs)`, pages « owned » cross-modulaires (`GeneralPage`, `RecordingPage`, `DiagnosticsPage`) + ViewModels, persistance des sections cross-modulaires (`SettingsService` pour Appearance / Paste / Startup / AutoRewriteRules / Overlay), bootstrap (`SettingsBootstrap.MigrateLegacyToPerModule`), registry de hooks statiques (`SettingsHost`), backup (`SettingsBackupService`), pattern `FolderPickerCard` / `FolderPickerEditableCard`, quatre dialogs de consentement. Module spécial — c'est un shell qui agrège les contributions des modules métier. La refonte Settings dynamique (acceptée par Louis) va transformer cette agrégation en mécanisme de découverte au runtime plutôt qu'en référence statique XAML.

`Deckle.Playground` est dev-only — `PlaygroundWindow.xaml(.cs)`, trois pages (HomePage, HudPage, AmbientPage) avec splitting partiel via partial classes, `PlaygroundShell.cs`, ViewModels, models. Sera supprimé à terme (Louis l'a explicité).

### Famille 4 — Host

Un module : `Deckle` (à renommer `Deckle.App`). Contient `App.xaml.cs` (882 lignes — composition, lifecycle, branchement complet des modules), les bridges (`Engine/AppWhispEngineHost.cs`, `Engine/AppAmbientEngineHost.cs`), les adapters télémétrie (`Logging/AppTelemetryGates.cs`, `Logging/TelemetryEventTemplateSelector.cs`, `Logging/HudFeedbackSink.cs`).

Aujourd'hui le host porte aussi quatre périmètres qui n'y appartiennent pas — c'est la zone d'ombre principale identifiée pendant la session. Voir « mouvements identifiés ».

### Module fantôme

`Deckle.Localization` — dossier `src/Deckle.Localization/` présent (avec `bin/` et `obj/`) mais le `.csproj` est supprimé. Résidu du rename `Localization → Catalog` du 18 mai 2026. À supprimer du tree.

## Diagnostic des frontières incorrectes

**1. Le host porte cinq périmètres distincts.** Le `CLAUDE.md` du host annonce « composition, fenêtres longue vie, tray, hotkeys, branchement modules ». La réalité observée : (a) composition pure et lifecycle, (b) surfaces HUD applicatives complètes (HudWindow + HudOverlayWindow + HudOverlayManager + Controls/HudMessage + Composition/HudPalette + WindowSlideAnimator + Logging/HudFeedbackSink), (c) LogWindow seule de son espèce, (d) wizard de setup avec quatre pages XAML, (e) adapters télémétrie. Asymétrie : `SettingsWindow` et `PlaygroundWindow` sont chacune leur module, mais `HudWindow`, `HudOverlayWindow` et `LogWindow` vivent dans l'host.

**2. Le wizard appartient à un futur `Deckle.Setup` transverse.** Aujourd'hui sous `src/Deckle/Shell/Setup/` (SetupWindow, ChoicesPage, InstallingPage, SummaryPage), le wizard est un héritage pré-modulaire. Louis tranche : il deviendra un module transverse `Deckle.Setup`, pas un sous-module de Whisp. Quand d'autres modules futurs auront besoin d'un setup first-run, ils contribueront au wizard via un mécanisme déclaratif (similaire à la refonte Settings dynamique — à concevoir).

**3. Le LogWindow vit dans le host alors qu'il est purement consumer de Deckle.Logging.** `LogWindow.xaml.cs` implémente `ITelemetrySink` sans logique business. Sera extrait dans un module dédié de la famille métier — Louis voit le viewer logs comme une interface logique (« le viewer des logs c'est plutôt sous interface » dans son vocabulaire, ce qui se traduit techniquement par « un module métier de la famille 3 qui consomme `Deckle.Logging` »). Nom proposé : `Deckle.Logs` (le viewer) parallèle à `Deckle.Logging` (le hub).

**4. Les surfaces HUD applicatives forment un sous-système cohérent à extraire.** `HudWindow` + `HudOverlayWindow` + `HudOverlayManager` + `Controls/HudMessage` + `Controls/HudState` (qui ne porte plus que `MessageKind` / `MessagePayload`) + `Composition/HudPalette` + `WindowSlideAnimator` + `Logging/HudFeedbackSink`. Module cible : `Deckle.Hud` (famille métier 3). Louis a précisé que d'autres modules futurs consommeront ce HUD : module `Notifications`, module gestion d'erreur, etc. Donc `Deckle.Hud` est une primitive d'affichage applicative.

**5. Trois namespaces dans Deckle.Core.** `Deckle` pour `AppPaths`, `Deckle.Core` pour `JsonSettingsStore`, `Deckle.Interop` pour les fichiers Interop. À homogénéiser sous `Deckle.Core` (et `Deckle.Core.Interop` pour le sous-dossier). Cosmétique mais propage à beaucoup de callsites.

**6. Module fantôme `Deckle.Localization`.** Voir section cartographie.

**7. Triple back-référence vers `Deckle.Settings` depuis les modules métier.** `Whisp`, `Llm`, `Lighting.Ambient` référencent `Deckle.Settings` uniquement pour la classe statique `SettingsHost` (RestartApp, GetSettingsWindow, etc.). Pas un cycle, mais référence à un gros module pour une classe statique. Avec la refonte Settings dynamique acceptée, le `SettingsHost` actuel va probablement disparaître ou se transformer — la dynamicité résoudra naturellement cette friction.

**8. Composition mélange primitives et factories HUD-spécifiques.** `Primitives/*` (ColorSpace, Easing, SwipeWaveAnimator) sont des primitives réutilisables ; `Core/HudComposition.cs` + `Core/ProcessingVariant.cs` sont des factories Win2D spécifiques au HUD chrono. Louis a noté que l'allègement de Composition pourra être examiné après stabilisation du reste — pas une priorité immédiate.

## Cadre cible

### Familles cibles et règles de standardisation

**Fondations (4 modules)** : `Deckle.Core`, `Deckle.Logging`, `Deckle.Catalog`, `Deckle.Composition`. Pas de standardisation forcée. Chaque module fait son métier technique avec la structure que son métier impose. Documenté par `CLAUDE.md` du module.

**Services de capability (5 modules)** : `Deckle.Shell`, `Deckle.Audio`, `Deckle.Vision`, `Deckle.Chrono`, `Deckle.Lighting`. Pas de standardisation forcée non plus. Pas d'UI propre (sauf petits UserControls de support si justifiés). Pas de page Settings — la page Settings, si elle existe, est dans le shell `Deckle.Settings` parce que les settings de capacité sont jugés transverses.

**Modules métier (3 actuels + ~6 cible)** : standardisation par convention de dossier interne. Squelette canonique : `Engine/`, `Ui/`, `Setup/`, `Strings/<locale>/Resources.resw`, `CLAUDE.md`, racine du module pour les POCO settings + service + contrat host. La liste exhaustive des dossiers possibles vit dans un skill dédié (à créer). Les dossiers ne sont créés que s'ils ont du contenu.

Modules métier cibles à terme : `Deckle.Whisp` (transcription — peut-être renommer à terme `Deckle.Transcription` avec un sous-module pour l'implémentation Whisper.cpp, à décider), `Deckle.Llm` (réécriture — possible évolution vers un module parent générique « inférence LLM » avec sous-modules pour réécriture, recherche Anytype, etc.), `Deckle.Lighting.Ambient` (consumer Hue temps réel), `Deckle.Lighting.Informative` (futur consumer LED retour physique — loading bars), `Deckle.Hud` (surfaces HUD applicatives), `Deckle.Logs` (viewer logs), `Deckle.Setup` (wizard transverse), futurs `Deckle.AskHud`, `Deckle.AskOllama`, `Deckle.Notifications`, `Deckle.Errors`, etc. Le shell `Deckle.Settings` reste un module métier spécial (shell + persistance cross-modulaire). `Deckle.Playground` disparaît à terme.

**Host (1 module)** : `Deckle.App` (renommé). Mince une fois les extractions faites. Contient `App.xaml.cs`, les bridges `App<Module>EngineHost`, les adapters télémétrie qui restent strictement applicatifs.

### Convention de dossier interne (modules métier)

```
src/Deckle.<MetierModule>/
├── <ModuleName>SettingsService.cs         persistance singleton
├── <ModuleName>Settings.cs                 POCO settings
├── I<ModuleName>EngineHost.cs              contrat host bridge si nécessaire
├── Engine/                                 logique métier
│   └── <ModuleName>Engine.cs
├── Setup/                                  contribution wizard si provisioning
├── Ui/                                     tout le XAML / xaml.cs
│   ├── <ModuleName>Page.xaml(.cs)
│   ├── ViewModels/
│   └── Controls/   (optionnel)
├── Strings/en-US/Resources.resw            localisation
└── CLAUDE.md                                doctrine module
```

Avantages observés : un développeur ou un designer qui ouvre `src/` peut taper `find . -path '*/Ui/*.xaml'` et obtenir la liste exhaustive des surfaces visuelles. Le squelette est prévisible — quelqu'un qui touche au moteur va dans `Engine/`, jamais dans `Ui/`. Les modules qui n'ont pas d'UI n'ont simplement pas de dossier `Ui/` — le squelette reste cohérent.

### Trois axes de scission — politique

**Par consumer** : axe privilégié. Un parent fournit une capacité, plusieurs enfants l'appliquent à des contextes différents. Exemples : `Deckle.Lighting` (drivers) → `Deckle.Lighting.Ambient` (consumer temps réel) + `Deckle.Lighting.Informative` (consumer LED loading). Probable application future : `Deckle.Llm` (moteur inférence générique) → `Deckle.Llm.Rewrite` (réécriture transcription) + `Deckle.Llm.Search` (recherche Anytype) + autres.

**Par layer** : rejeté. Le pattern actuel `Deckle.Chrono` + `Deckle.Chrono.Hud` sera dissous (voir mouvements).

**Par moteur/UI** : rejeté. Un module métier garde son UI à l'intérieur (dossier `Ui/`). Pas de scission `<Module>.Core` + `<Module>.Ui`.

### Refonte Settings dynamique

Le shell `Deckle.Settings` n'inscrit plus les pages modulaires en dur dans son XAML. Chaque module métier déclare ses metadata de page Settings via un mécanisme à concevoir (interface, attribute, ou méthode statique). Les metadata incluent au minimum : un identifiant stable, un titre (clé x:Uid), une icône (clé Glyphs), un type de page, un poids d'ordre (pour le tri NavView). Le shell découvre ces contributions au boot (via scan d'assemblies ou registration explicite par chaque module dans son point d'entrée). UX cible : NavView qui montre les modules avec dropdown pour les sous-modules quand ils existent (paramètres généraux du module + paramètres spécifiques de chaque sous-module).

À spécifier dans une passe ultérieure : mécanisme exact (DI ? registration manuelle dans App.OnLaunched ? attribute sur la page ?), contract des metadata, gestion de l'ordre, fallback si une page déclarée ne se résout pas, comportement quand un module n'a pas de page Settings.

### Documentation par module

Chaque module porte un `CLAUDE.md` à sa racine qui documente la doctrine spécifique au module — pas le code, pas les chemins de fichiers volatils. Règle d'écriture : seul ce qui est canonique et vérifiable au moment T entre dans le document. Ce qui peut devenir obsolète (état d'avancement, todos, hypothèses à valider) vit ailleurs.

`src/` porte probablement un `README.md` ou un index qui inventorie les modules. Idée à creuser : génération dynamique de cet inventaire pour qu'il ne périme pas (à valider plus tard).

## Mouvements identifiés

Ordre de priorité indicatif, à raffiner quand le chantier sera planifié pour de vrai.

**A. Supprimer le dossier fantôme `Deckle.Localization`.** Coût trivial. Le `.csproj` est déjà parti, seuls les artefacts `bin/` et `obj/` restent. Mise à jour du `CLAUDE.md` racine pour ne plus mentionner le module.

**B. Renommer `Deckle` → `Deckle.App`.** Coût moyen (un csproj à renommer, un dossier à renommer, `RootNamespace` à ajuster, `AssemblyName` à fixer à `Deckle` pour préserver le nom du `.exe`, pas de `ProjectReference` à mettre à jour parce que le host est en bout de chaîne). Bénéfice : `src/` devient lisible sans module spécial sans suffixe.

**C. Extraire le wizard setup dans `Deckle.Setup` (module transverse).** Déplacer les quatre fichiers XAML + .xaml.cs depuis `src/Deckle/Shell/Setup/` vers `src/Deckle.Setup/Ui/`. Les classes du provisioning Whisp restent dans `Deckle.Whisp/Setup/`. Le wizard est un consumer du provisioning — il appelle `NativeRuntime`, `SpeechModels`, `Downloader`. Coût moyen — 8 fichiers à déplacer, namespace renames, mise à jour de `App.OnLaunched` pour l'instanciation. Le wizard évolue vers un mécanisme de contribution multi-modules (similaire à la refonte Settings dynamique), à concevoir.

**D. Extraire les surfaces HUD applicatives dans `Deckle.Hud`.** Déplacer `HudWindow.xaml(.cs)`, `HudOverlayWindow.xaml(.cs)`, `HudOverlayManager.cs`, `Controls/HudMessage.xaml(.cs)`, `Controls/HudState.cs` (MessageKind/MessagePayload), `Composition/HudPalette.cs`, `WindowSlideAnimator.cs`, `Logging/HudFeedbackSink.cs`. Coût substantiel — création d'un csproj WinUI 3, ~9 fichiers déplacés, merge XAML cross-assembly à valider, `App.OnLaunched` à adapter. Le module devient consommable par tout autre module qui veut faire afficher un message ou un overlay (futurs Notifications, Errors).

**E. Extraire le LogWindow dans `Deckle.Logs`.** Déplacer `LogWindow.xaml(.cs)` + `Logging/TelemetryEventTemplateSelector.cs`. Coût moyen — un csproj à créer, deux fichiers à déplacer + leurs ressources XAML, un callsite à adapter dans `App.OnLaunched`. Le module devient le viewer canonique des logs ; le hub `Deckle.Logging` reste headless.

**F. Dissoudre `Deckle.Chrono.Hud`.** La scission par layer étant rejetée, le UserControl `HudChrono.xaml(.cs)` + le `HudState` actuel ne justifient plus leur propre module. Question ouverte (voir section dédiée) : où va le contenu (retour dans `Deckle.Chrono` ou intégration dans `Deckle.Hud` extrait au mouvement D).

**G. Normaliser les modules métier sur la convention de dossier.** Déplacer `WhisperPage.xaml(.cs)` + `ViewModels/` de Whisp vers `Whisp/Ui/`. Déplacer les 7 fichiers UI de Llm vers `Llm/Ui/`. Créer `Llm/Engine/` et y déplacer `LlmService`, `OllamaService`, `PromptTemplates`, `LlmOllamaContext`. Déplacer `AmbientPage.xaml(.cs)` + `Controls/` d'Ambient vers `Ambient/Ui/`. Créer `Ambient/Engine/` et y déplacer `AmbientEngine`, `HuePairingService`, `LightZone`, `LightZoneSuggester`, `AmbientModePresets`, `AmbientEngineState`. Coût moyen mais propagé sur trois modules. Bénéfice : tous les modules métier deviennent inspectables au même pattern.

**H. Refonte Settings dynamique.** Concevoir et implémenter le mécanisme de découverte des contributions Settings. Coût conséquent — design du contrat, implémentation du scan / registration, migration des trois modules existants, tests. À planifier comme chantier dédié, pas comme un sous-mouvement de la passe modulaire.

**I. Homogénéiser les namespaces de `Deckle.Core`.** Renommer `namespace Deckle` (dans `AppPaths.cs`) et `namespace Deckle.Interop` (dans les 4 fichiers Interop) en `namespace Deckle.Core` et `namespace Deckle.Core.Interop`. Coût propagé (~30-50 fichiers callers à mettre à jour leurs `using`). Bénéfice cosmétique. À faire en dernier ou à laisser de côté.

**J. Documentation par module — passe systématique.** Créer / mettre à jour le `CLAUDE.md` de chaque module pour refléter la cible. Mettre à jour le `CLAUDE.md` racine. Créer / mettre à jour un `README.md` ou index dans `src/` qui inventorie les modules. Coût moyen — passe d'écriture. À faire en parallèle des mouvements A-G pour que la documentation reste alignée sur l'état.

**K. Skill dédié — conventions modulaires.** Créer un skill qui documente : la typologie en 4 familles, la convention de dossier interne, la politique de scission, le mécanisme de contribution Settings, la liste exhaustive des dossiers possibles dans un module métier, le pattern de nommage. À créer en complément des skills `deckle-docs`, `deckle-logging`, `deckle-modularite` existants — probablement en consolidant / remplaçant partiellement `deckle-modularite`.

## Décisions tranchées dans cette session

- Typologie en 4 familles (fondations, services, métier, host) — validée.
- Standardisation s'applique uniquement à la famille métier — validée.
- Scission par consumer uniquement — validée. Par layer et par moteur/UI rejetées.
- Convention de dossier interne (`Engine/`, `Ui/`, `Setup/`, etc.) — validée, à appliquer partout dans les modules métier.
- Dossiers vides non matérialisés, mais liste exhaustive documentée dans un skill — validée.
- Rename `Deckle` → `Deckle.App` — validée.
- Refonte Settings dynamique acceptée — validée.
- Wizard setup devient module transverse `Deckle.Setup` — validée.
- LogWindow extrait dans `Deckle.Logs` (viewer) parallèle à `Deckle.Logging` (hub) — validée.
- Surfaces HUD applicatives extraites dans `Deckle.Hud` — validée.
- Scission `Deckle.Chrono.Hud` dissoute (l'axe par layer est rejeté) — validée. Destination du contenu à trancher (question ouverte).
- Module fantôme `Deckle.Localization` à supprimer — validée.
- Documentation canonique par `CLAUDE.md` de module + `CLAUDE.md` racine + `README.md` (ou index) sous `src/` — validée. Génération dynamique de l'inventaire `src/` à explorer.
- `Deckle.Playground` disparaîtra à terme — noté (pas un mouvement immédiat).
- `Deckle.Composition` pourra être allégé après stabilisation du reste — noté.

## Questions encore ouvertes

**Q1 — Destination du UserControl `HudChrono.xaml(.cs)` après dissolution de `Deckle.Chrono.Hud`.** Trois options possibles : (a) retour dans `Deckle.Chrono` mais alors Chrono porte de l'UI WinUI 3 et perd sa pureté de timer headless, ce qui rend impossible son usage dans des contextes sans WinUI ; (b) intégration dans `Deckle.Hud` extrait au mouvement D, ce qui couple `Deckle.Hud` à `Deckle.Chrono` (couplage déjà existant en pratique, mais conceptuellement le HUD applicatif devient « connaisseur du chrono ») ; (c) garder un module séparé malgré le rejet de la scission par layer parce que c'est le seul cas où elle est utile. À trancher avant le mouvement F.

**Q2 — Renommage `Deckle.Whisp` → `Deckle.Transcription` ?** Whisp réfère implicitement à Whisper.cpp comme implémentation. Si demain le moteur change (Voxtral, autre), le nom n'a plus de sens. Renommer en `Deckle.Transcription` avec sous-module `Deckle.Transcription.Whisper` (consumer scission par consumer) serait plus stable. Décision à prendre — pas urgente, mais à noter pour la passe nomenclature.

**Q3 — Renommage / scission `Deckle.Llm` ?** Le module mélange aujourd'hui inférence LLM (généralisable) et réécriture (cas spécifique). Louis envisage des consumers futurs (recherche Anytype) qui réutiliseront l'inférence sans la réécriture. Pattern probable : `Deckle.Llm` (moteur inférence générique) + `Deckle.Llm.Rewrite` (consumer réécriture transcription) + futurs consumers. Quand procéder ? Pendant la passe modulaire ou plus tard ?

**Q4 — Mécanisme exact de la refonte Settings dynamique.** Design à faire : contrat des metadata, mécanisme de découverte (scan d'assemblies via réflexion, ou registration manuelle dans chaque module via une convention type `static void RegisterSettings(ISettingsHost host)` appelée par le shell, ou attribute), gestion des sous-modules avec dropdown, fallback si la page ne se résout pas, ordre via poids. Chantier dédié à planifier.

**Q5 — `LoggingSettings` vs `TelemetrySettings`.** Coexistence observée dans `Deckle.Logging` — soit duplication, soit scission saine. À clarifier avec le chantier refonte logging EventSource qui tourne en parallèle (probable que la refonte rationalise).

**Q6 — `Deckle.Composition` — quand alléger ?** Les factories `HudComposition` + `ProcessingVariant` sont spécifiques au HUD chrono. Une fois `Deckle.Hud` extrait (mouvement D), elles pourraient le rejoindre, laissant `Deckle.Composition` aux pures primitives. Décision à prendre après stabilisation des autres mouvements.

**Q7 — `UIAutomation` dans `Deckle.Core` vs déménagement vers `Deckle.Whisp`.** Aujourd'hui dans Core, consommateur unique Whisp.PasteFromClipboard. Si reste unique-consumer à six mois, déménager. Note de surveillance.

**Q8 — `GgufImport` dans `Deckle.Llm` — supprimer ?** Surface UI grosse, ajoutée tard, peu utilisée. Louis a évoqué la possibilité de la virer. À trancher dans le chantier Llm.

**Q9 — Stratégie pour le wizard multi-modules.** Quand le wizard devra orchestrer les contributions de plusieurs modules (Whisp + futur Ambient setup + etc.), comment chaque module déclare-t-il ses étapes ? Pattern similaire à la refonte Settings dynamique mais à concevoir séparément.

**Q10 — Génération dynamique du README de `src/`.** Idée notée par Louis. Mécanisme : scan des csproj, lecture du `CLAUDE.md` de chaque module pour extraire la description courte, génération d'une table de matières. Implémentation : MSBuild target, script PowerShell, source generator ? À explorer.

## Liens avec autres chantiers

**Refonte logging EventSource (session parallèle).** La refonte du module `Deckle.Logging` vers `System.Diagnostics.Tracing.EventSource` est en cours. Elle va probablement rationaliser la coexistence `LoggingSettings` / `TelemetrySettings` (Q5). Les deux chantiers doivent rester alignés sur la doctrine modulaire — toute évolution structurelle de `Deckle.Logging` respecte la famille 1 (fondation, pas d'UI, pas de page Settings) et son extension `Deckle.Logs` respecte la famille 3 (métier, viewer canonique).

**Refonte testing (mentionnée en passant).** Louis a évoqué l'ajout de modules de testing dans la grande refonte. Pas creusé dans cette session. À aborder dans une passe dédiée — le pattern probable est de standardiser un dossier `Tests/` ou un csproj `Deckle.<Module>.Tests` par module métier, mais c'est à concevoir.

**Refonte commentaires en code (doctrine deckle-docs).** Le skill `deckle-docs` porte une doctrine sur les commentaires de code (pourquoi plutôt que quoi, vérité actuelle vérifiée, discipline des marqueurs, promotion vers journal). Pas un chantier modulaire mais une discipline transverse à respecter pendant tous les mouvements identifiés.

## Suite immédiate

Quand la session reprendra (fusionnée avec la session refonte logging EventSource ou en session dédiée), prochaines actions suggérées dans l'ordre :

1. Trancher les questions ouvertes les plus structurantes (Q1 destination de HudChrono, Q2 / Q3 renames modules métier).
2. Spécifier le skill « conventions modulaires Deckle » (mouvement K) qui devient le référentiel pour la suite.
3. Planifier la séquence des mouvements A → J avec leur ordre réel et leur découpage en chantiers atomiques (chaque mouvement = une branche / un worktree).
4. Démarrer par les mouvements à coût faible et risque faible (A, B, I) pour valider le squelette, puis attaquer les extractions structurelles (C, D, E) une par une.
5. Refonte Settings dynamique (H) à planifier en chantier dédié — probablement après les extractions structurelles.
