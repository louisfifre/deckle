---
description: Module catalogue and presence — which modules exist and are installed.
type: agent-instructions
---

# AGENTS.md — Deckle.Modules

- **Presence and runtime activation are two axes, never merged.** Presence is chosen at install: an absent module's engine is not composed and its settings pages never register. Activation is a module's own `Enabled` toggle: a disabled module is still installed.
- **Descriptors are declared by the composition root, not by the modules they name.** This is the deliberate inverse of `SettingsModuleDescriptor`. The catalogue must describe a module whose DLLs are not on disk, so this knowledge cannot live in the described assembly. Selector wording follows the same rule: it lives with the selector, keyed by module id.
- **No choice on disk means everything present.** Older installs and dev builds keep working. A corrupt file degrades the same way, with a warning, instead of making modules vanish.
- **The registry has no `Changed` event**, unlike `SettingsModuleRegistry`. Composition happens once in `OnLaunched`, so a presence change takes effect through the restart the wizard performs.
