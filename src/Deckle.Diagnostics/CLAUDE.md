---
name: claude-deckle-diagnostics
description: "Doctrine for Deckle.Diagnostics, the observability foundation module. Read before authoring or modifying an EventSource provider, an EventListener, an instrumentation site, or a sink contract."
type: agent-instructions
module: Deckle.Diagnostics
---

# CLAUDE.md — Deckle.Diagnostics

Foundation module of the observability pillar. Carries the technical plumbing shared by every `Deckle.*EventSource` in the project and by the EventListeners that consume their emissions. Also hosts the **transverse sub-providers** `Deckle<X>Source` (Windowing, Threading, Theme, Resource, Cancellation, Network) — technical providers not tied to a business module, whose primitive at least two modules consume with the same parameter set. Business-module providers (`DeckleAudioSource`, `DeckleHudSource`, etc.) stay in their own modules; only transverse sub-providers live here.

The module depends only on the BCL (`System.Diagnostics.Tracing`). In particular, **no dependency on `Deckle.Core`** — diagnostics sits underneath every other technical brick, including application paths. Concrete destinations (JSONL file paths, XAML LogWindow access, HUD wiring) are provided by consumer modules at boot time through the sink interfaces exposed here.

## Provider convention

One concrete EventSource per emitting module. Class name `Deckle<Module>Source`, ETW name `[EventSource(Name = "Deckle.<Module>")]`. The `.` in the ETW name is canonical for hierarchical names. Static singleton `public static readonly Log = new()`, `sealed` type, inherits from `DeckleEventSource` (which itself inherits from `EventSource`). Cross-cutting keywords (`Keywords.Lifecycle`, `Keywords.Capture`, `Keywords.Pipeline`, `Keywords.Push`, `Keywords.Heartbeat`, `Keywords.Windowing`, `Keywords.Threading`, `Keywords.Theme`, `Keywords.Resource`, `Keywords.Network`) occupy bits 0 to 9; bits 10 and above belong to the provider and stay local to the module.

**Transverse sub-providers.** Technical providers not tied to a business module live here under the ETW name `Deckle.Diagnostics.<X>` and the C# class `Deckle<X>Source` (the word "Diagnostics" does not appear in the class name). The physical file is `src/Deckle.Diagnostics/Deckle<X>Source.cs`. The LogWindow tag follows the existing rule (last segment uppercase) — `Deckle.Diagnostics.Windowing` → `[WINDOWING]`. Promotion criterion has two cumulative clauses: (1) at least two business modules consume the primitive with exactly the same parameter set AND (2) the primitive is of non-business technical nature (platform wiring: windowing, threading, theme, resources, network, cancellation). The [reference--eventsource-convention--1.2.md](../../docs/reference/reference--eventsource-convention--1.2.md) sheet, section *Transverse sub-providers*, keeps the up-to-date list of existing and deferred sub-providers, and fixes the canonical parameter set per provider.

Canonical provider skeleton:

```csharp
[EventSource(Name = "Deckle.Chrono")]
public sealed class DeckleChronoSource : DeckleEventSource
{
    public static readonly DeckleChronoSource Log = new();

    [Event(1, Level = EventLevel.Informational, Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Chrono started")]
    public void ChronoStarted()
    {
        if (IsEnabled()) WriteEvent(1);
    }
}
```

## Typed methods discipline

One `[Event(...)]` method per distinct operation at the call site. No generic `Log(string, EventLevel)` method on the base, no event that takes a typed payload as argument. Trivial parameter-less events are typed parameter-less methods (`WarmingUp()`), not uses of a generic channel.

Event parameters are in `snake_case` because they become directly the JSON keys in the JSONL output. This is an explicit derogation from the Framework Design Guidelines, justified by the machine contract of persistence — a third-party consumer (PerfView, dotnet-trace, benchmark scripts) finds the same names in the ETW manifest and in the file. The `IDE1006` warning is suppressed in the csproj of the Diagnostics module and of the emitting modules.

Five native `EventLevel` values only.

