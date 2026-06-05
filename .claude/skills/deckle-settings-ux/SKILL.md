---
name: deckle-settings-ux
description: What to expose in settings surfaces and how to organize it. Invoke before exposing a setting, organizing a page, or reworking a settings surface.
type: skill
---

# Deckle — Settings UX

## Intent

Build a settings surface on canonical, transferable good practices — what to expose, how to organize it, and which control fits each kind of setting.

## How

Every exposed setting is a debt — findability, testing, a real fix postponed — so each earns its place against a default that just works. If a value can be decided for the user, decide it. Exposing depth is legitimate when the audience wants it, but the cost is paid by good defaults and discoverability — grouping, search, a modified-only view — never by hiding what the user came for.

Two levels maximum to reach a setting: a top grouping, then an in-page fold for the less frequent. A third means rework, not more depth — and folds never nest. Organize by module rather than by loosely-related theme. A setting that only makes sense in a context is hidden entirely, never greyed out.

A changed setting applies immediately, with a light undo rather than a validation step.

## Categories and controls

First sort what you're looking at: a persistent setting, a command (an immediate action — it belongs in a button or menu, not the list), or a diagnostic (consulted, not configured). Then a real setting's value-nature picks its control and how it is shown:

- on/off → a switch; its state shown on the control itself (a shape legible in black-and-white), its label fixed and naming what it governs;
- one of a few mutually exclusive options → radio buttons; more than a few → a dropdown;
- a magnitude over a range — a level, a threshold, a duration/latency — → a slider showing its current value in its unit, increments sized to the range;
- a relationship or shape — a curve, an envelope — → a dedicated editor, never a row of raw numbers;
- free text → one shared input form;
- a path or file → one normalized picker;
- the fine configuration of something activatable → an inline disclosure revealed only when it is on;
- a heavy multi-step action → its own navigated sub-page, returnable from the title bar, never a dropdown or a modal.

For two similar options, say what each does (its effect), not what it is (its mechanism).
