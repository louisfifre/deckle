---
description: "Où passe le temps de build et de test de Deckle (mesuré le 2026-11-06/07 sur le worktree build-time-49b2a9) : le compilateur XAML des 17 projets WinUI pèse ~62 % d'un build propre et refait tout son travail à chaque build, défaut connu en amont (microsoft-ui-xaml #11264) que deux propriétés non documentées contournent (build sans changement 115 s → 44 s) ; coût d'un projet simple négligeable ; construire seulement le module touché (4 s au lieu de 115 s) ; 37 suites de tests, 83 à 130 s, dont des attentes et des tests d'appareillage vérifiés ; quatre propositions classées avec gain mesuré ou estimé, effort et coût de conception."
---

# Temps de build et de test — où il passe, ce qu'on peut en faire

Recherche de phase 1, suivie d'un approfondissement demandé par le mainteneur : mesurer, proposer, juger. Rien n'est refactoré, rien n'est fusionné dans `main`, `AGENTS.md` n'est pas modifié. Le worktree `D:\worktrees\deckle\build-time-49b2a9` (branche `build-time-49b2a9`) ne contient que ce rapport.

Convention de lecture : chaque affirmation est marquée **mesuré** (j'ai un chiffre, pris par moi), **vérifié** (j'ai lu le code ou la source en cause), **établi** (lu dans un fichier ou une page citée), **interprétation** (je l'explique, je ne l'ai pas prouvé) ou **incertain**. Les pistes que des agents de relecture ont remontées sont des pistes, pas des faits, tant que je ne les ai pas vérifiées moi-même ; la section 4 les sépare.

## 0. Ce qu'il faut retenir

