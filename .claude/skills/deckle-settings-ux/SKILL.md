---
name: deckle-settings-ux
description: "UX doctrine for Deckle's settings surfaces: what to expose and how to organize it — sensible defaults over knobs, two-level progressive disclosure, settings vs commands, conditional pages shown-not-greyed, immediate application, customization as a cost. Grounded in NN/g and Microsoft guidance. Invoke before exposing a setting, organizing a page, or overhauling a settings surface. Triggers like deckle settings UX, organize settings, what to expose, progressive disclosure, settings vs commands, toggle label."
type: skill
---

# Deckle — Experience doctrine for settings surfaces

## Role

Answers one question: **what do we expose in settings surfaces, and how do we organize it**. Invoked before adding an exposed setting, reorganizing a page, hiding or promoting an option, or overhauling a surface that has accumulated complexity. Applies to settings pages, the log window, the title bar — any surface where the user configures or inspects the system. It does not cover visual rendering (that is `deckle-xaml`); it describes what deserves to appear, where, and with what weight.

## Sensible defaults before customization

Most users never touch settings. So **every exposed setting is a UX debt, not a feature**: the default arbitration is to choose the right behavior, not to expose a knob. When a setting seems necessary, first ask whether it can be inferred automatically — if yes, the system decides and the user chooses nothing. If not, expose it, but pick the default as if it were the only possible value, so the user never needs to touch it for their common case.

## Progressive disclosure at two levels maximum

Never more than **two levels** to reach a setting — beyond that, navigation readability collapses and users get lost backtracking (an established usability rule). First level: the top-level navigation grouping surfaces by theme. Second level: in-page disclosure, where less frequent options live in a fold opened on demand. A need for a third level signals the information architecture should be overhauled, not deepened. **Folds never nest.**

## Settings versus commands

A **setting** is a persistent configuration that changes future behavior; a **command** is an immediate action on the current context. Settings inflation usually comes from confusing the two — "export the logs", "run calibration", "reset this group" are commands, not settings. Commands live in buttons, context menus, dialogs — not in the settings list. Keeping the distinction clean lightens configuration pages considerably.

## Staged disclosure for conditional pages

When a setting only makes sense in a context (another setting enabled, a module configured, a device detected), it **is not greyed out — it is not shown at all**; options appear only when relevant to the current task or selected object. A page that turns hyper-dynamic with many interdependencies signals a misapplication — either it mixes concerns that should split, or the conditional tree is too dense to stay legible. Overhaul it by identifying the independent axes (one setting governs only one other), grouping options that co-vary on the same axis, and showing or hiding them conditionally rather than greying them.

## Immediate application without validate button

Microsoft norm: a changed setting is reflected **immediately** — no Save, Apply, or OK validating a batch of modifications. Code side: auto-save persistence and immediate visual feedback. UX side: a lightweight undo (per session, ideally per setting) rather than a heavy explicit validation. The rare cases that warrant explicit validation (a setting whose error is costly, an action with an external consequence) are commands, not settings.

## Customization has a cost

NN/g distinction: **customization** gives the user control ("choose the theme"); **personalization** does it for them ("we detected your system theme"). Customization carries a real usability cost — users struggle with it. Another argument to reduce exposed settings and prefer automatic adaptation: "follow the system" is almost always the right default for options touching appearance (theme, language, contrast).

## Semantic distinction of options

For two visually similar options (two toggles side by side, two list items), make explicit what differentiates them: a short factual label plus a description of what it *does*, not what it *is* — the expected effect, not the technical justification. A toggle or `ToggleSwitch` never carries a label that changes with its state: the label names what is controlled and stays fixed; the state is read on the switch itself or on the button's checked-state, never restated in words that flip with the value.

## Go fetch the material, do not expose everything

Before overhauling a surface, take a **factual inventory** of what is exposed today and what is persisted but hidden. Overhauling is as much **removing** as organizing — many accumulated options no longer earn their place or can become default behaviors — and it is the moment to spot commands disguised as settings and put them back in the right category.

## Log surface and diagnostic window

The log window and any diagnostic surface follow the same principles — sensible defaults, controlled disclosure, settings (persistent filters) versus commands (live search, copy, export). A catalog or schema view is consulted, not configured: it is diagnostics, not settings, and belongs in the log window.

## Pointers

- **`deckle-xaml`** — the visual rendering doctrine; this skill decides *what* to expose, `deckle-xaml` decides *how* it is drawn.
- **`session-save-context`** — route a non-trivial UX decision through the cascade for a durable trace.
- **NN/g** (progressive disclosure, customization) and the **Microsoft** Windows-settings doctrine — the canonical sources, consulted when a case exceeds this skill's perimeter.
