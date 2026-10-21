---
name: deckle-settings-ux
description: Use when you add a setting, place one on a page or rework a settings surface, so that each setting earns its place, sits at the right depth and gets the control its value calls for.
metadata:
  version: "1.0"
---

## Why

Every exposed setting is a debt. It costs findability, testing and a real fix postponed. A surface carries only what a good default cannot decide, organized the same way everywhere, so a person finds a setting where they expect it.

## Statement

Each setting earns its place against a default that just works. If a value can be decided for the user, decide it. A setting stays when someone may need to move it for another model or for their own use. Exposing depth is legitimate when the audience wants it. Good defaults, clear grouping and search pay for it, and nothing the user came for is hidden.

A setting is reached in three levels at most, and the last two are earned. First the module. Under it a page, only once the module's surface no longer fits one. Then an in-page fold, for what configures a switch or depends on a choice. A fourth level means rework, and folds never nest. A module split into pages is a rail node that expands, its pages at one level and the first named General. Every page shows a breadcrumb in place of its title, the way up when the rail is closed. Settings are organized by module, so a setting is found under the module that acts on it. A setting that does not apply in the current state is hidden. Greying only marks an action in progress.

A page is a header, then sections of cards. The header carries the breadcrumb, one line of description and the reset of the whole surface. A module that can be off has its switch as the first card, alone. A section is a title, at most one line of caption, then cards. A card's description says the effect, as long as understanding needs, or is absent. It never says the default, which the reset tooltip carries. Reference values belong to a legend beside the instrument, not to a description. A choice that decides which settings exist sits in the header of the fold that holds them. A preview or a meter shows and never writes, and the values it draws are fields beside it. A drawing whose points are the value itself, such as a curve, also writes through its handles, each at its release, with its values as fields beside it.

A change applies at once and is written when its gesture ends, at the release of a slider or the commit of a field. A setting has no OK or Apply button. Its reset to the default is the way back. The default has one source. A reset exists for a value, for its group and for the whole surface, and each becomes active once something differs from its default. Two changes break the rule. One hard to undo is held until confirmed. One that needs a restart is confirmed first, then offered the restart.

Confirmation is set by how reversible an action is, never by where the setting sits. When a setting must warn, it speaks through one inline channel whose wording carries the tone.

First tell a setting from a command and from a diagnostic. A setting keeps a value. A command acts now, through a button or a menu. A diagnostic is read, never configured. The nature of a setting's value then picks its control, as the control catalogue says. Every surface is built from the blocks the application already has, in Settings and in the Playground alike, and a need no block covers is discussed before a new form appears. A numeric magnitude is always one control, a number field for the exact value with a slider beside it when the value earns a gesture, its grain derived from its range rather than hand-numbered. Between two similar options, say what each does, not what it is.

## Reference

- [controls-and-behaviour.md](references/controls-and-behaviour.md), the control for each nature of value and the finer magnitude, path, fold, reset and confirmation rules, when choosing a control or wiring a reset.