- **`Critical`** — blocking failure, the app can no longer fulfill its main function. Crash, first-impossibility dependency, corrupted state.
- **`Error`** — targeted failure of an operation, other operations can continue. Failed transcription, hotkey unavailable, Hue bridge unreachable.
- **`Warning`** — abnormal situation without breakage. Empty buffer, slow dependency, degraded state that recovers.
- **`Informational`** — progress milestone as a short Capital sentence ("Loading model", "Recording start"). It is the equivalent of legacy Info **and** Success — success semantics is carried by the message, no longer by a dedicated level.
- **`Verbose`** — structured technical details, machine-greppable. Measures, identifiers, structured payloads. This is the level that carries `LatencyRecorded`, `MicrophoneTelemetryRecorded`, `CorpusAsrRecorded`, `CorpusRewriteRecorded` and their detailed parameters.

A transient that a retry loop absorbs on its own — no user-visible effect, attempt count staying low — is `Verbose`, not `Warning`. It is technical detail, not an abnormal state worth a human's eye. `Warning` is reserved for a degradation a human would want to notice even though it eventually recovers (a dependency kept slow, a buffer repeatedly empty). The tell that a `Warning` is miscalibrated is a recurring line for an event that always self-heals on the first retry — the screen-capture `DuplicationRecreateAttemptFailed` (an `E_ACCESSDENIED` while an HDR mode change settles, attempt always 1) is exactly that case and belongs at `Verbose`.

Legacy `Narrative` is dropped. If a UX text addressed to the user is needed, it goes through `UserFeedbackEmitted` (HUD) or through a `.resw` string (UI surface).

## Performance — gate before payload

Every `[Event(...)]` method is gated by `IsEnabled()` or better `IsEnabled(level, keywords)` before any payload construction. The brief locks this point: `IsEnabled(level, keywords)` on the provider side before any payload construction, for zero allocation when no listener is listening. When the event has parameters, the pattern is:

```csharp
public void LatencyRecorded(double audio_sec, long whisper_ms, /* … */)
{
    if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
    WriteEvent(<id>, audio_sec, whisper_ms, /* … */);
}
```

A plain `if (IsEnabled())` is enough for parameter-less events. The parameterized gate only makes sense when it avoids a construction (string allocation, array, computation).

## Three consumer contracts

**HUD via `UserFeedbackEmitted`.** A canonical event of the same name (`UserFeedbackEmitted`) exposed by every provider that may emit one. Contract signature: `(int severity, string title, string body, int role)`. The `HudFeedbackEventListener` filters exclusively on this event name and ignores everything else. Severity and role pass as `int` because EventSource does not accept user enums; the App re-encodes to its own `UserFeedbackSeverity` and `UserFeedbackRole` on the sink side. A site that wants user feedback calls the milestone event **and** `UserFeedbackEmitted` — no substitution.

**Live LogWindow.** The `LogWindowEventListener` listens to every event of the `Deckle.*` family, including structured telemetry, with no masking at emission. User filtering (by level and by module via the SelectorBar) happens on the sink side in the viewer.

**JSONL routing.** One `JsonlEventListener` instance per destination file. Each listener receives a predicate that selects the events to write to its file, plus a `JsonlSchema` (envelope shape) and an optional `JsonlRotationPolicy` (size-based roll). The concrete wiring (file paths, user gates, schema, rotation) lives in `Deckle.Diagnostics.Telemetry`. Two envelope shapes exist, and the difference is the window↔telemetry symmetry decision recorded in [ADR-0017](../../docs/adr/0017-symetrie-fenetre-telemetrie-et-rotation-du-journal.md): the general `app.jsonl` journal is **self-describing** (it persists the event identity the LogWindow renders), while the dataset channels (latency, microphone, corpus) stay **payload-only and frozen** because their schema is a cross-session machine contract consumed by benchmark tooling and pinned by [ADR-0011](../../docs/adr/0011-corpus-normalise-comme-dataset-ml.md). Structured channels carry their own labels (`"latency"`, `"microphone"`, `"corpus"`); the general channel keeps `"log"`.

## JSONL schema — machine contract

One JSON line per event, `\n` separator, UTF-8 encoding without BOM. Two envelopes, selected per listener by `JsonlSchema` — the choice is governed by [ADR-0017](../../docs/adr/0017-symetrie-fenetre-telemetrie-et-rotation-du-journal.md).

**`PayloadOnly`** — the dataset channels (latency, microphone, corpus). Frozen, key for key, because benchmark tooling parses it:

