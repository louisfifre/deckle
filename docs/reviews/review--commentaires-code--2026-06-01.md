---
date: 2026-06-01
scope: commentaires-code
agent: codex
commit: a48f97c
status: corrected-pass
---

# Review — Commentaires code — 2026-06-01

## Portee

Review en lecture des commentaires dans le code et les scripts du depot Deckle. Objectif : trouver les commentaires faux, obsoletes ou trompeurs par rapport au comportement actuel de l'application ou du code environnant.

Hors scope par defaut : doctrine Markdown, ADR, README, journaux, notes de recherche, ressources binaires, fichiers generes, `bin/` et `obj/`. Les commentaires XML/XAML/PowerShell/Python/C# presents dans les surfaces de code sont inclus.

## Methode

Inventaire par module, avec confrontation des commentaires significatifs contre l'implementation adjacente et, quand necessaire, contre les `CLAUDE.md` de module.

## Progression

- [x] Inventaire global des commentaires.
- [x] `src/` modules applicatifs — passe cible + corrections appliquees.
- [x] `tests/` — stale mineur corrige.
- [x] `scripts/` — passe cible + corrections appliquees.
- [x] `benchmark/` code et scripts — stale PhiBench corrige.
- [x] Rapport consolide pour les findings verifies.

## Synthese provisoire

- Findings verifies dans la premiere passe : 13.
- Corrections additionnelles pendant la passe de fix : 5 zones de commentaires obsoletes.
- Gravite initiale : 5 P1, 6 P2, 2 P3.
- Theme dominant : commentaires de vague/handoff qui n'ont pas suivi les migrations de modules ou de surfaces UI.
- Etat : corrections appliquees pour les commentaires verifies et les restes evidents trouves par motifs obsoletes.

## Corrections appliquees

- Suppression des references a la recherche Settings inexistante et a la TitleBar interactive.
- Remplacement des commentaires Ambient qui confondaient Settings AmbientPage, Playground tuning et ancien mode `Realistic`.
- Alignement de la calibration micro sur `MicrophoneCalibrationCalculator` et de `LevelWindow` sur `AudioLevelMapper`.
- Nettoyage des commentaires telemetry/Chrono encore dates par les sous-vagues et le legacy pipeline.
- Mise a jour des chemins natifs/scripts, de PhiBench, des references docs inexistantes, des commentaires Setup B.x et du stale `InternalsVisibleTo`.
- Deuxieme passe documentaire : `CLAUDE.md` et README corriges pour les assertions fausses reperees (native runtime reference absente, DXGI/WGC, Hue REST vs Entertainment v2, calibration micro, Settings search, LogWindow/Telemetry boundaries, Setup wizard, module ownership).

## Findings verifies

### P1 — SettingsWindow annonce une recherche qui n'existe pas

- Fichiers : `src/Deckle.Settings/SettingsWindow.xaml:13`, `src/Deckle.Settings/SettingsWindow.xaml.cs:20`, `src/Deckle.Settings/SettingsWindow.xaml.cs:58`.
- Commentaire : la recherche vivrait dans `NavigationView.AutoSuggestBox`, avec un contenu interactif sous la TitleBar.
- Realite verifiee : aucun `AutoSuggestBox` ni slot de recherche n'est defini dans `SettingsWindow.xaml`; le `NavigationView` ne porte pas de `AutoSuggestBox`.
- Impact : un agent peut chercher une surface de recherche inexistante ou justifier `PreferredHeightOption`/layout sur un controle absent.

### P1 — Settings AmbientPage decrit encore l'ancien panneau de tuning

- Fichiers : `src/Deckle.Lighting.Ambient/Ui/AmbientPage.xaml.cs:18`, `src/Deckle.Lighting.Ambient/AmbientSettings.cs:172`, `src/Deckle.Lighting.Ambient/Engine/AmbientEngine.cs:252`, `src/Deckle.Lighting.Ambient/Engine/AmbientEngine.PushLoop.cs:23`, `src/Deckle.Vision/FrameSampler.cs:130`.
- Commentaire : la Settings `AmbientPage` aurait un mode `Game / Realistic`, des sliders HDR, un tuning panel, et les sliders `AmbientPage` appliqueraient live.
- Realite verifiee : `Ui/AmbientPage.xaml` expose `Game / Movie / Ambient / Custom`, Hue pairing et un bouton vers le Playground; le commentaire XAML indique explicitement que le tuning vit dans le Playground.
- Impact : confusion directe entre la Settings AmbientPage et la Playground AmbientPage; risque de modifier la mauvaise surface.

