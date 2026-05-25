---
name: deckle-testing
description: Doctrine de testing pour le projet Deckle (Windows .NET 10 / WinUI 3). Porte la façon dont le testing est conçu et exécuté — strates en scope automatique (unit, integration, observability, regression) versus hors scope automatique (system, interactive), stack technique (xUnit v3 + Microsoft Testing Platform, Assert natif, pas de mock framework), placement (tests/Deckle.Tests sibling de src/, mirror par dossier), conventions de nommage et trait Category, pattern TestEventListener pour les tests d'observabilité, stratégie leaf-first face au bug XamlCompiler MSB3073. S'invoque avant d'écrire un test, avant d'ajouter un module à la couverture, avant de décider d'une stratégie de fake ou d'isolation, et avant de modifier la structure du projet de tests. Triggers on phrases like deckle testing, tests deckle, xunit deckle, test unitaire deckle, test observabilité deckle, test integration deckle, ajouter test deckle, couverture deckle, TestEventListener, deckle-testing.
---

# Deckle — Doctrine de testing

## Rôle

Skill projet-spécifique qui répond à la question récurrente « comment Deckle teste son code ». S'invoque avant d'écrire un test, avant d'ajouter un module à la couverture, avant de décider d'une stratégie de fake ou d'isolation, et chaque fois qu'une décision touche à la structure du projet de tests.

Ne duplique pas le skill cross-projet `tdd` (philosophie générale et techniques) ni `deckle-nomenclature` (règles de nommage transverses). Capte le résidu Deckle-spécifique — stack technique gelée, frontières des strates, placement physique, pattern d'observabilité, posture face au bug historique XamlCompiler (cf. ADR-0012).

## Philosophie

Le code est conçu pour être testé, mais le test ne doit pas déformer l'interface publique. La dérive à éviter est le « code testable mais inutilisable » — sur-abstraction d'interfaces, injection de dépendances pour le plaisir, fakes partout là où une fonction pure suffit. Une couture (`seam`) ne se crée que quand le test en a besoin et que le besoin est réel — pas par anticipation.

On démarre simple — tests unitaires sur les modules-feuilles purs — et on étend strate par strate. Chaque strate ajoutée est une décision tracée, pas une migration de fond. La couverture progresse au rythme des chantiers ; on n'écrit pas de tests pour le passé, on en écrit pour ce qu'on touche.

## Strates en scope automatique

Quatre strates sont exécutées sans intervention humaine — `dotnet test` les invoque, un agent LLM les pilote, le CI éventuel les valide.

**Unit** — fonctions et types isolés, sans dépendance externe. Déterministe, rapide (millisecondes), pas de I/O, pas d'horloge, pas de threading visible. Catégorie : `[Trait("Category", "unit")]`. Exemple canonique : `ChronoFormatter` (décomposition `TimeSpan`, format `MM:SS.cc`).

**Observability** — exerce la chaîne EventSource depuis l'émission jusqu'à la collecte. Le provider est un singleton process-wide ; le listener s'abonne par nom ETW (`Deckle.<Module>`), naturellement isolé par `using`. Catégorie : `[Trait("Category", "observability")]`. Pattern canonique documenté dans `TestEventListener` (voir section dédiée).

**Integration** — exerce plusieurs unités ensemble derrière une frontière publique. Reste dans le process, pas de réseau, pas d'UI. Catégorie : `[Trait("Category", "integration")]`. À introduire au cas par cas, quand une responsabilité orchestrée mérite une vérification end-to-end interne.

**Regression** — test écrit en réaction à un bug corrigé, pour empêcher qu'il revienne. Catégorie : `[Trait("Category", "regression")]`. Le nom du test mentionne le symptôme reproduit. Toute correction de bug non-trivial s'accompagne idéalement d'un test de cette strate.

## Strates hors scope automatique

Deux strates restent manuelles — l'agent ne les déclenche pas, Louis les conduit.

**System** — vérification de l'app intégrée en conditions réelles (binaire publié, dépendances natives en place, hotkey enregistré, tray actif). Conduit par Louis, scriptable ponctuellement mais pas automatisé.