1. **Mesuré** — Un build propre de toute la solution prend **174 à 184 s**. Un build **sans aucun changement** prend encore **108 à 138 s** (115 à 119 s sans journal de mesure), soit 60 à 70 % d'un build propre.
2. **Mesuré** — La cause est le **compilateur XAML** (le programme qui transforme les fichiers d'interface `.xaml` en code), lancé en deux passes dans chacun des 17 projets WinUI, à chaque build : 118 s de travail cumulé, plus ~25 s de génération des ressources (fichiers PRI). Le compilateur C# ne pèse que ~13 % d'un build propre.
3. **Vérifié** — Ce n'est pas propre à Deckle. C'est un défaut connu du compilateur XAML de Windows App SDK, signalé en amont (ticket [#11264](https://github.com/microsoft/microsoft-ui-xaml/issues/11264)) : le build sans changement refait toutes les pages. Un contournement par deux propriétés MSBuild y est rapporté. **Mesuré ici** : avec ce contournement, le build sans changement tombe à **43,6 à 45,4 s** (contre 108 à 119 s), sans fichier XAML généré différent d'un état à l'autre (§6.2). Ce n'est **pas** un réglage pris en charge par Microsoft.
4. **Mesuré** — Le nombre de projets ne coûte presque rien **pour les projets simples** (~0,4 s chacun). Il coûte **5,6 à 8,8 s par projet WinUI**, et ce prix est fixe : 1 fichier XAML ou 9, c'est à peu près la même durée.
5. **Mesuré** — Modifier un fichier ne coûte presque rien de plus que ne rien modifier. Seul un changement de **surface publique** de `Deckle.Core` recompile les 88 projets (+25 s).
6. **Mesuré, décisif pour le quotidien** — Construire **seulement le projet de test du module touché** prend **3,8 s** pour un module simple (contre 115 s pour la solution), 26 à 30 s pour Autocorrect, 39 à 45 s pour Transcription.
7. **Mesuré** — Les 37 suites de tests (3 530 tests) durent **130 s à froid, 83 s à chaud**. Environ **45 s sur 83** sont un coût fixe de ~1,2 s par suite. Une seule classe de tests d'Autocorrect (`QwenAdapterCompatibilityProtocolTests`, 13,6 s) fixe la durée de la suite entière (15,2 s).

## 1. Contexte de la machine et méthode

**Établi** : 16 processeurs logiques, Windows 11 IoT Enterprise LTSC 2024, SDK .NET 10.0.303, solution `Deckle.Tests.sln` (85 projets C#), build Debug x64, `dotnet build ... /m:4`, Windows App SDK 2.5.1 (WinUI 2.3.9).

Vocabulaire, une fois pour toutes :

- **MSBuild** est le moteur qui exécute un build. Chaque **projet** (fichier `.csproj`) est une suite d'étapes appelées **cibles** (targets), faites elles-mêmes de **tâches** (tasks : « compiler du C# », « copier un fichier »).
- Un **build propre** repart de zéro (le dossier `artifacts` est supprimé). Un **build sans changement** (no-op) relance la commande alors que rien n'a bougé : il devrait être quasi instantané, car MSBuild compare les dates des fichiers d'entrée et de sortie d'une cible (ses `Inputs` et `Outputs`) et saute ce qui est à jour. C'est ce qu'on appelle un build **incrémental**.
- **`/m:4`** autorise 4 processus MSBuild en parallèle. **`Directory.Build.rsp`** (« fichier de réponse » lu à chaque commande) force en plus la **priorité basse** et interdit de garder les processus en vie entre deux builds (`-nodeReuse:false`). Ces règles ne sont pas contournées ici.
- Le **journal binaire** (`-bl`, binlog) enregistre chaque tâche avec sa durée. Je l'ai lu avec la bibliothèque MSBuild.StructuredLogger. Il ne fausse pas les mesures (108 et 138 s avec journal, 115 et 119 s sans : l'écart est dans le bruit).
- Le **temps exclusif d'une tâche** est la durée de la tâche sans ses sous-tâches ; les tâches `MSBuild` et `CallTarget` n'ont aucun travail propre (elles attendent d'autres projets) et sont exclues.
- Un **assembly** est une DLL .NET. Un **assembly de référence** est une copie réduite à la surface publique, que les projets dépendants lisent à la place de la vraie DLL.

Règles de mesure tenues : chaque mesure répétée au moins deux fois ; **jamais deux builds à la fois** ; toute mesure prise pendant qu'un autre build tournait sur la machine est **écartée et refaite**. La session voisine (`llm-legacy-fields`) a lancé des builds pendant mes lots 2, 3 et 6 : trois mesures du lot 2/3 et une du lot 6 ont été détectées polluées et écartées ; une quatrième n'a pas pu être prise (attente de 20 minutes dépassée).

**Leçon de méthode.** Mes premiers lots appelaient `dotnet build` directement, donc sans prendre le verrou machine `Global\Deckle.Build` que prend `scripts/build-locked.ps1`. Les builds verrouillés de l'autre session ne pouvaient donc pas m'attendre, ce qui explique en partie les collisions. À partir du lot 7, tout build passe par `build-locked.ps1`. **Trois pièges relevés dans `build-locked.ps1`** en lui passant des arguments pour `dotnet` (**vérifié**, trois essais qui ont échoué en 1,6 s avant d'être corrigés) : (1) `-v:q` échoue, car PowerShell lit `-v:q` comme une abréviation de son paramètre `-Verbose` (« Cannot convert 'System.String' to the type 'SwitchParameter' ») ; (2) `-bl:chemin` est découpé en `-bl` et `chemin`, et MSBuild lit le chemin comme un second projet (« MSB1008: Only one project can be specified ») ; (3) un argument sans nom placé juste après `-Project` est lu comme `-Configuration` (« does not belong to the set Debug,Release »). Ce qui passe : nommer `-Configuration Debug`, puis écrire les arguments de `dotnet` avec une barre oblique (`/v:q`, `/bl:chemin`, `/m:4`), que PowerShell ne prend pas pour des paramètres. Le script vient de deckle-scripts : l'amélioration (par exemple ranger les arguments de `dotnet` après `--`) revient à ce dépôt-là, je n'y touche pas.

Mesures écartées : `noop_app_1/2` (échec, plateforme x64 oubliée), `incr_Chrono_comment_1` du lot 2 (9 processus étrangers), `incr_Chrono_comment_a2/a3` du lot 3 (4 et 10), `wa_xamledit_a2` du lot 6 (4). Le lot 7 a d'abord été lancé avec `-v:q` et a échoué en 1,6 s par build ; ses résultats ont été jetés.

## 2. Les mesures de base

### 2.1 Restauration, build propre, build sans changement

La **restauration** (restore) télécharge ou vérifie les paquets NuGet de chaque projet avant de compiler.

| Cas | Mesures | Remarque |
|---|---|---|
| Restauration à froid | 5 à 8 s | négligeable |
| Build propre, solution | **183,7 s** et **174,0 s** | |
| Build sans changement, solution | 138,3 / 108,0 s (avec journal) ; 115,4 / 119,3 s (sans) | |
| Build sans changement, application seule (`Deckle.App.csproj`) | **85,4 s** et **90,9 s** | commande de `scripts/build-run.ps1` |
| Build sans changement, un seul projet de test simple (Chrono.Tests, Core.Tests) | **3,8 s** et **3,8 s** (les deux) | |
| Build sans changement, Autocorrect.Tests | 30,1 et 26,3 s | |
| Build sans changement, Transcription.Tests | 45,1 et 39,4 s | |

### 2.2 Où passe le temps d'un build propre

**Mesuré** (journal du build propre, 12 992 tâches, 232 s de travail cumulé pour 184 s d'horloge) :

| Tâche | Durée cumulée | Part | Ce que c'est |
|---|---|---|---|
| `Exec` = compilateur XAML (36 appels, 17 projets × 2 passes) | 144,8 s | **62 %** | transforme le XAML en code et en fichiers binaires |
| `Csc` = compilateur C# (102 appels) | 29,8 s | 13 % | le vrai travail de compilation |
| `WinAppSdkExpandPriContent` + `GenerateProjectPriFile` | 33,7 s | 15 % | assemble les ressources de l'application (fichiers PRI) |
| `ResolveAssemblyReference` | 5,6 s | 2,4 % | cherche chaque DLL dont un projet a besoin |
| Copies, fichiers `deps.json`, lanceurs | ~13 s | 5 % | |

### 2.3 Où passe le temps d'un build sans changement, et pourquoi le compilateur XAML ne s'arrête pas

**Mesuré** (un journal, 156 s de travail pour 115 s d'horloge) :

| Tâche | Durée cumulée |
|---|---|
| Compilateur XAML, passe 1 (17 appels) | 21,9 s |
| Compilateur XAML, passe 2 (17 appels) | 96,6 s |
| Expansion des ressources PRI | 27,7 s |
| `ResolveAssemblyReference` | 2,2 s |
| Compilateur C# (3 projets s'exécutent quand même : App, Lighting.Ambient, Playground) | 1,3 s |

**Vérifié** (lecture de `Microsoft.UI.Xaml.Markup.Compiler.interop.targets`, version 2.3.9, lignes 508, 607 et 646) : les cibles `MarkupCompilePass1` et `MarkupCompilePass2` n'ont ni `Inputs` ni `Outputs`, et lancent `XamlCompiler.exe` à chaque fois (une ligne `Exec`, avec `IgnoreExitCode`). MSBuild ne peut donc jamais les juger « à jour ».

**Mesuré** (repères de durée que le compilateur écrit lui-même dans son `output.json` et que MSBuild garde dans le journal ; projet `Shell.TrayMenu`, 1 fichier XAML, build sans changement) :

| Étape | Durée |
|---|---|
| Passe 1, totale | 0,8 s (essentiellement la carte des types, 0,6 s) |
| Passe 2, totale | 4,7 s |
| dont démarrage + contrôle d'empreinte | 0,07 s |
| dont carte des types (`InitializeTypeNameMap`) | 0,58 s |
| dont chargement de la première page | 1,09 s |
| dont parcours des assemblies de référence (`SearchIxmpAndBindable`) | **2,63 s** (56 %) |
| dont écriture des fichiers et XBF | ~0,1 s |

Sur les lignes propres des 17 projets : parcours des références 1,9 à 4,0 s, carte des types 0,5 à 0,8 s, chargement des pages 0,7 à 2,5 s (le total mesuré par le compilateur, 3,5 à 8,8 s par projet, concorde avec les 4,5 à 7,2 s de la passe 2 vue de MSBuild). Quelques lignes sont faussées par des repères qui se chevauchent ou par le passage de minuit ; je ne les ai pas gardées.

**Constats.**
- **Mesuré** : le contrôle d'empreinte (« fingerprint », une signature des fichiers d'entrée) du compilateur **ne fait pas sortir la passe 2 plus tôt** : même quand la signature est identique (aucun repère « Differ »), la passe poursuit tout le travail.
- **Mesuré** : la signature est différente, sans aucune modification de ma part, pour exactement trois projets : `Playground`, `Lighting.Ambient` et `App`. Ce sont les trois qui relancent aussi le compilateur C# à chaque build sans changement. **Interprétation** : leur assembly local est recompilé à chaque fois, donc leur signature change à chaque fois.
- **Correction d'une interprétation de la première version de ce rapport** : le coût fixe par projet n'est **pas** le démarrage du programme (70 ms). C'est du travail interne répété à chaque projet : parcourir ~210 références, construire la carte des types, charger la première page.

### 2.4 Parallélisme, priorité basse, `/m:4`

- **Mesuré** : sur un build propre, le travail cumulé (232 s) est seulement 1,26 fois l'horloge (184 s). En moyenne, **un peu plus d'une tâche tourne à la fois**, sur les quatre permises.
- **Mesuré** : l'utilisation moyenne du processeur est de **25 à 30 %** sur 16 processeurs logiques, avec des pointes à 90-100 %.
- **Interprétation** : le build n'est pas limité par la puissance de la machine ni par `/m:4`, mais par la **chaîne de dépendances** des projets WinUI : chaque projet attend celui dont il dépend. La chaîne critique relevée est Catalog → Shell → Audio → Settings → Lighting.Ambient → Playground → App → App.Tests. Un build plus parallèle n'aiderait donc que peu.
- **Incertain** : l'effet exact de la priorité basse n'a pas été mesuré par une expérience (la règle interdit de contourner `Directory.Build.rsp`). Les données (machine à 70 % libre) laissent penser qu'il est faible, mais c'est une déduction.

## 3. Question 1 — Le coût de la modularité

### 3.1 Le coût fixe d'un projet

**Mesuré** (journal du build propre, temps exclusif de tâches par projet) :

| Type de projet | Coût fixe approximatif |
|---|---|
| Bibliothèque C# simple | ≈ **0,4 s** |
| Projet de test | ≈ **1 s** |
| Projet WinUI | ≈ **7 à 15 s** au build propre ; **5,6 à 8,8 s** de compilateur XAML au build sans changement |

L'évaluation de MSBuild (lecture de chaque `.csproj`, avant toute tâche) pèse **5,4 s cumulées pour 86 projets**. La résolution des références vaut 5,6 s cumulées.

### 3.2 Le coût du compilateur XAML ne dépend pas du nombre de fichiers

**Mesuré** (build sans changement, deux passes du compilateur XAML par projet) :

| Projet WinUI | Fichiers XAML | Fichiers C# | Passes 1+2 |
|---|---|---|---|
| Hud | 5 | 31 | 5,6 s |
| Input.Trackpad | 1 | 13 | 5,6 s |
| Autocorrect | 3 | 91 | 5,7 s |
| Shell.TrayMenu | 1 | 11 | 6,1 s |
| Setup | 9 | 28 | 6,7 s |
| Settings | 7 | 36 | 7,1 s |
| Playground | 5 | 28 | 8,4 s |
| Llm.Rewrite | 8 | 42 | 8,5 s |
| App | 3 | 72 | 8,8 s |

Un projet à 1 fichier XAML coûte presque autant qu'un projet à 9 fichiers. **Interprétation** (corrigée, voir §2.3) : le prix par projet est surtout le parcours des références et la carte des types, qui se refont à chaque projet quel que soit le XAML qu'il contient.

### 3.3 Le nombre de fichiers coûte-t-il ?

**Mesuré** : non, ou si peu que cela ne se voit pas. Autocorrect compte 91 fichiers C# et son compilateur C# prend 1,3 s ; la durée de `Csc` suit la quantité de code, pas le nombre de fichiers (interprétation : il lit tous les fichiers d'un projet en un seul passage). La doctrine `deckle-modularity` (« un fichier par groupe de fonctions ») ne coûte donc rien de mesurable au build.

### 3.4 Les petits commits coûtent-ils ?

**Établi** : non. MSBuild ne sait pas ce qu'est un commit ; il ne regarde que les dates des fichiers modifiés depuis le dernier build. Le seul lien avec Git est la lecture du numéro de version (la cible `ReadVersionFromGit`, 0,15 s au total pour toute la solution). Dix petits commits ou un gros, le même diff coûte la même chose.

### 3.5 Fusionner des projets ferait-il gagner du temps ? Test du raisonnement d'origine

Le mainteneur a dit : « les DLL séparées permettaient de laisser de côté ce qui n'est pas nécessaire et d'avoir moins de liens », en doutant que cela tienne. Je teste ce raisonnement sur quatre plans.

**Exécution (établi, connaissance générale de .NET).** Un assembly n'est chargé que **la première fois qu'un de ses types est utilisé**, et chaque méthode n'est compilée en code machine (JIT) que **quand elle est appelée la première fois**. Les DLL natives (comme `onnxruntime.dll`) ne sont chargées qu'au premier appel qui en dépend. Une DLL présente sur le disque mais jamais utilisée ne coûte ni mémoire ni temps. Séparer en plusieurs DLL n'apporte donc **rien de mesurable à l'exécution** ; fusionner non plus. **Interprétation** sur la dernière phrase : je n'ai pas chronométré un démarrage.

**Livraison (mesuré + établi).** Le dossier de sortie de l'application contient 586 fichiers, 279 Mo, dont 48 DLL de Deckle pour environ 6 Mo. La famille autocorrect pèse 540 Ko (Autocorrect 376, Onnx 108, Mlm 46, Inference.Onnx 6 ; Lab absent). Les gros fichiers sont des dépendances tierces : `Microsoft.Windows.SDK.NET.dll` 25 Mo, `DirectML.dll` 17,7 Mo, `onnxruntime.dll` 17,3 Mo, `Microsoft.ui.xaml.dll` 14,6 Mo, `Anthropic.dll` 8,9 Mo. **Établi** : `Deckle.App.csproj` référence directement Autocorrect et Autocorrect.Onnx (Setup référence Mlm), donc **tout est livré quoi qu'on fasse**. L'idée de « laisser de côté l'ONNX » ne s'applique pas à l'application actuelle ; elle s'appliquerait à un second hôte plus léger qui n'existe pas.

**Build (mesuré).** Fusionner les bibliothèques simples de la famille autocorrect (Lab 0,96 s, Onnx 0,72 s, Mlm 0,62 s, Inference.Onnx 0,22 s de travail) économiserait environ **0,5 s chacune, soit 1 à 2 s sur 115 : moins de 2 %**.

**Conception (établi).** Une frontière de projet est **imposée par le compilateur** : un projet ne peut utiliser que ce qu'il référence, et `InternalsVisibleTo` ouvre les internes projet par projet. Un sous-dossier n'impose rien. Le mainteneur a confirmé que cette frontière est une valeur voulue. Aucun ADR ni skill ne s'appuie sur la frontière de csproj elle-même ; seul l'ADR-0008 touche une frontière de **processus**.

**Verdict par groupe :**

| Groupe | Gain mesuré en fusionnant | Perte de conception | Verdict |
|---|---|---|---|
| Famille autocorrect (Autocorrect, Onnx, Mlm, Inference.Onnx) | 1 à 2 s | perd l'interdiction que le cœur dépende d'ONNX ; `Inference.Onnx` est aussi utilisé par Vad, donc fusionné dans Autocorrect il fait dépendre Vad d'Autocorrect (mauvais sens) | **garder séparés** |
| Autocorrect.Lab, Autocorrect.Probe | ~0,5 s | Lab est un outil de développement hors application ; le fusionner le ferait livrer avec son paquet `ProtectedData` ; Probe est un exécutable | **garder séparés** |
| Bibliothèques simples (Core, Chrono, Presets, Security, Install, Modules) | ~0,4 s chacune | Core est référencé presque partout ; fusionner mélange des domaines | **garder séparés** |
| Projets WinUI (17) | 6 à 9 s de travail chacun | perd l'enforcement du compilateur sur la couche la plus riche en dépendances | **pas maintenant** : le contournement du §6.2 rend ce gain presque nul sans rien perdre |

## 4. Question 2 — Les tests

### 4.1 Inventaire (mesuré)

**37 projets de test**, 519 fichiers C# dans `tests/`, **3 530 tests**. Par catégorie du glossaire (`tests/GLOSSARY.md`), lue dans les traits de xUnit :

| Catégorie | Tests |
|---|---|
| unit | 2 281 |
| integration | 741 |
| observability | 340 |
| regression | 153 |
| maintenance (exclus par défaut, `Explicit`) | 11 |
| system | 4 |

Résultat à froid : 3 517 réussis, 1 échoué, 1 sauté, 11 non lancés (maintenance).

Chaque suite est un exécutable xUnit v3 lancé seul, une suite à la fois, sans build en cours. Deux passes complètes :

| Suite | Tests | 1re passe | 2e passe |
|---|---|---|---|
| Autocorrect | 902 | 16,3 s (échec) | 15,2 s |
| Transcription | 220 | 14,9 s | 12,6 s |
| Transcription.Whisper | 87 | 9,2 s | 5,8 s |
| Anytype | 544 | 5,8 s | 4,6 s |
| Core | 38 | 4,6 s | 4,7 s |
| Playground | 137 | 4,6 s | 1,2 s |
| Anytype.Mcp | 88 | 3,5 s | 2,3 s |
| Home | 127 | 3,5 s | 2,3 s |
| Les 29 autres | de 3 à 214 | 2,3 à 2,6 s | 1,1 à 1,3 s |
| **Total, 37 suites** | **3 530** | **130,5 s** | **83,4 s** |

Ce **~1,2 s fixe par suite** (démarrage de l'exécutable, découverte des tests, initialisation de Windows App SDK) fait environ **45 s sur 83**. **Incertain** : la part de chaque composant n'est pas isolée. **Vérifié** : `tests/Directory.Build.props` compile `WindowsAppSdkModuleInitializer` dans chaque projet de test ; il appelle `Bootstrap.TryInitialize` avant tout test, alors que le commentaire de `WindowsAppSdkBootstrap.cs` ne le justifie que pour les tests qui touchent Shell ou Vision.

### 4.2 Les tests les plus lents (mesuré, 2e passe)

41 tests durent 0,5 s ou plus (51 s cumulés, parallèles entre eux) ; 83 tests, 0,2 s ou plus (67 s). La somme des durées de tous les tests est de 85 s pour 83 s d'horloge cumulée sur les 37 suites, parce que les suites internes s'exécutent en parallèle.

| Test | Durée | Suite |
|---|---|---|
| `ATruncatedImageIsRefusedAsABadImageAtEveryLength` | 4,06 s | Transcription.Whisper |
| `RejectsAForeignNegativeFailureFamily` | 3,21 s | Autocorrect |
| `ClearsSeededSeparationEvidenceAfterALateFingerprintDrift` | 2,79 s | Autocorrect |
| `AHalfWrittenFileIsIgnoredUntilAValidOneLands` | 1,28 s | Core |
| `SafePatchIsReplayedAfterATransientServerError` | 1,02 s | Anytype |
| `ItsOwnWriteKeepsTheInMemoryInstance` | 1,01 s | Core |
| `ATakeStartedDuringAnIdleUnloadWaitsForTheFreeBeforeItsTest` | 0,92 s | Transcription |

**Autocorrect, par classe de test** (902 tests, 91 classes, 51 s de temps de test cumulé) : `QwenAdapterCompatibilityProtocolTests` 17 tests, **13,6 s** ; `AnticipationTransactionJoinTests` 20 tests, 7,1 s ; `AutocorrectSentenceTypingScenarioTests` 22 tests, 6,6 s ; `AutocorrectTypingScenarioTests` 10 tests, 6,5 s ; `SentenceUnanimityBundleTests` 9 tests, 5,3 s. **Interprétation** : xUnit exécute les tests d'une même classe l'un après l'autre et les classes en parallèle ; la classe Qwen (13,6 s) + ~1,2 s de démarrage fait donc quasiment toute la suite (15,2 s). La classe suivante dure 7,1 s.

### 4.3 Un test instable (mesuré, cause à confirmer ; traité par ailleurs)

`TerminalPunctuationRecoversEvenWhenNoWordWasObservedBeforeIt` (`AutocorrectEngineCaretRecoveryTests.cs:54`) a échoué à la 1re passe après **10,06 s**, puis réussi à la 2e. **Vérifié** : il attend trois fois une condition asynchrone avec `SpinWait.SpinUntil` et un délai de 10 000 ms (ligne 10) ; 10,06 s est donc ce délai expiré. La cause exacte reste à établir ; ce point est traité dans une autre session, je n'y donne pas suite ici.

### 4.4 Pertinence des tests : ce qui est vérifié, ce qui ne l'est pas

Une revue large a été confiée à 7 agents Sonnet. Leurs notes sont des pistes. Je ne garde ici, comme **vérifié**, que ce que j'ai relu dans le code ou chiffré moi-même.

**Vérifié, avec gain.**

| Piste | Preuve | Mesure | Gain estimé | Coût |
|---|---|---|---|---|
| Classe `QwenAdapterCompatibilityProtocolTests` : appareillage de laboratoire (tests d'un « runner » avec une fausse exécution) | lecture de deux tests ; 17 tests en série ; 13,6 s ; la classe suivante 7,1 s | suite Autocorrect 15,2 s | **15 → ~8 s (−7 s)** si la classe est retirée, scindée ou accélérée. **Non confirmé** : pourquoi chaque test dure 0,8 à 3,2 s avec une fausse exécution | retirer revient au mainteneur (ce sont les tests d'une expérience de laboratoire) |
| Délai fixe de 400 ms après chaque arrêt de prise (`CAPTURE_TAIL_AFTER_STOP_MS`, `TranscriptionEngine.StateMachine.cs:27`) | constante lue ; commentaire : « Not a setting » | 41 tests sur 220 durent ≥ 0,3 s et totalisent 18,0 s de 20,8 s de temps de test (≈ 0,44 s chacun) ; suite 12,6 à 14,9 s | **−8 à −10 s** (suite → ~4 s). Le lien entre ces 41 tests et le délai est **cohérent mais non prouvé** | une valeur interne modifiable en test (jamais un réglage, selon le commentaire du code) |
| Troncature de fichiers : `ATruncatedImageIsRefusedAsABadImageAtEveryLength` écrit un fichier pour chaque longueur de l'image | boucle lue (`PeImportsTests.cs:78-92`) | 4,1 à 6,3 s | **−3,5 à −5 s** | une surcharge de lecture sans fichier (code de production) ou un pas d'échantillonnage (couverture plus faible) |
| `JsonSettingsStoreTests` : deux tests attendent 4 × 250 ms pour prouver qu'un rechargement n'a pas lieu | `Thread.Sleep(QuietWindow)` aux lignes 87 et 103 | 1,0 et 1,28 s sur 4,7 s de suite | −1 à −1,5 s | multiplicateur réduit, ou délai de stabilisation injectable |
| Reprise réelle d'Anytype : le test paie le `DefaultBackoff` de 1 s du vrai client | `AnytypeApiClient.cs:50` | 1,02 s | −1 s | délai de reprise injectable |
| Parallélisme désactivé sur tout l'assembly dans 5 suites (Home, Modules, Anytype, Anytype.Mcp, Lighting.Ambient) | cinq fichiers `AssemblyInfo.cs` lus | 4 suites de 2 à 5 s | ≤ 2-3 s au total, **non mesuré** | risque d'états partagés et de ports fixes |

**Vérifié, sans gain de temps.**

- **Test dépendant de l'horloge.** `SentenceUnanimityBundleTests` (9 tests, 5,3 s) assertent `report.Hypothesis.Passed` ; `Passed` inclut `WarmP95BelowReference: p95 < 1,0 ms` (`SentenceUnanimityBundleCommand.cs` de la sonde, lignes 12, 126 et 238-242). Sous charge, le test peut échouer sans bug. Quatre tests appellent chacun `Measure()`. Même famille d'appareillage que la ligne Qwen.
- **Faux verts.** `CamembertRerankerIntegrationTests` : chaque test fait `return` en silence si le modèle est absent (code lu ; son commentaire le dit). Il est tagué `integration` et compté « réussi ». Il devrait être un test `system` avec une condition de saut visible. **Piste non vérifiée** : la même chose pour 3 tests de `OnnxSentenceScorerTests`.
- **Doublons, vérifiés par comparaison de fichiers.** `MessageTemplateDoctrineTests` existe en **30 copies** ; après avoir remplacé l'espace de noms et le type `typeof(...)`, **28 sont identiques** (même corps de 24 lignes, une seule assertion). Les deux autres diffèrent à bon droit : `App` déclare 4 exceptions connues (`KnownBreaches`, 28 lignes) et `Diagnostics` vérifie 8 sources. `MilestoneClosersTests` existe en **16 copies**, identiques au nom de la source près dans leur nom de méthode et leur type ; seul `Transcription` en vérifie deux (`DeckleWhispSource`, `DeckleLlmSource`). Ce sont donc **le même test écrit pour chaque module**, avec une source différente : un candidat à une suite partagée dans `TestSupport` (une règle qui énumère les sources). **Coût de conception** : chaque module cesse de posséder lui-même la vérification de sa doctrine ; une exception comme `App` devient une liste centrale. **Gain de temps : nul** (ces tests durent quelques millisecondes) ; le gain est de maintenance (46 fichiers).

**Piste réfutée.** « `Whisper.Tests` copie `speech.wav` à chaque build » : faux, le fichier est copié avec `PreserveNewest` (il n'est recopié que s'il est plus récent).

**Pistes non vérifiées** (à ne pas utiliser comme faits) : une vingtaine de chargements des données françaises empaquetées dans Autocorrect (vérifié seulement : 4 constructions du harnais dans `AutocorrectSentenceTypingScenarioTests`) ; ~185 tests d'Anytype qui démarrent un vrai serveur HTTP local ; ~36 vrais serveurs Kestrel dans Anytype.Mcp ; tests d'écho d'observabilité (~25 méthodes) ; états globaux modifiés sans restauration ; tests logés dans la mauvaise suite ; fusion de suites en peu de projets (coût de conception indiqué par les agents : `InternalsVisibleTo` de 36 projets, singletons de processus partagés, un plantage emporte toutes les suites ; gain **estimé** ~1,2 s par suite fusionnée, soit ~8 s pour 10 petites suites regroupées en 3).

## 5. Question 3 — Incrémental contre propre

### 5.1 Modification d'un seul fichier (mesuré, deux mesures propres par cas)

Méthode : build de la solution entière après modification d'un fichier de `Deckle.Chrono` (projet « feuille ») ou de `Deckle.Core` (le cœur, référencé presque partout). Deux types de modification : un **commentaire** (n'altère pas la surface publique) et une **constante publique** ajoutée (altère la surface publique, donc l'assembly de référence).

| Cas | Durées | Projets recompilés (C#) | Compilateur C# | Compilateur XAML |
|---|---|---|---|---|
| Aucun changement (référence) | 108 à 119 s | 3 | 1,1 s | 118 s |
| Commentaire dans Chrono | 106 s, 121 s | 4 | 1,0 à 1,2 s | 107 à 120 s |
| API publique de Chrono | 125 s, 116 s | 11 | 3,1 à 3,3 s | 114 à 115 s |
| Commentaire dans Core | 116 s, 119 s | 17 | 4,7 à 4,9 s | 114 s |
| API publique de Core | **148 s, 133 s** | **88** | 21,6 à 23,9 s | 114 à 121 s |

- **Mesuré** : la cascade de recompilation est très visible dans le nombre de projets (3 → 88) mais **presque invisible dans la durée**, sauf pour l'API publique de Core : +15 à +30 s. Le compilateur XAML ne change pas : ~114 s dans tous les cas.
- **Interprétation** : après un changement dans Core, même un commentaire, les 17 projets WinUI se recompilent ; je ne sais pas pourquoi. Le mécanisme n'est pas prouvé.
- **Réponse à la question** : la cascade n'est pas le problème. Le problème est le prix de base, payé tout le temps.

### 5.2 Test et build séparés (mesuré)

Les temps de test du §4 sont ceux de l'exécutable seul, sans build. Pour une boucle « modifier, construire, tester » sur un module simple : 3,8 s de build + 1,2 à 4,7 s de test, soit 5 à 9 s, contre 115 s de build (solution) + 83 s de tests (toutes suites), soit près de 200 s.

## 6. Propositions classées

Classement par rapport gain / effort / risque, après l'approfondissement. Le numéro de chaque proposition est celui de la première version du rapport, sauf la quatrième, nouvelle.

### 6.1 Proposition 1 — Construire et tester seulement ce qu'on a touché (gain mesuré, effort nul)

- **Gain mesuré** : build d'un projet de test de module simple **3,8 s** (au lieu de 115 s) ; Autocorrect.Tests 26 à 30 s ; Transcription.Tests 39 à 45 s. Suite de test seule : 1,2 à 15 s au lieu de 83 s. Boucle complète d'un module simple : de ~200 s à ~5-9 s.
- **Ce qui existe déjà.** **Vérifié** : `AGENTS.md` dit (règles dures) que la validation locale est un `dotnet build` seul avec `/m:4`, Debug x64, sans arrêter Deckle ; que deux builds ne tournent jamais en même temps ; qu'une branche se fusionne dans `main` une fois que la session qui la fusionne a lancé les suites des modules touchés et qu'elles sont vertes. `scripts/build-locked.ps1` (installé depuis deckle-scripts, décrit dans `scripts/README.md`) lance déjà un `dotnet build` ou `dotnet test` d'**un projet donné** (`-Project`, `-Test`) sous le verrou machine, avec les drapeaux obligatoires, et son en-tête donne en exemple un projet de test unique. `AGENTS.md` ne nomme pas ce script.
- **Ce qui est nouveau.** (a) La **cible par défaut** : `AGENTS.md` ne dit pas *quoi* construire ; le réflexe, et mes propres mesures, vont à la solution entière. (b) Le **filet** : un build de la solution entière avant fusion, parce qu'un changement public dans un module partagé (Core surtout : 88 projets recompilés) ne se voit que dans des dépendants qu'un build de projet unique ne compile pas. (c) **Nommer l'outil** dans `AGENTS.md` : le verrou y est exigé (« wait ») mais le script qui le prend n'y figure pas.
- **Brouillon de la plus petite formulation** (anglais, car lu par le dépôt ; à placer dans le paragraphe des règles dures, après la première phrase ; **non appliqué**) :

  > Build the project you changed, or its test project, with `scripts/build-locked.ps1 -Project <csproj>`; build `Deckle.Tests.sln` once before a merge, because a public change in a shared module such as Core only shows in dependants that a single-project build never compiles.

- **Coût de conception** : aucun. **Limite** : les modules qui dépendent de WinUI gardent un coût de 26 à 45 s tant que leur code testable vit dans des projets d'interface (`App.Tests` et `Settings.Tests` référencent des projets WinUI, d'après les agents ; à vérifier).

### 6.2 Proposition 2 — Ne plus refaire le travail du compilateur XAML (gain mesuré, effort faible, risque à surveiller)

**Réponse à la question « existe-t-il une voie officielle ? »** : non. Il existe un **défaut connu en amont**, un **correctif pas encore livré dans une version stable** (d'après les pages lues), et un **contournement par deux propriétés non documentées**. Verdict : **contournement seulement** (« hack only »), mais minuscule, réversible, et mesuré ici.

**État en amont** (pages GitHub lues le 2026-11-07 ; les dates et versions sont celles qu'elles indiquent) :

- [#11264](https://github.com/microsoft/microsoft-ui-xaml/issues/11264) : depuis Windows App SDK 2.3.1 (régression par rapport à 2.0.1 ; 1.8 non touché), un build sans changement refait la compilation XAML de toutes les pages. Le ticket est fermé comme corrigé (« Fixed by ce96a15 », 2026-07-23), mais un commentaire du 2026-10-19 affirme que 2.5.1 (WinUI 2.3.9) et 2.5.4-experimental compilent toujours chaque page à chaque build sans changement, et que le correctif n'est pas dans une version stable.
- [#12334](https://github.com/microsoft/microsoft-ui-xaml/issues/12334) : un fichier XAML dont seule la date change (changement de branche, rebase) fait refaire la passe 2 à tous les builds suivants. Fermé le 2026-10-27 comme déjà corrigé par la PR [#12062](https://github.com/microsoft/microsoft-ui-xaml/pull/12062).
- PR [#12062](https://github.com/microsoft/microsoft-ui-xaml/pull/12062) « Reduce XAML compiler metadata lookup overhead » : fusionnée dans `main` le 2026-10-10 selon la page ; elle accélère la recherche de types et rafraîchit la date des `.xbf` identiques. **Incertain** : dans quelle version elle sera livrée (la page ne le dit pas).
- PR [#12394](https://github.com/microsoft/microsoft-ui-xaml/pull/12394) : exécuter le compilateur en .NET 8+ sous `dotnet build` (−57 % de temps XAML propre, −60 % incrémental, sur WinUI Gallery) ; encore ouverte, avec une propriété `UseNetCoreXamlCompiler`. PR [#12517](https://github.com/microsoft/microsoft-ui-xaml/pull/12517) et [#12513](https://github.com/microsoft/microsoft-ui-xaml/pull/12513) : compilateur natif AOT ; encore ouvertes.

**Ce que le contournement est, exactement.** Deux propriétés MSBuild **non documentées comme interrupteur de performance** : `EnabledXamlOptionalChanges` et `DisabledXamlOptionalChanges`, toutes deux à la valeur `,`. Dans le fichier de Microsoft (`Microsoft.UI.Xaml.Markup.Compiler.interop.targets`, lignes 78-81), elles sont décrites comme des « listes séparées par des virgules de changements optionnels à activer ou désactiver dans le `Main`/`wWinMain` généré », vides par défaut. Leur effet sur le build sans changement vient d'un fil de discussion GitHub, pas d'une documentation Microsoft ; le mécanisme exact n'est **pas établi**. Ce n'est **pas** un réglage pris en charge par Microsoft. Emplacement : `Directory.Build.props` du dépôt (deux lignes dans le premier `PropertyGroup`, avec un commentaire qui cite le ticket), donc un seul endroit.

**Effet mesuré ici** (solution entière, Debug x64, `/m:4` ; mesures propres, sans build étranger) :

| Cas | Sans le contournement | Avec le contournement |
|---|---|---|
| Build sans changement | 108,0 / 138,3 s (journal) ; 115,4 / 119,3 s | **43,6 / 43,6 / 45,4 s** |
| Modification C# d'un commentaire dans Chrono | 106,0 / 121,4 s | **45,3 / 41,4 s** |
| Modification réelle d'un XAML (`MaxWidth` de `TrackpadPage`) | non mesuré (≥ 108 s, puisque le sans-changement l'est déjà) | **53,2 s** |
| Retour à l'état d'avant (annulation de cette modification) | non mesuré | **59,3 / 53,5 s** |

Pour la première ligne, 3 mesures sont propres sur 3 ; pour la modification de XAML, 1 sur 2 (la seconde a croisé 4 processus étrangers) ; l'annulation, 2 sur 2.

**Risque de XAML périmé** (le danger d'une passe sautée à tort : livrer une interface qui ne correspond pas au code).

- **Mesuré — XAML modifié.** Après une vraie modification de `TrackpadPage.xaml`, exactement 4 fichiers générés changent, tous ceux de cette page (`TrackpadPage.g.cs`, `.g.i.cs`, `.xbf`, et la copie `.xbf` embarquée), sur 297 comparés. Aucun autre projet n'est touché.
- **Mesuré — retour à l'état d'avant.** Après l'annulation de cette modification, **les 297 fichiers générés (XBF, `.g.cs`, `.g.i.cs`, PRI) sont identiques à ceux de l'état « sans changement »**, aux deux essais. Le contournement ne laisse donc pas de sortie périmée dans ce scénario.
- **Mesuré — état par défaut reproductible.** Les 297 fichiers de l'état sans contournement, avant l'expérience et après sa remise en place, sont identiques.
- **Mesuré — ce que le contournement change dans le code produit.** Sur les 297 fichiers comparés, un seul diffère entre les deux modes : `Deckle.Lighting.Ambient\debug\XamlTypeInfo.g.cs` (la table que le compilateur XAML génère pour décrire les types utilisés par les pages). Le lot 8 l'a relu en détail (builds de `Deckle.App`, tous sous verrou, sans build étranger). Le fichier garde les **mêmes 2 724 lignes** ; seules **40 lignes changent de place** dans le type `SettingsExpander` (un composant de la bibliothèque Community Toolkit), et leurs numéros d'ordre (`get_46_…`, `set_46_…`) se décalent. Une fois les chiffres ôtés, les deux versions sont **identiques**. C'est donc un **ordre de membres** différent, pas un contenu différent. Quand il apparaît : l'empreinte de ce fichier est `E008…` sans le contournement, et aussi juste après le premier build avec le contournement ; elle devient `997B…` au **premier build sans changement** qui suit, puis ne bouge plus (trois builds identiques). **Non établi** : pourquoi le compilateur énumère ces membres dans un autre ordre une fois l'état sauvegardé ; je n'ai pas d'explication. **Interprétation** : sans effet visible sur le comportement, puisque le contenu est le même, mais je ne l'ai pas vérifié à l'exécution.
- **XAML qui dépend de C#** (le cas dangereux : un `x:Bind` lié à un membre défini ailleurs). Test du lot 7, mesuré, tous les builds passant par `scripts/build-locked.ps1`, sans attente du verrou ni build étranger. Cas : la page `ChoicesPage` (projet `Deckle.Setup`) lie par `{x:Bind}` un membre du type `ModelEntry`, défini dans un autre projet (`Deckle.Transcription`). J'ai ajouté un membre `ProbeLabel` à `ModelEntry`, lié la page dessus, puis changé uniquement le C# :

  | Étape | Avec le contournement | Sans le contournement |
  |---|---|---|
  | Membre ajouté côté C# **et** lié côté XAML | réussi (100,6 s) | — |
  | Renommé **côté C# seulement** (le XAML lie encore l'ancien nom) | **échec** (code 1), erreur du compilateur XAML `WMC9999` (42,1 s) | **même échec, même erreur** (87,5 s) |
  | Ancien nom remis | réussi (48,7 s) | — |
  | **Type** changé (`string` → `int`), XAML inchangé | réussi (52,0 s) ; `ChoicesPage.g.cs` **régénéré** | réussi (115,2 s) |
  | Renommé des deux côtés ensemble | réussi (52,2 s) | réussi (110,7 s) |

  **Verdict : pas de XAML périmé dans ces quatre cas.** Quand le C# dont dépend une page change, la passe XAML de cette page se refait (le fichier `ChoicesPage.g.cs` est régénéré après le seul changement de type) ; le renommage C# seul échoue comme sans contournement ; et dans les états « type changé » et « renommé des deux côtés », les **29 fichiers générés de `Deckle.Setup` sont identiques** avec et sans le contournement. **Interprétation** : le contrôle d'empreinte du compilateur englobe les assemblies référencés, donc un changement dans `Transcription` suffit à défaire le raccourci du contournement. **Limites** : une seule page, un seul `x:Bind` ; je n'ai pas testé les clés de ressources ni les styles. Le texte de l'erreur `WMC9999` (« Could not find any resources appropriate for the specified culture… ») est trompeur : c'est l'enveloppe d'erreur interne du compilateur, identique dans les deux modes. Comparer les durées de la colonne de droite avec celle de gauche demande de la prudence : chaque ligne suit un changement, mais l'ordre des étapes n'est pas le même dans les deux colonnes.

**Quand l'enlever.** Dès qu'une version **stable** de Windows App SDK livre le correctif de [#12062](https://github.com/microsoft/microsoft-ui-xaml/pull/12062) (et vers laquelle le dépôt migre), remesurer le build sans changement **sans** les deux lignes : si le temps reste à ~45 s, supprimer le contournement. À remesurer aussi à chaque montée de version de Windows App SDK.

**Risque s'il reste.** (1) Une version future peut lire la valeur `,` autrement (la valeur est transmise telle quelle à `EnableChange()`/`DisableChange()`, d'après le commentaire de Microsoft) : erreur de build ou `Main` généré différent. (2) Il masque une régression future du compilateur : on ne verrait plus que le build est rapide *grâce* à lui. (3) Un lecteur futur ne sait pas pourquoi ces deux lignes sont là sans le commentaire et le lien. Les trois se limitent par un commentaire daté dans `Directory.Build.props` et la remesure ci-dessus.

**Ce que devient le temps du compilateur XAML** (lot 8, mesuré, journaux `x8_*`, build de `Deckle.App` seul et non de la solution entière, sous verrou, sans build étranger). Les durées de la tâche `Exec` (celle qui lance le compilateur) sont **additionnées sur tous les projets**, donc supérieures à l'horloge, puisque quatre projets tournent en parallèle :

| Build sans changement de `Deckle.App` | Horloge | `Exec` cumulé |
|---|---|---|
| Sans le contournement | 96,0 s (1 mesure) | 116,4 s |
| Avec, 1er build sans changement après le build de mise en route | 80,5 s | 96,6 s |
| Avec, 2e et 3e builds sans changement | **25,9 / 27,6 s** | **13,6 / 13,7 s** |

**Lecture** : avec le contournement, le compilateur XAML passe d'environ 116 s cumulées à environ 14 s, soit **−88 %**, et il reste ces ~14 s : le contournement ne supprime pas tout le travail, il en saute l'essentiel. **Attention au premier build** : après l'ajout des deux lignes, il faut **deux builds** avant d'atteindre le régime rapide (le premier refait tout, le second « règle » l'état sauvegardé, et c'est lui qui réécrit `XamlTypeInfo.g.cs` ci-dessus). Mes mesures de la solution entière (43,6 à 45,4 s) étaient prises après deux builds de mise en route, ce qui est cohérent. Les chiffres de `Deckle.App` seul (25,9 / 27,6 s) et ceux de la solution (43,6 à 45,4 s) ne se comparent pas entre eux : la solution contient 85 projets, dont les tests.

**Estimations pour le reste de la boucle** (interprétation, fondée sur les mesures de la solution entière ci-dessus) : un build complet dont rien n'a bougé passe de ~108-138 s à ~44 s, et la boucle « modifier un fichier C#, tout reconstruire » de ~106-121 s à ~41-45 s. Le gain sur l'horloge ne se retrouve pas à 100 % pour un changement de l'API de Core (les 88 projets recompilent).

**Effort** : 2 lignes + un commentaire + une remesure. **Coût de conception** : aucun sur les frontières de projet.

### 6.3 Proposition 3 — Retirer les attentes et les tests d'appareillage (gain estimé, effort petit à moyen)

Seules les pistes **vérifiées** du §4.4 comptent. Gain estimé cumulé (somme des lignes du tableau, non mesurée en réel) : suites Autocorrect −7 s, Transcription −8 à −10 s, Whisper −3,5 à −5 s, Core −1 à −1,5 s, Anytype −1 s, soit environ **−20 à −25 s sur 83 s** pour l'ensemble. À remesurer après chaque changement : le classement vient des durées de test, l'effet sur l'horloge d'une suite dépend de ses classes parallèles.

Les pistes restent **séparées** : chacune est un petit changement indépendant. Ordre suggéré, du meilleur rapport gain / effort : Transcription (une valeur interne), Whisper (une surcharge de lecture), Core et Anytype (un délai injectable chacune), puis la classe Qwen (décision du mainteneur, car ce sont des tests d'expérience).

**Autres pistes**, sans gain de temps : le test dépendant de l'horloge de `SentenceUnanimityBundleTests` ; les faux verts de `CamembertRerankerIntegrationTests` ; les 28 copies identiques de la doctrine de gabarits (§4.4).

**Coût de conception** : un seam interne par suite concernée ; retirer des tests d'expérience demande de vérifier qu'aucun code vivant ne dépend de ces appareils.

### 6.4 Proposition 4 — Un journal des temps de build (conception, non installé)

**Ce que je recommande.** Un **script à la demande**, pas un journal passif. Raison : le gros levier est connu (§6.2) et un journal passif écrirait un fichier à chaque projet de chaque build de chaque session (85 écritures par build, avec des écritures concurrentes de plusieurs nœuds MSBuild et un risque de collision), pour une valeur que quelques mesures répétées donnent aussi bien.

**Conception.**
- **Où il vit.** Un script propre à Deckle, `scripts/measure-build.ps1` (les scripts propres à Deckle cohabitent avec ceux de deckle-scripts ; rien d'installé n'est modifié), plus un test de script dans `scripts/tests/commands/` selon la convention du dépôt. `Directory.Build.rsp` ne change pas. Sortie locale à la machine, hors dépôt : `artifacts/build-journal/journal.csv` (le dossier `artifacts/` est ignoré par Git) et les journaux binaires des dernières exécutions.
- **Ce qu'il enregistre.** Date, commit court, version du SDK .NET, version de Windows App SDK, nombre de processeurs, nom du scénario, durée, code de sortie, indicateur « un autre build a tourné pendant la mesure », et — lu dans le journal binaire — la somme des deux passes XAML. Scénarios fixes : build sans changement de la solution ; build sans changement de l'application seule ; modification d'un commentaire dans `Deckle.Chrono` ; construction du projet de test de `Deckle.Core`. C'est exactement ce que mes scripts de mesure de cette recherche font (`measure-lib.ps1`), mis au propre.
- **Comment il se déclenche.** À la main : après chaque montée de version de .NET ou de Windows App SDK, avant/après l'ajout ou le retrait du contournement du §6.2, et de temps en temps. Il prend lui-même le verrou `Global\Deckle.Build` (comme `build-locked.ps1`, en appelant ce script) pour ne jamais se croiser avec un autre build.
- **Effort** : environ une demi-journée à une journée, tests de script compris.

**Vaut-il la peine ?** Pas avant la décision sur la proposition 2. Il devient utile **après** : c'est lui qui dira si le contournement tient d'une version à l'autre et quand l'enlever. Avant, mes chiffres de cette recherche suffisent. Avantage de ne pas en faire un journal passif : aucune écriture dans les builds de tout le monde, aucun risque pour `Directory.Build.targets`.

### 6.5 Considérée et non recommandée — Fusionner des projets WinUI

- **Gain** : 6 à 9 s de travail cumulé par projet (mesuré). **Incertain** : les plus petits projets ne sont pas sur la chaîne critique relevée ; le gain d'horloge pourrait être nettement plus faible. `Shell.TrayMenu` documente sa propre raison d'être séparé (fusion de la ressource principale PRI).
- **Coût de conception** : on perd l'interdiction imposée par le compilateur. Le remplaçant serait un **test d'architecture** (un test qui lit les `.csproj` ou les `using` et échoue si un module utilise ce qu'il ne devrait pas) ; son coût : écrire et maintenir les règles, un retour plus tardif (à l'exécution des tests plutôt qu'à la compilation).
- **Verdict** : si la proposition 2 tient, le prix par projet WinUI tombe presque à zéro (sans changement) et la fusion ne rapporte plus rien. À ne pas faire.

## 7. Ce qui reste incertain ou non fait

- Le **mécanisme** par lequel le contournement fait sauter le travail n'est pas établi ; seuls son effet (durées, fichiers générés) et le fil GitHub le soutiennent.
- Les gains de la proposition 3 sont des estimations non remesurées après changement.
- L'effet de la priorité basse et de `/m:4` n'est pas mesuré par comparaison (règles) ; seules les données d'utilisation du processeur permettent une déduction.
- La part de chaque composant du coût fixe de 1,2 s par suite n'est pas isolée.
- La différence entre l'horloge (184 s) et la chaîne critique (74 s de travail) n'est pas entièrement décomposée.
- Je n'ai mesuré ni le temps de démarrage de l'application selon le nombre de DLL, ni l'effet d'un antivirus, ni une machine autre que celle-ci.
- Les affirmations sur l'état en amont (versions, dates de correctifs) viennent de pages GitHub lues le 2026-11-07, résumées par un outil ; elles sont à relire avant toute décision qui en dépend.

## 8. Fichiers de mesure

Les chiffres bruts (CSV de mesures, journaux binaires, empreintes, notes des agents) sont dans le dossier de travail de la session (scratchpad) ; ils ne sont pas copiés dans le dépôt. Les fichiers temporaires de test (`BuildTimeProbe.cs`, les modifications de `Directory.Build.props`, `ModelEntry.cs`, `ChoicesPage.xaml`, `TrackpadPage.xaml`) ont tous été retirés ; `git status` du worktree ne montre que ce rapport.
