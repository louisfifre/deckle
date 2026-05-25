# CLAUDE.md — Deckle.App (app hôte)

L'app hôte WinUI 3 unpackaged qui rassemble les modules `Deckle.*`. Point d'entrée unique du projet côté UI. La responsabilité de ce module se limite à la composition : lifecycle de l'app, fenêtres longue vie (HUD, LogWindow, SettingsWindow, PlaygroundWindow), tray system, hotkeys globaux, branchement des modules métier via leurs interfaces de host. Aucune logique métier n'est censée vivre ici en dehors des handlers d'événements et des adapters de bridge — quand on en ajoute, c'est presque toujours un signal qu'elle aurait dû atterrir dans un module spécifique.

Avant tout test runtime, tuer toute instance déjà en cours (Deckle ou prototype antérieur). Deux processus qui appellent `RegisterHotKey` sur la même combinaison se collisionnent avec `err 1409`.

## Build

Le build se fait via `dotnet build`. Depuis `src/Deckle.App/`, PowerShell sans admin :

```
dotnet build -c Release -p:Platform=x64
```

Sortie : `bin\x64\Release\net10.0-windows10.0.26100.0\Deckle.exe` (self-contained). Le restore est implicite (phase séparée avant Build), pas besoin de target Restore explicite. Le contournement historique via `MSBuild.exe` de Visual Studio est conservé en mémoire dans [ADR-0012](../../docs/adr/0012-adoption-de-dotnet-build-et-dotnet-test.md) — réactivable si le bug `XamlCompiler.exe` MSB3073 réapparaît un jour.

Points de vigilance côté csproj. `Microsoft.WindowsAppSDK` est épinglé à `1.8.260317003` (stable officielle). `global.json` épingle SDK `10.0.104` — à conserver. `<EnableMsixTooling>true</EnableMsixTooling>` force le pipeline Publish à générer `Deckle.pri` dans `PublishDir` ; sans ça, en WindowsAppSDK 1.8 unpackaged, les `.xbf` embarqués dans le `.pri` sont injoignables et l'app démarre sans fenêtre (voir [microsoft/WindowsAppSDK#3451](https://github.com/microsoft/WindowsAppSDK/issues/3451)).

Les scripts d'orchestration vivent sous `scripts/`. Le menu interactif `scripts/deckle.ps1` est le point d'entrée quotidien ; les scripts feuilles vivent sous `scripts/lib/` et restent invoquables en CLI direct. `scripts/lib/build-run.ps1` tue Deckle s'il tourne, build via `dotnet build`, lance l'exe — switches `-NoRun`, `-Wait`, `-Configuration`, `-Target`, `-Pick`, `-NoAutoRestart`.

## Pièges WinUI 3 transverses

Ces pièges concernent tout le code WinUI 3 de l'app, pas seulement le module hôte. Ils sont consignés ici parce que c'est ici que la passe d'instrumentation initiale les a tous capturés, mais ils s'appliquent dès qu'un autre module touche au XAML ou aux fenêtres WinUI 3.

`AllowUnsafeBlocks` est obligatoire dans tout csproj qui utilise `LibraryImport`. Sans cette propriété, le compilateur émet `SYSLIB1062` ou `CS0227`.

`UseWindowsForms` est interdit dans tout csproj WinUI 3. Le mix WinUI 3 + Windows Forms casse la résolution XAML.

`Window` n'expose pas de `Resources` directement en WinUI 3. Les ressources XAML se déclarent sur le `Grid` racine via `<Grid.Resources>`, pas sur `<Window.Resources>` (erreur de compilation `WMC0011`).

Tout objet UI WinUI 3 vit uniquement sur le thread UI, y compris `SolidColorBrush`. Tout objet UI instancié depuis un thread de fond lève `COMException` (`RPC_E_WRONG_THREAD`). Le pattern à appliquer : créer les brushes et objets UI dans le constructeur de la `Window` et les réutiliser dans les handlers venant des threads Record ou Transcribe.

Les caption buttons Tall ne se déclenchent pas avec `ExtendsContentIntoTitleBar=true` seul. Il faut ajouter explicitement `AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall`. La règle vaut aussi avec le contrôle `Microsoft.UI.Xaml.Controls.TitleBar` natif.

Le délégué `SubclassProc` Win32 doit être un champ d'instance, jamais une lambda locale (sinon le GC le collecte et le subclass crash). Le pattern est en place dans `MessageOnlyHost`.