**Interactive** — vérification visuelle et sensorielle (HUD, animations, contraste, lisibilité, response time perçu). Reste l'apanage de Louis ; aucune automatisation crédible à ce stade.

## Stack technique

**xUnit v3 (3.2.2)** — recommandation officielle de l'équipe xUnit pour tout nouveau projet en 2026 (Brad Wilson). Le projet de tests est un exécutable autonome (`OutputType=Exe`) sous Microsoft Testing Platform, compatible aussi avec VSTest via `xunit.runner.visualstudio` pour le Test Explorer de Visual Studio.

**Microsoft.NET.Test.Sdk + xunit.runner.visualstudio** — orchestration VSTest pour découverte par `dotnet test` et Test Explorer. Versions gelées : `Microsoft.NET.Test.Sdk 17.13.0`, `xunit.v3 3.2.2`, `xunit.runner.visualstudio 3.1.5`.

**Assert natif xUnit** — `Assert.Equal`, `Assert.Single`, `Assert.IsType`, etc. Pas de FluentAssertions (v8 commercial, hors-cadre du projet). Pas de Shouldly ni équivalent — l'assertion native est suffisamment lisible et n'ajoute pas de dépendance.

**Pas de framework de mock** — Moq, NSubstitute, FakeItEasy ne sont pas introduits. Quand une couture est nécessaire, le fake est écrit à la main (classe `Fake<Interface>` dans `tests/.../Shared/`). Cette discipline maintient la simplicité de l'interface réelle et oblige à se demander si la couture est légitime.

**`dotnet test` direct** — l'agent invoque la commande sans script intermédiaire (`dotnet test tests/Deckle.Tests/Deckle.Tests.csproj`). Les filtres par catégorie marchent nativement (`--filter "Category=unit"`). L'intégration au menu `scripts/deckle.ps1` est un confort humain optionnel, pas une dépendance.

## Placement et structure

**Un seul projet de tests** — `tests/Deckle.Tests/` sibling de `src/`. Pas un projet par module — la fragmentation viendra si et seulement si elle se justifie par une frontière de cycle de build ou de plateforme.

**Suffixe `.Tests` permanent** — `Deckle.Tests` n'est pas un nom transitoire en attente de « promotion » vers `src/`. Le projet vit parallèlement aux modules testés, indéfiniment.

**Mirror par dossier** — la structure interne calque celle de `src/`. Tests du module `Deckle.Chrono` sous `tests/Deckle.Tests/Chrono/`, tests du module `Deckle.Diagnostics` sous `tests/Deckle.Tests/Diagnostics/`. Le namespace suit (`Deckle.Tests.Chrono`).

**Helpers partagés sous `Shared/`** — `tests/Deckle.Tests/Shared/` héberge les utilitaires réutilisables entre modules testés (`TestEventListener`, fakes communs, builders de fixtures). `internal sealed` par défaut — visibilité minimale, surface contrôlée.

**`ProjectReference` à la demande** — le csproj référence uniquement les modules effectivement testés. Chaque ajout de module à la couverture ajoute une `ProjectReference`.

## Conventions de nommage

**Classe de test** : `<TypeTesté>Tests`. Exemple : `ChronoFormatterTests`, `DeckleChronoSourceTests`. Une classe par type ou par responsabilité testée.

**Méthode de test** : PascalCase, phrase complète sans underscore, décrit le comportement attendu. Exemples : `DecomposeReturnsZeroForTimeSpanZero`, `PilotEmittedCarriesTheNoteAsFirstPayload`. La forme `Methode_Etat_Resultat` avec underscores (style Microsoft historique) n'est pas adoptée — elle entre en friction avec `deckle-nomenclature` (PascalCase strict, pas d'underscore dans les identifiants publics).

**Trait Category** : appliqué au niveau classe quand toutes les méthodes de la classe relèvent de la même strate. Au niveau méthode si une classe mélange unit et observability (cas rare, signal de scission).

**Arrange / Act / Assert** : séquence visible, séparée par lignes vides, sans commentaires `// Arrange` redondants. Un test = un fait. Si l'assert demande plusieurs vérifications corrélées (par exemple : un event a bien le bon ID et le bon niveau), elles tiennent ensemble dans une seule méthode ; sinon, scinder.

## Pattern TestEventListener

