# CLAUDE.md — Deckle.Diagnostics

Module fondation du nouveau pilier observabilité. Porte la plomberie technique partagée par tous les `Deckle.*EventSource` du projet et par les EventListeners qui consomment leurs émissions. Ne contient aucun provider concret — chaque module métier qui émet des événements déclare son propre EventSource héritant de `DeckleEventSource` et l'expose en singleton statique.

Le module ne dépend que de la BCL (`System.Diagnostics.Tracing`). En particulier, **aucune dépendance vers `Deckle.Core`** — la diagnostics est sous toutes les autres briques techniques, y compris les chemins applicatifs. Les destinations concrètes (chemins de fichiers JSONL, accès au LogWindow XAML, branchement HUD) sont fournies par les modules consommateurs au moment du boot via les interfaces sink exposées ici.

## Convention de provider

Un EventSource concret par module qui émet. Nom de classe `Deckle<Module>Source`, nom ETW `[EventSource(Name = "Deckle.<Module>")]`. Le `.` dans le nom ETW est canonique pour les noms hiérarchiques. Singleton statique `public static readonly Log = new()`, type `sealed`, hérite de `DeckleEventSource` (qui hérite lui-même de `EventSource`). Les keywords transverses (`Keywords.Lifecycle`, `Keywords.Capture`, `Keywords.Pipeline`, `Keywords.Push`, `Keywords.Heartbeat`) occupent les bits 0 à 4 ; les bits 5 et au-dessus appartiennent au provider et restent locaux au module.

Exemple canonique de squelette de provider :

```csharp
[EventSource(Name = "Deckle.Chrono")]
public sealed class DeckleChronoSource : DeckleEventSource
{
    public static readonly DeckleChronoSource Log = new();

    [Event(1, Level = EventLevel.Informational, Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Chrono started")]
    public void ChronoStarted()
    {
        if (IsEnabled()) WriteEvent(1);
    }
}
```

## Discipline des méthodes typées

Une méthode `[Event(...)]` par opération distincte au site d'appel. Pas de méthode `Log(string, EventLevel)` générique sur la base, pas d'event qui prend un payload typé en argument. Les events triviaux sans paramètre sont des méthodes parameter-less typées (`WarmingUp()`), pas une utilisation d'un canal générique.

Les paramètres d'event sont en `snake_case` parce qu'ils deviennent directement les clés JSON dans la sortie JSONL. C'est une dérogation explicite aux Framework Design Guidelines, justifiée par le contrat machine de la persistance — un consommateur tiers (PerfView, dotnet-trace, scripts de benchmark) trouve les mêmes noms côté ETW manifest et côté fichier. Le warning `IDE1006` est supprimé au csproj du module Diagnostics et des modules qui émettent.

Cinq `EventLevel` natifs uniquement.

- **`Critical`** — défaillance bloquante, l'app ne peut plus servir sa fonction principale. Crash, première-impossibilité dépendance, état corrompu.
- **`Error`** — défaillance ciblée d'une opération, autres opérations peuvent continuer. Transcription échouée, hotkey unavailable, bridge Hue inaccessible.
- **`Warning`** — situation anormale sans casse. Buffer vide, dépendance lente, état dégradé qui se rétablit.
- **`Informational`** — jalon de progression en phrase Capital courte (« Loading model », « Recording start »). C'est l'équivalent du legacy Info **et** Success — la sémantique de réussite se porte par le message, plus par un niveau dédié.
- **`Verbose`** — détails techniques structurés, machine-greppables. Mesures, identifiants, payloads structurés. C'est le niveau qui porte les `LatencyRecorded`, `MicrophoneTelemetryRecorded`, `CorpusRecorded` et leurs paramètres détaillés.

Le legacy `Narrative` est abandonné. Si un texte UX adressé à l'utilisateur est nécessaire, il passe par `UserFeedbackEmitted` (HUD) ou par une string `.resw` (surface UI).

## Performance — gate avant payload