```json
{ "timestamp": "<ISO 8601 with local offset>", "kind": "<channel label>", "session": "YYYY-MM-DD-XXXX", "payload": { "<snake_case parameter>": <typed value>, … } }
```

**`SelfDescribing`** — the general `app.jsonl` journal. Same envelope plus the event identity the LogWindow renders, so the file is a faithful, greppable mirror of the live window rather than an anonymous payload; a parameter-less event keeps its `provider`/`event`/`level` instead of collapsing to an empty `payload`:

```json
{ "timestamp": "…", "kind": "log", "session": "…", "provider": "Deckle.Vision", "event": "ScreenCaptureStarted", "level": "Informational", "message": "<rendered Message template, null when none>", "payload": { … } }
```

`level` is the `EventLevel` name (`Critical`/`Error`/`Warning`/`Informational`/`Verbose`). `message` is the rendered `Message` template, `null` when the provider declared none — `provider`+`event` still identify the line. Adding the four keys is additive: a reader that keys on `payload` is unaffected.

Primitive payload values are serialized by their native type (`int` → JSON number, `string` → JSON string, `bool` → `true`/`false`). `DateTime`/`DateTimeOffset` go through their `"o"` representation (round-trip ISO 8601), `Guid` through `"D"`. Any other type is stringified — in practice this never happens, EventSource forbidding complex types in `[Event]` parameters.

`kind` takes `"log"` (general, `app.jsonl`), `"latency"`, `"microphone"`, `"corpus"`. The `"log"` label is kept as-is for compatibility with existing benchmark tools.

**Rotation.** `app.jsonl` carries a `JsonlRotationPolicy` (size-based roll: `app.jsonl` → `app.1.jsonl` → … → `app.{N}.jsonl`, oldest dropped) so a long session can't grow it without bound — the friction that first surfaced this work was a 23 MB / 118k-line file with no cap. The current bound is 5 MB × 5 generations (≈30 MB total). The dataset channels carry **no** policy and stay append-only: rolling them would truncate an ML dataset. The roll is best-effort under the write lock, with the active-file size tracked in process to avoid a stat syscall per line.

## Provider inventory and listening pipeline

Thirteen concrete EventSource providers active at boot, plus the non-instantiable `DeckleEventSource` base class. Each emitting module declares its own `Deckle<Module>Source.cs` inheriting from `DeckleEventSource`. List in alphabetical order of the ETW Name, with the host module and the corresponding LogWindow tag in parentheses:

- `Deckle.Ambient` (`Deckle.Lighting.Ambient`, tag `AMBIENT`) — ambient lighting orchestrator, Hue pairing consumer, aggregated heartbeat.
- `Deckle.App` (`Deckle.App`, tag `APP`) — application host, crashes, boot, status transitions, restart, hotkey orchestration.
- `Deckle.Audio` (`Deckle.Audio`, tag `AUDIO`) — microphone capture, waveIn anomalies, `MicrophoneTelemetryRecorded` telemetry rollup.
- `Deckle.Chrono` (`Deckle.Chrono`, tag `CHRONO`) — historical wave 1 pilot, fleshed out once the module has milestones to emit.
- `Deckle.Hud` (`Deckle.Hud`, tag `HUD`) — currently a single `HudWarning(string)`, under-instrumented.
- `Deckle.Lighting` (`Deckle.Lighting`, tag `LIGHTING`) — Hue REST CLIP v1/v2 driver, discovery, pairing, color push at 10-15 Hz.
- `Deckle.Llm` (`Deckle.Llm`, tag `LLM`) — Ollama rewriting, `/api/ps` polling, Settings → LLM surface.
- `Deckle.Playground` (`Deckle.Playground`, tag `PLAYGROUND`) — dev-only surface, generic per-channel events.
- `Deckle.Settings` (`Deckle.Settings`, tag `SETTINGS`) — legacy → per-module migration, backup/restore, folder pickers, NavView navigation, ViewModel setters.
- `Deckle.Setup` (`Deckle.Setup`, tag `SETUP`) — first-run wizard, three generic events (`SetupInfo`/`Warning`/`Error`).
- `Deckle.Shell` (`Deckle.Shell`, tag `SHELL`) — message-only host, hotkeys, HKCU\Run autostart, dispatcher.
- `Deckle.Vision` (`Deckle.Vision`, tag `VISION`) — DXGI screen capture, FrameSampler, acquisition loop anomalies.
- `Deckle.Whisp` (`Deckle.Transcription`, tag `WHISP`) — transcription engine, native model state, paste, clipboard. The `DeckleWhispSource` symbol was kept as-is after the modular refactor that renamed `Deckle.Whisp` to `Deckle.Transcription` — the ETW Name stays `Deckle.Whisp` to preserve the LogWindow tag and the benchmark tooling compat.

