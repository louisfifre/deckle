---
name: claude-deckle-diagnostics
description: "Doctrine for Deckle.Diagnostics, the observability foundation module. Read before authoring or modifying an EventSource provider, an EventListener, an instrumentation site, or a sink contract."
type: agent-instructions
module: Deckle.Diagnostics
---

# CLAUDE.md — Deckle.Diagnostics

Module fondation du pilier observabilité. Porte la plomberie technique partagée par tous les `Deckle.*EventSource` du projet et par les EventListeners qui consomment leurs émissions. Ne contient aucun provider concret — chaque module métier qui émet des événements déclare son propre EventSource héritant de `DeckleEventSource` et l'expose en singleton statique.

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
- **`Verbose`** — détails techniques structurés, machine-greppables. Mesures, identifiants, payloads structurés. C'est le niveau qui porte les `LatencyRecorded`, `MicrophoneTelemetryRecorded`, `CorpusAsrRecorded`, `CorpusRewriteRecorded` et leurs paramètres détaillés.

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

## Schéma JSONL — contrat machine

Le schéma émis par `JsonlEventListener` est figé. Une ligne JSON par event, séparateur `\n`, encodage UTF-8 sans BOM. Structure d'enveloppe :

```json
{
  "timestamp": "<ISO 8601 avec offset local>",
  "kind": "<label de canal>",
  "session": "YYYY-MM-DD-XXXX",
  "payload": { "<paramètre snake_case>": <valeur typée>, … }
}
```

Sérialisation des valeurs primitives par leur type natif (`int` → JSON number sans guillemets, `string` → JSON string, `bool` → `true`/`false`). Les `DateTime` et `DateTimeOffset` passent par leur représentation `"o"` (round-trip ISO 8601). Les `Guid` passent par leur représentation `"D"` (segments uppercase, dashes). Tout autre type est stringifié — en pratique ce cas ne survient pas, EventSource interdisant les types complexes en paramètres `[Event]`.

Le `kind` prend les valeurs `"log"` (canal général, dans `app.jsonl`), `"latency"` (canal latency, dans `latency.jsonl`), `"microphone"` (canal microphone, dans `microphone.jsonl`), `"corpus"` (canal corpus, dans `corpus.jsonl` ou `<profile>/corpus.jsonl` selon le contexte). Le label `"log"` est conservé tel quel pour la compatibilité avec les outils de benchmark existants.

## Inventaire des providers et pipeline d'écoute

Treize providers EventSource concrets actifs au boot, plus la base class `DeckleEventSource` non instanciable. Chaque module qui émet déclare son propre `Deckle<Module>Source.cs` héritant de `DeckleEventSource`. Liste par ordre alphabétique du Name ETW, avec le module hôte et le tag LogWindow correspondant entre parenthèses :

- `Deckle.Ambient` (`Deckle.Lighting.Ambient`, tag `AMBIENT`) — orchestrateur ambient lighting, pairing Hue consumer, heartbeat agrégé.
- `Deckle.App` (`Deckle.App`, tag `APP`) — host applicatif, crashes, boot, status transitions, restart, hotkey orchestration.
- `Deckle.Audio` (`Deckle.Audio`, tag `AUDIO`) — capture micro, anomalies waveIn, récap télémétrie `MicrophoneTelemetryRecorded`.
- `Deckle.Chrono` (`Deckle.Chrono`, tag `CHRONO`) — pilote historique de la vague 1, étoffé quand le module aura des jalons à émettre.
- `Deckle.Hud` (`Deckle.Hud`, tag `HUD`) — actuellement un seul `HudWarning(string)`, sous-instrumenté.
- `Deckle.Lighting` (`Deckle.Lighting`, tag `LIGHTING`) — driver Hue REST CLIP v1/v2, discovery, pairing, color push à 10-15 Hz.
- `Deckle.Llm` (`Deckle.Llm`, tag `LLM`) — réécriture Ollama, polling `/api/ps`, surface Settings → LLM.
- `Deckle.Playground` (`Deckle.Playground`, tag `PLAYGROUND`) — surface dev-only, events génériques per-canal.
- `Deckle.Settings` (`Deckle.Settings`, tag `SETTINGS`) — migration legacy → per-module, backup/restore, folder pickers, navigation NavView, setters ViewModels.
- `Deckle.Setup` (`Deckle.Setup`, tag `SETUP`) — wizard first-run, trois events génériques (`SetupInfo`/`Warning`/`Error`).
- `Deckle.Shell` (`Deckle.Shell`, tag `SHELL`) — message-only host, hotkeys, autostart HKCU\Run, dispatcher.
- `Deckle.Vision` (`Deckle.Vision`, tag `VISION`) — capture écran DXGI, FrameSampler, anomalies de la boucle d'acquisition.
- `Deckle.Whisp` (`Deckle.Transcription`, tag `WHISP`) — moteur de transcription, état du modèle natif, paste, clipboard. Le symbole `DeckleWhispSource` est resté tel quel après la refonte modulaire qui a renommé `Deckle.Whisp` en `Deckle.Transcription` — le Name ETW reste `Deckle.Whisp` pour préserver le tag LogWindow et la compat des outils de benchmark.

