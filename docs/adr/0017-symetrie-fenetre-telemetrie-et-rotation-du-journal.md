---
name: adr-0017-symetrie-fenetre-telemetrie-et-rotation-du-journal
description: "Acte que le journal applicatif persisté app.jsonl devient le miroir auto-descriptif du journal live (provider/event/level/message + payload), borné par rotation par taille, tandis que les canaux dataset (latency/microphone/corpus) restent gelés en payload-only. Conséquences : le rollup heartbeat survit au gate capture, et un transitoire auto-résolu est Verbose et non Warning."
type: adr
---

# ADR-0017 — Symétrie fenêtre↔télémétrie et rotation du journal applicatif

**Status** — accepted le 2026-05-31

## Contexte

Un diagnostic de capture le 2026-05-31 (journal `Deckle.Vision`) a été posé en lisant `app.jsonl` directement sur disque plutôt que la LogWindow, et a exposé trois frictions de la surface d'observabilité.

Le fichier `app.jsonl` avait gonflé à ~23 Mo / 118k lignes sans cap ni rotation — archivé à la main. Aucune app Windows first-party ne laisse un journal croître sans borne.

Le schéma persisté ne portait que l'enveloppe `{timestamp, kind, session, payload}` : ni nom d'event, ni provider, ni niveau, ni message rendu. Or `EventEntry`, le DTO que les listeners construisent, porte déjà tout cela — la LogWindow l'affiche, le JSONL le jetait. Un event sans paramètre devenait alors un `payload: {}` vide, illisible et indistinguable, et le fichier était asymétrique avec la fenêtre live. Le diagnostic a buté précisément là : impossible de distinguer « lampes qui suivent un écran statique » de « lampes gelées » sur le seul payload.

Cette asymétrie pose la question de fond de la relation voulue entre la fenêtre live (`LogWindowEventListener`, exhaustive, éphémère, rendue pour un humain) et la télémétrie persistée (`JsonlEventListener`, durable, gatée par consentement). La fenêtre est le miroir exhaustif ; le fichier en était une version dégradée et anonyme.

Une contrainte encadre la réponse. Les canaux `latency`/`microphone`/`corpus` sont des **datasets** : leur schéma est un contrat machine stable, consommé par l'outillage benchmark et figé par [ADR-0011](./0011-corpus-normalise-comme-dataset-ml.md). On ne peut ni les rouler (on tronquerait un dataset) ni réécrire leur schéma. Le seul canal réellement asymétrique est `app.jsonl`, le journal applicatif général — et aucun script benchmark ne le lit (vérifié).

Effet de bord lié, découvert au même diagnostic : le rollup heartbeat ambient (périodique, « boucle vivante, N pushs, N drops » toutes les 5 s) est émis en `Verbose` avec le keyword `Heartbeat`, et le drop-filter capture jette tout `Verbose` Ambient/Vision/Lighting pendant la capture quand le toggle `LogAmbientCaptureActivity` est off. Le filet « boucle vivante, rien à pousser » était donc filtré à mort en même temps que le `Verbose` par-tick qu'on voulait taire.

## Options considérées (symétrie de `app.jsonl`)

- **A. Symétrie complète.** Chaque ligne porte `provider` + `event` + `level` + `message` (le `FormattedMessage` rendu) à côté du `payload`. Le fichier devient auto-descriptif : reconstruction de la fenêtre depuis le disque, grep par niveau/provider/event, un event sans param garde son identité. Standard first-party (logging structuré à la Serilog/ETW). Les données sont déjà dans `EventEntry` → zéro plomberie côté producteur ; le changement est purement additif en JSON (un lecteur qui clé sur `payload` n'est pas affecté).

- **B. Identité minimale.** Ajouter `provider` + `event` + `level` seulement, pas le message rendu (dérivable du manifeste + payload). Lignes plus courtes, désambiguïse le blob vide, mais l'asymétrie de lisibilité demeure : il faut re-render pour lire le fichier.

- **C. Canal miroir séparé.** Laisser `app.jsonl` payload-only, ajouter un nouveau canal miroir complet. Plus de fichiers, vérité scindée en deux, plus de machinerie pour zéro gain sur le canal qui posait problème.

## Décision

**Option A.** `app.jsonl` devient le miroir auto-descriptif du journal live : chaque ligne porte `provider`, `event`, `level` (nom de `EventLevel`), `message` (rendu, `null` quand le provider n'a pas de template), puis le `payload` flat inchangé. Le sens de symétrie retenu : **le fichier persiste l'identité que la fenêtre rend**, pas seulement la charge utile. La sélection (le gate `ApplicationLogToDisk`, les drop-filters) décide *ce qui* est persisté ; ce qui est persisté est désormais *complet*. Le choix de schéma est porté par-listener via l'enum `JsonlSchema` (`PayloadOnly` / `SelfDescribing`), orthogonal au gate.

Les canaux dataset (`latency`, `microphone`, `corpus`) **restent gelés en `PayloadOnly`** — contrat ADR-0011, consommateurs benchmark.

**Rotation.** `app.jsonl` est borné par une `JsonlRotationPolicy` (roll par taille : `app.jsonl` → `app.1.jsonl` → … → `app.{N}.jsonl`, plus ancienne génération supprimée). Borne retenue : **5 Mo × 5 générations** (≈30 Mo total). Les datasets ne reçoivent aucune politique et restent append-only.

**Couverture du signal de liveness.** Le rollup heartbeat doit survivre au gate capture — corollaire direct de la symétrie : si la surface persistée prétend refléter l'état du système, elle doit porter le signal qui prouve la liveness sur écran statique. Le drop-filter capture devient keyword-aware (les keywords sont déjà sur `EventWrittenEventArgs`, donc l'exemption reste sans allocation) et n'efface jamais un event portant le keyword `Heartbeat`, dans la fenêtre comme dans `app.jsonl`. Le `Verbose` par-tick (keyword `Push`, etc.) reste tu.

**Règle de niveau corollaire.** Un transitoire qu'une boucle de retry absorbe seule, sans effet visible ni accumulation, est `Verbose`, pas `Warning` (gravé dans la doctrine [Deckle.Diagnostics](../../src/Deckle.Diagnostics/CLAUDE.md)). Le `DuplicationRecreateAttemptFailed` de `Deckle.Vision` (un `E_ACCESSDENIED` pendant qu'un toggle HDR se stabilise, attempt toujours 1) en est le cas d'école ; sa correction d'une ligne revient au module de capture et suit la règle posée ici.

## Conséquences

Devient plus facile : diagnostiquer sur disque sans rouvrir la fenêtre — `app.jsonl` est greppable par `level`, `provider`, `event`, et lisible directement (le `message` rendu y est). Un event sans param n'est plus un blob anonyme. Le journal ne peut plus saturer le disque.

Devient encadré : tout nouveau canal JSONL choisit explicitement son `JsonlSchema`. Par défaut `PayloadOnly` (le défaut sûr pour un dataset) ; `SelfDescribing` réservé aux journaux destinés à la lecture humaine. La règle « datasets gelés / journaux auto-descriptifs » est la ligne de partage.

À vérifier en usage : que le heartbeat apparaisse bien pendant une capture ambient avec le toggle off (validé par construction — l'exemption est déterministe — mais la confirmation terrain reste à faire). Le coût d'allocation de l'exemption est nul (décision au niveau early, avant `BuildEntry`).

Non couvert ici : la troncature du Copy de la LogWindow sur grosses sélections relevée au même diagnostic — corrigée par ailleurs côté `Win32Clipboard`, friction distincte.
