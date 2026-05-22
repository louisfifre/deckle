# Deckle — Taxonomie nomenclature (compagnon)

Fichier compagnon de `SKILL.md`. Chargé à la demande quand la doctrine seule ne suffit pas et qu'il faut le détail tabulé : suffixes admis et leur sémantique, patterns x:Uid détaillés, structure canonique d'un provider EventSource, exemples bons et mauvais.

## Suffixes admis — sémantique précise

### Suffixes BCL et GoF canoniques

`Attribute` — sous-classe de `System.Attribute`. Le nom du type **doit** se terminer ainsi (CA1019, convention de la BCL).

`EventArgs` — sous-classe de `System.EventArgs` portant la charge utile d'un événement. Le suffixe est obligatoire.

`Exception` — sous-classe d'une exception. Suffixe obligatoire. Exemple Deckle potentiel : `WhisperRuntimeException`.

`Stream`, `Reader`, `Writer` — pour les types qui implémentent ou prolongent `System.IO.Stream` / `TextReader` / `TextWriter` ou leurs équivalents. Le pattern Reader/Writer s'applique aussi à du métier non-IO quand la responsabilité est strictement lecture ou écriture isolée.

`Collection`, `Dictionary`, `List` — pour les types qui sont eux-mêmes des collections. Pas pour les types qui contiennent une collection (`Inventory` plutôt que `ItemsCollection`).

`Builder` — construction complexe pas à pas avec API fluent. Le `Builder` produit une instance finale via une méthode terminale (`Build()`, `Create()`).

`Factory` — produit des instances d'un type. Suffixe canonique en GoF.

`Service` — orchestration métier statelessment ou faiblement stateful. Exemples Deckle : `TelemetryService`, `LogService`, `LlmService`, `OllamaService`, `ScreenCaptureService`, `*SettingsService`.

`Provider` — produit des valeurs ou configurations à la demande sans orchestrer. Exemples BCL : `TimeProvider`, `ConfigurationProvider`, `AuthenticationStateProvider`. Distinction d'avec `Service` : un Provider est passif côté métier — il répond, il n'orchestre pas.

`Repository` — abstraction sur la persistance d'un agrégat. Conventions DDD. Peu pertinent pour Deckle qui n'a pas de stockage relationnel.

`Store` — état mutable centralisé pour un sous-système. Le Store est l'endroit qui détient l'état actuel ; les lecteurs y interrogent, les mutations passent par lui. Exemples Deckle potentiels : `AmbientStateStore`.

