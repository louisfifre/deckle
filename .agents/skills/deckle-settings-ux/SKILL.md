---
name: deckle-settings-ux
description: What to expose in settings surfaces and how to organize it. Invoke before exposing a setting, organizing a page, or reworking a settings surface.
---

# Deckle — Settings UX

## Intent

Build a settings surface on transferable good practice — what to expose, how to organize it, and which control fits each kind of setting.

## How

Every exposed setting is a debt — findability, testing, a real fix postponed — so each earns its place against a default that just works. If a value can be decided for the user, decide it. Exposing depth is legitimate when the audience wants it, but the cost is paid by good defaults and discoverability — grouping, search, a modified-only view — never by hiding what the user came for.

Three levels at most to reach a setting, and the last two are earned. A module, under it a page only once the module's surface no longer fits one, then an in-page fold for what configures a switch or depends on a choice. A fourth means rework, not more depth, and folds never nest. A module split into pages is a rail node that expands, its pages at one level and the first named General. Every page shows a breadcrumb in place of its title, the way up when the rail is closed. Organize by module rather than by loosely related theme. A setting that does not apply in the current state is hidden, not greyed. Greying is only a transient busy signal while an action runs, never a substitute for hiding.

A page is a header, then sections of cards. The header carries the breadcrumb, one line of description and the reset of the whole surface. A module that can be off has its switch as the first card, alone. A section is a title, at most one line of caption, cards. A card's description says the effect in one sentence or is absent, never the default, which the reset tooltip carries. A choice that decides which settings exist sits in the header of the fold that holds them. An instrument shows and never writes, and the values it draws are fields beside it.

A change applies at once and is written when its gesture ends, at the release of a slider or the commit of a field, never on every step. A reset to its default replaces any OK to confirm. The default has one source, and the reset appears on hover, only once the value has changed, at three levels, the value, its group, the whole surface. Two changes break the rule. One hard to undo is held until confirmed, not applied then reversed. One that needs a restart is confirmed first, then offered the restart.

Confirmation is set by how reversible an action is, never by where the setting sits. And when a setting must warn, it speaks through one inline channel whose wording carries the tone — no ladder of severities.

## Categories and controls

First sort what you're looking at: a persistent setting, a command (an immediate action — a button or menu, not the list), or a diagnostic (consulted, not configured). Then a setting's value-nature picks its control — see the control catalogue in `references/controls-and-behaviour.md` for the full mapping and the finer kinds. The one worth stating here. A numeric magnitude is always one control, a number field for the exact value with a slider beside it when the value earns a gesture, sized by a chosen fineness, not by hand-numbered increments.

For two similar options, say what each does (its effect), not what it is (its mechanism).
