---
description: Module catalogue and presence — which user-facing modules exist, their dependency edges, and which ones the user chose to have installed.
type: agent-instructions
---

# AGENTS.md — Deckle.Modules

The presence catalogue: `ModuleDescriptor` names a user-facing module (id, glyph, dependency edges, provisioning probe), `ModuleRegistry` holds the set the composition root declares at boot, `ModulePresence` answers « did the user choose to have this module installed? » from `modules/presence.json`, and `ModuleGraph` carries the selector's cascade rules as pure set arithmetic. Non-UI support: the checkbox page rendering this model lives in `Deckle.Setup`.

Two axes, never merged: **presence** (chosen at install — unchecked means the module's engine is not composed and its settings pages never register) sits above **runtime activation** (a module's own `Enabled` toggle — a disabled module is still installed). A third state falls out of the descriptor: chosen but not provisioned (`IsProvisioned` false) — present, visible, not runnable yet.

Non-obvious decisions:

- **Descriptors are declared by the composition root, not by the modules they name** — the deliberate inverse of `SettingsModuleDescriptor`. The installer companion's end state is a catalogue that can describe a module whose DLLs are *not on disk*; knowledge hosted inside the described assembly can never reach that state. Selector wording follows the same logic and lives with the selector, keyed by module id.
- **No choice on disk = everything present.** Installs that predate the presence model and dev builds keep today's behaviour; a corrupt file degrades the same way (with a warning) instead of making modules vanish on a bad byte.
- **No Changed event on the registry**, unlike `SettingsModuleRegistry`: composition happens once in `OnLaunched`, so a presence change only takes effect through the restart the wizard already performs.
