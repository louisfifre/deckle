# ADR-0009 — Assets résolus via UserDataRoot exclusivement

**Status** — accepted le 2026-05-23 (acte rétroactif de la décision 2026-04-30)

## Contexte

Deckle dépend de deux familles d'assets runtime non embarqués dans le binaire : les DLLs natives whisper.cpp + backends ggml-vulkan (~50 Mo), et les modèles Whisper (small ~500 Mo, large-v3 ~3 Go). Avant 2026-04-30, l'app résolvait ces assets selon une cascade de fallbacks — `AppContext.BaseDirectory`, puis un walk-up vers `<repo>/native` ou `<repo>/models` en mode dev, puis junctions pour les worktrees, puis `UserDataRoot` en dernier recours. Cette cascade rendait le diagnostic de provisioning instable : un build dev pouvait tomber sur la version du repo, un build publish sur celle de `%LOCALAPPDATA%`, un worktree sur une junction, et chaque cas demandait son propre setup.

La décision 2026-04-30 (merge `chore/cleanup-and-launcher`) a tranché : un seul endroit où l'app cherche ses assets, peu importe le binaire qui tourne.

## Options considérées

- **A. Conserver la cascade multi-fallback.** Flexible côté dev (chemins multiples utilisables), maximaliste côté provisioning. Coût de raisonnement et de diagnostic continu.
- **B. Forcer `<repo>/native` et `<repo>/models` même en publish.** Modèle « assets dans le repo », simple à comprendre pour qui clone le projet. Incompatible avec les gros modèles (3 Go versionnés rejeté), et complique la cohabitation entre plusieurs worktrees qui auraient chacun leur copie.
- **C. Forcer `<UserDataRoot>` exclusivement.** Un seul point de vérité — `%LOCALAPPDATA%\Deckle\native\` et `%LOCALAPPDATA%\Deckle\models\` par défaut. L'app, peu importe le binaire (build dev, worktree, publish), lit toujours là. Le repo ne contient ni `native/` ni `models/`. Le wizard first-run ou `scripts/lib/setup-assets.ps1` peuple. Plus simple à raisonner, plus simple à packager plus tard.

## Décision

Option C retenue. `UserDataRoot` (par défaut `%LOCALAPPDATA%\Deckle\`) est le **seul** chemin de résolution des assets natifs et des modèles. `NativeRuntime.IsInstalled()` ne vérifie que `NativeDirectory` sous `UserDataRoot`. `AppPaths.ResolveModelsDirectory()` est inlined en `Path.Combine(UserDataRoot, "models")`. Le csproj de l'app n'a plus de `<Content Include="..\..\native\…">` qui copierait les DLLs à côté de l'exe. `scripts/lib/build-run.ps1` n'a plus de `Sync-WorktreeJunctions` — les worktrees partagent automatiquement les assets via `UserDataRoot` parce qu'il n'y a plus de chemin alternatif à synchroniser.

Le repo ne contient ni `native/` ni `models/`. Le first-run wizard ou `scripts/lib/setup-assets.ps1` peuple. Switches du script : `-DataRoot` (override la cible), `-AlsoInRepo` (mode dev pour aussi remplir `<repo>/native` et `<repo>/models`, utile uniquement pour produire une release `native-vX.Y.Z`), `-WithLarge` (fetch `ggml-large-v3.bin` ~3 Go), `-Force` (re-download).

## Conséquences

Devient plus facile : un seul chemin à vérifier pour diagnostiquer un asset manquant ; cohabitation triviale entre builds dev et publish (ils lisent le même `UserDataRoot`) ; les worktrees ne demandent pas de junction ni de copie ; la perspective d'un futur packaging MSIX (cf. [ADR-0002](./0002-reporter-msix-rester-unpackaged.md)) reste ouverte sans dette d'architecture sur la résolution d'assets.

Devient plus difficile : un dev qui clone le repo doit obligatoirement lancer le wizard first-run ou `setup-assets.ps1` avant le premier build runtime — la copie locale `<repo>/native` ne fonctionne plus comme atterrissage d'urgence.

Devient impossible : copier les DLLs à côté de l'exe pour distribuer une release portable autonome. Acceptable parce que la distribution Deckle reste source-only (cf. [ADR-0003](./0003-distribution-source-only-pour-l-app.md)) et que le wizard first-run gère l'installation runtime sur la machine cible.
