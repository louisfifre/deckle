---
name: journal-vision
description: "Journal daté du module Deckle.Vision — diagnostics de la capture DXGI Output Duplication, hypothèses terrain, learnings du workstream screen capture. Complément réversible au CLAUDE.md du module."
type: module-journal
---

# Journal — Deckle.Vision

Chronique datée du module, complément réversible au [CLAUDE.md](./CLAUDE.md) qui reste timeless. Accueille les diagnostics de la boucle de capture, les observations marquées comme hypothèses, et les learnings du workstream. Quand une entrée stabilise — hypothèse confirmée par mesure, doctrine acquise, décision tranchée — elle monte vers le `CLAUDE.md` ou un ADR et l'entrée devient un pointeur. Entrées récentes en haut.

---

## 2026-05-31 — Diagnostic : la screen capture gèle au toggle HDR (fix non codé)

Le « screen capture s'arrête de lui-même pendant la capture ambient » a été reproduit et diagnostiqué. Le déclencheur n'est ni un jeu ni le secure desktop comme supposé d'abord — c'est le **toggle HDR du bureau**. Diagnostic posé en lisant directement `app.jsonl` (la séquence `CaptureSessionConfigured` + `DuplicationRecreateAttemptFailed`), pas le code.

**Établi (mesuré dans `app.jsonl`).** Deux modes selon l'état HDR au démarrage de la session de capture :

- **Démarré en SDR, on active HDR** → `DuplicateOutput1` renvoie `E_ACCESSDENIED` au recreate (`DuplicationRecreateAttemptFailed`, Warning **visible**), puis ça récupère. Transitoire.
- **Démarré en HDR, on désactive HDR** → **aucune ligne de log, capture gelée**, et pas de récupération. Les sessions dont le `CaptureSessionConfigured` porte `hdr_state:on` (format `R16G16B16A16Float`) ne portent **aucun** `Access is denied` ensuite : mort silencieuse confirmée par l'absence.

Sur **tous** les échecs de recreate, `attempt` vaut toujours **1**, jamais 2/3 → le recreate ne boucle pas / ne storm pas. Le « bloqué » n'est donc pas un retry-storm.

**Établi (lecture code).** `TryRecreateDuplication` lit la nouvelle taille (`GetDuplicationDesc`) mais ne réécrit **jamais** `_activeFormat`/`_activeDxgiFormat` — seule `Start()` les pose. Et le `FrameSampler` est construit **une seule fois** dans `AmbientEngine.StartAsync` (format + peak luminance figés), jamais reconstruit. La pipeline assume un format fixe pour la vie de la session.

**Hypothèse forte, à confirmer par instrumentation.** Le cas silencieux HDR→SDR viendrait de ce que `DuplicateOutput1` réussit en négociant BGRA8 (le fallback de la liste HDR) **sans exception** — donc pas de Warning — mais le pipeline croit toujours FP16, le sampler tone-mappe du FP16 sur des octets BGRA8 → sortie morte/garbage sans erreur. Conséquence directe sur l'instrumentation : un watchdog basé sur les **acquires** ne verrait pas ce cas (les acquires tournent, c'est la sortie sampler qui est morte) — le bon capteur est un **détecteur de changement de format au recreate**.

**Non expliqué (hypothèse ouverte).** Le « pas de récupération même en repassant en HDR » observé par Louis n'est pas couvert par la seule hypothèse mismatch de format. À éclaircir avec l'instrumentation en place.

**Direction de fix (non codée).** Rendre le recreate format-aware — réécrire `_activeFormat`/`_activeDxgiFormat` après `DuplicateOutput1` et exposer un signal `FormatChanged` — puis `AmbientEngine` reconstruit le `FrameSampler` sur ce signal (le format **et** la peak luminance changent au toggle HDR). Cross-module, avec du threading : le signal partirait du worker thread de capture, à marshaller comme `OnCaptureStopped` le fait déjà.

**WIP en place.** [`CaptureStallDetector`](./CaptureStallDetector.cs) + tests unit (commit `b6abd1d`) — logique de décision pure, horloge injectée, 7 tests verts. Écrit comme watchdog acquire-based avant que le diagnostic ne montre que le cas silencieux a des acquires OK → **à repurposer en capteur de changement de format**.

**Learning méthodo.** Lire `app.jsonl` directement sur disque bat le copier-coller depuis la LogWindow (dont le Copy tronque). Limite découverte en chemin : `app.jsonl` ne persiste **que le payload** (ni event name, ni provider, ni level, ni message rendu) — un event sans params devient un blob vide, et le fichier est asymétrique avec ce qu'affiche la fenêtre. Le fichier avait par ailleurs gonflé à 118k lignes / 23 Mo faute de cap/rotation disque (archivé). Ces points alimentent le chantier « surface d'observabilité » séparé.