`Deckle.Core` and `Deckle.Composition` stay silent by doctrine — no call site justifies a provider.

Six listeners instantiated at boot in `AppDiagnosticsBootstrap`, persist for the lifetime of the process. Four `JsonlEventListener`, one per destination file (`app.jsonl`, `latency.jsonl`, `microphone.jsonl`, `corpus.jsonl`). Each receives a predicate that selects the events to write to its file — selection by canonical event name for structured heartbeats (`LatencyRecorded`, `MicrophoneTelemetryRecorded`, `CorpusRecorded`), selection by keyword for the general channel. One `LogWindowEventListener` with a ring buffer of capacity 5000 and multi-sink `AttachSink` / `DetachSink` — the LogWindow attaches on its first lazy open and receives the boot history in replay. One `HudFeedbackEventListener` that filters exclusively on the `UserFeedbackEmitted` event name and routes to the concrete sink of the host.

User configuration sources:

- `Deckle.Diagnostics.Logging.LoggingSettingsService` → `<UserDataRoot>/modules/logging/settings.json` → `LogAmbientCaptureActivity` toggle, plus the volatile `AmbientCaptureGate` that `AmbientEngine` sets to `true` around its loop to drop Verbose during capture.
- `Deckle.Diagnostics.Telemetry.TelemetrySettingsService` → `<UserDataRoot>/modules/telemetry/settings.json` → gates `LatencyEnabled`, `MicrophoneTelemetry`, `CorpusEnabled`, `RecordAudioCorpus`, `ApplicationLogToDisk`, `StorageDirectory`. The delegate injected into `TelemetryListenerBootstrap.ConfigureGates` is consulted on every emission by the `JsonlEventListener` instances.

## Session id

A single `SessionId` in the format `YYYY-MM-DD-XXXX` is generated the first time a provider emits, and shared by all `Deckle.*` providers for the lifetime of the process. Stored as a static property on `DeckleEventSource`. Reproduces exactly the behavior of legacy `TelemetryService.SessionId` so that benchmarks can keep grouping by session during and after the migration.

## Coexistence during migration

Legacy `Deckle.Logging` coexists until wave 6. Operational consequence: during the migration, a migrated module calls **only** its EventSource, a non-migrated module keeps calling `TelemetryService`. No double emission, no cross-pipeline bridge path. The EventListeners declared here are registered at boot in `App.OnLaunched` **alongside** the legacy sinks, and write to parallel files for the duration of schema validation. The final swap happens in wave 6 when legacy disappears.

## Measurement vocabulary

Every measure exposed as an event parameter has a canonical format. The **parameter name** serves as the JSON key, the **unit**, **precision**, and **suffix** follow the tables below so that a human grepping a measure in the LogWindow or in a JSONL finds the same thing everywhere. Any appearance of a measure in a new event must follow this contract — if a unit is missing, add it here before using it.

**Time** — short durations `<name>_ms` integer (`load_ms=420`, source `Stopwatch`), long durations `<name>_sec` 1 decimal (`audio_sec=12.3`, computed `samples / 16000`), sub-millisecond durations `<name>_us` integer (microseconds, `Stopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency`), whisper segment timing `t0` / `t1` / `dur` 1 decimal (`t0=1.2 t1=3.4 dur=2.2`).

**Audio** — linear RMS `rms` 4 decimals on `[0,1]` (`rms=0.0123`, `sqrt(Σv²/n)` with `v = pcm16/32768`), level `dbfs` 1 decimal (`dbfs=-38.2`, `20 * log10(rms)`), frequency in `kHz` integer (always `16` in Deckle), channels always `mono`, samples `samples` integer, buffer size `bytes` integer.

