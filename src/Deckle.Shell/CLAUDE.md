# CLAUDE.md — Deckle.Shell

Module shell système. Couvre les interactions avec le système d'exploitation qui n'appartiennent à aucun pipeline métier : hotkeys globaux Win32, tray icon, autostart HKCU, message-only window invisible servant de point d'attache au tray et aux hotkeys, et un wrapper `DispatcherQueueExtensions` pour signaler les enqueues UI rejetés. Le module est intentionnellement bas-niveau : aucune connaissance applicative au-delà de ces quatre primitives. Les actions concrètes derrière chaque entrée du menu tray ou chaque hotkey sont branchées par l'app hôte avant `Register` — pas d'auto-binding, pas de service locator, pas de couplage vers les modules métier.

## Message-only host

Le tray et les hotkeys globaux ne peuvent pas être hébergés par une `Microsoft.UI.Xaml.Window` : le sous-classage Win32 nécessaire (`SetWindowSubclass`) est incompatible. La solution canonique est une message-only window Win32 (`MessageOnlyHost`, parent `HWND_MESSAGE`) créée dans `App.OnLaunched`. Invisible par construction — pas de flash possible, pas de trick off-screen. `TrayIconManager.Register(hwnd)` et `HotkeyManager` s'attachent dessus. L'ordre des branchements compte : créer le `MessageOnlyHost` avant de tenter `RegisterHotKey`, et brancher les callbacks tray avant `TrayIconManager.Register`.

Piège technique récurrent : le délégué `SubclassProc` Win32 doit être un champ d'instance, jamais une lambda locale. Sinon le GC le collecte et le subclass crash quand Windows essaie de l'invoquer. Le pattern est en place dans `MessageOnlyHost`.

## Hotkeys

Trois hotkeys globaux par défaut : `Win+\`` (transcription), `Shift+Win+\`` (rewrite primary), `Ctrl+Win+\`` (rewrite secondary). Tous enregistrés via `RegisterHotKey` Win32 sur le `MessageOnlyHost`. Avant tout test runtime, tuer toute instance déjà en cours — deux processus qui appellent `RegisterHotKey` sur la même combinaison se collisionnent avec `err 1409`.

Le `HotkeyManager` écoute aussi les changements de layout clavier (`WM_INPUTLANGCHANGE`) et re-résout les VKs depuis les scancodes pour préserver la combinaison sur un autre layout. Si le re-register échoue (rare), passe en `Warning` sans bloquer — l'utilisateur garde une UI fonctionnelle même si le hotkey tombe momentanément.

## Tray

`TrayIconManager` enregistre une icône Shell_NotifyIcon avec menu contextuel. Les callbacks (start recording, open settings, open logs, quit) sont fournis par l'app hôte avant `Register`. L'icône peut basculer entre état idle et état recording (rouge) via `SetState` — c'est le pipeline de transcription qui pousse l'état au démarrage et à la fin de session.

## Autostart

`AutostartService` gère l'entrée HKCU `Software\Microsoft\Windows\CurrentVersion\Run`. La valeur écrite cible `Environment.ProcessPath` (chemin absolu de l'exe courant). Le `Disable` ne touche pas une entrée qui pointe vers un autre install — utile quand l'utilisateur a lancé Deckle depuis un build dev pendant qu'une release est installée ailleurs. Les états et erreurs sont remontés en `Lifecycle`. Pas de MSIX StartupTask — décision actée par [ADR-0002](../../docs/adr/0002-reporter-msix-rester-unpackaged.md).

## DispatcherQueueExtensions

Wrapper `TryEnqueue` qui logge en `Warning` quand le dispatch UI est rejeté (queue shut down). Le caller passe une source label libre (`"HUD"`, `"LOGWIN"`, etc.) qui est préfixée dans le message ETW — le payload structuré garde uniquement le `what` (description de l'event perdu). Utile pour repérer les fenêtres qui essaient de marshaler après leur fermeture.

## Observabilité

Toutes les émissions passent par `DeckleShellSource.Log` — provider `Deckle.Shell` (ETW name) exposé en singleton statique. La doctrine « l'observation s'attache au module qui contient l'opération » fait converger plusieurs sources legacy (`LogSource.Hotkey`, `LogSource.MsgHost`, `LogSource.Settings` pour la branche autostart, plus le paramètre `source` libre de `DispatcherQueueExtensions`) vers un seul provider — tag SHELL dans la LogWindow. Les keywords distinguent les sous-domaines internes (`Lifecycle` pour host/autostart, `Capture` pour les hotkeys).

## Pointeurs

- [src/Deckle.App/CLAUDE.md](../Deckle.App/CLAUDE.md) — lifetime de l'app hôte, ordre des branchements (`MessageOnlyHost` avant `RegisterHotKey`, callbacks tray avant `TrayIconManager.Register`).
- [src/Deckle.Transcription/CLAUDE.md](../Deckle.Transcription/CLAUDE.md) — pipeline qui consomme le hotkey de transcription et qui porte la doctrine paste (le paste n'est pas une primitive shell : c'est une politique métier de la transcription).
- [src/Deckle.Core/Interop/UIAutomation.cs](../Deckle.Core/Interop/UIAutomation.cs) — wrapper `IUIAutomation` consommé par la transcription pour le probe focus.
