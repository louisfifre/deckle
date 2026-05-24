# CLAUDE.md — Deckle.Shell.TrayMenu

Module qui rend le menu contextuel du tray Deckle au pattern WinUI 3 natif. Il existe en sibling de [Deckle.Shell](../Deckle.Shell/CLAUDE.md) pour préserver l'invariant Win32-pur du shell : Shell continue de ne porter que des primitives système (Shell_NotifyIcon, hotkeys, autostart, message-only host) sans aucune dépendance XAML, et la couche WinUI 3 du menu vit ici, isolée. `Deckle.App` référence les deux et câble la jonction dans `OnLaunched`.

## Pattern SecondWindow

Un `MenuFlyout` WinUI 3 ne peut pas être ancré à un HWND message-only ou à une icône Shell_NotifyIcon — il lui faut un `XamlRoot`, donc une `Window` WinUI. Le pattern, inspiré de la librairie open source [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) (MIT) sans en prendre la dépendance, consiste à pré-créer une `Window` WinUI 3 transparente porteuse du flyout. Sur clic droit du tray, cette fenêtre est positionnée via l'API Win32 `CalculatePopupWindowPosition`, rendue visible, et le `MenuFlyout` est ouvert dessus en `FlyoutShowMode.Transient`. La fenêtre porteuse elle-même reste invisible — c'est le popup interne de WinUI qui peint le menu, avec son mica, ses coins arrondis et son ombre Fluent automatiques. La même `Window` / `Frame` / `MenuFlyout` est réutilisée à chaque clic droit ; entre deux ouvertures, la fenêtre est cachée par `SW_HIDE`.

## Anchoring — rect de l'icône, pas le curseur

L'ancrage du menu se fait à partir du **rect réel de l'icône tray** (`Shell_NotifyIconGetRect`), pas de la position du curseur. La raison : un calcul ancré au curseur avec un rect d'exclusion arbitraire (typiquement 36×36 px autour du clic) donne une position qui dépend du point exact où l'utilisateur a cliqué sur l'icône — `CalculatePopupWindowPosition` peut alors basculer entre « au-dessus » et « en bas » de façon erratique selon les marges restantes après exclude. En passant le rect réel de l'icône comme exclude, l'API place le popup tangent à l'icône, ce qui rend la position automatiquement correcte quelle que soit l'orientation de la taskbar (en bas → menu au-dessus, à gauche → menu à droite, à droite → menu à gauche, en haut → menu en dessous). C'est le pattern canonique Windows utilisé par les apps natives qui hébergent un menu tray. Le rect de l'icône est exposé par `TrayIconManager.GetIconRect()` côté [Deckle.Shell](../Deckle.Shell/CLAUDE.md), branché ici via la property `GetIconRect`. Fallback sur la position du curseur avec exclude 36×36 px si l'API shell ne sait pas localiser l'icône (icône encore non enregistrée, repliée dans l'overflow caché, ou explorer.exe en cours de restart).

## Invisibilité de la fenêtre porteuse

La transparence est obtenue via `WS_EX_LAYERED` posé par `SetWindowLongPtr` puis `SetLayeredWindowAttributes` avec `alpha=0`. Pas de `WS_EX_TRANSPARENT` — la fenêtre doit recevoir le focus pour que le dismiss au click-outside fonctionne (le mécanisme repose sur `Window.Activated → Deactivated`). Pas non plus de color key noir ni de subclass `WM_ERASEBKGND` (que H.NotifyIcon applique en double sécurité) ; alpha=0 seul suffit dans le contexte unpackaged Deckle. Si un scintillement apparaît un jour à l'ouverture, le fallback documenté est d'ajouter `LWA_COLORKEY` avec une couleur noire et un subclass qui peint le background en noir.

## Style fenêtre — WS_POPUPWINDOW post-Loaded

Le `OverlappedPresenter` configuré avec `SetBorderAndTitleBar(false, false)` ne suffit pas — un résidu de `WS_CAPTION` reste sur le HWND et interfère avec le rendu DWM (coins arrondis, absence de bordure). La correction se fait dans `Frame.Loaded` en réécrivant complètement `GWL_STYLE` à `WS_POPUPWINDOW` (= `WS_POPUP | WS_BORDER | WS_SYSMENU`) suivi d'un `SetWindowPos(..., SWP_FRAMECHANGED)` qui force DWM à recalculer la non-client area. Cette correction doit attendre `Loaded` parce qu'elle nécessite que le HWND ait été pleinement initialisé par WinUI.

