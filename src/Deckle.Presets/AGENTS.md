---
description: Preset families shared by the modules — built-in value sets, a stored selection, and the user's own values under Custom.
type: agent-instructions
---

# AGENTS.md — Deckle.Presets

- A preset family fixes one group of settings in one module. The group is a record class; a preset is an id, a `.resw` label key and the values it writes. Pure support: no UI, no persistence of its own.
- The module keeps its own store and hands it over through `IPresetStore<T>`: the live values, the stored selection, the Custom values. The family never holds a parallel copy.
- Real time, no exceptions (Louis, 2026-10-12): a hand edit saves at once and switches the selection to Custom; picking a preset writes its values; picking Custom again writes the kept Custom values back. The selection is stored, and so are the Custom values. Custom is never derived from value equality once stored.
- After each hand edit of a value the family fixes, the consumer calls `NotifyEdited()`. The family ignores the call while it applies a preset (`IsApplying`), so the consumer keeps no guard of its own for presets.
- The default preset's values are the module's POCO initializers (`From(new XSettings())`), so the per-card reset, the page reset and a fresh install agree on one source.
- A store with no selection yet (a file from before) gets it derived once from its values; after that the stored selection is the truth, even when the values no longer equal the preset's.
- A new consumer adopts the mechanism in five pieces: the values record with `From` / `ApplyTo`, the preset list with its ids, a store adapter over its settings POCO, a `string` selection property on its view model that calls `Select`, and `NotifyEdited()` in the setters the presets cover.
