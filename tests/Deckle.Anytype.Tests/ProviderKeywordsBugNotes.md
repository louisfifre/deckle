# Anytype provider keyword bug notes

## Backend lifecycle lines were invisible to the cross-module Lifecycle filter

- Trigger: someone filters the log on the transverse `Lifecycle` keyword (bit 0) to follow how modules start, stop and fail.
- Symptom: the backend's start, stop and failure milestones appeared, but `BackendUnresponsive`, `BackendResponsive`, `BackendRestartsFailing`, the lease and reconciliation lines and the health probes did not, although several of them are Warnings or journal lines. The MCP host's `RequestRejected`, `HostInternalLog` and the rejection recovery were missing the same way.
- Cause: each provider has a module-local `Lifecycle` bit (0x800 in Anytype, 0x400 in Anytype.Mcp). Only the events written with a second constant, `JournalLifecycle`, also carried the transverse bit, so which lines the filter found depended on which constant the author reached for.
- Violated invariant: a term a person filters on is spelled one way across providers.
- Fix: the module `Lifecycle` constant now includes the transverse bit and `JournalLifecycle` is gone, so one constant means one thing.
- Recurrence cue: an event that carries the module Lifecycle bit and not bit 0 in the manifest.
- Guard: `ProviderKeywordsTests` in `Deckle.Anytype.Tests` and in `Deckle.Anytype.Mcp.Tests`. They hard-code the module bit, so a renumbered bit needs the test updated with it.
- Interpretation: the audit also named the Gesture and Tool events. Those are module-local families on purpose (they are not lifecycle), so they were left alone.