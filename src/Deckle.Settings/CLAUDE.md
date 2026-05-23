# CLAUDE.md — Deckle.Settings

Shell UI Settings de l'app. Héberge la `SettingsWindow` (NavigationView Auto adaptatif + Frame de pages), les pages owned (`GeneralPage`, `RecordingPage`, `DiagnosticsPage`), les dialogs de consentement (corpus logging, paste opt-in, autorewrite rules), la racine de persistance (`SettingsService` pour les settings non-modulaires) et le registry de delegates `SettingsHost` que les modules métier consomment pour appeler des actions côté shell (theme broadcast, level window propagation, restart, accès parent-window pour les dialogs cross-module, ouverture du wizard first-run).

Les pages modulaires (`WhisperPage` dans `Deckle.Transcription`, `LlmPage` dans `Deckle.Llm.Rewrite`, et la future `AmbientPage` dans `Deckle.Lighting.Ambient`) ne vivent pas ici — elles sont possédées par leur module respectif et résolues via `Type.GetType(tag)` à partir du `Tag` du `NavigationViewItem` (par exemple `Tag="Deckle.Transcription.WhisperPage, Deckle.Transcription"`).

**Doctrine de modularité Settings**. La page Settings qui configure un domaine vit dans le module qui possède ce domaine, et son service de persistance aussi. C'est la règle pour toute nouvelle page Settings — elle naît dans le module du domaine, jamais dans ce shell. Le shell agrège dynamiquement, il n'héberge pas. `RecordingPage` et `DiagnosticsPage` sont aujourd'hui des résidus historiques encore portés ici ; leur migration vers `Deckle.Audio` et `Deckle.Diagnostics.Logging` est prévue sous le code-nom Move H (cf. [docs/reference/reference--cartographie-modules--1.1.md](../../docs/reference/reference--cartographie-modules--1.1.md)).

## TitleBar et backdrop

`Microsoft.UI.Xaml.Controls.TitleBar` natif (WindowsAppSDK 1.8), caption buttons **Standard** (`AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard`). Icône app via `ImageIconSource` nommé. `ExtendsContentIntoTitleBar=true` + `SetTitleBar(AppTitleBar)`. Couleurs des caption buttons posées manuellement par `UpdateCaptionButtonColors` avec re-binding sur `ActualThemeChanged` pour suivre le thème live (backgrounds transparents pour laisser passer la Mica, foreground adapté light/dark). Backdrop `MicaBackdrop`. `OverlappedPresenter` classique (min/max/resize). Resize initial 960×1440. `Closing → Cancel + Hide` — la fenêtre est réutilisée via le tray.

## NavigationView adaptatif — PaneDisplayMode Auto

Pas de code-behind custom pour les breakpoints. `PaneDisplayMode="Auto"` (défaut WinUI) gère seul la bascule entre les trois modes : **Left** ≥ 1008 dip, **LeftCompact** 641–1007, **LeftMinimal** ≤ 640. `PreferredMinimumWidth=320` sur le presenter expose le mode LeftMinimal. Slot `NavigationView.AutoSuggestBox` réservé pour la recherche live (à activer quand l'inventaire dépasse 15–20 paramètres visibles d'un coup, NN/G — pas implémentée à V1). Le handler `DisplayModeChanged` gère uniquement le padding du Frame : `+48 px top` en mode Minimal pour ne pas chevaucher le hamburger (pattern Windows Terminal Settings).

Contenu : `NavigationView.MenuItems` = General → Recording → Transcription → Rewriting → Diagnostics. `FooterMenuItems` = Logs (`SelectsOnInvoked=False`, clic via `ItemInvoked` qui délègue à `SettingsHost.OpenLogWindow` pour ouvrir la `LogWindow` partagée — Logs n'est pas une page nav, c'est une action). Avant le split du 2026-05-04 il n'y avait que 3 pages (General concentrait Recording et Diagnostics) ; la séparation a tiré General de 28 réglages / 7 sections vers 6 sections cohérentes et créé deux pages dédiées pour les surfaces fonctionnelles distinctes.

## Navigation Frame + Page