## Prime measure — workaround microsoft-ui-xaml#7374

La première mesure d'un `MenuFlyoutItem` après instanciation renvoie une valeur instable ([microsoft-ui-xaml#7374](https://github.com/microsoft/microsoft-ui-xaml/issues/7374)). Le contournement transmis depuis H.NotifyIcon est d'amorcer le visual tree par un cycle `flyout.ShowAt(...) + flyout.Hide()` invisible dans `Frame.Loaded` (fenêtre porteuse à `alpha=0` à ce stade, donc imperceptible). Tentative passée d'aller plus loin en ré-amorçant à chaque `Show()` : confirmée nuisible — le `ShowAt+Hide` répété détache les items du visual tree au lieu de les amorcer, `DesiredSize` retombe à `(0, 0)` sur 100 % des ouvertures, et le popup s'affiche en 10×10 px. La doctrine est donc « amorcer une seule fois dans `Frame.Loaded`, jamais re-amorcer à chaque ouverture ». Un bug résiduel de mesure intermittente subsiste à la première ouverture après inactivité prolongée (premier clic donne `size=10x10`, les suivants donnent la mesure correcte) — à traiter par une approche distincte, probablement mesure du `MenuFlyoutPresenter` complet plutôt que somme des items individuels.

La mesure laisse le rendu natif WinUI 3 Win11 décider de la hauteur et du padding des items — pas de hardcode. Une tentative passée de forcer `Height=32` et `Padding=(11,0,11,0)` réduisait seulement le `ContentRoot` interne, mais le `MenuFlyoutPresenter` continuait d'allouer la hauteur native par cellule : le hover ne couvrait qu'une partie de la cellule et le texte n'était pas centré dans l'espace alloué. Avec le rendu natif, hover et texte tombent juste, et le menu colle au comportement de la WinUI 3 Gallery pour ce type de surface. Pas de `MinWidth` forcé non plus — la largeur naturelle, déterminée par le libellé le plus long, garde le menu compact et évite que les libellés courts (« Logs », « Quit ») soient centrés dans une cellule trop large. Marge interne de 4 px chaque côté pour couvrir la card de fond du `MenuFlyout`.

## Ambient Light — ToggleSwitch visuel via ControlTemplate custom

L'item Ambient Light est un `ToggleMenuFlyoutItem` natif, mais stylé via `ToggleSwitchMenuItemStyle` (défini dans `Themes/TrayMenu.xaml`, mergé dans `App.xaml` au boot). Le Style remplace le `ControlTemplate` par défaut (checkmark canonique à gauche) par un Grid à deux colonnes : libellé à gauche, `ToggleSwitch` Win11 natif à droite. Le `ToggleSwitch` est `IsHitTestVisible=False` (indicateur visuel pur, pas interactif) avec `IsOn={TemplateBinding IsChecked}` — l'interaction passe par le `Click` de l'item parent qui flippe `IsChecked` nativement, le `ToggleSwitch` reflète l'état one-way.

Pourquoi un `ControlTemplate` complet plutôt qu'un `Content` custom : `MenuFlyoutItem` n'hérite pas de `ContentControl` et n'a pas de propriété `Content`. Sa slot rigide ne permet d'injecter qu'un `IconElement` (typé) ou un `Text` (string). Pour intégrer un `ToggleSwitch`, il faut réécrire le template entier. La doctrine `deckle-workflow` « primitive native d'abord » ne s'applique pas littéralement ici parce que le composé tray-menu-avec-toggle-droit n'est pas un primitive Win11 (les menus contextuels Win11 utilisent le checkmark canonique) ; le choix est une dérogation assumée pour rendre l'état perceptible d'un coup d'œil dans un menu visité fréquemment.

Le Style maintient les `VisualStates` `Normal` / `PointerOver` / `Pressed` / `Disabled` mappés sur les theme resources `MenuFlyoutItemBackground*` et `MenuFlyoutItemForeground*` natives — light/dark mode et accent system gérés gratuitement. `MinHeight=32` aligne sur le gabarit canonique Win11. `Padding=11,0,11,0` reprend le padding par défaut du ToggleMenuFlyoutItem natif. `ToggleSwitch` configuré avec `OnContent=OffContent=""` et `MinWidth=0` pour éviter que la pillule (MinWidth 154 par défaut, prévu pour porter les labels On/Off) ne gonfle inutilement la cellule.

