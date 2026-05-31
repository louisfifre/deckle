---
name: claude-deckle-diagnostics-telemetry
description: "Doctrine for Deckle.Diagnostics.Telemetry (JSONL telemetry listeners and user gates). Read before wiring a new telemetry channel, adding a consent toggle, or touching listener configuration at boot."
type: agent-instructions
module: Deckle.Diagnostics.Telemetry
---

# CLAUDE.md — Deckle.Diagnostics.Telemetry

Child module of `Deckle.Diagnostics` that carries the **structured persistence** of Deckle telemetry. At boot it configures the parent's `JsonlEventListener` instances with their destination file paths and filter predicates, and exposes the user consent settings that gate those listeners (latency, microphone, corpus, application log).

The module depends on `Deckle.Diagnostics` (interfaces + JsonlEventListener) and `Deckle.Core` (AppPaths for file paths). No dependency on legacy `Deckle.Logging`.

## Responsibilities

`TelemetrySettings` carries the user consent toggles. Defaults are cautious — the posture stays closed until the user opts in.

- **`LatencyEnabled`** — bool, on by default on a dev install, off in preview release. Gates the write of `latency.jsonl`.
- **`MicrophoneTelemetry`** — bool, off by default (GDPR: a microphone RMS summary is not voice content but remains a measurement of the user's microphone).
- **`CorpusEnabled`** — bool, off by default. Gates the write of the two normalized corpus events (`CorpusAsrRecorded` to `<UserDataRoot>/telemetry/corpus/<bucket>/<tier>/corpus.jsonl`, `CorpusRewriteRecorded` to `<UserDataRoot>/telemetry/corpus/<bucket>/corpus.jsonl`). Schema set by ADR-0011: the ASR layer is tier-bucketed by length (`raw/very-short/`, `raw/short/`, …) and rewrite is flat-bucketed by profile (`rewrite-<name>-<id>/`).
- **`RecordAudioCorpus`** — bool, off by default. Gates the write of the raw WAV under `<UserDataRoot>/telemetry/audio/<transcription_id>.wav`, a flat directory deduplicated by invocation (the same WAV is referenced by both JSONL lines, ASR and rewrite). Non-trivial disk cost, consent to request.

`TelemetrySettingsService` is the per-module persistence singleton. Storage under `<UserDataRoot>/modules/telemetry/settings.json`. Pattern aligned with the other `*SettingsService` instances.

`TelemetryListenerBootstrap` is the listener registration API. The App calls `TelemetryListenerBootstrap.Configure(...)` at boot after `TelemetrySettingsService.Instance`; the bootstrap instantiates one `JsonlEventListener` per destination file (one general for `app.jsonl`, specialized listeners for latency / microphone / routed corpus) with the right predicate on the canonical event name. Each listener checks its gate via `TelemetrySettingsService.Instance.Current` on every emission — a toggle change propagates immediately without restart. The general `app.jsonl` listener excludes the dedicated structured telemetry events (`LatencyRecorded`, `MicrophoneTelemetryRecorded`, `CorpusAsrRecorded`, `CorpusRewriteRecorded`) and writes readable journal metadata (`provider`, `event_name`, `level`, `source`, `message`, `line`) alongside the raw payload. It also accepts application-log drop predicates supplied by the App so runtime logging filters owned by `Deckle.Diagnostics.Logging` can affect persistence without creating a module dependency from Telemetry to Logging.

## Consent dialogs

The consent request dialogs ("Enable latency telemetry?", "Record the benchmark corpus?") live here. Standard XAML ContentDialog surface, opened from the relevant Settings page. The pattern reproduces what `Deckle.Settings` did for legacy persistence; migration is progressive, the new dialogs coexist with the old ones until wave 6.

## Boundary with `Deckle.Diagnostics.Logging`

The live application journal (LogWindow + SelectorBar filters + `ApplicationLogToDisk` gate) lives in `Deckle.Diagnostics.Logging`. Structured telemetry lives here. Both modules depend independently on the parent; they do not reference each other. The `app.jsonl` channel is nominally an *application journal* (so on the Logging side) but its **persistence** goes through a `JsonlEventListener` — hence its configuration on the Telemetry side. The clean boundary is: Logging decides *whether* to write (gate), Telemetry configures *how* to write (path + listener).