**Text** — character length `text_chars`, word length `text_words`, token length `prompt_tok` or `tok` (`text_chars=142`, `prompt_tok=512`).

**Compute** — `n_seg` integer (segments), `tok_s` 1 decimal (tokens/s), percentage `<name>_pct` 1 decimal (`reduction_pct=62.4`), confidence `p̄` / `min` 2 decimals on `[0,1]`, probability `<name>_pct` integer (`nsp=12`).

**Image / video capture** — frames per second `fps` 1 decimal (measured on a 1 s sliding window), frame count `frames` integer (since the Start of the session), resolution `size=WxH` integer (`Direct3D11CaptureFrame.ContentSize`), pixel format `format=<DirectXPixelFormat enum>`, pool buffers `bufs` integer (typically 2), monitor handle `hmon=0x{hex}` (`MonitorFromPoint` return).

**Native call returns** — native code `result=<int>` or `mmsys=<int>`, HRESULT `hr=0x{hex}`, outcome enum `outcome=<value>`, native pointer `ctx=0x{hex}`.

**Network and LED drivers** — IPv4 `bridge_ip=192.168.1.5`, Hue serial number `bridge_id=001788FFFE3A2C18` (hex16), application key `username=eDOvxk-...` (truncated to 8 chars + `...`), pre-shared key `clientkey=[redacted]` (DTLS PSK never logged in clear), group ID `group_id=3` (CLIP v1 integer, v2 UUID), HTTP status `hr=200` / `hr=401`, CIE color `xy=0.4521,0.3895` 4 decimals, luminance `bri=200` integer 0–254, RGB `rgb=180,60,240` 3 bytes.

## Verbose ↔ Info separation doctrine

**Opaque identifiers and the `k=v` format are Verbose-only.** A `Message` `[Event]` of level `Informational`, `Warning`, `Error` or `Critical` is a short Capital sentence, readable by a human with no knowledge of the implementation. If the `Message` contains an ID (Hue light id, group id, file path, hash, line index, any opaque token) or `|` separators, then by definition it is a Verbose event, not a semantic event. An Info that contains an ID is a level mistake, not a stylistic variant.

When an action deserves both a semantic signal for the user AND a technical detail for diagnostics, we emit **two events**: a Capital Info without IDs, and its Verbose mirror with the IDs as typed snake_case parameters. No overlap.

| ❌ Bad (mixed) | ✅ Good (separated) |
|---|---|
| `Info AMBIENT zone assign \| id=42 \| zone=Top` | `Info AMBIENT Zone Top assigned to Falcon` |
| | `Verbose AMBIENT zone assign \| id=42 \| zone=Top` |
| `Info AMBIENT settings update \| key=UseMultiLight \| value=true` | `Info AMBIENT Pipeline mode set to per-zone` |
| | `Verbose AMBIENT settings update \| key=UseMultiLight \| value=true` |

The Verbose mirror **always follows** the Capital Info when there is a technical detail to record. It is not optional — it is the contract that makes logs greppable.

## Format by level — two distinct registers

**Informational and milestone-level success** — short Capital sentence, read as a milestone in the LogWindow Activity view. No `k=v`, no technical units. A short parenthetical remains acceptable when it carries the gist of the milestone (backend, perceived duration, outcome). Examples: `MODEL Loading model`, `MODEL Model loaded (Vulkan)`, `CAPTURE Recording start`, `CAPTURE Recording complete (12.3 s)`, `TRANSCRIBE Transcribing`, `TRANSCRIBE Transcription complete (5 seg)`, `LLM Rewriting (Short)`, `LLM Rewrite complete`, `CLIPBOARD Copied to clipboard`, `PASTE Pasted`, `DONE Done (Pasted)`.

**Warning and Error** — rich Capital sentence. When the alert needs details (endpoint, error code, duration), express them in prose (`Ollama busy — model X resident (2.1 GB). Waited 60s so far…`). No `k=v` in visible Warning / Error prose, even if a mirror Verbose event may expose the machine-greppable fields in parallel.

**Verbose** — machine-greppable technical detail. The `Message` template follows the format `<action or state> | <measure1>=<val1> | <measure2>=<val2> ...`. Short prefix (verb or state) at the head, measures separated by ` | `, first word lowercase, single line. Never repeat the module in the message — the source tag (`CAPTURE`, `LLM`, etc.) already carries it.