### P1 — Auto-calibration micro documentee avec l'ancienne formule

- Fichier : `src/Deckle.Audio/CaptureSettings.cs:41`.
- Commentaire : auto-calibration depuis `microphone.jsonl` avec `median(p10) -> MinDbfs` et `median(p90 + 2 dB) -> MaxDbfs`.
- Realite verifiee : `MicrophoneCalibrationCalculator` calcule `MinDbfs = median(p25) - 5 dB`, `MaxDbfs = median(p90) + 5 dB`, avec clamp plancher `-75 dBFS`, spread minimal `10 dB`, clamp slider et tolerance `0.5 dB`.
- Impact : commentaire trompeur sur le comportement utilisateur et sur la calibration effective du HUD.

### P1 — TelemetrySettings / bootstrap gardent des etats de vague obsoletes

- Fichiers : `src/Deckle.Diagnostics.Telemetry/TelemetrySettings.cs:18`, `src/Deckle.Diagnostics.Telemetry/TelemetryListenerBootstrap.cs:30`, `src/Deckle.Core/CorpusPaths.cs:29`, `src/Deckle.App/App.xaml.cs:156`.
- Commentaire : `ApplicationLogToDisk` et `StorageDirectory` seraient precables mais pas branches au runtime; l'App lirait encore un legacy `AppTelemetryGates` ou un futur `TelemetrySettingsService`.
- Realite verifiee : `App.xaml.cs` cable deja `TelemetryListenerBootstrap.ConfigureGates` sur `TelemetrySettingsService.Instance.Current`, et `CorpusPaths.ConfigureStorageDirectoryOverride` lit `TelemetrySettingsService.Instance.Current.StorageDirectory`.
- Impact : commentaire inverse la source de verite actuelle et peut faire reintroduire une couche legacy inexistante.

### P1 — Pilot Event Chrono annonce une retraite Wave 2 mais tourne toujours au boot

- Fichiers : `src/Deckle.App/App.xaml.cs:215`, `src/Deckle.App/App.xaml.cs:218`, `src/Deckle.App/App.xaml.cs:220`, `src/Deckle.Chrono/DeckleChronoSource.cs:35`.
- Commentaire : `PilotEmitted` serait une sanity check Wave 1 retiree en Wave 2 quand un vrai provider applicatif existe.
- Realite verifiee : l'app emet encore `DeckleChronoSource.Log.PilotEmitted("wave 1 boot")` au demarrage, alors que les commentaires alentour situent deja le code en vagues beaucoup plus tardives.
- Impact : bruit d'observabilite maintenu sous un commentaire qui dit qu'il aurait du disparaitre.

### P2 — LevelWindow pointe encore vers des statics HudChrono

- Fichiers : `src/Deckle.Audio/CaptureSettings.cs:29`, `src/Deckle.Transcription/ITranscriptionEngineHost.cs:31`, `src/Deckle.Transcription/Engine/TranscriptionEngine.Pipeline.cs:57`, `src/Deckle.App/App.xaml.cs:558`, `src/Deckle.App/App.xaml.cs:609`.
- Commentaire : `ApplyLevelWindow` pousserait les valeurs dans des statics `HudChrono`.
- Realite verifiee : `App.ApplyLevelWindow` ecrit `Audio.AudioLevelMapper.MinDbfs`, `MaxDbfs`, `DbfsCurveExponent`; `HudChrono` consomme `AudioLevelMapper.RmsToPerceptualLevel`.
- Impact : ownership mal decrit; le domaine est audio, pas HUD.

### P2 — PlaygroundWindow dit Auto alors que le pane est force en LeftCompact

- Fichiers : `src/Deckle.Playground/Views/PlaygroundWindow.xaml:2`, `src/Deckle.Playground/Views/PlaygroundWindow.xaml.cs:18`.
- Commentaire : le shell utiliserait `NavigationView Auto`.
- Realite verifiee : le XAML force `PaneDisplayMode="LeftCompact"` et `IsPaneOpen="False"`.
- Impact : commentaire de shell faux pour tout raisonnement responsive sur le Playground.

### P2 — AppSettings pointe TelemetrySettings vers l'ancien module

- Fichier : `src/Deckle.Settings/AppSettings.cs:23`.
- Commentaire : `TelemetrySettings -> Deckle.Logging/TelemetrySettingsService`.
- Realite verifiee : le service actuel est `Deckle.Diagnostics.Telemetry.TelemetrySettingsService`.
- Impact : erreur de dependance/module dans la carte mentale du settings split.

### P2 — AppSettings decrit une resolution AppPaths qui n'existe plus