## Placement du popup — FlyoutPlacementMode.Full

`MenuFlyout.ShowAt(_frame, ...)` ouvre le popup interne WinUI selon un `Placement` que l'on doit spécifier explicitement à `Full`. Le mode `Auto` par défaut place le popup **adjacent** au target (typiquement au-dessus du frame), ce qui double l'offset déjà calculé par `CalculatePopupWindowPosition` — le menu apparaît alors décalé d'environ une hauteur de menu vers le haut par rapport à la position voulue. La fenêtre porteuse étant déjà positionnée à la coordonnée exacte calculée par l'API Win32, `Full` est le seul mode qui colle : il dit au popup « apparais à l'emplacement exact du target », sans ajouter d'offset.

Le scaling vers les pixels physiques se fait via `GetDpiForMonitor(MonitorFromPoint(curseur))`, pas via `XamlRoot.RasterizationScale`. La raison : le `XamlRoot` du frame retourne le DPI du moniteur où la fenêtre porteuse est cachée (typiquement primaire, où elle a atterri au boot), pas où le tray est cliqué. En multi-monitor ou quand l'écran primaire est à 150 % et le secondaire à 100 % (ou inversement), `RasterizationScale` donne le mauvais facteur et le popup atterrit à une position décalée. `MonitorFromPoint(curseur)` capture le bon moniteur sans ambiguïté.

## Animations désactivées

`AreOpenCloseAnimationsEnabled = false` sur le `MenuFlyout`. La raison est documentée par H.NotifyIcon : avec les animations actives, masquer la fenêtre porteuse pendant la transition de fermeture du flyout coupe l'animation, ce qui force un hack de ré-ouverture pendant la fermeture. Les couper évite le hack entièrement, et le tempo d'apparition instantanée colle exactement à `TrackPopupMenu` natif. Si un jour Louis souhaite réactiver les animations, le ré-ouverture-pendant-fermeture devra être restauré.

## Lifecycle et dismiss

La `Window`, le `Frame`, le `MenuFlyout` et l'`AppWindow` sont construits une seule fois dans le constructeur et réutilisés à chaque `Show()`. Le `Dispose()` ferme la fenêtre et débranche les handlers — appelé par `App.OnExit`.

Trois vecteurs de dismiss convergent vers le même chemin. `Window.Activated → Deactivated` couvre le click-outside et toute perte de focus globale. `MenuFlyout.Closed` couvre la fermeture déclenchée par WinUI lui-même (Escape, etc.). Le `Click` de chaque item appelle explicitement le `Hide()` avant d'invoquer la callback. Le chemin `Hide()` met à jour le drapeau d'état, ferme le flyout et masque la fenêtre par `SW_HIDE`. Le drapeau évite les doubles dismiss.

## Pièges WinUI 3 transverses applicables

Tous les pièges WinUI 3 documentés dans [Deckle.App/CLAUDE.md](../Deckle.App/CLAUDE.md) s'appliquent ici, notamment : tout objet UI (y compris les items du menu) doit être créé sur le thread UI ; le délégué `SubclassProc` Win32 doit vivre dans un champ d'instance s'il était introduit (ce module n'en a pas pour l'instant) ; `AllowUnsafeBlocks` reste obligatoire pour les `LibraryImport` (et est posé dans le csproj du module).

## Pointeurs

- [src/Deckle.Shell/CLAUDE.md](../Deckle.Shell/CLAUDE.md) — module sibling qui porte le tray Win32 (`TrayIconManager`, `Shell_NotifyIcon`, subclass `WM_TRAY`). `TrayContextMenuHost` consomme son event `RightClickRequested`.
- [src/Deckle.App/CLAUDE.md](../Deckle.App/CLAUDE.md) — séquence d'instanciation et de wiring dans `App.OnLaunched`, ordre des branchements (tray callbacks → TrayContextMenuHost → tray.Register).
- [H.NotifyIcon repo](https://github.com/HavenDV/H.NotifyIcon) — référence source du pattern SecondWindow. Inspiration uniquement, pas de dépendance.
