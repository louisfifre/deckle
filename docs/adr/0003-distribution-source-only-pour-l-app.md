# ADR-0003 — Distribution source-only pour l'app, releases natives pour les redistribuables

**Status** — accepted le 2026-04-30

## Contexte
Le repo public github.com/PelopeeNoire/deckle a deux usages concrets : récupérer le projet depuis n'importe quelle machine du maintainer, et permettre une collaboration éventuelle. Pas de positionnement « produit grand public » pour l'instant.

La question : produit-on des releases binaires de l'app, et/ou des releases des redistribuables natifs (whisper.cpp + MinGW C++ runtime) que l'app consomme à l'exécution ?

## Options considérées
- **A. Releases binaires complètes** — produire `Deckle.exe` zippé, un installer (Inno Setup, MSI, MSIX). Demande pipeline CI, scope d'installer, signature. Coût significatif sans audience.
- **B. Source-only intégrale** — clone, build, run. Aucune release GitHub. Cohérent avec le scope perso mais oblige chaque utilisateur à recompiler les natives whisper.cpp avec MinGW + Vulkan, ce qui est non trivial (recette dans `docs/reference--native-runtime--V.V.md`).
- **C. Source-only de l'app, releases des redistribuables tiers** — pas de release `Deckle.exe`, mais publication d'un bundle natif `native-vX.Y.Z` (DLLs whisper.cpp Vulkan + MinGW C++ runtime) téléchargé par le first-run wizard de l'app.

## Décision
Option C. Pas de release binaire de l'app pour V0.1.0 ; releases GitHub `native-vX.Y.Z` pour les redistribuables tiers, taguées séparément. Premier release `native-v1.0.0`, asset `deckle-native-1.0.0.zip` (~18 MB).

Justification de la nuance : republier des binaires sous licences MIT et GPL+exception ne pose pas de problème de scope (ce ne sont pas du code Deckle), et c'est ce qui permet à un utilisateur de cloner-builder-runner sans recompiler la chaîne native lourde.

## Conséquences
Le README documente le chemin `fresh clone → build → run` avec ses prérequis. Pas de pipeline CI pour publier des artefacts d'app. Un `CONTRIBUTING.md` clarifie la posture « personal project, contributions case-by-case ». Le wizard de premier lancement auto-télécharge le bundle natif via `NativeRuntime.CurrentBundle.Url` ; producteur côté maintainer : `scripts/publish-native-runtime.ps1`. Cohérent avec [ADR-0002](./0002-reporter-msix-rester-unpackaged.md) — pas d'installer MSIX/MSI nécessaire à ce stade. À reconsidérer si le projet décolle au-delà du cercle proche, ou si on veut mettre l'app entre les mains d'utilisateurs qui ne builderont pas eux-mêmes.
