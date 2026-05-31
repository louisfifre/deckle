---
name: journal-vision
description: "Journal daté du module Deckle.Vision — diagnostics de la capture DXGI Output Duplication, hypothèses terrain, learnings du workstream screen capture. Complément réversible au CLAUDE.md du module."
type: module-journal
---

# Journal — Deckle.Vision

Chronique datée du module, complément réversible au [CLAUDE.md](./CLAUDE.md) qui reste timeless. Accueille les diagnostics de la boucle de capture, les observations marquées comme hypothèses, et les learnings du workstream. Quand une entrée stabilise — hypothèse confirmée par mesure, doctrine acquise, décision tranchée — elle monte vers le `CLAUDE.md` ou un ADR et l'entrée devient un pointeur. Entrées récentes en haut.

---

## 2026-05-31 (suite) — Fix livré et confirmé : recreate format-aware, le « gel » re-HDR était le delta-gate

Le mismatch de format diagnostiqué plus bas est **corrigé et confirmé en usage**. `TryRecreateDuplication` est devenu format-aware : re-détection HDR fraîche (facteur DXGI neuf), liste de formats alignée sur l'état courant, readback du format négocié, réécriture de `ActiveFormat`/`PeakLuminance`, et nouvel event `FormatChanged` levé sur changement de format **ou** de taille. `AmbientEngine` reconstruit le `FrameSampler` en place sur ce signal (thread worker capture, sérialisé avec `Process`, swap atomique). Commits `5e35b0c` `fix(vision): make the duplication recreate format-aware` + `e4829c2` `fix(ambient): rebuild the frame sampler on capture format change`. La doctrine timeless est montée dans le [CLAUDE.md](./CLAUDE.md) (section *HDR and cadence*), cette entrée en devient le pointeur chronologique.

**Scénario A confirmé, B écarté (mesuré).** La télémétrie `app.jsonl` porte l'event Info `{"mode":"SDR"}` puis `{"mode":"HDR"}` à chaque toggle (renégociation + rebuild, ~2 ms d'écart) : HDR→SDR déclenche bien un `ACCESS_LOST` + recreate, le funnel voit le flip. Le scénario B (duplication qui ne se recrée pas, gel hors-format) est écarté. HDR→SDR : les lampes suivent.

**Le « pas de récup en re-HDR » résolu — ce n'était pas la capture.** L'hypothèse ouverte du diagnostic ci-dessous est levée : le moteur ne s'arrête jamais tout seul au re-HDR (aucun `capture_lost` ni `DeviceLost` en télémétrie ; la session tournait encore 90 s après le toggle, le seul arrêt observé portait `reason:"user"`). Le « gel » perçu était le **delta-gate** (`ChangeThreshold`, push loop d'`AmbientEngine`) : écran statique après le toggle → aucun changement de couleur → push supprimé → les lampes tiennent leur dernière valeur. Comportement voulu, pas un bug — ça réagit dès que l'écran bouge (validé en usage).

**Résidu cosmétique (observé).** `DuplicateOutput1` renvoie un `E_ACCESSDENIED` transitoire sur un recreate pendant que le mode HDR se stabilise (vu dans les deux sens). La boucle de retry l'absorbe (backoff 2 s). Émis en Warning (`DuplicationRecreateAttemptFailed`) — niveau discutable pour un transitoire auto-résolu ; reporté au chantier observabilité, pas tranché ici.

**`CaptureStallDetector` orphelin.** Écrit comme watchdog acquire-based (`b6abd1d`, 7 tests verts), puis pressenti pour un repurpose en capteur de changement de format. Le scénario A étant confirmé — le funnel recreate détecte le flip inline, sans capteur séparé — ce type n'a plus d'objet. Il reste committé mais non câblé ; décision « retirer ou garder parké » laissée à Louis.

**Learning méthodo.** `app.jsonl` ne persiste que le payload (ni nom d'event, ni provider, ni niveau, ni message rendu) : un event sans param devient un blob vide, et avec le gate `LogAmbientCaptureActivity` off seuls les jalons Info/Warning/Error passent — impossible de distinguer « lampes suivent un écran statique » de « lampes gelées » sans rouvrir le gate. Le Copy de la LogWindow tronque sur les grosses sélections, ce qui a forcé la lecture directe du disque pour ce diagnostic. Ces deux points alimentent le chantier observabilité séparé.

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
