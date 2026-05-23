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

## Vocabulaire de mesures

Chaque mesure exposée comme paramètre d'event a un format canonique. Le **nom du paramètre** sert de clé JSON, **l'unité**, **la précision** et le **suffixe** suivent les tables ci-dessous pour qu'un humain qui grep une mesure dans la LogWindow ou dans un JSONL retrouve la même chose partout. Toute apparition d'une mesure dans un nouvel event doit suivre ce contrat — si une unité manque, l'ajouter ici avant de l'utiliser.

**Temps** — durées courtes `<name>_ms` entier (`load_ms=420`, source `Stopwatch`), durées longues `<name>_sec` 1 décimale (`audio_sec=12.3`, calcul `samples / 16000`), timing segment whisper `t0` / `t1` / `dur` 1 décimale (`t0=1.2 t1=3.4 dur=2.2`).

**Audio** — RMS linéaire `rms` 4 décimales sur `[0,1]` (`rms=0.0123`, `sqrt(Σv²/n)` avec `v = pcm16/32768`), niveau `dbfs` 1 décimale (`dbfs=-38.2`, `20 * log10(rms)`), fréquence en `kHz` entier (toujours `16` dans Deckle), canaux toujours `mono`, échantillons `samples` entier, taille buffer `bytes` entier.

**Texte** — longueur caractères `text_chars`, longueur mots `text_words`, longueur tokens `prompt_tok` ou `tok` (`text_chars=142`, `prompt_tok=512`).

**Compute** — `n_seg` entier (segments), `tok_s` 1 décimale (tokens/s), pourcentage `<name>_pct` 1 décimale (`reduction_pct=62.4`), confiance `p̄` / `min` 2 décimales sur `[0,1]`, probabilité `<name>_pct` entier (`nsp=12`).

**Image / capture vidéo** — frames par seconde `fps` 1 décimale (mesuré sur fenêtre glissante 1 s), compte de frames `frames` entier (depuis Start de la session), résolution `size=WxH` entier (`Direct3D11CaptureFrame.ContentSize`), format pixel `format=<enum DirectXPixelFormat>`, buffers pool `bufs` entier (typiquement 2), handle moniteur `hmon=0x{hex}` (retour `MonitorFromPoint`).

**Retours d'appel natifs** — code natif `result=<int>` ou `mmsys=<int>`, HRESULT `hr=0x{hex}`, outcome enum `outcome=<value>`, pointeur natif `ctx=0x{hex}`.

**Réseau et drivers LED** — IPv4 `bridge_ip=192.168.1.5`, Hue serial number `bridge_id=001788FFFE3A2C18` (hex16), application key `username=eDOvxk-...` (tronqué à 8 chars + `...`), pre-shared key `clientkey=[redacted]` (PSK DTLS jamais loggée en clair), group ID `group_id=3` (CLIP v1 entier, v2 UUID), HTTP status `hr=200` / `hr=401`, couleur CIE `xy=0.4521,0.3895` 4 décimales, luminance `bri=200` entier 0–254, RGB `rgb=180,60,240` 3 octets.

## Doctrine de séparation Verbose ↔ Info

**Les identifiants opaques et le format `k=v` sont Verbose-only.** Un `Message` `[Event]` de niveau `Informational`, `Warning`, `Error` ou `Critical` est une phrase Capital courte, lisible par un humain qui n'a aucune connaissance de l'implémentation. Si le `Message` contient un ID (light id Hue, group id, file path, hash, line index, opaque token quelconque) ou des séparateurs `|`, alors par définition c'est un event Verbose, pas un event sémantique. Un Info qui contient un ID est une erreur de niveau, pas une variante stylistique.

Lorsqu'une action mérite à la fois une signalisation sémantique pour l'utilisateur ET un détail technique pour le diag, on émet **deux events** : un Info Capital sans IDs, et son miroir Verbose avec les IDs en paramètres typés snake_case. Aucun chevauchement.

| ❌ Mauvais (mélange) | ✅ Bon (séparation) |
|---|---|
| `Info AMBIENT zone assign \| id=42 \| zone=Top` | `Info AMBIENT Zone Top assigned to Falcon` |
| | `Verbose AMBIENT zone assign \| id=42 \| zone=Top` |
| `Info AMBIENT settings update \| key=UseMultiLight \| value=true` | `Info AMBIENT Pipeline mode set to per-zone` |
| | `Verbose AMBIENT settings update \| key=UseMultiLight \| value=true` |