Le testing d'observabilité Deckle s'appuie sur un `EventListener` instrumenté — `tests/Deckle.Tests/Shared/TestEventListener.cs`. Le pattern est canonique pour tout futur provider `Deckle.<Module>`.

Utilisation typique dans un test :

```csharp
using var listener = new TestEventListener("Deckle.Chrono");
DeckleChronoSource.Log.PilotEmitted("payload-content");

var ev = Assert.Single(listener.Events);
Assert.Equal(DeckleChronoSource.EvtPilotEmitted, ev.EventId);
```

Deux pièges natifs à connaître. `OnEventSourceCreated` est invoqué pour les sources préexistantes pendant le constructeur de la classe de base `EventListener`, avant que les champs de la classe dérivée soient assignés — d'où le re-scan explicite via `EventSource.GetSources()` après assignment du nom dans le constructeur du listener. Et `OnEventWritten` peut recevoir des events système non-Deckle (`RuntimeEventSource`) selon les `EnableEvents` passifs — d'où le filtre défensif par nom de provider à l'entrée de `OnEventWritten`.

Le `using` est important : `Dispose` désinscrit le listener, sinon il continue de capter les émissions des tests suivants.

## Progression de la couverture

`dotnet test` est utilisable sur n'importe quel module Deckle, y compris ceux qui tirent transitivement `Microsoft.WindowsAppSDK`. Le bug `MSB3073 XamlCompiler.exe exited with code 1` qui avait motivé une stratégie « leaf-first » historique (cf. `CLAUDE.md` racine et [microsoft-ui-xaml#8871](https://github.com/microsoft/microsoft-ui-xaml/issues/8871)) ne se reproduit plus dans la combinaison actuelle. Décision actée par [ADR-0012](../../../docs/adr/0012-adoption-de-dotnet-build-et-dotnet-test.md).

L'ordre de progression « modules purs avant modules à WinAppSDK » reste une **préférence pédagogique** raisonnable — démarrer par `Deckle.Chrono` puis `Deckle.Core` puis les parties pures de `Deckle.Composition` permet d'isoler la mécanique testing avant de croiser des dépendances plateforme. Mais ce n'est plus une **contrainte technique**. Quand un chantier touche un module à WinAppSDK (`Deckle.Catalog`, `Deckle.Hud`, `Deckle.Settings`, `Deckle.Transcription`, etc.), la couverture peut s'y étendre directement.

Si le bug réapparaît (signal : `MSB3073` sur `dotnet build` ou `dotnet test`, log enrichi par le fix WindowsAppSDK 1.8.8 qui rendra l'erreur lisible, échec sur un environnement CI/CD éventuel), réintroduire le contournement MSBuild VS — la recette technique est tracée dans [ADR-0012](../../../docs/adr/0012-adoption-de-dotnet-build-et-dotnet-test.md), réapplicable.

## Évolution

Le projet de tests démarre minimaliste. L'ajout d'une dimension (catégorie nouvelle, dépendance NuGet de test, structure de dossier qui dévie du mirror, helper partagé qui change de forme) est une décision tracée, pas un automatisme — quand le besoin se présente, il se discute avant de s'écrire. Les choix gelés ci-dessus (xUnit v3, Assert natif, pas de mock framework, `.Tests` sibling, mirror par dossier) ne se remettent en cause que sur dérive observée et discutée explicitement.

## Pointeurs

- **`tdd`** — philosophie générale du TDD, techniques de design pour la testabilité (deep modules, interface design, mocking, refactoring). Lu en complément quand une question de conception émerge.
- **`deckle-nomenclature`** — règles de nommage transverses (PascalCase, suffixes admis) ; ce skill applique ces règles au contexte testing.
- **`deckle-workflow`** — doctrine quotidienne de build (`dotnet build`, scripts d'orchestration) que cette doctrine recoupe.
- **`deckle-docs`** — où vivent les traces écrites du projet ; un changement structurel du testing laisse trace ici ou en ADR selon le poids.
- **`src/Deckle.Diagnostics/CLAUDE.md`** — convention transverse EventSource (providers, listeners, schéma JSONL, classes d'observables canoniques) et pattern test côté provider (motivation, exemple, lien ADR-0005).