`Deckle.Core` et `Deckle.Composition` restent silencieux par doctrine — aucun site d'appel ne justifie un provider.

Six listeners instanciés au boot dans `AppDiagnosticsBootstrap`, persistent pour la vie du process. Quatre `JsonlEventListener`, un par fichier de destination (`app.jsonl`, `latency.jsonl`, `microphone.jsonl`, `corpus.jsonl`). Chacun reçoit un prédicat qui sélectionne les events à écrire dans son fichier — sélection par nom d'event canonique pour les heartbeats structurés (`LatencyRecorded`, `MicrophoneTelemetryRecorded`, `CorpusRecorded`), sélection par keyword pour le canal général. Un `LogWindowEventListener` avec buffer ring de capacité 5000 et multi-sink `AttachSink` / `DetachSink` — le LogWindow s'attache à sa première ouverture lazy et reçoit l'historique boot en replay. Un `HudFeedbackEventListener` qui filtre exclusivement sur le nom d'event `UserFeedbackEmitted` et route vers le sink concret du host.

Sources de configuration utilisateur :

- `Deckle.Diagnostics.Logging.LoggingSettingsService` → `<UserDataRoot>/modules/logging/settings.json` → toggle `LogAmbientCaptureActivity`, plus la gate volatile `AmbientCaptureGate` que `AmbientEngine` met à `true` autour de sa boucle pour drop les Verbose pendant la capture.
- `Deckle.Diagnostics.Telemetry.TelemetrySettingsService` → `<UserDataRoot>/modules/telemetry/settings.json` → gates `LatencyEnabled`, `MicrophoneTelemetry`, `CorpusEnabled`, `RecordAudioCorpus`, `ApplicationLogToDisk`, `StorageDirectory`. Le délégué injecté dans `TelemetryListenerBootstrap.ConfigureGates` est consulté à chaque émission par les `JsonlEventListener`.

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

## Classes d'observables canoniques

Quand on instrumente un bout de code, quels paramètres viser par défaut. Les sections précédentes répondent *où* et *comment* écrire l'event (provider, niveau, keyword, format, vocabulaire de mesures). Cette section couvre *quoi* émettre selon la classe de situation rencontrée. Neuf classes suffisent à couvrir le code Deckle existant et futur ; un site peut relever de deux classes simultanément.

### Classe 1 — Lifecycle et boot

Démarrage process, init paths, warmup ressources, chargement module, transitions d'état d'app (`idle → recording → transcribing → done`), shutdown amorcé, restart post-build, crash safety nets. Opérations uniques par cycle, jalons attendus en `Informational` avec keyword `Lifecycle`, miroirs en `Verbose` quand des paramètres techniques justifient un détail séparé.

**Set canonique** — nom de l'étape, durée `<name>_ms`, outcome (`succeeded` / `skipped` / `failed`), backend ou variant actif quand pertinent (`backend=Vulkan`, `model=ggml-large-v3.bin`), version du composant si charge réseau ou disque, motif de transition pour les state changes (`reason=hotkey`, `reason=tray`, `reason=auto-shutdown`).

**État actuel** — très bien instrumenté côté `Deckle.App` (boot, status transitions, shutdown/restart), `Deckle.Transcription` (warmup boot, model load via `DeckleWhispSource`), `Deckle.Audio` (capture lifecycle), `Deckle.Vision` (`ScreenCaptureStarted`/`Stopped`). Pattern `PathsInitialized` + `PathsDetail` (jalon Info + miroir Verbose) est l'archétype propre.

### Classe 2 — Pipeline batch

Transcription d'un blob audio, réécriture LLM, calibration appareil, push ambient sur un frame complet. Opération discrète début → fin → résultat. Cadres dominants RED et Four Golden Signals.

**Set canonique** — identifiant d'opération (`transcription_id` si pertinent), durée totale et par phase clé (`hotkey_to_capture_ms`, `record_drain_ms`, `whisper_init_ms`, `whisper_ms`, `llm_ms`, …), métriques d'entrée (`audio_sec`, `text_chars`, `prompt_tok`), métriques de sortie (`n_segments`, `text_words`, `tok_s`), outcome enum (`outcome=ok|repetition_loop|llm_failed|user_cancelled`), profil ou stratégie active (`strategy=`, `profile=`), flag binaire d'effet de bord (`pasted=true`).