Mirror examples:

```
Info     MODEL       Loading model
Verbose  MODEL       load start | file=ggml-large-v3.bin | file_mb=2951.7 | use_gpu=1
Info     MODEL       Model loaded (Vulkan)
Verbose  MODEL       load complete | load_ms=420 | backend=Vulkan
```

**Raw text** (transcribed segment, clipboard content, user prompt) keeps its native casing, does not undergo the Capital rule. It is content, not a message.

## Canonical observable classes

When instrumenting a piece of code, which parameters to target by default. The previous sections answer *where* and *how* to write the event (provider, level, keyword, format, measurement vocabulary). This section covers *what* to emit depending on the class of situation encountered. Nine classes are enough to cover existing and future Deckle code; a site may belong to two classes simultaneously.

### Class 1 — Lifecycle and boot

Process start, path init, resource warmup, module loading, app state transitions (`idle → recording → transcribing → done`), shutdown initiated, post-build restart, crash safety nets. One-off operations per cycle, milestones expected as `Informational` with the `Lifecycle` keyword, mirrors as `Verbose` when technical parameters justify a separate detail.

**Canonical set** — name of the step, duration `<name>_ms`, outcome (`succeeded` / `skipped` / `failed`), active backend or variant when relevant (`backend=Vulkan`, `model=ggml-large-v3.bin`), component version if network or disk load, transition reason for state changes (`reason=hotkey`, `reason=tray`, `reason=auto-shutdown`).

**Current state** — very well instrumented in `Deckle.App` (boot, status transitions, shutdown/restart), `Deckle.Transcription` (boot warmup, model load via `DeckleWhispSource`), `Deckle.Audio` (capture lifecycle), `Deckle.Vision` (`ScreenCaptureStarted`/`Stopped`). The `PathsInitialized` + `PathsDetail` pattern (Info milestone + Verbose mirror) is the clean archetype.

### Class 2 — Batch pipeline

Transcription of an audio blob, LLM rewriting, device calibration, ambient push on a full frame. Discrete operation start → end → result. Dominant frames RED and Four Golden Signals.

**Canonical set** — operation identifier (`transcription_id` if relevant), total duration and per key phase (`hotkey_to_capture_ms`, `record_drain_ms`, `whisper_init_ms`, `whisper_ms`, `llm_ms`, …), input metrics (`audio_sec`, `text_chars`, `prompt_tok`), output metrics (`n_segments`, `text_words`, `tok_s`), outcome enum (`outcome=ok|repetition_loop|llm_failed|user_cancelled`), active profile or strategy (`strategy=`, `profile=`), binary side-effect flag (`pasted=true`).

**Current state** — `LatencyRecorded` with 24 fields (`DeckleWhispSource`) is the canonical successful example, *canonical log line* in the industry sense that colocates all key measures in one line per invocation. `CorpusAsrRecorded` (14 fields) and `CorpusRewriteRecorded` (12 fields) follow the same pattern for dataset persistence (cf. [ADR-0011](../../docs/adr/0011-corpus-normalise-comme-dataset-ml.md)). The pattern is mature on the transcription side, not systematized elsewhere.

### Class 3 — High-frequency real-time loop

Audio capture polling at 50 ms, DXGI screen capture at ~15 Hz, light push at 10-15 Hz, raw cursor input at ~125 Hz for HUD proximity fade. Operations numerous, brief; the stake is throughput stability. Dominant frames USE and Four Golden Signals on the outgoing flow side.

**Canonical set** — on a sliding window (typically 1 s): observed `fps` or `ticks/s`, `drops` (frames acquired but not processed), intra-tick latency `p50_ms` / `p95_ms`, queue saturation (`queue_depth` or `pending_frames`), intra-window errors (`acquire_fail=N`). Pattern called *rollup* — a periodic line that summarizes N ticks, rather than one line per tick which would drown the observation.

**Current state** — the `Heartbeat` of `DeckleAmbientSource` is the current incarnation of the pattern (7 fields, periodic). `DeckleVisionSource` has no equivalent — the capture loop emits on incident (anomalies, recovery) but no regular trace of throughput. `DeckleAudioSource` emits the RMS tick on a direct UI event (HUD feed), explicitly *not* logged per the rule "high-frequency heartbeats < 1 s are not logged". The distributive rollup `MicrophoneTelemetryRecorded` with 14 fields at session end compensates.

