---
name: deckle-nomenclature
description: Doctrine de nomenclature pour le projet Deckle (Windows .NET 10 / WinUI 3). Porte les règles de casing et de préfixes alignées sur les Framework Design Guidelines, la position assumée du projet sur les suffixes admis versus les suffixes flous à proscrire, la convention de namespaces miroir des dossiers, les patterns x:Uid et theme resources WinUI, et la discipline de renommage progressive. La taxonomie détaillée (suffixes tabulés, patterns x:Uid, structure EventSource, exemples commentés) vit dans le fichier compagnon taxonomie.md chargé à la demande. Triggers on phrases like nomenclature deckle, comment je nomme deckle, renommer deckle, convention nommage deckle, suffixe deckle, namespace deckle, audit nomenclature deckle, x:Uid deckle, EventSource provider deckle, ambiguïté Service Manager deckle.
---

# Deckle — Doctrine de nomenclature

## Rôle

Skill projet-spécifique qui répond à une question récurrente : **quel nom mérite ce symbole, ce fichier, ce dossier, cette ressource, ce provider**. S'invoque avant d'introduire un nouveau type ou une nouvelle ressource, avant un renommage non trivial, et au moment d'auditer une zone du dépôt dont la nomenclature a dérivé.

La doctrine couvre toutes les surfaces nommées du projet — modules, namespaces, classes, méthodes, propriétés, champs, événements, paramètres, dossiers, fichiers, clés `.resw` et `x:Uid`, theme resources WinUI, providers et événements `EventSource`, vocabulaire `LogSource`. Elle ne décrit pas la structure des modules — cela relève de `deckle-modularite` — ni la rédaction des écritures lisibles de logging — cela relève de `deckle-logging`. Ici on s'occupe uniquement du choix des noms. L'objectif est qu'un agent qui découvre un fichier puisse reconstruire la responsabilité à partir des noms seuls, et qu'une décision de nommage soit prise en référence à une doctrine, pas par mimétisme du code voisin qui peut être de la dette.

## Le nom décrit la responsabilité, pas l'implémentation