`<Frame x:Name="PageFrame" />` dans le slot content du NavigationView. Navigation via `Type.GetType(tag)` (pattern du sample officiel Microsoft Learn). Garde `CurrentSourcePageType != pageType` contre les re-Navigate redondants. Toutes les pages en `NavigationCacheMode.Required` pour préserver l'état entre visites. Guard `_initializing` autour des sync code-behind (combos, folder pickers) — empêche les écritures parasites pendant le `Load()` initial ; le flag se relâche en `DispatcherQueuePriority.Low` post-layout pour passer les TwoWay bindings qui appliquent leur valeur initiale après le ctor.

## Patterns non négociables

**Auto-save partout.** Aucune page Settings n'a de bouton Save ou Cancel. Chaque contrôle propage sa valeur au ViewModel à chaque changement, le ViewModel pousse au service correspondant qui sérialise immédiatement (debounce léger via `JsonSettingsStore`). Conséquence : pas de modèle « dirty », pas de prompt « unsaved changes » à la fermeture, pas de Cancel button. Cohérent avec le pattern Windows 11 Settings.

**SettingsCard et SettingsExpander.** Tous les contrôles de réglage sont enveloppés dans `SettingsCard` (toggle simple, slider, ComboBox) ou `SettingsExpander` (groupe de réglages liés ou liste éditable). Package NuGet `CommunityToolkit.WinUI.Controls.SettingsControls`. Ressource `SettingsCardSpacing=4` posée globalement, style `SettingsSectionHeaderTextBlockStyle` (`BodyStrongTextBlockStyle` + `Margin 1,30,0,6`), `StackPanel MaxWidth=1000` dans un `Grid` wrapper (workaround bug [microsoft-ui-xaml#3842](https://github.com/microsoft/microsoft-ui-xaml/issues/3842)). Aucun `StackPanel` ou `Grid` custom enveloppant un contrôle de réglage à la racine d'une page.

**Header H1 par page.** Chaque page commence par un `TextBlock` style `TitleLargeTextBlockStyle` qui annonce le nom de la section. Pas de header sticky au scroll, pas de sub-tab dans une page, pas de breadcrumb. La hiérarchie reste plate sur deux niveaux : `NavigationViewItem` → page.

## FolderPickerCard — pattern unique pour les chemins

`UserControl` réutilisé partout où un dossier est exposé (Backup location dans General, Telemetry storage dans Diagnostics, Models directory dans Whisper, variante éditable). Avant la refonte de mai 2026, trois implémentations divergentes coexistaient — text label « Set / Change folder / Pick a folder » selon le lieu, icônes ou pas, TextBox éditable ou pas.

Layout canonique : `TextBlock` read-only en `CaptionTextBlockStyle` qui affiche le path en pleine largeur sous la description (pas serré contre les boutons), boutons **Set** + **Open** à droite **en texte uniquement** (pas d'icônes — décision actée 2026-05-04), `IsTextSelectionEnabled=True` sur le path pour permettre le copier-coller manuel.

API picker : `Microsoft.Windows.Storage.Pickers.FolderPicker(WindowId)` — la nouvelle API qui prend un `WindowId` au constructeur, pas l'ancienne `Windows.Storage.Pickers.FolderPicker` UWP qui nécessite `WinRT.Interop.InitializeWithWindow` et casse en élévation. La résolution de la `Window` traverse `SettingsHost.GetSettingsWindow?.Invoke()` — le module ne référence pas la fenêtre directement.

Path resolution : la card lit `Path` (DependencyProperty TwoWay). Si vide, affiche `DefaultPath` en placeholder transparent — ne stocke pas le default dans le setting, préserve la sémantique « empty = système choisit le défaut ».

Variante éditable : `FolderPickerEditableCard` ajoute un `TextBox` éditable et un slot `RightContent` pour caser un bouton Reset. Utilisée uniquement pour Models directory. Cas réaliste : cloner un dossier de modèles depuis un autre poste et taper le chemin résultant.

Important : le card lui-même est un `UserControl` minimal, **pas** un wrapper de `SettingsCard`. C'est le consumer qui le pose dans un `<controls:SettingsCard ContentAlignment="Vertical">`. Ça permet de réutiliser le card dans `SettingsExpander.Items` qui rejette les UserControls wrappant eux-mêmes un SettingsCard.

## Pattern SettingsExpander parent pour groupes de sliders

Quand plusieurs sliders apparentés ont chacun 3–5 lignes de description (Decoding avec Temperature + Fallback step, Confidence avec Entropy + Logprob + No-speech), un layout horizontal serre le slider à `MinWidth=180` et tronque la description. Pattern retenu : `SettingsExpander` parent (header + icône) + sliders enfants en `SettingsCard ContentAlignment="Vertical"`. La description prend toute la largeur, le slider full-width en dessous. Staged disclosure (NN/G) : sliders cachés derrière l'expander, visibles seulement quand l'utilisateur les cherche. Glyphes par convention : `` (Tuner) pour Decoding, `` (gauge) pour Confidence. Children sans `HeaderIcon` — l'identité visuelle est portée par le parent. Une `InfoBar` qui dépend d'un slider du groupe (e.g. `TemperatureIncrementWarning`) reste **hors** expander pour rester visible quand le groupe est replié.

## GeneralPage

Niveau shell et configuration globale. Auto-save via `SettingsService`. Six sections dans l'ordre : **Hotkeys** (3 read-only display, principal `` Win + ` ``, primary rewrite `` Shift + Win + ` ``, secondary rewrite `` Ctrl + Win + ` ``), **Appearance** (ComboBox System / Light / Dark, appliqué live sur toutes les fenêtres via `SettingsHost.ApplyTheme`), **Behaviour** (auto-paste après transcription + overlay HUD master toggle / fade on proximity / animations / screen position — migré depuis Recording le 2026-05-04 parce que ces réglages décrivent ce que Deckle fait pour l'utilisateur, pas le pipeline de capture), **Startup** (autostart HKCU géré par `AutostartService`, hors `AppSettings` + warmup on launch), **Backup & restore** (`SettingsExpander` PowerToys-style avec `SettingsBackupService` snapshot ponctuel `settings-YYYYMMDD-HHmmss.json` sous `<ConfigDirectory>/backups/`, restore via swap atomique, `BackupDirectory` configurable via `FolderPickerCard` pour pointer vers OneDrive/Drive), **Application data** (data folder display + Open in Explorer + Re-run setup). `HyperlinkButton` Reset par section (Appearance, Behaviour, Startup) — pattern Win11 Settings restore les défauts de la section seule, pas toute la page.

## RecordingPage

Page extraite de General le 2026-05-04. Concentre tout ce qui relève strictement du pipeline de capture audio. **Microphone** : `ComboBox` Audio input device, énumération `waveIn` Win32, `AudioInputDeviceId` (`-1 = WAVE_MAPPER`) avec « System default » en index 0. **Voice level window** : `SettingsExpander` master (Auto-calibration toggle dans header) + 3 sliders enfants (Floor `MinDbfs`, Ceiling `MaxDbfs`, Curve exponent). Les drags poussent live dans `AudioLevelMapper` via `SettingsHost.ApplyLevelWindow` — le HUD reflète la nouvelle courbe à la sub-window suivante sans restart. Persistance : `CaptureSettingsService` (`capture.json`), séparé du shell depuis la slice C2b.

## DiagnosticsPage

Page extraite de General le 2026-05-04. Vocabulaire interne : *log* désigne le temps réel (LogWindow), *telemetry* désigne ce qui est persisté sur disque (JSONL). « Diagnostics » est l'ombrelle. Structurée pour accueillir des sections futures (settings de log temps réel : niveaux, filtrage, capacité du buffer LogWindow) ; à ce jour une seule section Telemetry.

Telemetry, 5 opt-ins tous off par défaut, dans l'ordre : **Application log to disk** (toggle, persiste l'événementiel dans `app.jsonl` ; en haut de section par décision design), **Microphone telemetry** (toggle + consent dialog privacy pour le résumé RMS par recording, glyph microphone), **Latency telemetry** (toggle, mesures de pipeline par run), **Corpus** (`SettingsExpander` master Corpus toggle dans header + consent, Audio corpus enfant avec toggle + consent séparé), **Storage folder** (`FolderPickerCard` pointant vers le dossier où sont sérialisés les `.jsonl`).

Pattern consent dialog : re-entry guards (`_suppressMicrophoneToggled`, etc.) — un revert programmatique post-Cancel ne re-ouvre pas le dialog. Persistance : `TelemetrySettingsService` (`telemetry.json`).

## SettingsHost — registry de delegates côté shell

`SettingsHost` est une classe statique de delegates que l'app branche au boot et que les pages Settings invoquent pour les actions côté shell. Le pattern évite à `Deckle.Settings` d'avoir une référence sur le projet hôte tout en permettant à n'importe quelle page Settings d'appeler `ApplyTheme(string theme)`, `ApplyLevelWindow(LevelWindow lw)`, `RestartApp()`, `GetSettingsWindow()` (pour passer un `WindowId` au FolderPicker ou un hwnd parent aux dialogs), `OpenSetupWizard()`, `OpenLogWindow()`. L'app pose ces hooks dans `App.OnLaunched` avant la première instanciation de fenêtre Settings.

C'est un pattern intentionnel — le registry n'est pas un Service Locator déguisé. Les delegates sont nominaux (un par capability), pas un dictionnaire de strings. Ajouter un hook signifie ajouter un champ statique typé sur `SettingsHost`, à brancher au boot, à appeler explicitement depuis la page qui en a besoin via `SettingsHost.X?.Invoke(...)` — null-safe quand le shell n'a pas wiré (test isolé du module, intégration partielle).

## Persistance per-module

Chaque module possède son fichier de settings sous `<UserDataRoot>/modules/<moduleId>/settings.json`. Les services concernés sont `SettingsService` (shell, non-modulaire), `CaptureSettingsService` dans `Deckle.Audio`, `TelemetrySettingsService` (Diagnostics), `TranscriptionSettingsService` dans `Deckle.Transcription`, `LlmSettingsService` dans `Deckle.Llm.Rewrite`, et la future `AmbientSettingsService` dans `Deckle.Lighting.Ambient`. Chaque service expose `Current` (POCO singleton), `Save()` (debounced ~300 ms), et un event `Changed`. Atomic write-then-swap.

| Service | Fichier | Contenu |
|---|---|---|
| `SettingsService` | `settings.json` | Shell : Hotkeys, Theme, Behaviour (auto-paste + overlay), Startup, `Paths.BackupDirectory` |
| `CaptureSettingsService` | `modules/audio/settings.json` | Microphone, Voice level window |
| `TelemetrySettingsService` | `modules/telemetry/settings.json` | Diagnostics opt-ins + storage path |
| `TranscriptionSettingsService` | `modules/transcription/settings.json` | Transcription orchestrator + active backend settings |
| `LlmSettingsService` | `modules/llm/settings.json` | Ollama + profiles + auto-rewrite rules + shortcuts |

La migration de l'ancien fichier combiné vers le layout per-module vit dans `SettingsBootstrap.MigrateLegacyToPerModule()`. Cette méthode tourne en tout premier dans `App.OnLaunched`, avant qu'un service ne touche son fichier — sinon le service écrirait des defaults et la migration verrait une cible déjà existante. Elle gère aussi le renommage de la section JSON `recording → capture` (héritage 2026-05-02), le dispatch de la clé JSON `capture` vers le module id `audio` (2026-05-15 rename), et la migration de dossier `modules/capture/ → modules/audio/` pour les utilisateurs déjà passés en per-module. Toute future migration de module suit ce pattern via `MigrateModuleFolder` et l'ajustement du dispatch.

## Restart cible

`SettingsHost.RestartApp?.Invoke(pageTag?)` relance l'exe avec `--settings [pageTag]` ; `OnLaunched` détecte le flag et rouvre Settings sur la page nommée. Utilisé par les pages qui exigent un restart d'engine pour appliquer un changement (typiquement Whisper Model et UseGpu — `MarkRestartPending()` côté ViewModel pousse une `InfoBar` « Restart required » + footer avec boutons Restart now / Discard).