Le miroir Verbose **suit toujours** l'Info Capital quand il y a un détail technique à acter. Ce n'est pas optionnel — c'est le contrat qui rend les logs greppables.

## Format par niveau — deux registres distincts

**Informational et niveau jalon ré-réussi** — phrase Capital courte, lue comme un jalon dans la vue Activity de la LogWindow. Pas de `k=v`, pas d'unités techniques. Un détail court entre parenthèses reste admis quand il porte l'essentiel du jalon (backend, durée perçue, outcome). Exemples : `MODEL Loading model`, `MODEL Model loaded (Vulkan)`, `CAPTURE Recording start`, `CAPTURE Recording complete (12.3 s)`, `TRANSCRIBE Transcribing`, `TRANSCRIBE Transcription complete (5 seg)`, `LLM Rewriting (Short)`, `LLM Rewrite complete`, `CLIPBOARD Copied to clipboard`, `PASTE Pasted`, `DONE Done (Pasted)`.

**Warning et Error** — phrase Capital riche. Quand l'alerte nécessite des détails (endpoint, code d'erreur, durée), les exprimer en prose (`Ollama busy — model X resident (2.1 GB). Waited 60s so far…`). Pas de `k=v` dans la prose Warning / Error visible, même si un event Verbose miroir peut exposer les champs machine-greppables en parallèle.

**Verbose** — détail technique machine-greppable. Le `Message` template suit le format `<action ou état> | <mesure1>=<val1> | <mesure2>=<val2> ...`. Préfixe court (verbe ou état) en tête, mesures séparées par ` | `, premier mot en minuscule, une seule ligne. Ne jamais répéter le module dans le message — le tag de source (`CAPTURE`, `LLM`, etc.) le porte déjà.

Exemples miroir :

```
Info     MODEL       Loading model
Verbose  MODEL       load start | file=ggml-large-v3.bin | file_mb=2951.7 | use_gpu=1
Info     MODEL       Model loaded (Vulkan)
Verbose  MODEL       load complete | load_ms=420 | backend=Vulkan
```

**Texte brut** (segment transcrit, contenu clipboard, prompt utilisateur) conserve sa casse native, ne subit pas la règle Capital. C'est du contenu, pas un message.

## Règles d'application durables

- **Une étape = un Info de début, un Info de fin.** Entre les deux, du Verbose si nécessaire. Pas d'Info répétés au milieu d'une étape.
- **Les heartbeats haute fréquence (< 1 s) ne sont pas loggués.** Ils alimentent les events UI (`AudioLevel` → HUD, RMS au tick) mais pas la LogWindow. La LogWindow porte des étapes, pas des frames.
- **Les mesures suivent le vocabulaire ci-dessus.** Si une unité manque, l'ajouter dans cette doctrine avant de l'utiliser. Pas de mesure ad-hoc.
- **Logs en anglais d'emblée**, Info techniques comme jalons sémantiques. Pas de français dans les events.
- **Un `UserFeedbackEmitted` est toujours doublé d'un event** du même niveau. L'event reste pour diagnostic, le HUD est pour l'utilisateur.
- **Jamais d'event multi-ligne.** Une émission = une ligne dans le viewer.
- **La source porte le contexte.** Ne pas écrire `CAPTURE: started recording` dans le `Message` — la colonne Source de la LogWindow affiche déjà `CAPTURE`.

## Tests

EventSource est conçu pour être testable via un EventListener custom branché dans le test. Pattern canonique : instancier le provider via `[EventSource(Name = "Deckle.Foo")]` (le test peut aussi enregistrer manuellement un nouveau provider via `EventSource.SendCommand` sur une instance existante), brancher un `TestEventListener` qui collecte les `EventEntry`, exécuter le code, assert sur la séquence collectée. C'est cette propriété de testabilité native qui motive en partie le choix EventSource — voir [ADR-0005](../../docs/adr/0005-adoption-eventsource-pour-l-observabilite.md).