`Microsoft.UI.Xaml.Controls.ItemsRepeater` n'est pas un `ItemsControl` au sens classique UWP/WPF — il ne propage pas le `DataContext` aux descendants de son `DataTemplate`. Conséquence : un `ComboBox` (ou tout control) inscrit dans le template a `DataContext == null` au `Loaded` et à tous les events, même si `x:Bind` dans le template résout correctement contre l'item implicite. Décision design assumée côté ItemsRepeater pour perf et virtualisation (cf. [microsoft-ui-xaml#7726](https://github.com/microsoft/microsoft-ui-xaml/issues/7726)). Pattern correct : `Tag="{x:Bind}"` sur le control pour capturer la référence VM à l'inflation du template, et `combo.Tag is MyViewModel vm` côté handler. Remonter le visual tree via `VisualTreeHelper.GetParent` à la recherche d'un parent avec `DataContext` est fragile et casse à la moindre refactor du template.

Le lifetime des fenêtres WinUI 3 dans Deckle est piloté par `Closing→Cancel`. Toutes les Windows (HUD, LogWindow, SettingsWindow, PlaygroundWindow) bloquent leur fermeture. La sortie unique est le menu Quitter du tray, qui appelle `QuitApp()` (libère tray, message host, engine puis `Environment.Exit(0)`). Conséquence : la `LogWindow` jamais affichée n'a pas de layout initialisé — `LogScrollViewer.UpdateLayout()` ne peut être appelé qu'après que la fenêtre a été montrée au moins une fois (drapeau `_isVisible` en place).

Le tray et les hotkeys globaux ne peuvent pas être hébergés par une `Microsoft.UI.Xaml.Window` : le sous-classage Win32 nécessaire (`SetWindowSubclass`) est incompatible. La solution canonique est une message-only window Win32 (`MessageOnlyHost`, parent `HWND_MESSAGE`) créée dans `App.OnLaunched`. Invisible par construction — pas de flash possible, pas de trick off-screen. `TrayIconManager.Register(hwnd)` et `HotkeyManager` s'attachent dessus.

## LogWindow

`LogWindow.xaml(.cs)` est la fenêtre live de visualisation des événements EventSource. `OverlappedPresenter` redimensionnable, min 400×300, `MicaBackdrop`, theme système (light/dark auto, pas de `RequestedTheme` forcé). Close → Cancel + Hide comme toutes les Windows longue vie.

TitleBar natif `Microsoft.UI.Xaml.Controls.TitleBar` (WindowsAppSDK 1.8) avec caption buttons **Tall** (`AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall`). L'icône app vit dans `TitleBar.IconSource` : `ImageIconSource` reconstruit complètement à chaque bascule idle/recording (muter `ImageSource` in-place ne propage pas visuellement). `AppWindow.SetIcon` suit le même état. L'`AutoSuggestBox` de recherche live est placée dans `TitleBar.Content` — pattern Win11 Settings. En dessous de 520 DIPs de largeur, la SearchBox bascule en bouton icône-only (`TitleBar.Content` swap), pattern Win11 Task Manager.

Sous la TitleBar deux zones. À **gauche** une `SelectorBar` All / Activity / Alerts (sélection initiale All — tout passe). À **droite** une `CommandBar` avec `IsDynamicOverflowEnabled` et `DynamicOverflowOrder` : groupe Copy/Save/Clear migre en overflow avant le groupe AutoScroll/Wrap. Glyphs Segoe Fluent : Copy `E8C8`, Save `E74E`, Clear `E74D`, AutoScroll `EC8F` (toggle, on par défaut), Wrap `E751`.

Deux collections en mémoire. `_entries` (`List<LogEntry>`) est le tampon complet, cap 5000 entrées — sur overflow on retire la plus vieille des deux collections par ref equality (`LogEntry` est une classe). `_visible` (`ObservableCollection<LogEntry>`) est la projection bindée au `ListView.ItemsSource`. Filtre = `Matches()` qui combine SelectorBar + recherche live (`IndexOf` case-insensitive, debounce 200 ms pour ne pas bloquer le UI thread sur frappe rapide). Copy/Save opèrent sur `_visible` — l'utilisateur copie ce qu'il voit.

Le modèle de données wrappe `EventEntry` produit par le listener `Deckle.Diagnostics`. Le niveau est `EventLevel` natif (Critical / Error / Warning / Informational / Verbose) ; il n'y a plus de niveaux applicatifs Success / Narrative — l'ère LogService est révolue. Le mapping `Provider` → label source court (`"Deckle.Whisp"` → `"WHISP"`, `"Deckle"` → `"APP"`) vit dans `LogEntry.MapSource` ; il est appliqué une fois à la construction et précomputé dans `Text` (format `HH:mm:ss.fff [SOURCE] message`) pour éviter le reformatage à chaque realization de ligne lors de la virtualisation. Les couleurs sont bindées via `ThemeResource` dans les `DataTemplates` (`Grid.Resources > ThemeDictionaries`), theme switch runtime automatique.

`LogLevelTemplateSelector` (classe C# qui hérite `DataTemplateSelector`) route les templates par `EventName` pour les rows télémétrie spécialisées (Latency / Corpus / Microphone) et par `EventLevel` pour le reste. Le toggle Wrap swap `ItemTemplate` entre `NoWrapRoot` et `WrapRoot`. Piège WinUI 3 : `ItemsControl.ItemTemplateSelector` n'est pas honoré à l'exécution (seul `ListViewBase` le respecte). Le contournement est en place : `ItemTemplate` pointe sur un `ContentControl` wrapper dont le `ContentTemplateSelector` est le bon selector.

Le toggle Wrap bascule aussi `HorizontalScrollBarVisibility` entre `Auto` et `Disabled`. Sans ça, `TextWrapping="Wrap"` ne s'applique pas — le `ScrollViewer` mesure son contenu en largeur infinie tant que le scroll horizontal est autorisé. **Shift+molette = comportement natif WinUI 3 assumé** : le `ScrollViewer` interne du `ListView` scrolle verticalement, pas horizontalement, parce que WinUI 3 n'expose pas de routing Tunnel/Preview pour intercepter `PointerWheelChanged` avant que le SV interne ne le consomme. Toute tentative custom (re-injection horizontale via `ChangeView`, baseline sync via `ViewChanged`) produit un effet visuel saccadé/inversé à chaque cran de molette — pire qu'un simple comportement natif. Pour parcourir une longue ligne sans wrap, utiliser la scrollbar horizontale, ou activer le toggle Wrap.

Padding bas `12,4,12,24` sur le ListView. Les 24 px de marge basse évitent que la scrollbar horizontale flottante (~12 px) recouvre la dernière entrée, qui est précisément l'endroit où les nouvelles lignes apparaissent quand AutoScroll est on.

La `LogWindow` jamais affichée n'a pas de layout initialisé — `LogScrollViewer.UpdateLayout()` ne peut être appelé qu'après que la fenêtre a été montrée au moins une fois (drapeau `_isVisible` en place). Pattern lazy windows acté par [ADR-0004](../../docs/adr/0004-lazy-windows-pour-stabilite-au-boot.md).

## HudWindow — usage côté hôte

La classe `HudWindow` vit désormais dans `Deckle.Hud` (extraite du hôte en cartographie cleanup). Le hôte instancie le singleton une fois dans `OnLaunched` et ne le détruit jamais. Les handlers UI sont marshalés via `DispatcherQueue.TryEnqueue` car les events `TranscriptionEngine` viennent de threads de fond. Détail interne de la fenêtre : `Window` WinUI 3 d'environ 320×64, positionnée bas-centre via `DisplayArea.Primary.WorkArea`, en `OverlappedPresenter` non resizable, avec `ExtendsContentIntoTitleBar=true`.

Pour afficher la HUD, la séquence est `MoveAndResize` puis `ShowWindow(SW_SHOWNOACTIVATE)` suivi de `SetWindowPos(HWND_TOP, SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE)`. Jamais `SetForegroundWindow` — la HUD ne doit pas voler le focus. Pour la masquer, `ShowWindow(SW_HIDE)`. Les détails (coloration progressive du chrono, fade proximité souris via Raw Input et alpha layered avec smoothstep, contrainte d'ombre layered, régressions de notification) vivent dans [src/Deckle.Hud/CLAUDE.md](../Deckle.Hud/CLAUDE.md).

## Lifetime — `App.xaml.cs`

L'ordre de `OnLaunched` est sensible parce qu'il croise plusieurs invariants : la migration de settings doit tourner avant qu'un service ne touche son fichier, le `MessageOnlyHost` doit exister avant l'enregistrement des hotkeys, le tray doit avoir ses callbacks branchés avant son `Register`. La séquence canonique est : migration `SettingsBootstrap.MigrateLegacyToPerModule()` en premier, puis registration de `TelemetryGates.Configure` et des sinks de logging, puis first-run gate (wizard si les natives ou modèles manquent), puis instanciation de `TranscriptionEngine`, puis création des fenêtres longue vie (HUD prime, LogWindow et SettingsWindow et PlaygroundWindow restent lazy), puis création du `TrayIconManager` (callbacks seulement, pas encore `Register`), puis branchement des events engine → tray + windows, puis création du `MessageOnlyHost`, puis `tray.Register(messageHost.Hwnd)` et `hotkeyManager.Register()`, puis application du theme persisté et du level window de calibration, puis ouverture conditionnelle de Settings si `--settings` est passé en CLI.

Trois filets de diagnostic globaux sont posés dans le constructeur de `App` : `Application.UnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`. Tous les trois routent vers `LogService` avec `LogSource.Crash` et un préfixe distinct (`CRASH`, `CRASH-AD`, `CRASH-TS`). Sans ces filets, une exception qui surgit dans un handler `TranscriptionEngine` peut disparaître silencieusement — le pattern existait avant l'unification télémétrie et reste en place pour les cas où le sink principal n'est pas encore inscrit (boot précoce).
