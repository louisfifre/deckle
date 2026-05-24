# CLAUDE.md — Deckle.Shell.TrayMenu

Module qui rend le menu contextuel du tray Deckle au pattern WinUI 3 natif. Il existe en sibling de [Deckle.Shell](../Deckle.Shell/CLAUDE.md) pour préserver l'invariant Win32-pur du shell : Shell continue de ne porter que des primitives système (Shell_NotifyIcon, hotkeys, autostart, message-only host) sans aucune dépendance XAML, et la couche WinUI 3 du menu vit ici, isolée. `Deckle.App` référence les deux et câble la jonction dans `OnLaunched`.

## Pattern SecondWindow

Un `MenuFlyout` WinUI 3 ne peut pas être ancré à un HWND message-only ou à une icône Shell_NotifyIcon — il lui faut un `XamlRoot`, donc une `Window` WinUI. Le pattern, inspiré de la librairie open source [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) (MIT) sans en prendre la dépendance, consiste à pré-créer une `Window` WinUI 3 transparente porteuse du flyout. Sur clic droit du tray, cette fenêtre est positionnée au curseur via l'API Win32 `CalculatePopupWindowPosition` (avec un rect d'exclusion autour du curseur pour éviter le recouvrement de l'icône tray), rendue visible, et le `MenuFlyout` est ouvert dessus en `FlyoutShowMode.Transient`. La fenêtre porteuse elle-même reste invisible — c'est le popup interne de WinUI qui peint le menu, avec son mica, ses coins arrondis et son ombre Fluent automatiques. La même `Window` / `Frame` / `MenuFlyout` est réutilisée à chaque clic droit ; entre deux ouvertures, la fenêtre est cachée par `SW_HIDE`.

## Invisibilité de la fenêtre porteuse

La transparence est obtenue via `WS_EX_LAYERED` posé par `SetWindowLongPtr` puis `SetLayeredWindowAttributes` avec `alpha=0`. Pas de `WS_EX_TRANSPARENT` — la fenêtre doit recevoir le focus pour que le dismiss au click-outside fonctionne (le mécanisme repose sur `Window.Activated → Deactivated`). Pas non plus de color key noir ni de subclass `WM_ERASEBKGND` (que H.NotifyIcon applique en double sécurité) ; alpha=0 seul suffit dans le contexte unpackaged Deckle. Si un scintillement apparaît un jour à l'ouverture, le fallback documenté est d'ajouter `LWA_COLORKEY` avec une couleur noire et un subclass qui peint le background en noir.

## Style fenêtre — WS_POPUPWINDOW post-Loaded

Le `OverlappedPresenter` configuré avec `SetBorderAndTitleBar(false, false)` ne suffit pas — un résidu de `WS_CAPTION` reste sur le HWND et interfère avec le rendu DWM (coins arrondis, absence de bordure). La correction se fait dans `Frame.Loaded` en réécrivant complètement `GWL_STYLE` à `WS_POPUPWINDOW` (= `WS_POPUP | WS_BORDER | WS_SYSMENU`) suivi d'un `SetWindowPos(..., SWP_FRAMECHANGED)` qui force DWM à recalculer la non-client area. Cette correction doit attendre `Loaded` parce qu'elle nécessite que le HWND ait été pleinement initialisé par WinUI.

## Prime measure — workaround microsoft-ui-xaml#7374

La première mesure d'un `MenuFlyoutItem` après instanciation renvoie 40 px au lieu des 32 px effectifs ([microsoft-ui-xaml#7374](https://github.com/microsoft/microsoft-ui-xaml/issues/7374)). Conséquence : la première ouverture du menu est mal dimensionnée et le popup déborde ou laisse un vide. Le contournement, transmis depuis H.NotifyIcon, est d'amorcer le visual tree dans `Frame.Loaded` par un cycle `flyout.ShowAt(...) + flyout.Hide()` invisible. À l'utilisateur, l'amorce est imperceptible (la fenêtre porteuse est toujours alpha=0 à ce stade). Les mesures suivantes sont correctes.

La mesure utilise une itération sur les items avec `Height=32` et `Padding=(11,0,11,0)` forcés — valeurs canoniques d'un item de menu Win11. Le scaling vers les pixels physiques se fait via `XamlRoot.RasterizationScale` (DPI per-monitor). Une marge interne de 4 px chaque côté couvre la card de fond du `MenuFlyout`.

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
