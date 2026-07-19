---
description: Live LogWindow filters and runtime logging gates.
type: agent-instructions
---

# AGENTS.md — Deckle.Diagnostics.Logging

Owns the human-facing live journal, the reusable filter model/control, and the persistent policy read by producer-side verbosity gates. The operational application log is distinct from purpose-specific telemetry datasets: `app.jsonl` mirrors admitted log observations; dataset files follow their own explicit consent.

## Authority and settings surface

Settings > Diagnostics is the sole editing authority. It presents three distinct groups: **Logging details**, **Application log**, and **Telemetry**. Module pages and the LogWindow never duplicate their controls. The LogWindow shows a compact read-only indication when details are disabled and routes to Settings.

A verbosity boundary follows a chatty **activity**, not an entire provider by convenience and not merely the `Verbose` level. It may reject repetitive `Informational` outcomes when they belong to the governed activity. Workflow lifecycle milestones, durable incident/recovery milestones and their one-shot technical mirrors, every `Warning`/`Error`, and independently consented telemetry stay outside the gate. The producer evaluates the policy before every probe, computation, allocation, formatting step, payload, or counter collection done only for that log activity.

`app.jsonl` is an optional operational-log sink stored under the diagnostics directory, never the telemetry directory. Enabling it retains a disk-recording privacy confirmation. Its persisted `Severity` / `Module` / `Category` recording filter is configurable before recording starts, defaults and resets to all admitted observations, survives disabling the sink, and affects only future entries. It never includes text search.

Purpose-specific telemetry events never enter the LogWindow or `app.jsonl`. When the same fact matters to a human, its producer emits a distinct operational observation with no dependency on telemetry consent.

## Viewer projections

The LogWindow's `Severity` / `Module` / `Category` selection is a viewer-only lens: it decides what the live window shows, never what exists. Empty dimensions mean all; values within a dimension are OR-ed and active dimensions are AND-ed. The selection survives lazy-window recreation for the current process, then resets on restart. Search stays UI-local. The LogWindow view filter and `app.jsonl` recording filter are independent instances of the same model/control; changing one can never change the other.

**View condensation** is another viewer-only projection. After search and filters, it folds consecutive admitted `Verbose` observations sharing provider and event name. A collapsed run shows the latest message unchanged, occurrence count, and first-to-last interval; expanding restores every raw entry in order. Condensation is on by default, has one process-lifetime LogWindow toggle, and resets on restart. Copy and Save export the raw filtered entries regardless of collapsed state. `Informational`, `Warning`, and `Error` repetition is never condensed: it is a producer defect.

## Activity boundaries

### Ambient

- The activity control governs frame-analysis detail, calculated colours, individual light pushes, heartbeats, echo attribution, and their log-only probes. Lifecycle, display-mode adaptation, incidents, and recoveries remain.
- `AmbientEngine` owns the visible workflow lifecycle: one start and completion milestone for starting, then the corresponding stopping milestones. Successful Vision capture, sampler, Hue streaming, and transport steps are governed technical detail. HDR/resolution adaptation is one Ambient `Informational`; Vision and sampler steps are `Verbose` mirrors.
- Group and multi-light push failures share one incident. The third consecutive failure opens one `Warning`. A recoverable network/bridge outage retries at 1 Hz, then every 5 s after 30 s, without stopping; the first success restores normal cadence and emits one recovery with a `Verbose` duration/failure-count mirror. A confirmed invalid authentication or configuration emits one `Error`, stops Ambient, and alerts the user.
- Screen-capture recreation distinguishes expected Windows unavailability from unexpected technical failure. Both open one `Warning` on the second failed attempt. Lock, UAC, or session disconnect retries every 2 s through attempt five, then every 5 s indefinitely, with an immediate attempt on unlock. Unexpected recreation failure emits one `Error` on attempt five and stops. Success closes either incident once.
- Texture, readback, sampling, and consumer failures are technical causes of one frame-analysis incident. Individual failures are `Verbose`; one second of actual failed processing opens one `Warning`; normal static-screen silence does not count. Five further seconds without a successful analysed frame emit one Ambient `Error`, stop the workflow, and alert. The first success before escalation closes the incident once.
- Every fatal stop has one human `Error` owned by Ambient; Vision and Lighting carry the technical cause in `Verbose` mirrors. Fatal alerts use a persistent Windows notification with an action to open Ambient settings. A LogWindow entry or transient HUD is not the alert.
- Hue EventStream loss does not stop lighting. Five seconds without the protection opens one `Warning` and one notification because external changes may be overwritten; reconnection emits one recovery. A detected external light change is a normal stop with one `Informational` and a neutral notification.
- Functional fallbacks (Entertainment to REST, multi-light to group, requested monitor to primary) emit one `Warning` and no notification. Discovery, pairing, and listing are Settings-owned workflows: the surface owns visible milestones and outcomes while the Hue driver supplies `Verbose` detail. Normal settings loading is `Verbose` and never prefixes the message with `[ambient]`.

### Transcription

One **Transcription details** control spans Dictation and File transcription across Audio, VAD, Whisp, LLM, and delivery providers. Raw transcript text never belongs to the operational log; it is available only in the explicitly consented corpus dataset. Dependency incidents such as microphone, model, VAD, or Ollama availability survive individual takes until true recovery or process restart; take-specific failures close with their take.

### Autocorrect

One **Autocorrect activity** control spans focus/surface probes, decisions, reranking, learning, keyboard rollups, and successful-correction observations, including events emitted by supporting providers. Lifecycle and incidents remain. Typed text and raw decisions belong only to their explicitly consented datasets.

### Input

One **Input activity** control spans Raw Input frame rollups and per-gesture Trackpad detail. Input/Trackpad lifecycle and device presence remain admitted. Keyboard detail remains governed by Autocorrect activity; mouse-wheel JSONL capture remains independent telemetry owned by Deckle.Input.

### Windowing

Windowing is one technical diagnostics control with no human timeline and no sub-toggles. Placement, anchoring, z-order, resize, Win32 enumeration, timing, and payload construction are gated at their producers. Provider existence alone never earns a new setting.
