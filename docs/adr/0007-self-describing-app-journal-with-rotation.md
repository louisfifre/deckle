---
name: adr-0007-self-describing-app-journal-with-rotation
description: "Records that the persisted app journal (app.jsonl) becomes the self-describing mirror of the live window (provider/event/level/message + payload), size-rotated, while dataset channels (latency/microphone/corpus) stay frozen in payload-only. Read before changing a JSONL listener schema, the rotation policy, or a capture drop-filter."
type: adr
---

# ADR-0007 — Self-describing app journal with rotation

**Status** — accepted 2026-05-31

## Context

A capture diagnosis on 2026-05-31 (the `Deckle.Vision` journal) was done by reading `app.jsonl` directly on disk rather than the LogWindow, and exposed three frictions of the observability surface.

`app.jsonl` had grown to ~23 MB / 118k lines with no cap or rotation, archived by hand. No first-party Windows app lets a journal grow unbounded.

The persisted schema carried only `{timestamp, kind, session, payload}` — no event name, provider, level, or rendered message. Yet `EventEntry`, the DTO the listeners build, already carries all of it; the LogWindow renders it, the JSONL discarded it. A parameterless event then became an empty `payload: {}`, illegible and indistinguishable, and the file was asymmetric with the live window. The diagnosis stalled precisely there: impossible to distinguish "lamps tracking a static screen" from "frozen lamps" on the payload alone.

A constraint frames the answer. The `latency`/`microphone`/`corpus` channels are **datasets**: a stable machine contract consumed by the benchmark tooling and frozen by ADR-0006. They can neither be rolled (a dataset would be truncated) nor reschematized. The only truly asymmetric channel is `app.jsonl`, the general application journal — and no benchmark script reads it (verified). A linked side effect found in the same diagnosis: the ambient heartbeat rollup (periodic, `Verbose`, keyword `Heartbeat`) was dropped by the capture drop-filter together with the per-tick `Verbose` it was meant to silence.

## Options considered

- **A. Full symmetry.** Each line carries `provider` + `event` + `level` + `message` (the rendered `FormattedMessage`) next to the `payload`. The file becomes self-describing: reconstruct the window from disk, grep by level/provider/event, a parameterless event keeps its identity. The data is already in `EventEntry` → zero producer-side plumbing, a purely additive JSON change.
- **B. Minimal identity.** Add `provider` + `event` + `level` only, not the rendered message. Shorter lines, disambiguates the empty blob, but the legibility asymmetry remains — one must re-render to read the file.
- **C. Separate mirror channel.** Leave `app.jsonl` payload-only, add a new full mirror channel. More files, truth split in two, more machinery for zero gain on the channel that posed the problem.

## Decision

Option A. `app.jsonl` becomes the self-describing mirror of the live journal: each line carries `provider`, `event`, `level`, `message` (rendered, `null` when the provider has no template), then the unchanged flat `payload`. The retained sense of symmetry: **the file persists the identity the window renders**, not only the payload. Selection (the `ApplicationLogToDisk` gate, the drop-filters) decides *what* is persisted; what is persisted is now *complete*. The schema is chosen per-listener via the `JsonlSchema` enum (`PayloadOnly` / `SelfDescribing`), orthogonal to the gate. The dataset channels stay frozen in `PayloadOnly` — ADR-0006 contract, benchmark consumers.

**Rotation.** `app.jsonl` is bounded by a `JsonlRotationPolicy` (roll by size: `app.jsonl` → `app.1.jsonl` → … → `app.{N}.jsonl`, oldest generation deleted). Bound: **5 MB × 5 generations** (≈ 30 MB). The datasets receive no policy and stay append-only.

**Liveness coverage.** The heartbeat rollup MUST survive the capture gate — a direct corollary of symmetry: if the persisted surface claims to reflect system state, it must carry the signal that proves liveness on a static screen. The capture drop-filter becomes keyword-aware and never drops an event carrying the `Heartbeat` keyword, in the window as in `app.jsonl`. The per-tick `Verbose` (keyword `Push`, etc.) stays silenced.

**Corollary level rule.** A transient that a retry loop absorbs alone, with no visible effect or accumulation, is `Verbose`, not `Warning` (carried in the `Deckle.Diagnostics` module doctrine).

## Consequences

Easier: diagnosing on disk without reopening the window — `app.jsonl` is greppable by `level`, `provider`, `event`, and directly readable (the rendered `message` is there). A parameterless event is no longer an anonymous blob. The journal can no longer saturate the disk.

Framed: every new JSONL channel explicitly chooses its `JsonlSchema`. Default `PayloadOnly` (the safe default for a dataset); `SelfDescribing` reserved for human-read journals. "Datasets frozen / journals self-describing" is the dividing line.
