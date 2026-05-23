# ADR-0006 — Structure Diagnostics parent, Logging et Telemetry enfants

**Status** — accepted le 2026-05-22

## Contexte

La décision actée dans [ADR-0005](./0005-adoption-eventsource-pour-l-observabilite.md) introduit `EventSource` comme pilier d'observabilité. Reste à choisir où vit la plomberie technique partagée (base class abstraite, keywords transverses, EventListeners, interfaces sink) par rapport aux surfaces consommateurs (LogWindow live, fichiers de télémétrie structurée et leurs dialogs de consentement).

Le module `Deckle.Logging` actuel mélange ces deux dimensions. Il porte à la fois le hub d'émission (`TelemetryService`), les sinks concrets (`JsonlFileSink`), les payloads structurés (LatencyPayload, CorpusPayload, MicrophoneTelemetryPayload), les settings utilisateur de logging (`LoggingSettings`) et de télémétrie (`TelemetrySettings`), et l'interface des gates (`ITelemetryGates`). Pendant la première année de vie du projet ce groupement était lisible. La refonte observabilité oblige à clarifier : le pilier émission est consommé par tous les modules, alors que les surfaces consommateurs sont elles-mêmes des feature areas séparées (un viewer live, une persistance structurée avec consentement utilisateur).

## Options considérées

- **A. Module unique `Deckle.Diagnostics` qui porte tout.** Base class, listeners, settings de logging, settings de télémétrie, dialogs de consentement, configuration des fichiers de persistance — tout vit ensemble. Continuité simple avec le pattern legacy. Mais : le module devient gros et hétérogène, et les modules feuilles qui n'ont besoin que de la base class doivent transitivement dépendre des dialogs de consentement et de la configuration de persistance — couplage non nécessaire.

- **B. Module parent `Deckle.Diagnostics` + module unique `Deckle.Diagnostics.UI` qui regroupe surfaces et settings.** Sépare la plomberie technique de la surface, mais regroupe artificiellement le viewer live (LogWindow) et la persistance structurée (latency, microphone, corpus). Les deux n'ont rien en commun côté consumer humain — LogWindow est un viewer interactif, la persistance structurée est un contrat machine avec gates de consentement.

- **C. Module parent `Deckle.Diagnostics` + deux enfants `Deckle.Diagnostics.Logging` et `Deckle.Diagnostics.Telemetry`.** Le parent porte la plomberie technique consommée par tous les modules émetteurs : base class abstraite `DeckleEventSource`, enum partagé `Keywords`, interfaces sink (`ILogWindowSink`, `IHudFeedbackSink`), implémentations des EventListeners (`LogWindowEventListener`, `JsonlEventListener`, `HudFeedbackEventListener`). L'enfant `Logging` porte la surface viewer (LogWindow XAML, ViewModels, filtres SelectorBar, gate `ApplicationLogToDisk`). L'enfant `Telemetry` porte la persistance structurée (TelemetrySettings, dialogs de consentement, configuration boot des `JsonlEventListener` avec leurs chemins de fichier).

## Décision

Option C retenue. La pipeline d'observabilité s'organise en trois modules : un parent `Deckle.Diagnostics` qui porte la plomberie technique et est consommé par tous les modules émetteurs ; deux enfants `Deckle.Diagnostics.Logging` (surface viewer + gate journal applicatif) et `Deckle.Diagnostics.Telemetry` (persistance structurée + consentement) qui sont consommés uniquement par l'app hôte au moment du boot.

## Conséquences

Devient plus facile : un module feuille qui veut émettre des événements dépend uniquement de `Deckle.Diagnostics` (la plomberie), pas des surfaces de consommation. Le graphe de dépendance reste minimal pour les briques techniques. Les deux enfants évoluent à leur rythme — la surface LogWindow et la persistance structurée n'ont pas de raison de se coordonner.

Devient plus difficile : trois modules à créer et à maintenir au lieu d'un. Surcoût administratif réel (trois `csproj`, trois `CLAUDE.md`, trois fois la cérémonie de boot dans l'app hôte). Compensé par la clarté du graphe et par le fait que les trois rôles sont effectivement distincts.

Devient impossible : un consommateur de la plomberie technique qui se mettrait à dépendre transitivement d'une surface XAML. La frontière du parent est gardée par le fait qu'il ne référence ni WinUI 3 ni les modules de surface.

Le mapping concret. **`Deckle.Diagnostics`** porte `DeckleEventSource`, `Keywords`, `EventEntry`, `ILogWindowSink`, `IHudFeedbackSink`, `FeedbackEntry`, et les EventListeners (`LogWindowEventListener`, `JsonlEventListener`, `HudFeedbackEventListener`). Aucune dépendance sortante hormis la BCL. **`Deckle.Diagnostics.Logging`** porte `LoggingSettings` et, à terme, le viewer LogWindow XAML lui-même (mouvement E de la passe modulaire). Dépend de `Deckle.Diagnostics` et `Deckle.Core` (AppPaths pour la persistance settings). **`Deckle.Diagnostics.Telemetry`** porte `TelemetrySettings`, les dialogs de consentement, et `TelemetryListenerBootstrap` qui inscrit les `JsonlEventListener` au boot avec leurs chemins de fichier et leurs prédicats. Dépend de `Deckle.Diagnostics` et `Deckle.Core`.

L'app hôte référence les trois modules. Elle wire les listeners au boot via `AppDiagnosticsBootstrap.Initialize(...)` qui invoque `TelemetryListenerBootstrap.Configure(...)` côté Telemetry et instancie un `LogWindowEventListener` avec son sink concret côté Logging.