### Class 4 — Hardware driver and external integration

Microphone driver (WASAPI), Hue REST HTTP client, Ollama HTTP client, SSE EventStream, native whisper.cpp P/Invoke. Boundary between internal code and an external system over which we have little control. Dominant frames RED (round-trip duration, error rate, call rate) plus USE on internal resources consumed.

**Canonical set** — connection lifecycle events (`discovery`, `pairing`, `session_opened`, `session_closed`, `signal_lost`, `reconnected`); native return codes with a stable canonical notation (`hr=0x{hex}` HRESULT, `result=<int>` mmsys, `status=<int>` HTTP, `mmsys=<int>` waveIn); truncated or masked identifiers for secrets (`username=eDOvxk-...`, `clientkey=[redacted]`); round-trip latency (`rtt_ms`); consumed resources (`http_clients`, `socket_pool`).

**Current state** — `DeckleLightingSource` (40 events) covers the whole Hue cycle well: discovery, pairing, control, EventStream, identify, color push. The secret-masking discipline (clientkey never in clear, username truncated) holds. `DeckleLlmSource` instruments Ollama states (`OllamaBusy`, `/api/ps` polling). `DeckleAudioSource` covers waveIn anomalies via `mmsys` codes. A cross-cutting normalization is missing — there is no uniform reusable `HttpRequestCompleted(verb, endpoint, status, rtt_ms, retry_count)` pattern.

### Class 5 — UI surface and navigation

Settings page opened, dialog confirmed, form validated, NavView navigation, ViewModel setter changing a value, page loaded ready, page failed to init. Dominant frames Four Golden Signals adapted (perceived latency, actions-per-session rate, visible errors) plus RED on user-triggered operations.

**Canonical set** — UI state transitions as concise milestones (`Page loaded`, `Dialog opened`, `Form validated`); technical details in Verbose mirror (`page=Llm | duration_ms=120 | items=5`); user-addressed UserFeedback via the canonical separate channel `UserFeedbackEmitted` with the strict contract `(severity, title, body, role)`.

**Current state** — `DeckleSettingsSource` is the rich example, 46 events covering NavView navigation, ViewModel setters, backup/restore, folder picker, setup wizard. The parameterized generic event `SettingChanged(string, string, string)` is the accepted exception to the strict-typed discipline — a generic MVVM setter cannot distinguish 30 different setters at the call site.

### Class 6 — Windowing

Positioning and sizing of every WinUI 3 or Win32 window — `HudWindow` (320×64 bottom-center), `HudOverlayWindow`, `HudMessage` hybrid bleed (400×160 then retract 272×78), `SettingsWindow`, `LogWindow`, `SetupWindow`, tray menu popup, folder picker popup. All these sites compute a position by hand in DIP, multiply by `GetDpiForWindow(hwnd) / 96.0`, choose a `DisplayArea` or a `MonitorFromPoint`, manage multi-screen.

**Canonical set**:

- `hmon=0x{hex}` — monitor handle returned by `MonitorFromPoint` or `GetMonitorInfo`.
- `dpi=192` — integer, result of `GetDpiForWindow`.
- `scale=2.0` — one decimal, derived `dpi/96`.
- `work_area=2560,40,2520,1392` — rect in absolute screen pixels (x, y, w, h).
- `cursor=1240,860` — absolute screen pixels, return of `GetCursorPos`.
- `anchor=BottomCenter` — anchoring chosen on the settings side.
- `pos=1100,820 size=320,64` — rect computed in absolute screen pixels (convention fixed by this doctrine to allow the reverse via `dpi`).
- For stacked overlays: `slot=0` or `slot=1`.
- For popups: `parent_rect=x,y,w,h` of the anchored control.

Coordinate convention — absolute screen pixels everywhere. Internal computations may start from DIP, but events emitted for observation carry values in pixels, consistent with what `GetCursorPos`, `GetWindowRect`, `GetMonitorInfo` return, and allow reversing to DIP via `dpi`.