Toute méthode `[Event(...)]` testée par `IsEnabled()` ou mieux `IsEnabled(level, keywords)` avant la moindre construction de payload. Le brief verrouille ce point : `IsEnabled(level, keywords)` côté provider avant toute construction de payload, pour zéro alloc quand aucun listener n'écoute. Quand l'event a des paramètres, le pattern est :

```csharp
public void LatencyRecorded(double audio_sec, long whisper_ms, /* … */)
{
    if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
    WriteEvent(<id>, audio_sec, whisper_ms, /* … */);
}
```

Le `if (IsEnabled())` simple suffit pour les events sans paramètre. Le gate paramétré n'a de sens que quand on évite une construction (allocation de string, tableau, calcul).

## Contrats des trois consumers

**HUD via `UserFeedbackEmitted`.** Un event canonique du même nom (`UserFeedbackEmitted`) exposé par chaque provider qui peut en émettre. Signature contrat : `(int severity, string title, string body, int role)`. Le `HudFeedbackEventListener` filtre exclusivement sur ce nom d'event et ignore tout le reste. Severity et role passent en `int` parce que EventSource n'accepte pas les enums utilisateur ; l'App ré-encode vers ses propres `UserFeedbackSeverity` et `UserFeedbackRole` côté sink. Un site qui veut un feedback utilisateur appelle l'event de jalon **et** `UserFeedbackEmitted` — pas de substitution.

**LogWindow live.** Le `LogWindowEventListener` écoute tous les events de la famille `Deckle.*`, y compris les télémétries structurées, sans masquage à l'émission. Le filtrage utilisateur (par niveau et par module via la SelectorBar) se fait côté sink dans le viewer.

**Routage JSONL.** Une instance de `JsonlEventListener` par fichier de destination. Chaque listener reçoit un prédicat qui sélectionne les events à écrire dans son fichier. Le wiring concret (chemins de fichiers, gates utilisateur) vit dans `Deckle.Diagnostics.Telemetry`. Le schéma JSON reproduit le legacy à la clé près :

```json
{"timestamp":"<ISO 8601>","kind":"<label>","session":"YYYY-MM-DD-XXXX","payload":{<flat snake_case>}}
```

Les payloads structurés (latency, microphone, corpus) ont leurs propres labels (`"latency"`, `"microphone"`, `"corpus"`) ; le canal général garde `"log"` comme legacy.

## Session id

Une seule `SessionId` au format `YYYY-MM-DD-XXXX` est générée la première fois qu'un provider émet, et partagée par tous les providers `Deckle.*` pour la durée du process. Stockée comme propriété statique sur `DeckleEventSource`. Reproduit exactement le comportement du legacy `TelemetryService.SessionId` pour que les benchmarks puissent continuer à grouper par session pendant et après la migration.

## Coexistence pendant la migration

Le legacy `Deckle.Logging` coexiste jusqu'à la vague 6. Conséquence opérationnelle : pendant la migration, un module migré appelle **uniquement** son EventSource, un module non migré continue d'appeler `TelemetryService`. Pas de double émission, pas de chemin bridge cross-pipeline. Les EventListeners ici déclarés sont inscrits au boot dans `App.OnLaunched` **à côté** des sinks legacy, et écrivent dans des fichiers parallèles le temps de la validation schéma. Le swap final se fait en vague 6 quand le legacy disparaît.

## Tests

EventSource est conçu pour être testable via un EventListener custom branché dans le test. Pattern canonique : instancier le provider via `[EventSource(Name = "Deckle.Foo")]` (le test peut aussi enregistrer manuellement un nouveau provider via `EventSource.SendCommand` sur une instance existante), brancher un `TestEventListener` qui collecte les `EventEntry`, exécuter le code, assert sur la séquence collectée. C'est cette propriété de testabilité native qui motive en partie le choix EventSource — voir [ADR-0005](../../docs/adr/0005-adoption-eventsource-pour-l-observabilite.md).
