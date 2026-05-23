# ADR-0005 — Adoption d'EventSource pour l'observabilité

**Status** — accepted le 2026-05-22

## Contexte

Le pilier d'observabilité de Deckle reposait jusqu'ici sur un hub maison `TelemetryService` couplé à des sinks `ITelemetrySink` interchangeables. L'architecture avait été conçue pour centraliser l'émission, ce qu'elle réussit. Elle a accumulé en parallèle plusieurs faiblesses qui se manifestent au moment où le projet entre dans une phase plus large d'instrumentation et de tests.

L'API d'émission n'est pas typée au site d'appel. Tout passe par `TelemetryService.Log(source, message, level, feedback)` ou par une des trois méthodes structurées (`Latency`, `Corpus`, `Microphone`) qui acceptent un POCO record. Le `source` est une `string` (constante d'un `LogSource` vocabulaire fermé), le `message` est libre, le niveau choisi à la main par l'appelant. Aucun outil de la BCL ne contraint la cohérence — un site d'appel qui se trompe de constante ou de niveau passe en review humaine.

Le format `TelemetryEvent` est interne au projet. Aucun outil externe ne le lit. La trace runtime n'est pas exploitable par PerfView, dotnet-trace, ou les outils ETW standards.

Le couplage entre l'émetteur et les sinks est explicite et runtime — `TelemetryService.Instance.AddSink(...)`. La discipline de single-émission requiert que tout chemin parallèle (Console.WriteLine, File.AppendAllText, logger dupliqué) soit refusé en review. Cette discipline tient mais demande une attention humaine constante.

L'écriture de tests sur la séquence d'événements émise par un module métier exige un mock de `ITelemetrySink` qu'on inscrit dans la pipeline avant le test. Pas de contrat natif de test ; il faut connaître l'architecture interne du module et ses appels.

Le chantier panoramique d'observabilité (cf. mémoire projet « Session réflexion observabilité ») demande à reprendre l'infrastructure pour y attacher quatre volets imbriqués : logging refondu, refonte settings, suite de tests, inventaire des modules. Avant d'investir, on choisit la base.

## Options considérées

- **A. Conserver `TelemetryService` et le faire évoluer en place.** Continuer sur le hub maison, en typant progressivement l'API d'émission (par exemple via des wrappers spécifiques par module au-dessus de `Log`). Coût d'évolution faible à court terme, dette continue sur le typage et l'interopérabilité externe. Aucun gain sur les tests natifs ni sur l'intégration aux outils ETW.

- **B. Adopter `System.Diagnostics.Tracing.EventSource` comme pilier d'émission.** Mécanisme natif .NET de tracing typé, normalisé ETW, supporté par les outils Microsoft (PerfView, dotnet-trace, dotnet-counters) et par chaque IDE qui sait lire ETW. Chaque module métier déclare son propre `EventSource` (un par provider), avec une méthode `[Event]` par opération distincte au site d'appel — la signature est typée et statique, le compilateur refuse une signature incohérente. Les EventListeners sont des classes du framework, branchables au boot et au runtime, sans contrat propriétaire. Coût initial d'apprentissage et de bascule, gain durable sur le typage, l'interopérabilité, et la testabilité native.

- **C. Adopter une bibliothèque tierce (Serilog, NLog, Microsoft.Extensions.Logging avec providers).** Frameworks de logging matures, large écosystème de sinks, syntaxe fluide. Mais : dépendances externes, posture moins alignée avec la doctrine « primitive native d'abord » du projet, pas d'intégration ETW directe sauf via un provider de plus, et la sémantique `ILogger<T>` reste textuelle plutôt que typée par opération.

## Décision

Option B retenue. La pipeline d'observabilité de Deckle bascule sur `System.Diagnostics.Tracing.EventSource`. Chaque module métier expose un `Deckle<Module>Source` héritant d'une base abstraite `DeckleEventSource` qui porte la session id et le format ETW self-describing. Les destinations live et fichier sont des `EventListener` standards branchés au boot. La signature de chaque émission devient typée : une méthode `[Event(...)]` par opération distincte au site d'appel, paramètres en `snake_case` qui deviennent directement les clés JSON dans la sortie JSONL.

## Conséquences

Devient plus facile : le typage statique au site d'appel rattrape les incohérences au build ; la trace runtime est lisible par tout outil ETW standard (PerfView, dotnet-trace) sans code spécifique côté Deckle ; les tests d'observabilité s'écrivent en attachant un `EventListener` collector dans le test, sans toucher au module — contrat natif. La discipline de single-émission est portée par le runtime ETW (un module qui ne déclare pas son `EventSource` ne peut pas émettre), plus par la review humaine.

Devient plus difficile : la migration de l'existant exige une passe par module sur les sites d'appel actuels de `TelemetryService` et `LogService`, qui sont nombreux et dispersés ; la signature ETW interdit les types complexes en paramètres, donc les payloads structurés actuels (LatencyPayload, MicrophoneTelemetryPayload, CorpusPayload) doivent être explosés en paramètres flat de leurs propriétés primitives à chaque émission ; le filtre runtime central « capture-active » du hub legacy doit être ré-implémenté comme filtre côté provider ou côté listener.

Devient impossible : un appel d'émission générique du type `Log(source, message, level)` qui aurait servi d'échappatoire et rotted la discipline de typage. La base `DeckleEventSource` n'expose pas de telle API ; chaque event doit être déclaré comme méthode `[Event]` sur le provider du module.

La migration suit le plan de vagues documenté dans la fiche [reference--eventsource-convention--1.0.md](../reference/reference--eventsource-convention--1.0.md). Les modules `Deckle.Logging`, `LogService`, `JsonlFileSink`, `TelemetryGates`, `UserFeedback` legacy disparaissent à la vague 6, après vérification d'un grep zéro sur leurs symboles dans le code applicatif.