Un nom dit **ce que le symbole est responsable de faire ou de représenter**, jamais comment il le fait. `ScreenCaptureService` est légitime parce que la responsabilité — fournir des frames d'écran à la demande — est nommée ; `WaveInPollingLoopRunner` serait au contraire un nom qui se périme au prochain changement de backend. Quand un nom porte un détail d'implémentation (framework, pattern d'appel, mécanisme interne), c'est un signal qu'il faut remonter d'un cran. Conséquence sur les renommages : une implémentation invisible aux consommateurs ne force pas un renommage ; un changement de responsabilité publique en exige un — c'est ce qui a justifié `Deckle.Capture → Deckle.Audio` et `Deckle.Localization → Deckle.Catalog`.

## Vocabulaire fermé par dimension

Plusieurs dimensions portent un vocabulaire **fermé** dont les éléments sont décidés une fois et réutilisés à l'identique : sources d'observation `LogSource.*`, suffixes admis pour les classes, préfixes booléens, noms de modules et namespaces. Dans une dimension fermée, on choisit dans le vocabulaire existant ou on l'étend par décision tracée — pas d'invention ad hoc. Un cas réel qui ne tient pas dans l'existant est l'occasion d'étendre proprement ou de reformuler la responsabilité pour qu'elle entre dans une catégorie déjà nommée.

## Casing et préfixes

Les règles de casing suivent les [Framework Design Guidelines](https://learn.microsoft.com/dotnet/standard/design-guidelines/capitalization-conventions) et le guide [C# identifier naming rules](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names). **PascalCase** pour tout ce qui est visible (namespaces, types, méthodes, propriétés, événements, champs publics, constantes, valeurs d'énum, paramètres positionnels de records). **camelCase** pour paramètres et locals, et pour les paramètres positionnels de classes et structs. Aucune notation hongroise, aucun tiret ni underscore dans les identifiants publics.

Pour les **champs privés**, la convention adoptée est celle du [.NET Runtime coding style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md) — `_camelCase` pour l'instance, `s_camelCase` pour le statique privé, `t_camelCase` pour `[ThreadStatic]`. Ce n'est pas dans les Framework Design Guidelines historiques mais c'est la pratique vivante de Microsoft sur son propre runtime, et Deckle l'adopte pour aligner et signaler visuellement la portée.

Acronymes de deux lettres en majuscules (`IOStream`, `DbContext`), trois lettres et plus en PascalCase (`Xml`, `Json`, `Html`). Conséquence Deckle : `Llm`, `Hud`, `Vad` dans le code — la forme majuscule familière reste valide en commentaires et en logs lisibles. Interfaces préfixées `I`, génériques préfixés `T` (règle [CA1715](https://learn.microsoft.com/visualstudio/code-quality/ca1715)), méthodes asynchrones suffixées `Async` sans exception.

## Suffixes — admis et à éviter

Le détail tabulé vit dans `taxonomie.md` avec leur sémantique précise et exemples. Trois familles à retenir au niveau doctrine.

**Suffixes canoniques admis** — `Attribute`, `EventArgs`, `Exception`, `Stream`, `Reader`, `Writer`, `Collection`, `Builder`, `Factory`, `Service`, `Provider`, `Repository`, `Store`, `Strategy`, `Visitor`. Tous portent une sémantique reconnue de la BCL ou des patterns GoF.

**Suffixes Deckle-spécifiques stabilisés** — `Engine` pour un pipeline métier complexe avec cycle de vie, `Host` pour un adapter qui pontifie une frontière (interop, isolement), `Mapper` pour une transformation pure `(In) → Out`, `Calculator` pour un calcul stateless agrégatif, `Detector` pour un classifieur binaire d'une condition. Ajouter un nouveau suffixe au vocabulaire fermé suppose une responsabilité nommable en une phrase et une décision tracée.

**Suffixes à éviter dans le code applicatif neuf** — `Manager`, `Helper`, `Utility`/`Util`/`Utils`, `Wrapper` générique, `Handler` sans contexte pipeline. La position est documentée côté communauté .NET (voir [Name Smells](https://daedtech.com/name-smells/)). `Helper` indique que le type principal n'est pas autosuffisant ; `Manager` signale typiquement un débordement non refactoré ; `Utils` est le réceptacle des fonctions sans foyer. Pour Deckle, `TrayIconManager` et `HotkeyManager` sont des cas hérités d'interop Windows admis par dérogation explicite — tout nouveau code préfère le rôle précis (`Registry`, `Store + Reader`, `Coordinator`).

**Désambiguïsation Service / Provider / Engine / Host**. Un `Service` orchestre ; un `Provider` répond passivement ; un `Engine` orchestre un pipeline lourd avec cycle de vie propre ; un `Host` adapte ou ponte une frontière. Quand deux suffixes paraissent applicables au même type, c'est généralement qu'il porte deux responsabilités — décomposer.

## Booléens, collections, événements

Booléens préfixés par un verbe d'état ou de capacité — `Is*`, `Has*`, `Can*`, `Should*`, `Are*`, `Supports*`, `Allows*`. Le préfixe est requis dans Deckle pour lever l'ambiguïté avec un type ou une méthode du même nom (la position « optionnel » des Framework Design Guidelines est durcie ici). Négations dans le nom proscrites — `CanSeek`, pas `CantSeek` ; pas de double négation. Booléens sans verbe (`Flag`, `Status`, `Mode`) n'indiquent rien — nommer ce qui est vrai.

Collections au pluriel (`Items`, `Subscribers`, `Sinks`), élément unique au singulier. Énumérations non-flags au singulier, flags au pluriel. Namespaces au pluriel quand sémantiquement juste (`Strings`, `Controls`, `Converters`, `ViewModels`), au singulier pour les agrégats fonctionnels (`Engine`, `Setup`, `Telemetry`).

Événements au passé pour le fait accompli (`Changed`, `Stopped`, `FrameArrived`, `TranscriptionFinished`), au participe présent pour la preview cancelable (`Changing`, `Closing`). La règle [CA1713](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1713) proscrit `Before*` et `After*`. La méthode raise associée porte le préfixe `On` (pattern protected virtual canonique) ; ce préfixe est **réservé à la méthode raise sur l'émetteur** — un handler côté abonné est nommé par son intention, pas par `On*`.

## Namespaces et frontières de modules

Le namespace **miroite la hiérarchie des dossiers** — un fichier sous `Engine/` déclare `<Module>.Engine`. La doc [Program organization](https://learn.microsoft.com/dotnet/csharp/fundamentals/program-structure/program-organization) appelle violer cette convention « actively confusing ». L'organisation à l'intérieur d'un module se fait par feature, pas par stéréotype technique, sauf quand le module est petit et que le stéréotype reste lisible (`Controls`, `Converters`, `Strings`, `Themes`).

Les namespaces génériques flous sont à éviter — `Common`, `Shared`, `Utilities`, `Helpers`, `Misc`. Le projet préfère nommer la capability réelle. Cas particulier de `Deckle.Core` : admissible **tant que** sa responsabilité reste « fondations cross-module sans dépendance applicative » et que sa surface publique reste étroite — sinon scinder ou renommer.

Le choix sous-namespace versus sous-projet suit `deckle-modularite`. La règle synthétique : un sous-namespace tant que le cycle de déploiement et le graphe de dépendance restent simples ; un sous-projet quand une frontière acyclique, un cycle de test isolé, ou un volume problématique le justifient. Le nom du sous-projet reflète la capability métier (`Deckle.Lighting.Ambient`), pas le stéréotype.

## Ressources WinUI et localisation

Trois directives XAML ne se confondent jamais. `x:Name` identifie un élément pour le code-behind (PascalCase, unique par namescope). `x:Key` est la clé d'un `ResourceDictionary`. `x:Uid` est la clé de **localisation** côté PRI — distincte du XAML namescope.

Les clés `.resw` suivent le pattern `<Scope>_<Element>.<Property>` ou `<Scope>.<Property>`, avec scope par page ou par dialog (`WhisperPage_HeaderText.Text`, `CorpusConsent_Title`, `Common_Cancel`). Un `Resources.resw` unique par module sous `Strings/en-US/`. Une clé envoyée en traduction ne change plus — un renommage déclenche un cycle de retraduction et se traite comme un changement de contrat. Voir `taxonomie.md` pour les exemples détaillés.

Les **theme resources** WinUI sont nommées par leur sémantique fonctionnelle, jamais par valeur — `LayerFillColorDefaultBrush`, `CardStrokeColorDefaultBrush`, `OverlayCornerRadius`, `ControlCornerRadius`. Toute valeur littérale dans le XAML qui devrait suivre le thème est un signal de mauvaise primitive (voir doctrine racine). Pour les theme resources Deckle locales, convention `<Domain>.<Descriptor>.<Variant>` avec suffixe de type (`Hud.Glow.BrushDefault`), domaine reconnaissable, vivant sous `Themes/<Domain>.xaml` du module concerné.

## Observabilité typée et providers

Quand Deckle bascule sur EventSource (chantier suivi par `deckle-logging`), le **nom de provider** suit `Deckle-<Composant>` avec `-` comme séparateur (jamais point — collision ETW). Le nom est défini via `[EventSource(Name = "...")]`, pas hérité du nom C#. Le singleton `public static readonly Log = new()`, type `sealed` héritant directement de `EventSource`.

Événements au passé pour les faits accomplis (`ModelLoaded`, `AppStarted`), paires `XStart`/`XStop` adjacentes avec IDs consécutifs pour les unités de travail mesurées. Keywords nommés par domaine fonctionnel (`Lifecycle`, `Transcription`, `Capture`), pas par module ni par technique. Structure canonique complète avec code de référence dans `taxonomie.md`.

Le **vocabulaire fermé `LogSource`** reste pertinent même quand le moteur sous-jacent bascule sur EventSource — c'est la dimension « catégorie d'événement » exposée côté UI. Le mapping `LogSource ↔ Keywords` doit être explicite et tracé. Les sources hiérarchiques (`SET.WHISPER`, `SET.GENERAL`) utilisent le point comme séparateur de niveau, distinct du format de nom de provider.

## Renommage et hygiène progressive

Un renommage non trivial est un changement de contrat — il se fait quand la responsabilité a effectivement bougé ou quand une dérive passée est consciemment corrigée. **Module par module au moment où le module est touché**, jamais en passe géante centralisée. Cette discipline rejoint celle des commentaires (`deckle-docs`).

Trois signaux invitent à reconsidérer un nom existant. Le nom **décrit l'implémentation** plutôt que la responsabilité. Le nom porte un **suffixe flou** alors que la responsabilité est précise et nommable autrement. Deux noms **se ressemblent au point d'être confondus** (cas `HudWindow` et `HudOverlayWindow` qui partagent l'essentiel — soit factoriser, soit renommer pour expliciter la différence de rôle).

Un renommage tracé laisse une **entrée dans le journal du module** concerné avec l'ancien nom, le nouveau, ce qui a déclenché le changement. Les renommages historiques (`Deckle.Capture → Deckle.Audio`, `Deckle.Localization → Deckle.Catalog`) sont les exemples canoniques.

## Pointeurs

- **`taxonomie.md`** dans ce skill — détail tabulé des suffixes, patterns x:Uid, structure EventSource avec keywords et tasks, exemples bons et mauvais commentés. Chargé à la demande.
- **`deckle-logging`** — vocabulaire `LogSource`, niveaux d'écriture, procédure pour décider quoi observer.
- **`deckle-modularite`** — où s'arrête un module, quand éclater en sous-projet.
- **`deckle-docs`** — convention documentaire et hygiène des commentaires ; un renommage non trivial laisse trace dans le journal du module.
- **`personal-conventions`** — conventions cross-projet (langue, wording, git, worktrees). `deckle-nomenclature` applique ces conventions pour le contexte .NET / WinUI 3.
