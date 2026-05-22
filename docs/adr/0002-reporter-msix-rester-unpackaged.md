# ADR-0002 — Reporter MSIX, rester unpackaged HKCU Run

**Status** — accepted le 2026-04-30

## Contexte
Deckle est distribué unpackaged, avec autostart géré via la clé registre HKCU `Run`. La question du passage en packaging MSIX revient pour deux raisons : accéder à des API packaged-only (`BackgroundTaskBuilder`, certains contrats COM `CoreApplication`) et bénéficier du mécanisme `Windows.ApplicationModel.StartupTask` plus propre que HKCU Run.

L'utilisateur final actuel est le maintainer plus une collaboration éventuelle. Pas de positionnement « produit grand public ».

## Options considérées
- **A. Adopter MSIX maintenant** — nécessite un certificat de signature payant (~150 €/an minimum hors Store) ou un certificat self-signed qui demande à chaque utilisateur d'installer le cert dans Trusted Root avant `Add-AppxPackage`. Demande aussi un `Package.appxmanifest`, la validation runtime sous conteneur MSIX, et la migration `AutostartService` HKCU Run → `StartupTask`. Plusieurs jours focalisés.
- **B. Rester unpackaged HKCU Run** — aucun certificat, aucune migration, autostart inchangé via la clé registre. `<WindowsPackageType>None</WindowsPackageType>` reste dans le csproj, `scripts/publish-unpackaged.ps1` reste utilisable. L'ownership check de l'autostart se fait via `Environment.ProcessPath`.
- **C. MSIX self-signed sans Store** — supprime le coût du cert payant mais dégrade l'expérience first-run (warning SmartScreen, installation manuelle du cert dans Trusted Root par chaque utilisateur).

## Décision
Option B pour V0.1.0 et la phase courante. Le scope packaging n'est pas sur le chemin critique de l'app — le maintainer et une éventuelle collab n'ont pas besoin de la chaîne de signature ni du Store.

## Conséquences
Pas de Store ni de side-loading propre pour un cercle élargi. Les API packaged-only restent inaccessibles jusqu'à ré-évaluation. La chaîne `csproj WindowsPackageType=None + publish-unpackaged.ps1 + AutostartService via HKCU Run` reste la posture de référence. À reconsidérer si distribution large public, publication Microsoft Store, ou besoin de sandbox/isolement renforcé.
