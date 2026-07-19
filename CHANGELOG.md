# Changelog

All notable changes to Deckle are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the project adheres to [Semantic Versioning](https://semver.org/). Deckle has no
public API: the version is read at the **user/behaviour** level, and during the
`0.x` phase any release may change behaviour (see the `deckle-versioning`
doctrine). This file is generated from the Conventional-Commit history by
`scripts/lib/changelog.ps1` — do not edit it by hand.

## [0.13.4](https://github.com/louisfifre/deckle/compare/v0.13.3...v0.13.4) — 2026-07-19

### Fixed

- **app:** Resolve DirectML publish collision

## [0.13.3](https://github.com/louisfifre/deckle/compare/v0.13.1...v0.13.3) — 2026-07-19

### Changed

- **anytype:** Split schema administration gestures
- **app:** Separate startup and diagnostics concerns
- **catalog:** Decompose settings composition
- **core:** Group native interop by subsystem
- **input:** Separate keyboard host responsibilities
- **taskbar-cover:** Separate host lifecycle concerns
- **autocorrect:** Separate engine processing stages
- **autocorrect-lab:** Decompose lexicon generation
- **autocorrect-onnx:** Separate scorer concerns
- **lighting:** Separate Hue service operations
- **ambient:** Separate engine and interface concerns
- **llm:** Separate runtime and persistence concerns
- **settings:** Separate settings diagnostics events
- **setup:** Separate setup diagnostics events
- **transcription:** Separate pipeline responsibilities
- **whisper:** Separate backend operations
- **resources:** Prune unused localization entries

## [0.13.1](https://github.com/louisfifre/deckle/compare/v0.13.0...v0.13.1) — 2026-07-19

## [0.13.0](https://github.com/louisfifre/deckle/compare/v0.12.0...v0.13.0) — 2026-07-18

### Added

- **diagnostics:** Gate input activity and relocate wheel capture
- **hud:** Honor motion preferences across overlay feedback
- **app:** Make log transfers explicit and rows readable

### Changed

- **diagnostics:** Centralize operational detail admission
- **transcription:** Align observations with workflow stages
- **audio:** Aggregate capture anomaly episodes
- **llm:** Gate diagnostic model polling
- **ambient:** Model lifecycle recovery episodes
- **input:** Gate activity diagnostics and recovery
- **shell:** Gate windowing diagnostics
- **anytype:** Demote self-healing retry diagnostics
- **settings:** Silence successful persistence loads
- **speech:** Silence successful settings loads
- **app:** Clarify lifecycle milestones

## [0.12.0](https://github.com/louisfifre/deckle/compare/v0.9.2...v0.12.0) — 2026-07-16

### Added

- **logging:** Refine live log interaction
- **autocorrect:** Capture the typing stream on enrolled surfaces
- **autocorrect:** Ventilate surface profiles from the typed corpus
- **autocorrect:** Mine mistouch families from the typed corpus
- **autocorrect:** Route approved mistouch families to the commit stage
- **autocorrect:** Anticipate the sentence stage on a typing pause

### Changed

- **diagnostics:** Separate admission from projections

### Fixed

- **setup:** Make data relocation transactional
- **autocorrect:** Enforce corpus consent boundaries
- **whisper:** Centralize speech model resolution
- **shell:** Harden optional rewrite hotkeys
- **logging:** Refine structured filter controls
- **shell:** Accept semantic recording state from the host
- **hud:** Separate reveal failure details
- **input:** Separate keyboard host failure details
- **whisper:** Keep the native log callback rooted
- Align runtime behavior and observability

## [0.9.2](https://github.com/louisfifre/deckle/compare/v0.9.1...v0.9.2) — 2026-07-14

### Fixed

- **app:** Gate rewrite pipeline and hotkeys on module presence

## [0.9.1](https://github.com/louisfifre/deckle/compare/v0.9.0...v0.9.1) — 2026-07-14

### Added

- **autocorrect-probe:** Select execution provider and stream batch progress
- **autocorrect:** Collect the typed corpus on every editable surface
- **autocorrect-lab:** Guard replay intake and overlay maintainer ground truth
- **autocorrect:** Gate rare forms out of the sentence candidate set
- **autocorrect-onnx:** Abstain below a four-word context floor
- **transcription:** Default to ggml-base with installed-model fallback
- **setup:** Update pipeline — silent check, download page, deploy update mode
- **settings:** Update opt-out and version row on the General page
- **app:** Wire the in-app updater end to end
- **setup:** Data-root relocation page
- **settings:** Move action on the data-folder card
- **app:** Wire the data-root relocation end to end
- **autocorrect:** Fall back the rarity gate to the slot's best variant
- **autocorrect:** Wire the sentence judge into the live stage, margin 1.0

### Changed

- **install:** Move the release resolver into Deckle.Install

### Fixed

- **autocorrect-onnx:** Score in one forward and read fp16 logits for DirectML
- **autocorrect:** Keep fragment tails out of corpus sentence starts
- **setup:** Persist the chosen speech model into engine settings
- **autocorrect-onnx:** Absorb the transient DML flake at model construction
- **input:** Coalesce duplicate focus events

## [0.9.0](https://github.com/louisfifre/deckle/compare/v0.8.1...v0.9.0) — 2026-07-14

### Added

- **scripts:** Add record version workflow
- **audio:** Decode audio files to pipeline PCM via Media Foundation
- **transcription:** Add transcript writer and destination-folder setting
- **transcription:** Run picked audio files through the monolithic pipeline
- **hud:** Add the saved-transcript success message
- **traymenu:** Add the transcribe-a-file command item
- **app:** Wire the file-transcription flow from tray to HUD
- **anytype:** Add schema admin MCP surface
- **anytype:** Support project-to-epic links
- **catalog:** Add an application-log consent slot to the telemetry registry
- **catalog:** Give settings cards a searchable identity
- **settings:** Move the Settings nav chrome into the native TitleBar
- **settings:** Add the cross-page search index
- **audio:** Declare the Recording page's search entries
- **transcription:** Declare the Whisper page's search entries
- **llm:** Declare the rewrite page's search entries
- **autocorrect:** Declare the Autocorrect page's search entries
- **lighting:** Declare the Ambient page's search entries
- **trackpad:** Declare the Trackpad page's search entries
- **diagnostics:** Declare the Diagnostics page's search entries
- **settings:** Declare the General page's search entries
- **settings:** Search every settings page from the TitleBar
- **app:** Register the settings search contributions at boot
- **settings:** Rework the TitleBar search presentation and focus exits
- **settings:** Trace the TitleBar's settled layout geometry
- **lighting:** Discover Hue bridges over local DNS-SD
- **vision:** Expose available capture displays
- **ambient:** Integrate local discovery and display selection
- **modules:** Add the module presence catalogue
- **app:** Gate module composition and settings on presence
- **setup:** Open the wizard on a module selector
- **mlm:** Pin the CamemBERT reranker asset catalog
- **anytype:** Pin the backend bundle and install it from a zip
- **setup:** Drive the install step from the selected modules
- **setup:** Total the estimate over the whole install plan
- **setup:** Total the estimate live on the module selector
- **setup:** Run the wizard as installer with --install / --install-continue
- **installer:** Make the stub a silent web-installer with a native window
- **logging:** Add structured log window filters

### Changed

- **diagnostics:** Host the Diagnostics page in Deckle.Diagnostics.Logging
- **shell:** Share the TitleBar caption-inset correction
- **install:** Extract the Windows integration primitives
- **logging:** Streamline live log filtering
- **transcription:** Avoid redundant PCM copies
- **logging:** Batch live log dispatch
- **setup:** Move provisioning into owning backends
- **setup:** Keep payload measurement off the UI thread

### Fixed

- **anytype:** Align schema admin with live type contract
- **settings:** Honour display scale in caption reserve and window minimums
- **settings:** Pack the collapsed search icon against the title
- **app:** Honour display scale in the log window
- **playground:** Honour display scale in the playground window
- **settings:** Scale the initial window size
- **app:** Scale the log window's initial size
- **playground:** Scale the playground's initial size
- **trackpad:** Yield active drag to four-finger gestures
- **anytype:** Bound schema preview retention
- **lighting:** Surface Hue discovery failures
- **installer:** Keep cancellation aligned with setup state
- **anytype:** Make preview lookup null-safe

## [0.8.1](https://github.com/louisfifre/deckle/compare/v0.8.0...v0.8.1) — 2026-07-04

### Added

- **autocorrect:** Exempt reopened-retyped words from the commit stage
- **autocorrect:** Adapt the ONNX judge to a slot reranker
- **autocorrect:** Add the offline sentence-replay core
- **autocorrect:** Read and align the typed-text corpus for replay
- **autocorrect:** Calibrate the sentence margin from a corpus replay
- **settings:** Let modules own their settings nav identity
- **lighting:** Add Hue Entertainment streaming output
- **catalog:** Add a telemetry consent registry for module settings pages
- **autocorrect:** Select the sentence judge's execution provider
- **lighting:** Add ambient Bezier brightness response
- **anytype:** Expose document creation tool

### Changed

- **diagnostics:** Extract the shared settings-UX log source
- **audio:** Host the Recording settings page in its module
- **transcription:** Host the HUD overlay and auto-paste settings on the Dictation page
- **lighting:** Host the ambient capture-log toggle on the Ambient page
- **audio:** Host the microphone-telemetry opt-in on the Recording page
- **transcription:** Host the dictation observability opt-ins on the Dictation page
- **autocorrect:** Host the autocorrect observability opt-ins on the Autocorrect page

### Fixed

- **lighting:** Label ambient heartbeat push latency
- **vision:** Reduce capture recovery warning noise
- **lighting:** Prepare hue entertainment startup
- **lighting:** Keep Hue Entertainment stream alive

## [0.8.0](https://github.com/louisfifre/deckle/compare/v0.7.2...v0.8.0) — 2026-07-03

### Added

- **scripts:** Fold the version cut into the publish release flow
- **anytype:** Resolve the installed backend binary and its serve spec
- **app:** Start the Anytype backend at launch
- **anytype:** Resolve the API bearer from the vault, headless-first
- **mcp:** Serve external clients over one resident HTTP door
- **app:** Host the MCP HTTP door in the resident core
- **installer:** Let setup choose Start Menu shortcut
- **anytype:** Supervise the serve in-process, windowless
- **autocorrect:** Give the typed corpus an ordered per-slot history
- **autocorrect:** Count the personal WMR in the activity rollup
- **autocorrect:** Wire the restricted English lexicon tier
- **autocorrect:** Complete phase 2 correction pipeline
- **catalog:** Extend the settings composer for the refonte
- **rewrite:** Compose the enable and endpoint settings
- **settings:** Add a whole-page Reset all to General and Diagnostics
- **autocorrect:** Add ONNX sentence judge probe
- **autocorrect:** Add closed correction benchmark

### Changed

- **mcp:** Pivot the host core to per-message dispatch
- **rewrite:** Expose shared rewrite service
- **settings:** Register module nav pages through a runtime registry
- **trackpad:** Adopt the magnitude control for drag speed
- **settings:** Compose the diagnostics corpus consent fold

### Fixed

- **core:** Run the post-load migration on the parse-failure fallback
- **autocorrect:** Land the phase-1 sanitization gesture
- **installer:** Ignore missing install location
- **autocorrect:** Add deterministic locative la rule
- **autocorrect:** Score suffix evidence in ONNX judge

## [0.7.2](https://github.com/louisfifre/deckle/compare/v0.7.1...v0.7.2) — 2026-07-02

### Added

- **installer:** Recognise the installed copy and run as an update

### Fixed

- **installer:** Honour an existing DECKLE_DATA_ROOT across reinstalls
- **installer:** Gate install and uninstall on a running app
- **installer:** Clean the binaries folder before extracting
- **installer:** Clean before extraction only over a recognised install
- **installer:** Match running processes anywhere under the install folder

## [0.7.1](https://github.com/louisfifre/deckle/compare/v0.7.0...v0.7.1) — 2026-07-01

### Added

- **installer:** Recap-and-one-keystroke console install

## [0.7.0](https://github.com/louisfifre/deckle/compare/v0.6.0...v0.7.0) — 2026-07-01

### Added

- **catalog:** Extend the composer with Text, radio Choice, inline advisory, and a master-less Section
- **transcription:** Migrate Whisper settings onto the composer
- **catalog:** Add an editable folder-picker Path mode, per-card Path reset, and root-resolved reset tooltip
- **settings:** Compose the backup and telemetry-storage folder pickers
- **trackpad:** Compose the trackpad settings onto the composer
- **transcription:** Compose the GPU toggle and the editable models folder
- **security:** Add DPAPI secret vault

### Changed

- Route destructive gestures through the shared ConfirmationService
- **autocorrect:** Compose the master toggle and mask the per-app list when disabled
- **llm:** Mask the dependent sections and endpoint instead of greying them

### Fixed

- **ambient:** Ignore weak Hue echo mismatches
- **ambient:** Attribute Hue bridge changes

## [0.6.0](https://github.com/louisfifre/deckle/compare/v0.5.0...v0.6.0) — 2026-07-01

### Added

- **diagnostics:** Add autocorrect activity log toggle
- **benchmark:** Add local ONNX TTS audition harness
- **benchmark:** Chatterbox FR-reference accent probe
- **benchmark:** Reorder audition page, drop English ref, add run history
- **benchmark:** Chatterbox temperature sweep (exaggeration is inert on ML)
- **benchmark:** Working Orpheus FR on DirectML GPU
- **autocorrect:** Per-app enrollment decisions and passive suggestion
- **autocorrect:** Reactive enrollment toast
- **hud:** Reveal chrono digits through the shared conic material
- **catalog:** Add the Autocorrect glyph
- **autocorrect:** Settings page for per-app enrollment
- **settings:** Surface the Autocorrect page in navigation
- **core:** Add waveOut render bindings
- **audio:** Add speaker render output via waveOut
- **speech:** Add the dormant read-aloud module and placeholder backend
- **app:** Construct the dormant speech engine at boot
- **hud:** Conic clone target + swipe animator rewrite (WIP checkpoint)
- **shell:** Coalesce per-frame recompute during interactive window resize
- **hud:** Reveal chrono digits through a placed, auto-scaled conic clone
- **playground:** Segmentation curve tuning page
- **transcription:** Default the segmenter to the tuned values
- **hud:** Keep chrono digits inked in Tertiary, layer comet reveal on top
- **input:** Share the keyboard/mouse Raw Input host and read the wheel
- **input:** Capture mouse-wheel events to JSONL (Palier 0)
- **scripts:** Add launch-only app workflow
- **whisper:** Filter known subtitle-credit hallucinations post-decode
- **scripts:** Add Anytype MCP install off the build tree
- **hud:** Give the chrono digit reveal its own OKLCh palette and cone defaults
- **hud:** Pin the chrono digit reveal and disable the swipe wave
- **diagnostics:** Trace each coalesced resize frame for latency diagnosis
- **playground:** Uniform hover-revealed section reset across the tuning pages
- **observability:** Time page navigation and page-ready latency
- **observability:** Time lazy window first-open construction
- **observability:** Time the autocorrect lexicon build and mark readiness
- **observability:** Time the Silero VAD session construction
- **transcription:** Overlap the Whisper warm-up with capture
- **scripts:** Split the menu launch entry into Release and Debug
- **scripts:** Add a version bump-and-tag menu command
- **scripts:** Rework the dev menu as a 2D navigable grid
- **anytype:** Management layer — lifecycle verbs and reversible delete
- **diagnostics:** Add windowing activity log toggle
- **scripts:** Summarize workflow outcomes
- **scripts:** Configure Anytype MCP management
- **anytype-mcp:** Rename objects via optional name on update
- **autocorrect:** Correct real typos to the nearest French word
- **input:** Relay a drain-request thread message on the keyboard host
- **autocorrect:** Correct real-word ambiguities from sentence context
- **autocorrect:** Trace the contextual reranker decision path
- **autocorrect:** Restore dropped elision apostrophes
- **autocorrect:** Reach bigger typos with a two-tier corrector
- **autocorrect:** Feed the disambiguator two words of context (trigram)
- **catalog:** Resolve module-scoped .resw strings at runtime
- **settings:** Declarative settings composer with per-page migrations
- **settings:** Group descriptor with master toggle and gated children
- **autocorrect:** Instrument per-word decisions and the typed-sentence corpus
- **settings:** Group children hide instead of grey when master off
- **settings:** Migrate Recording voice-level group with inverted master
- **autocorrect:** Record the revert gesture in the decision dataset
- **settings:** Per-card and section reset from single-source defaults
- **settings:** Wire Recording reset to composer defaults
- **settings:** Add Number kind and migrate Whisper VAD and streaming groups
- **settings:** Confirm-on-enable gate for consent toggles
- **settings:** Reusable confirmation service for destructive commands
- **autocorrect:** Derive the verb-morphology artifact and its loader
- **autocorrect:** Add the grammar stage with subject–verb agreement
- **shell:** Unify both logon vehicles behind a StartupService facade
- **anytype:** Backend lifecycle mechanism — triggerless on-demand task + supervisor
- **app:** Launch without the speech setup gate
- **setup:** Recover a failed first-run download with a link and local import
- **transcription:** Surface a set-up call-to-action when speech is unprovisioned

### Changed

- **autocorrect:** Remove the CLI, relocate its code into modules
- **autocorrect:** Move offline train and eval into the Lab module
- **autocorrect:** Move the per-app decision write into the settings service
- **bench:** Freeze the ASR/TTS spikes under studies/ and fix their run paths
- **composition:** Extract shareable rotation helpers from StartRotation
- **onnx:** Add an allocation-free Run overload writing into caller buffers
- **vad:** Reuse the input tensors and output buffers across windows
- **hud:** Cache the window DPI instead of querying it per mouse move
- **app:** Load the autocorrect lexicon off the UI thread
- **audio:** Use Exp/Log over Pow/Log10 in the compressor
- **audio:** Derive the buffer dBFS from the sub-window sums
- **scripts:** Rework the menu into a looping two-level router
- **taskbar-cover:** Drive suppression by event, drop the 5 s poll
- **observability:** Collapse the sinks behind a single dispatch listener
- **transcription:** Single-source the default hangover curve
- **autocorrect:** Lift the module out of Input to Deckle.Autocorrect
- **benchmark:** Split shared and ASR workspaces
- **settings:** Relocate tunable controls from Playground
- **catalog:** Host the settings composer and confirmation service

### Fixed

- **transcription:** Widen streaming ramp bounds and refine step
- **autocorrect:** Recognise Chromium/Electron editable surfaces
- **benchmark:** Correct Orpheus SNAC decode (sliding centre window)
- **taskbar-cover:** Harden suppression and z-order against false reveals
- **playground:** Defer TunableRow setup to Loaded
- **playground:** Polish the segmentation curve editor and save bar
- **playground:** Round the TunableRow value box to its step
- **playground:** Round the curve boxes and tighten the save-bar spacer
- **playground:** Load segmenter settings on view-model construction
- **playground:** Stop the slider clamp from overwriting the loaded value
- **playground:** Stop the tuning rows crashing on a fractional step
- **autocorrect:** Place lexicons flat beside the exe so the engine arms
- **taskbar-cover:** Create the band topmost so it covers under foreground lock
- **scripts:** Ignore untracked files in the version-bump guard
- **transcription:** Raise capture-thread priority to stop audio drops under load
- **transcription:** Align hangover ramp defaults
- **observability:** Stop the LogWindow view selector from gating the disk journal
- **scripts:** Prevent lingering dotnet build servers
- **scripts:** Report cleanup outcomes
- **autocorrect:** Bound the correction-revert window in time
- **scripts:** Wait for Deckle shutdown before builds
- **scripts:** Restore terminal menu selection
- **autocorrect:** Rewrite the slot the reranker actually judged
- **scripts:** Harden menu pickers against redirected I/O
- **playground:** Fold wheel capture into home
- **input:** Capture wheel messages from hook
- **scripts:** Keep launcher prompts on clean lines
- **settings:** Make the autostart toggle honest across logon vehicles
- **vision:** Bound frame ownership recovery
- **vision:** Initialize capture interop outputs
- **ambient:** Log external stop decision context
- **setup:** Point the native runtime bundle at the louisfifre release
- **scripts:** Point the native runtime source at the louisfifre release
- **scripts:** Parenthesise the version-bump arithmetic

## [0.5.0](https://github.com/louisfifre/deckle/compare/v0.4.4...v0.5.0) — 2026-06-14

### Added

- **scripts:** Generate changelog and release notes from git history
- **scripts:** Publish the installer exe as the release headline asset
- **input:** Bare Raw Input probe for the precision touchpad
- **input:** SendInput mouse injection primitive
- **shell:** Elevated startup via scheduled task
- **trackpad:** Three-finger drag domain module
- **app:** Compose the trackpad module
- **trackpad:** Settings page and navigation entry
- **trackpad:** Freeze calibrated values, retire the tuning expander
- **anytype:** Core library over the live PM space — client, frozen schema, gestures
- **mcp:** Stdio JSON-RPC host exposing the 13 PM tools
- **transcription:** Paragraph break on silence-cut utterances
- **notifications:** Notification catalogue, dispatcher, and interactive toast channel
- **playground:** Manual test surface for the notification toast channel
- **app:** Compose the notification dispatcher at boot
- **anytype:** Create projects and tasks from their default templates
- **input:** Keyboard and pointer raw input host with focus signals
- **core:** Describe the focused element for the autocorrect surface gate
- **autocorrect:** Typed-word tracking over the raw keyboard stream
- **autocorrect:** Conservative lexical gate for diacritics restoration
- **autocorrect:** Minimal-diff injection and decayed personal dictionary
- **autocorrect:** Left-context pair model and restoration eval harness
- **autocorrect:** Engine wiring - surface gate, correction revert, learning signals
- **mcp:** Self-documenting host copy
- **autocorrect:** Cli host - watch, inject, run, eval, data pipeline, enroll, dict
- **autocorrect:** Derived lexical artifacts (Lexique, Norvig, Wikipedia FR pairs)
- **autocorrect:** Calibrate the context margin from the eval matrix
- **anytype:** Select options are applied, never created
- **taskbar-cover:** Edge-aware cover band domain module
- **shell:** Taskbar cover switch in the tray menu
- **app:** Compose the taskbar cover module
- **autocorrect:** Trace mode attributes every key event by origin
- **core:** Add a dedicated diagnostics directory under the data root
- **app:** Persist the setup narrative and critical errors locally
- **settings:** Link to the local diagnostics folder
- **setup:** Offer the diagnostics folder on a failed first run
- **anytype:** Replace_section — heading-located body edit, verified
- **anytype:** Add dialogue chat tools
- **autocorrect:** N-gram left-context disambiguation and precision-first eval
- **autocorrect:** CamemBERT MLM reranker probe (offline)
- **autocorrect:** Post-sentence reranker stage in the offline eval
- **autocorrect:** Proper-noun caps guard for the lexical gate
- **autocorrect:** Reranker frequency prior and eval tuning flags
- **autocorrect:** Offline dry-run command
- **scripts:** Add README stats automation
- **anytype:** Serialize cross-session writes with a file lock
- **autocorrect:** Observation-live harvest command
- **autocorrect:** Optional Morphalou inflected-form overlay
- **app:** Wire the autocorrect engine into the app

### Changed

- **tray-menu:** Split milestones from their Verbose detail mirrors
- **lighting:** Split milestones from their Verbose detail mirrors
- **audio:** Split milestones from their Verbose detail mirrors
- **shell:** Split milestones from their Verbose detail mirrors
- **input:** Split milestones from their Verbose detail mirrors
- **trackpad:** Split milestones from their Verbose detail mirrors
- **threading:** Split milestones from their Verbose detail mirrors
- **anytype:** Split milestones from their Verbose detail mirrors
- **hud:** Split the HideSync-timeout warning from its Verbose detail
- **settings:** Split milestones from their Verbose detail mirrors
- **ambient:** Split milestones from their Verbose detail mirrors
- **llm:** Split milestones from their Verbose detail mirrors
- **app:** Split milestones from their Verbose detail mirrors
- **whisp:** Split milestones from their Verbose detail mirrors
- **vision:** Split milestones from their Verbose detail mirrors
- **chrono:** Split the pilot milestone from its Verbose detail
- **vad:** Split milestones from their Verbose detail mirrors
- **resource:** Split the leak-suspect warning from its Verbose detail
- **playground:** Type the diagnostic event channels
- **setup:** Type the wizard event channels
- **settings:** Type the per-setting change sub-channel
- **autocorrect:** Extract OS-port interfaces for the test seam
- **diagnostics:** Self-create the JSONL sink parent directory
- **hud:** Share one cursor-movement signal across the HUD surfaces
- **app:** Split LogWindow into Model/Interaction/Chrome partials
- **tray-menu:** Split TrayContextMenuHost into Window/Flyout/Show/Measure partials
- **ambient:** Split AmbientEngine.Lifecycle event handlers into partials
- **whisper:** Extract WhisperNativeLogCompactor from WhisperBackend
- **transcription:** Split TranscriptionEngine.Pipeline into Finalize/Telemetry partials
- Collapse to one namespace per module
- **hud:** Split HudWindow and organize into Chrono/Windows/Model
- **settings:** Organize into Dialogs/Pages/Controls/Persistence
- **playground:** Organize Views into Ambient/Hud

### Fixed

- **transcription:** Pin SHA-256 verification on Whisper model downloads
- **lighting:** Validate the bridge IP at HueBridgeClient construction
- **installer:** Refuse cmd metacharacters in the delayed self-delete
- **transcription:** Fail closed when the native runtime DLL is absent
- **audio:** Unwind partial buffer prep when capture setup throws
- **setup:** Download silero_vad.onnx instead of ggml binary
- **trackpad:** Mirror gesture-button Loc keys into the app resw
- **trackpad:** Rework the Settings page after first hands-on
- **anytype:** Tolerate the bare-string list-add response on epic attach
- **notifications:** Self-settling prompts, live availability gate, complete narrative
- **notifications:** Mirror descriptor Loc keys into the App resw, harden Loc misses
- **mcp:** Link copy states the real pair matrix; instructions carry the property discipline
- **autocorrect:** Land tracker state before raising commit events
- **autocorrect:** Chorded editing keys decode as shortcuts
- **autocorrect:** Corrections no longer feed their own defeat
- **autocorrect:** Make the live run path diagnosable
- **taskbar-cover:** Serialize host restarts and unblock Start from shell hangs
- **taskbar-cover:** Observe timer arming failures
- **taskbar-cover:** Pin the pump imports to their Unicode entry points
- **app:** Detach and flush taskbar cover settings at shutdown
- **taskbar-cover:** Hold the provider to the Verbose/Info separation
- **input:** Guard the parser-failure detail behind its braces
- **anytype:** Invert the rapport↔task link, derive the project through tasks
- **app:** Always surface the streaming transcript in the log
- **app:** Add missing Setup_OpenDiagnosticsFolder to the root resource map
- **app:** Register always-on local sinks before settings migration

## [0.4.4](https://github.com/louisfifre/deckle/compare/v0.4.3...v0.4.4) — 2026-06-07

### Added

- **scripts:** Add a GitHub Release action to the dev menu

### Fixed

- **transcription:** Start chrono and duration on real capture start

## [0.4.3](https://github.com/louisfifre/deckle/compare/v0.4.2...v0.4.3) — 2026-06-07

### Added

- **transcription:** Dynamic hangover ramp + observable streaming pipeline
- **transcription:** Detect AB-AB period-2 repetition loops
- **inference:** Silero VAD v5 ONNX module
- **transcription:** Trim streaming utterances with the external Silero VAD
- **transcription:** Surface an untrimmed streaming take; test Reset
- **transcription:** Make the external Silero VAD the speech-detection toggle
- **transcription:** Expose the Silero VAD parameters and log span counts

### Changed

- **hud:** Decouple the chrono lifecycle from the paint states
- **playground:** Drive the chrono clock explicitly in HUD previews
- **hud:** Split HudChrono into per-concern partials
- **diagnostics:** Name ETW providers Deckle-<component>, not Deckle.<module>
- **audio:** Sharpen the capture-lag probe to attribute a cause
- **vad:** Autonomous module, kill the dead whisper-internal VAD

### Fixed

- **app:** Switch the HUD to Transcribing the instant Stop is pressed
- **hud:** Serialize the chrono stroke lifecycle against the RMS pump
- **inference:** Dispose SessionOptions when the Silero session fails to construct
- **transcription:** Checksum-verify the Silero VAD download and self-heal a corrupt model
- **inference:** Run the v6.2 Silero VAD model and verify the on-disk build
- **app:** Keep the streaming firehose log gate whole across the VAD split

## [0.4.2](https://github.com/louisfifre/deckle/compare/v0.4.1...v0.4.2) — 2026-06-05

### Fixed

- **scripts:** Serialize the publish build to avoid the WinAppSDK PRI race
- **hud:** Suppress stale delayed z-order probes
- **build:** Dedupe self-contained project references

## [0.4.1](https://github.com/louisfifre/deckle/compare/v0.4.0...v0.4.1) — 2026-06-04

### Added

- **installer:** Add NativeAOT download-stub installer
- **diagnostics:** Roll app journal by line count into a kept archive
- **telemetry:** Route the post-DSP distribution to its own channel

### Fixed

- **scripts:** Mark 0.x releases as pre-release

## [0.4.0](https://github.com/louisfifre/deckle/compare/v0.3.5...v0.4.0) — 2026-06-04

### Added

- **vision:** Roll up the capture heartbeat over a 5s window
- **observability:** Gate the heartbeat behind the capture toggle
- **ambient:** Name the stop reason in the pipeline-stopped milestone
- **audio:** Add transcription pre-processing DSP module
- **whisp:** Run DSP pre-processing before transcription
- **settings:** Expose the transcription pre-processing toggle on Recording
- **audio:** Add a mic level check for the pre-processing toggle
- **settings:** Add the mic level check to the Recording page
- **audio:** Make the pre-processing toggle take effect immediately
- **whisp:** Let the audio corpus follow the processed signal
- **scripts:** Streamline deckle maintenance stats
- **audio:** Emit live capture frames for stream consumers
- **transcription:** Add energy segmenter producing utterances
- **transcription:** Add optional priming context to the ASR contract
- **transcription:** Add streaming utterance pipeline
- **transcription:** Expose streaming strategy and segmenter in Settings
- **observability:** Add post-DSP microphone telemetry aggregate
- **transcription:** Run DSP preprocessing in the streaming pipeline
- **observability:** Tag corpus rows with raw vs processed audio

### Changed

- **transcription:** Extract pipeline seam and shared finalize
- **composition:** Split oversized host and HUD files
- **transcription:** Readable per-take streaming logs
- Split hue and vision oversized files

### Fixed

- **transcription:** Harden the streaming pipeline drain and ordering
- **transcription:** Hide segmenter params when streaming is off
- **app:** Close secondary windows for real
- **hud:** Reassert topmost on show
- **ui:** Sync secondary window navigation panes
- **ambient:** Treat stale matching Hue echo as echo, not external
- **scripts:** Align publish folder name with the release artifact
- **hud:** Stabilize post-build topmost show

## Earlier history

Versions 0.2.0 – 0.3.5 — the WhispUI genesis and the early Deckle cycles
(hotkey transcription, ambient lighting, observability) — predate this
generated changelog and are not itemised here. See the git history.
