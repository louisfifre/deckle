# CLAUDE.md — Deckle.Diagnostics.Logging

Module enfant de `Deckle.Diagnostics` qui porte la **surface LogWindow** — la fenêtre de visualisation des événements live, ses filtres utilisateur, et la persistance disque optionnelle du journal applicatif. Le viewer XAML lui-même sera porté depuis `src/Deckle/Ui/LogWindow*` (mouvement E de la passe modulaire) ; en vague 1 ce module contient uniquement les settings et un bridge sink pour que le LogWindow legacy reçoive les événements émis par les nouveaux EventSources.

Le module dépend de `Deckle.Diagnostics` (interfaces sink, EventEntry) et `Deckle.Core` (AppPaths pour le fichier de settings per-module). Aucune dépendance vers `Deckle.Logging` legacy.

## Responsabilités actuelles

`LoggingSettings` porte les choix utilisateur sur le journal live :

- **Filtres SelectorBar** — sélection par niveau (Critical, Error, Warning, Informational, Verbose) et par module pour le viewer.
- **Gate persistance** — `ApplicationLogToDisk` (bool), gate qui contrôle si le `JsonlEventListener` du canal général écrit dans `app.jsonl`. Off par défaut en preview, on assumé en debug local.
- **Capture loop noise** — `LogAmbientCaptureActivity` (bool) ; quand off et qu'une capture loop est active, les events `Verbose` des providers ambient (vision, lighting) sont droppés avant émission. Reproduit la posture du legacy `TelemetryService._captureActive` mais portée comme filtre côté listener plutôt que côté hub.

`LoggingSettingsService` est le singleton de persistance par-module qui charge / sauvegarde le POCO sous `<UserDataRoot>/modules/diagnostics-logging/settings.json`. Pattern aligné sur les autres `*SettingsService` du projet.

## Frontière avec `Deckle.Diagnostics.Telemetry`

Le partage est par **consumer humain vs machine**. Tout ce qui touche au viewer interactif (filtres SelectorBar, mise en forme texte, gate journal applicatif) vit ici. Tout ce qui touche aux fichiers de télémétrie structurée (latency, microphone, corpus, dialogs de consentement) vit dans `Deckle.Diagnostics.Telemetry`. Les deux modules dépendent indépendamment de `Deckle.Diagnostics` ; ils ne se référencent pas entre eux.

## Migration progressive du LogWindow

En vague 1 le module n'expose pas encore de fenêtre XAML — seulement `LoggingSettings` + l'implémentation concrète d'`ILogWindowSink` qui forwards vers le LogWindow legacy installé par l'App. Quand le LogWindow lui-même sera porté ici (vague de surface, palier modulaire ultérieur), le sink concret deviendra une méthode directe sur le ViewModel de la fenêtre, et le bridge legacy disparaîtra.