**Current state** — not observed. The HUD has a single `HudWarning(string)` parameterized by free message. `SettingsWindow`, `LogWindow`, `SetupWindow` emit nothing about their positioning. `TrayIconManager` logs neither icon position nor popup position. Class to wire progressively on existing positioning sites — workstream tracked in the roadmap memory.

### Class 7 — User activity

Hotkey pressed, tray entry clicked, settings toggle changed, settings page opened manually. Dominant frame RED on triggered operations.

**Canonical set** — trigger (`trigger=hotkey:WinTilde | tray:Quit | settings:OllamaModel`), result (`outcome=triggered|ignored:busy|ignored:not-configured`), value before and after for a toggle (`before=true after=false`).

**Current state** — `DeckleShellSource` covers hotkeys (`HotkeyRegistered`, `HotkeyToggleIgnored`). `DeckleAppSource` covers `HotkeyStart`, `HotkeyStop`, `HotkeyNoProfile`. `DeckleSettingsSource` covers setters via generic `SettingChanged`. Coherent but scattered across three providers — Shell for the primitive, App for the orchestration, Settings for the value change. Doctrinally correct ("observation attaches to the module that contains the operation"), a bit heavy to mentally piece together when reading the LogWindow.

### Class 8 — Per-module settings persistence

Each module that has settings (`Audio`, `Transcription`, `Llm`, `Lighting.Ambient`, …) loads and persists via `JsonSettingsStore<T>` under `<UserDataRoot>/modules/<name>/settings.json`. Four transient events share the pattern: `SettingsLoaded`, `SettingsLoadComplete`, `SettingsLoadWarning`, `SettingsLoadError`, all parameterized by a free string message.

**Target canonical set** — `module=<name>`, `path=<abs>`, `outcome=loaded|defaulted|migrated|failed`, `size_bytes=<n>`, `version=<schema>`, duration `load_ms=<n>`, reason on failure (`reason=missing|corrupt|migration_failed`).

**Current state** — documented exception. The `Action<string>` delegate of `JsonSettingsStore` cannot distinguish at the call site between "Settings loaded", "Settings initialized (defaults)" and "Settings reloaded from disk". The strict-typed discipline is temporarily traded for typing by level and keyword. A clean refactor lands when `SettingsHost` / `JsonSettingsStore` themselves switch to a direct EventSource contract.

### Class 9 — Crash and safety nets

`Application.UnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`. Three nets set in the `App` constructor. Captures exception type, message, stack trace, context (handler invoked, thread).

**Canonical set** — `source=app|appdomain|task-scheduler`, `ex_type=System.Foo.Bar`, `ex_message=<short>`, `stack=<multi-line or pointed to via a separate event>`, `thread_id=<n>`, `terminating=true|false` (for AppDomain).

**Current state** — `DeckleAppSource` carries the 4 events `CrashUnhandled`, `CrashAppDomain`, `CrashTaskScheduler`, `CrashStackTrace`. Pattern well held — the stack trace is on a separate event to avoid blowing up the primary signature.

## Durable application rules

- **One step = one start Info, one end Info.** Between them, Verbose if necessary. No repeated Infos in the middle of a step.
- **High-frequency heartbeats (< 1 s) are not logged.** They feed UI events (`AudioLevel` → HUD, RMS per tick) but not the LogWindow. The LogWindow carries steps, not frames.
- **Measures follow the vocabulary above.** If a unit is missing, add it to this doctrine before using it. No ad-hoc measure.
- **Logs in English from the start**, technical Infos as semantic milestones. No French in events.
- **A `UserFeedbackEmitted` is always doubled by an event** of the same level. The event stays for diagnostics, the HUD is for the user.
- **Never a multi-line event.** One emission = one line in the viewer.
- **The source carries the context.** Do not write `CAPTURE: started recording` in the `Message` — the LogWindow Source column already shows `CAPTURE`.

## Tests

EventSource is designed to be testable via a custom EventListener wired in the test. Canonical pattern: instantiate the provider via `[EventSource(Name = "Deckle.Foo")]` (the test may also manually register a new provider via `EventSource.SendCommand` on an existing instance), attach a `TestEventListener` that collects `EventEntry` items, run the code, assert on the collected sequence. It is this native testability property that partly motivates the EventSource choice — see [ADR-0005](../../docs/adr/0005-adoption-eventsource-pour-l-observabilite.md).