`Strategy`, `Visitor`, `Specification` — patterns GoF. Suffixe canonique quand le pattern est effectivement appliqué (sinon c'est juste un méthode polymorphe).

### Suffixes Deckle-spécifiques stabilisés

`Engine` — orchestre un pipeline métier complexe avec son propre cycle de vie. Plus lourd qu'un `Service`. Exemples : `WhispEngine` (transcription complète du hotkey au paste), `AmbientEngine` (capture → analyse → push Hue en boucle). Critère : si l'orchestration mérite son propre cycle Start/Stop public et porte plusieurs étapes coordonnées en interne, c'est un Engine.

`Host` — adapter qui pontifie une frontière (process, thread, interop COM, isolement de module). Exemples : `MessageOnlyHost` (Win32 message-only window qui héberge tray et hotkeys), `AppWhispEngineHost` (implémentation app de `IWhispEngineHost` qui fournit les settings et bridges au moteur). Le Host n'a pas de logique métier — il traduit ou héberge.

`Mapper` — transformation pure entre deux représentations. Sans effet de bord, sans état. Exemples : `AudioLevelMapper` (dBFS → niveau perceptuel `[0, 1]`), `WhisperParamsMapper` (POCO settings → struct natif). Critère : si la fonction principale est `(In) → Out`, c'est un Mapper.

`Calculator` — calcul stateless récapitulatif sur un dataset ponctuel. Exemples : `MicrophoneTelemetryCalculator` (RMS samples → p10/p50/p90/peak), `MicrophoneCalibrationCalculator` (sessions historique → bornes dBFS). Distinct du Mapper par la nature agrégative.

`Detector` — classifieur binaire d'une condition. Exemple : `RepetitionDetector`. Renvoie typiquement un `bool` ou un `DetectionResult`.

`Bootstrap` — code de migration ou de provisioning au démarrage, à exécution unique. Exemple : `SettingsBootstrap.MigrateLegacyToPerModule()`. Non-standard mais cohérent avec son rôle one-shot.

### Tableau de désambiguïsation

| Suffixe        | Quand              | Critère discriminant                                      |
| -------------- | ------------------ | --------------------------------------------------------- |
| `Service`      | Orchestration      | Coordonne plusieurs collaborateurs pour produire un effet |
| `Provider`     | Production passive | Répond à des requêtes sans orchestrer                     |
| `Engine`       | Pipeline lourd     | Cycle Start/Stop, plusieurs étapes coordonnées            |
| `Host`         | Frontière          | Adapte ou héberge entre deux mondes (interop, isolement)  |
| `Mapper`       | Transformation     | Fonction pure `(In) → Out`, sans état                     |
| `Calculator`   | Agrégation         | Calcul récapitulatif sur dataset, sans état persistant    |
| `Store`        | État               | Source de vérité mutable centralisée                      |
| `Reader`       | Lecture pure       | Requête sur une source sans mutation                      |
| `Factory`      | Instanciation      | Produit des instances d'un type                           |
| `Builder`      | Construction       | Construction pas à pas avec API fluent                    |
| `Detector`     | Classification     | Renvoie un verdict binaire sur une condition              |

## Suffixes à éviter

`Manager` — débordement d'une classe devenue trop grosse. La « gestion » s'extrait au lieu de refactoriser la responsabilité initiale, et le Manager finit par connaître trop d'internes de la classe gérée. Cas tolérés : interop Windows avec cycle de vie de handle système (`TrayIconManager`, `HotkeyManager` historiques). Pour du code applicatif neuf, préférer le rôle précis (`Registry`, `Watcher`, `Coordinator`, `Store + Reader`).

`Helper` — la classe principale n'est pas autosuffisante. Si `Foo` et `FooHelper` coexistent, ils changent ensemble et trahissent une mauvaise cohésion. Réintégrer dans `Foo` ou nommer la vraie responsabilité (`FooParser`, `FooRenderer`).

`Utility` / `Util` / `Utils` — réceptacle de fonctions qu'on n'a pas su rattacher à un objet. Exige du tribal knowledge pour savoir quelle méthode statique vit où. Reformuler en classe qui porte une responsabilité nommable.

`Wrapper` générique — ambigu. Préférer `Adapter` quand on traverse une frontière (interface externe → interface interne), `Decorator` quand on enrichit un comportement, ou nommer ce que le wrapper apporte (`CachingX`, `LoggingX`, `ThrottledX`).

`Handler` sans contexte — admissible dans un pipeline middleware nommé (`HttpMessageHandler`, `CommandHandler` en CQRS), suspect ailleurs. Pour Deckle, préférer des verbes plus précis.

`Processor`, `Worker` — vagues. Demandent contexte. À utiliser uniquement si le domaine impose le terme (background worker, message processor d'une queue).

## Préfixes booléens

| Préfixe    | Usage                               | Exemple                                  |
| ---------- | ----------------------------------- | ---------------------------------------- |
| `Is`       | État ou nature                      | `IsEnabled`, `IsReadOnly`, `IsConnected` |
| `Has`      | Présence ou accumulation            | `HasChildren`, `HasErrors`, `HasFocus`   |
| `Can`      | Permission ou capacité conditionnel | `CanSeek`, `CanExecute`, `CanWrite`      |
| `Should`   | Recommandation déclarative          | `ShouldSerialize`, `ShouldRetry`         |
| `Are`      | Assertion collective                | `AreEqual`, `AreAllValid`                |
| `Supports` | Capacité forte                      | `SupportsAsync`, `SupportsCancellation`  |
| `Allows`   | Permission lâche                    | `AllowsMultipleSelection`                |

Anti-patterns à proscrire dans Deckle :
- `IsNot*`, `Cant*`, `Hasnt*` (négations dans le nom)
- `Flag`, `Mode`, `Status` sans verbe (n'indique rien)
- `Before*` / `After*` pour événements (CA1713 le proscrit explicitement)

## Préfixes structurels

`I` pour interfaces (`ITelemetrySink`, `ILightOutput`, `IWhispEngineHost`).

`T` pour génériques sans rôle particulier ; `T<Role>` quand le générique a un rôle nommé (`TKey`, `TValue`, `TSession`). Règle CA1715.

`_` pour champs privés d'instance (`_engine`, `_settings`). Convention `dotnet/runtime`.

`s_` pour champs statiques privés (`s_instance`, `s_currentLog`). Convention `dotnet/runtime`.

`t_` pour `[ThreadStatic]` privés.

`On` pour méthode raise d'événement protected virtual côté émetteur (`OnChanged`, `OnFrameArrived`). **Réservé** à la méthode raise — un handler côté abonné est nommé par son intention, pas par `On*`.

## x:Uid — pattern et exemples

Format Deckle : `<UidScope>_<Element>` ou `<UidScope>` quand l'élément est implicite. Le scope est typiquement le nom de page ou de dialog. Les entrées `.resw` portent ce nom suivi d'un point et de la propriété XAML ciblée.

Exemples vus dans le code Deckle :

```
Common_Cancel.Text                          (bouton Cancel cross-dialog)
Common_Back.Text                            (bouton Back cross-dialog)
FolderPickerCard_PickButton.Content         (label du bouton Browse)
CorpusConsent_Title.Text                    (titre du dialog de consentement corpus)
WhisperPage_HeaderText.Text                 (header H1 de la page Whisper)
```

Côté XAML, la consommation se fait par `x:Uid` placé sur l'élément :

```xml
<TextBlock x:Uid="WhisperPage_HeaderText"/>
<Button x:Uid="Common_Cancel"/>
```

Le système PRI résout `WhisperPage_HeaderText.Text` depuis `Resources.resw` automatiquement. Plusieurs propriétés du même élément peuvent être localisées en parallèle (`Greeting.Text`, `Greeting.AutomationProperties.Name`, `Greeting.Width`).

Règles invariantes :
- Une clé envoyée en traduction ne change plus. Un renommage de clé déclenche un cycle de retraduction et est traité comme un changement de contrat.
- Un `Resources.resw` unique par module sous `Strings/en-US/`. Pas de découpage par page à l'intérieur du module.
- Les clés sont case-insensitive côté PRI mais on les écrit PascalCase pour préserver la lisibilité.

## Theme resources WinUI — vocabulaire fonctionnel

Catégories canoniques par sémantique fonctionnelle (jamais par valeur de couleur ou de taille).

### Brushes de fond
- `LayerFillColorDefaultBrush` — fond de calque (carte, panneau)
- `CardBackgroundFillColorDefaultBrush` — fond de card
- `SolidBackgroundFillColorBaseBrush` — fond solide page
- `SolidBackgroundFillColorSecondaryBrush` — fond solide secondaire
- `SubtleFillColorTransparentBrush` — fond hover/pressed subtil

### Brushes de stroke
- `CardStrokeColorDefaultBrush` — bordure de card
- `ControlStrokeColorDefaultBrush` — bordure de contrôle
- `DividerStrokeColorDefaultBrush` — séparateur

### Brushes de texte
- `TextFillColorPrimaryBrush` — texte principal
- `TextFillColorSecondaryBrush` — texte secondaire
- `TextFillColorTertiaryBrush` — texte tertiaire (placeholder, hint)
- `TextFillColorDisabledBrush` — texte désactivé

### Coins arrondis
- `OverlayCornerRadius` — pour les surfaces overlay (popups, menus, HUD, dialogs)
- `ControlCornerRadius` — pour les contrôles standards

### Theme resources Deckle locales

Convention : `<Domain>.<Descriptor>.<Variant>` avec suffixe de type. Exemples projet :
- `Hud.Glow.BrushDefault` — brush du glow HUD en état nominal
- `Hud.Glow.BrushAlert` — brush du glow HUD en alerte
- `Ambient.Preview.StrokeBrush` — stroke d'un overlay de zone Ambient

Le préfixe de domaine est un identifiant Deckle reconnaissable. Éviter les préfixes génériques (`App`, `Custom`). Les theme resources locales vivent dans `Themes/<Domain>.xaml` du module concerné.

## EventSource — structure canonique d'un provider

```csharp
[EventSource(Name = "Deckle-Whisp-Engine")]
public sealed class WhispEngineEventSource : EventSource
{
    public static readonly WhispEngineEventSource Log = new();

    public static class Keywords
    {
        public const EventKeywords Lifecycle = (EventKeywords)0x0001;
        public const EventKeywords Transcription = (EventKeywords)0x0002;
        public const EventKeywords Capture = (EventKeywords)0x0004;
        public const EventKeywords HighVolume = (EventKeywords)0x0008;
    }

    public static class Tasks
    {
        public const EventTask Transcribe = (EventTask)1;
        public const EventTask LoadModel = (EventTask)2;
    }

    [Event(1, Level = EventLevel.Informational, Keywords = Keywords.Lifecycle)]
    public void AppStarted(string version) => WriteEvent(1, version);

    [Event(2, Level = EventLevel.Informational, Keywords = Keywords.Transcription,
           Task = Tasks.Transcribe, Opcode = EventOpcode.Start)]
    public void TranscribeStart(string modelId) => WriteEvent(2, modelId);

    [Event(3, Level = EventLevel.Informational, Keywords = Keywords.Transcription,
           Task = Tasks.Transcribe, Opcode = EventOpcode.Stop)]
    public void TranscribeStop(int segmentCount, int durationMs) => WriteEvent(3, segmentCount, durationMs);
}
```

Règles structurelles :
- Type `sealed`, héritage direct de `EventSource`.
- Nom de provider via `[EventSource(Name = "Deckle-<Composant>")]`, jamais hérité du nom C#. Pas de point dans le nom — `-` est le séparateur ETW canonique.
- Singleton `public static readonly Log = new()`.
- `Keywords` imbriquée, constantes `EventKeywords` avec bits distincts (bits 48-63 réservés Microsoft).
- `Tasks` et `Opcodes` imbriqués, constantes `EventTask` / `EventOpcode`.
- Paires Start/Stop adjacentes avec IDs consécutifs, mêmes `Task`, opcodes `EventOpcode.Start` / `EventOpcode.Stop`.
- Noms d'événements au passé pour faits accomplis (`AppStarted`, `ModelLoaded`), pattern `XStart`/`XStop` pour les unités de travail mesurées.
- Keywords nommés par domaine fonctionnel (`Lifecycle`, `Transcription`, `Capture`), pas par module ni par technique.

## Exemples commentés — bons et mauvais

### Modules

Bon : `Deckle.Audio` — nom de capability métier suffisamment large pour absorber les évolutions (capture, TTS futur, monitoring loopback) sans devenir un faux générique.

Bon : `Deckle.Lighting.Ambient` — hiérarchie qui reflète la relation (Ambient est un consumer de Lighting).

Suspect : `Deckle.Core` — admissible **tant que** la responsabilité reste « fondations cross-module sans dépendance applicative ». Si le contenu déborde, scinder vers des modules nommés (`Deckle.Interop`, `Deckle.Storage`).

À éviter : `Deckle.Common`, `Deckle.Shared`, `Deckle.Utilities`, `Deckle.Misc`. N'engage personne, fait diverger les contrats.

### Classes

Bon : `WhispEngine` — orchestre un pipeline complexe (capture, VAD, transcription, rewrite, paste) avec son propre cycle de vie. Suffixe Engine pertinent.

Bon : `AudioLevelMapper` — transformation pure dBFS → `[0, 1]`. Suffixe Mapper pertinent.

Bon : `MicrophoneCalibrationCalculator` — calcul agrégatif sur historique de sessions. Suffixe Calculator pertinent.

Suspect : `TrayIconManager`, `HotkeyManager` — suffixe `Manager` toléré par héritage interop. Pour du code neuf, préférer `TrayIconRegistry` + `TrayIconReader` ou `TrayIconController` selon le découpage.

Suspect : `HudOverlayWindow` versus `HudWindow` — deux classes qui partagent l'essentiel. Soit factoriser et garder un seul nom, soit renommer pour expliciter la différence de rôle (`HudWindow` + `HudShadowOverlay` par exemple).

Suspect : `AppWhispEngineHost`, `AppAmbientEngineHost` — suffixe `Host` utilisé pour un adapter d'implémentation côté app. Cohérent avec `IWhispEngineHost`/`IAmbientEngineHost` qui définissent la frontière, mais à surveiller — un adapter qui grossit deviendrait un `AppWhispEngineFacade` ou `AppWhispBridge`.

À éviter : `WaveInRecorder` — décrit l'implémentation (WaveIn = API Win32). Préférer `MicrophoneCapture` (responsabilité métier).

### Méthodes et propriétés

Bon : `MicrophoneCapture.RecordAsync(IAudioRecordingHost, CancellationToken)` — verbe d'action, suffixe Async, paramètre d'host explicite.

Bon : `IsConnected`, `HasFocus`, `CanExecute` — préfixe verbe + état explicite.

À éviter : `Flag`, `Status`, `Mode` comme booléen sans verbe. Nommer ce qui est vrai.

À éviter : `IsNotEmpty`, `CantSeek` — négations dans le nom.

### Événements

Bon : `StatusChanged`, `TranscriptionFinished`, `FrameArrived` — participe passé pour fait accompli.

Bon : `Closing` pour preview cancelable côté Window.

À éviter : `BeforeTranscribe`, `AfterFrame` — règle CA1713.

À éviter : `OnTranscriptionFinished` comme **nom d'event public** — `On*` est réservé à la méthode raise sur le sender.

### Champs

Bon : `private readonly WhispEngine _engine;`

Bon : `private static readonly object s_lock = new();`

À éviter : `private readonly WhispEngine engine;` (manque le `_` qui signale la portée privée d'instance).

À éviter : `private static WhispEngine instance;` (manque le `s_`).