- Fichier : `src/Deckle.Settings/AppSettings.cs:67`.
- Commentaire : les chemins vides seraient resolus par `AppPaths` "a cote de l'exe en dev unpackaged, sous LocalState en package MSIX".
- Realite verifiee : `AppPaths.ResolveUserDataRoot` resout `DECKLE_DATA_ROOT`, puis `%LOCALAPPDATA%\\Deckle`, puis seulement en dernier fallback `<exeDir>\\Deckle`; aucun chemin `LocalState` MSIX n'est implemente.
- Impact : commentaire faux sur l'emplacement des donnees utilisateur et des backups.

### P2 — Scripts natifs referencent des chemins de code devenus faux

- Fichiers : `scripts/lib/setup-assets.ps1:6`, `scripts/lib/setup-assets.ps1:15`, `scripts/lib/publish-native-runtime.ps1:54`, `scripts/lib/publish-native-runtime.ps1:259`.
- Commentaire : `AppPaths.cs` serait sous `src/Deckle.App/`, `NativeRuntime.cs` sous `src/Deckle/Setup/`, et `-FromRelease` serait le default pour les non-rebuilders.
- Realite verifiee : `AppPaths` vit dans `src/Deckle.Core/Paths/AppPaths.cs`; `NativeRuntime` vit dans `src/Deckle.Transcription.Whisper/Setup/NativeRuntime.cs`; `setup-assets.ps1` a `DefaultParameterSetName = 'WhisperRepo'` et ne telecharge la release que si `-FromRelease` est fourni.
- Impact : mauvais pointeurs pour maintenance scripts et onboarding.

### P2 — PhiBench promet un fallback prompt qui ne s'applique pas aux regimes vides

- Fichiers : `benchmark/cs/PhiBench/Models/Regime.cs:8`, `benchmark/cs/PhiBench/Phi4Transcriber.cs:21`, `benchmark/cs/PhiBench/Phi4Transcriber.cs:83`, `benchmark/cs/PhiBench/CorpusRunner.cs:73`.
- Commentaire : un regime `Prompt + SystemPrompt` vide serait coerced vers `"Transcribe this audio in French."` au niveau transcriber.
- Realite verifiee : `Phi4Transcriber` fallback uniquement sur `userPrompt == null`; une chaine vide est explicitement honoree. `CorpusRunner` passe `regime.Prompt`, que `RegimesLoader` remplit avec `string.Empty` quand le TOML est vide ou absent.
- Impact : les runs corpus PhiBench peuvent envoyer un prompt utilisateur vide alors que les commentaires du model/regime disent l'inverse.

### P3 — Commentaires code pointant vers des docs inexistantes

- Fichiers : `src/Deckle.Vision/ScreenCaptureService.cs:25`, `src/Deckle.Lighting/Hue/HueColorMath.cs:93`, `scripts/lib/setup-assets.ps1:18`, `scripts/lib/publish-native-runtime.ps1:19`.
- Commentaire : renvois vers `docs/architecture--color-science-pipeline--0.1.md` et `docs/reference/reference--native-runtime--1.0.md`.
- Realite verifiee : ces deux fichiers n'existent pas dans `docs/` au commit audite.
- Impact : moins grave pour le runtime, mais les commentaires promettent une justification introuvable au moment de modifier ces zones.

### P3 — Test ProximityRollup demande encore d'ajouter InternalsVisibleTo deja present

- Fichiers : `tests/Deckle.Tests/Hud/ProximityRollupAggregatorTests.cs:7`, `src/Deckle.Hud/Deckle.Hud.csproj:29`.
- Commentaire : "Verifier au build" et ajouter `[InternalsVisibleTo("Deckle.Tests")]` cote `Deckle.Hud` si l'acces est bloque.
- Realite verifiee : `Deckle.Hud.csproj` declare deja `<InternalsVisibleTo Include="Deckle.Tests" />` via MSBuild.
- Impact : faible, mais le commentaire entretient une consigne de setup devenue obsolète; il devrait plutot pointer vers le csproj actuel ou disparaitre.

## Notes de passage

- `audits/` existe mais son schema est volontairement hebdomadaire et court ; cette review exhaustive vit donc dans `docs/reviews/`.
- Worktree initial deja sale hors review : `audits/index.csv` modifie et `audits/runs/2026/2026-06-01--codex.md` non suivi.
- Etat worktree observe plus tard : deux suppressions non liees a cette review dans `docs/research/`.
- Les `CLAUDE.md`/README signales pendant la premiere passe ont ete corriges dans la deuxieme passe. Les ADR et `docs/research/` restent hors scope de cette review.