**État actuel** — `LatencyRecorded` à 24 champs (`DeckleWhispSource`) est l'exemple canonique réussi, *canonical log line* au sens industrie qui colocalise toutes les mesures clés en une ligne par invocation. `CorpusAsrRecorded` (14 champs) et `CorpusRewriteRecorded` (12 champs) suivent le même pattern pour la persistance dataset (cf. [ADR-0011](../../docs/adr/0011-corpus-normalise-comme-dataset-ml.md)). Le pattern est mature côté transcription, pas systématisé ailleurs.

### Classe 3 — Boucle temps réel haute fréquence

Capture audio polling 50 ms, capture écran DXGI à ~15 Hz, push lumière à 10-15 Hz, raw input curseur ~125 Hz pour fade proximité HUD. Opérations nombreuses, brèves, l'enjeu est la stabilité du débit. Cadres dominants USE et Four Golden Signals côté flux sortant.

**Set canonique** — sur fenêtre glissante (1 s typique) : `fps` ou `ticks/s` observés, `drops` (frames acquis mais non traités), latence intra-tick `p50_ms` / `p95_ms`, saturation de file (`queue_depth` ou `pending_frames`), erreurs intra-fenêtre (`acquire_fail=N`). Pattern dit *rollup* — une ligne périodique qui résume N ticks, plutôt qu'une ligne par tick qui noierait l'observation.

**État actuel** — la `Heartbeat` de `DeckleAmbientSource` est l'incarnation actuelle du pattern (7 champs, périodique). `DeckleVisionSource` n'a pas d'équivalent — la boucle de capture émet par incident (anomalies, recovery) mais pas une trace régulière du débit. `DeckleAudioSource` émet le RMS tick sur un event UI direct (alimentation HUD), explicitement *non* loggué selon la règle « heartbeats haute fréquence < 1 s ne sont pas loggués ». Le récap distributif `MicrophoneTelemetryRecorded` à 14 champs en fin de session compense.

### Classe 4 — Driver matériel et intégration externe

Pilote micro (WASAPI), client HTTP Hue REST, client HTTP Ollama, EventStream SSE, P/Invoke whisper.cpp natif. Frontière entre code interne et système externe sur lequel on a peu de contrôle. Cadres dominants RED (durée aller-retour, taux d'erreur, taux d'appel) plus USE sur ressources internes consommées.

**Set canonique** — événements de cycle de vie de la connexion (`discovery`, `pairing`, `session_opened`, `session_closed`, `signal_lost`, `reconnected`) ; codes de retour natifs avec notation canonique stable (`hr=0x{hex}` HRESULT, `result=<int>` mmsys, `status=<int>` HTTP, `mmsys=<int>` waveIn) ; identifiants tronqués ou masqués pour les secrets (`username=eDOvxk-...`, `clientkey=[redacted]`) ; latence aller-retour (`rtt_ms`) ; ressources consommées (`http_clients`, `socket_pool`).

**État actuel** — `DeckleLightingSource` (40 events) couvre bien tout le cycle Hue : discovery, pairing, control, EventStream, identify, color push. La discipline de masquage des secrets (clientkey jamais en clair, username tronqué) est tenue. `DeckleLlmSource` instrumente les états Ollama (`OllamaBusy`, polling `/api/ps`). `DeckleAudioSource` couvre les anomalies waveIn par codes `mmsys`. Une normalisation transverse manque — il n'y a pas de pattern uniforme `HttpRequestCompleted(verb, endpoint, status, rtt_ms, retry_count)` réutilisable.

### Classe 5 — Surface UI et navigation

