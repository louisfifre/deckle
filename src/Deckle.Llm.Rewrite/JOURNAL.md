---
description: Dated decisions and findings for Deckle.Llm.Rewrite — engine seam, paragraph retaille, gate calibration, measurements.
type: module-journal
---

# JOURNAL — Deckle.Llm.Rewrite

## 2026-07-20 — Retaille slice 2: the gated offer is an interactive transient

Chose a revisioned offer lifecycle: Shift+Enter closes the continuously observed paragraph, the interactive request has a 3 s deadline on ministral-3:14b, and any later text edit, opaque caret move, pointer interaction, or surface change cancels the request and hides an existing offer. An autocorrection is mirrored into the observed paragraph only when its original span occurs once; ambiguity invalidates the paragraph instead of guessing.

Chose a separate Acrylic tool window for the offer, anchored to the focused UIA element and shown with `SW_SHOWNOACTIVATE` but without `WS_EX_NOACTIVATE`: its appearance leaves typing focus in place, while an explicit click can activate native WinUI buttons, keyboard focus, Escape, and accessibility peers. Accept restores the captured target window, replaces the exact closed tail in one `SendInput` batch, and recreates the closing Shift+Enter gesture; the HUD remains click-through and unchanged.

## 2026-07-20 — Retaille slice 1: gate + seam + prompt, measured on the paragraph-gate study

The diff gate is a pure all-or-nothing validator (`Gate/`): strictly monotone DP alignment whose transitions are exactly the three framed rules — bounded-form replacement (Levenshtein on normalized concatenations, groups up to 3→3 for phonetic re-segmentation), closed-class insertion, duplicate/crutch deletion. Disallowed edits carry a penalty larger than any all-allowed path, so the optimum contains a violation only when no violation-free alignment exists; the verdict carries the full edit script either way (future offer display and dataset). The engine seam landed with it: `IRewriteEngine` behind `RewriteService` (API unchanged for transcription), Ollama transport extracted to `OllamaEngine`, deadline owned by the caller — an OnnxEngine drops in without clients moving.

Measured on the paragraph-gate study (16 typed-style samples, RX 7900 XT, warm model): ministral-3:14b gives 11 offers + 1 identity / 16 at p50 ~650 ms, p95 1.7 s — the ~2-3 s offer budget holds with margin; cold load is ~10 s, which is what the opportunistic warm-up exists to hide. Every accepted output was faithful; every reject was a caught model hallucination. ministral-3:3b is under the capability floor for this task (paraphrases, francizes, spells out digits): the gate neutralizes all of it but the offer rate stays low (~8-11/16) — model choice is a product decision, not a prompt fix.

Prompt decided by a judged optimization (4 variants vs baseline, criterion = fidelity of gate-ACCEPTED outputs, not raw offer count): the few-shot form won — two instruction paragraphs, contract carried by six entrée/sortie pairs each pinning a measured failure mode. Failure modes measured on the baseline prompt, worth remembering: meta-capture (a paragraph that TALKS about rules/edits flips the model into demonstrating instead of processing — inline examples are the hallucination's raw material), completion of unfinished sentences, register promotion ("direct" → "directement", "config" → "configuration"), and do-something bias on already-clean text (identity must be stated as the first-rank outcome).

Three gate false-accept classes were measured on that eval and closed the same day: markdown formatting characters passing as "punctuation insertion" (insertable punctuation is now a closed French-text set), an identical neighbor inside a group replacement diluting the concatenated distance ("pas lisible" → "peu lisible" as one 2→2 group — groups now refuse any verbatim-surviving word), and francization at the 34 % relative bound ("gate" → "gâteau") — bound tightened to 25 % (cap 3), which keeps "samarreter" → "sans m'arrêter" in and known vocabulary drifts out. Each class is pinned by a test.
