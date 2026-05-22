# ADR-0004 — Construire les fenêtres secondaires en lazy pour la stabilité au boot

**Status** — accepted le 2026-04-15

## Contexte
Deckle expose plusieurs fenêtres WinUI 3 : HUD (chemin chaud du hotkey de transcription), Settings, Logs, Playground. Initialement toutes étaient construites au démarrage dans `App.OnLaunched`. Cette approche provoquait des problèmes de stabilité runtime — crashs à l'initialisation, pas de simples pertes de perf.

Le diagnostic n'a pas isolé une cause unique mais a établi le fait empirique : le boot all-windows n'était pas tenable. Le pattern lazy a éliminé les crashs.

## Options considérées
- **A. Toutes les fenêtres au boot** — schéma initial, simple à raisonner mais cause des crashs au démarrage.
- **B. Toutes les fenêtres en lazy** — construites uniquement à l'ouverture utilisateur. Élimine les crashs mais ajoute une latence visible au premier appui sur le hotkey de transcription (HUD invisible à construire à chaud).
- **C. Lazy pour secondaires, eager pour HUD** — Settings, Logs, Playground en `App.ShowXxxLazy`, HUD construit au boot dans `OnLaunched` pour préserver la latence du chemin chaud.

## Décision
Option C. Le HUD reste créé au boot parce qu'il est sur le chemin chaud du hotkey de transcription. Settings, Logs et Playground passent en lazy via `App.ShowSettingsLazy`, `ShowLogsLazy`, `ShowPlaygroundLazy`.

Le HUD est l'exception, justifiée par la criticité de sa latence d'apparition, pas par une immunité au problème de boot all-windows.

## Conséquences
Stabilité au démarrage retrouvée. Pour les problèmes de premier rendu cosmétiques (latence du premier open d'une secondaire, flicker), la doctrine est de viser des techniques just-in-time — off-screen prewarm avant first show, font preload via DirectWrite — plutôt qu'un retour au schéma all-windows-at-boot. Aucune proposition de revenir à un schéma all-at-boot pour résoudre un symptôme de premier rendu ne doit être acceptée sans nouvelle investigation des crashs originels. La décision est dure jusqu'à preuve technique du contraire.