Page settings ouverte, dialog confirmé, formulaire validé, navigation NavView, ViewModel setter qui change une valeur, page chargée prête, page failed to init. Cadres dominants Four Golden Signals adaptés (latence perçue, taux d'actions par session, erreurs visibles) plus RED sur opérations déclenchées utilisateur.

**Set canonique** — transitions d'état UI en jalons concis (`Page loaded`, `Dialog opened`, `Form validated`) ; détails techniques en Verbose miroir (`page=Llm | duration_ms=120 | items=5`) ; UserFeedback adressé à l'utilisateur via le canal canonique séparé `UserFeedbackEmitted` au contrat strict `(severity, title, body, role)`.

**État actuel** — `DeckleSettingsSource` est l'exemple riche, 46 events couvrant navigation NavView, ViewModel setters, backup/restore, folder picker, setup wizard. L'event générique paramétré `SettingChanged(string, string, string)` est l'entorse acceptée à la discipline strict-typed — un setter générique du MVVM ne sait pas distinguer 30 setters distincts au site d'appel.

### Classe 6 — Windowing

Positionnement et dimensionnement de toute fenêtre WinUI 3 ou Win32 — `HudWindow` (320×64 bas-centre), `HudOverlayWindow`, `HudMessage` hybrid bleed (400×160 puis retract 272×78), `SettingsWindow`, `LogWindow`, `SetupWindow`, popup tray menu, popup folder picker. Tous ces sites calculent à la main une position en DIP, multiplient par `GetDpiForWindow(hwnd) / 96.0`, choisissent un `DisplayArea` ou un `MonitorFromPoint`, gèrent le multi-écran.

**Set canonique** :

- `hmon=0x{hex}` — handle moniteur retourné par `MonitorFromPoint` ou `GetMonitorInfo`.
- `dpi=192` — entier, résultat `GetDpiForWindow`.
- `scale=2.0` — une décimale, dérivé `dpi/96`.
- `work_area=2560,40,2520,1392` — rect en pixels écran absolus (x, y, w, h).
- `cursor=1240,860` — pixels écran absolus, retour `GetCursorPos`.
- `anchor=BottomCenter` — ancrage choisi côté settings.
- `pos=1100,820 size=320,64` — rect calculé en pixels écran absolus (convention fixée par cette doctrine pour permettre la reverse via `dpi`).
- Pour les overlays empilés : `slot=0` ou `slot=1`.
- Pour les popups : `parent_rect=x,y,w,h` du contrôle ancré.

Convention de coordonnées — pixels écran absolus partout. Les calculs internes peuvent partir de DIP, mais les events émis pour observation portent les valeurs en pixels, cohérent avec ce que retournent `GetCursorPos`, `GetWindowRect`, `GetMonitorInfo`, et permet de reverse vers DIP via `dpi`.

**État actuel** — non observé. Le HUD a un seul `HudWarning(string)` paramétré par message libre. `SettingsWindow`, `LogWindow`, `SetupWindow` n'émettent rien sur leur positionnement. `TrayIconManager` ne loggue ni position icône ni position popup. Classe à câbler progressivement sur les sites de positionnement existants — chantier suivi en mémoire roadmap.

### Classe 7 — Activité utilisateur

Hotkey pressé, entrée tray cliquée, toggle settings changé, page settings ouverte manuellement. Cadre dominant RED sur opérations déclenchées.

**Set canonique** — déclencheur (`trigger=hotkey:WinTilde | tray:Quit | settings:OllamaModel`), résultat (`outcome=triggered|ignored:busy|ignored:not-configured`), valeur avant et après pour un toggle (`before=true after=false`).

**État actuel** — `DeckleShellSource` couvre les hotkeys (`HotkeyRegistered`, `HotkeyToggleIgnored`). `DeckleAppSource` couvre `HotkeyStart`, `HotkeyStop`, `HotkeyNoProfile`. `DeckleSettingsSource` couvre les setters via `SettingChanged` générique. Cohérent mais éclaté entre trois providers — Shell pour la primitive, App pour l'orchestration, Settings pour la modification de valeur. Correct doctrinairement (« l'observation s'attache au module qui contient l'opération »), un peu lourd à recoller mentalement quand on lit la LogWindow.

### Classe 8 — Persistance settings per-module

Chaque module qui a des settings (`Audio`, `Transcription`, `Llm`, `Lighting.Ambient`, …) charge et persiste via `JsonSettingsStore<T>` sous `<UserDataRoot>/modules/<name>/settings.json`. Quatre events transitoires partagent le pattern : `SettingsLoaded`, `SettingsLoadComplete`, `SettingsLoadWarning`, `SettingsLoadError`, tous paramétrés par message string libre.

**Set canonique cible** — `module=<name>`, `path=<abs>`, `outcome=loaded|defaulted|migrated|failed`, `size_bytes=<n>`, `version=<schema>`, durée `load_ms=<n>`, raison si échec (`reason=missing|corrupt|migration_failed`).

**État actuel** — entorse documentée. Le delegate `Action<string>` de `JsonSettingsStore` ne sait pas distinguer au site d'appel entre « Settings loaded », « Settings initialized (defaults) » et « Settings reloaded from disk ». La discipline strict-typed est temporairement échangée contre un typage par niveau et keyword. Refonte propre quand `SettingsHost` / `JsonSettingsStore` basculeront eux-mêmes sur un contrat EventSource direct.

### Classe 9 — Crash et safety nets

`Application.UnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`. Trois filets posés au constructeur de `App`. Capture exception type, message, stack trace, contexte (handler invoqué, thread).

**Set canonique** — `source=app|appdomain|task-scheduler`, `ex_type=System.Foo.Bar`, `ex_message=<short>`, `stack=<multi-line ou indiqué via event séparé>`, `thread_id=<n>`, `terminating=true|false` (pour AppDomain).

**État actuel** — `DeckleAppSource` porte les 4 events `CrashUnhandled`, `CrashAppDomain`, `CrashTaskScheduler`, `CrashStackTrace`. Pattern bien tenu — la stack trace est sur un event séparé pour ne pas exploser la signature primaire.

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
