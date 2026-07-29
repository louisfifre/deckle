# Changelog

All notable changes to Deckle are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the project adheres to [Semantic Versioning](https://semver.org/). Deckle has no
public API: the version is read at the **user/behaviour** level, and during the
`0.x` phase any release may change behaviour (see the `deckle-versioning`
doctrine). This file is generated from the Conventional-Commit history by
`scripts/lib/changelog.ps1` — do not edit it by hand.

## [Unreleased]

### Added

- **rewrite:** Surface gated paragraph correction offers
- **anytype:** Add optional search context
- **anytype:** Expose cross-space schema and collection APIs
- **home:** Add guarded Anytype inventory domain
- **anytype-mcp:** Expose guarded Home surface
- **anytype:** Support type icons in schema manifests
- **scripts:** Add context footprint inspection
- **scripts:** Track context document drift
- **anytype:** Add bounded cross-space utilities
- **autocorrect:** Prepare verified sentence proposals
- **travel:** Scaffold the trip-preparation MCP module
- **app:** Mount the Travel MCP client
- **anytype:** Upload files through the REST transport
- **travel:** Attach local files to the objects that carry them
- **travel:** Give every type its Anytype icon
- **anytype:** Align epic planning gestures
- **autocorrect:** Fabricate the pilot IT domain pack from kaikki
- **autocorrect:** Apply the gray-zone judge verdicts to the IT pack
- **autocorrect:** Promote common pack forms, decided at the IT bench
- **autocorrect:** Merge active domain packs into the effective lexicon
- **autocorrect:** Expose vocabulary pack activation in settings
- **autocorrect:** Surface each pack's dilution indicator
- **autocorrect:** Add the word exclusion register
- **scripts:** Add navigable maintenance results
- **settings:** Teach the rail child pages and page-to-page drill-in
- **autocorrect:** Move lexicon settings to domain-first sub-pages
- **autocorrect:** Parameterize the pack fabrication chain by language
- **autocorrect:** Fabricate and judge the en-IT pack
- **autocorrect:** Ship the en-IT pack
- **input:** Add native precision wheel scrolling
- **input:** Preserve wheel device telemetry
- **precision-scroll:** Derive motion from wheel cadence
- **scripts:** Add targeted maintenance scans
- **scripts:** Keep action logs in menu viewport

### Changed

- **mcp:** Isolate custom surfaces
- **autocorrect:** Collapse duplicate lexical pass
- **autocorrect:** Bound sentence candidate search

### Fixed

- **setup:** Make native runtime installation releasable
- **autocorrect:** Make sentence judge loading reliable
- **app:** Show log entry severity
- **input:** Preserve focused-object events
- **autocorrect:** Expose abandoned sentence work
- **input:** Capture precision touchpad clicks
- **app:** Prevent duplicate resident processes
- **autocorrect:** Apply sentence corrections safely
- **scripts:** Refine context inspection output
- **scripts:** Show parent paths in context inspection
- **autocorrect:** Harden corrections under live typing
- **autocorrect:** Reconcile external correction bursts
- **autocorrect:** Preserve typed sentence separators
- **autocorrect:** Improve keyboard correction quality
- **travel:** Embed the terms file culture-neutral
- **anytype:** Compare the existing type icon before flagging a conflict
- **anytype:** Keep project status and history observable
- **scripts:** Install hooks from linked worktrees
- **scripts:** Restore launcher visual hierarchy
- **scripts:** Align launcher spacing and quit navigation
- **input:** Retain touchpad frame device identity
- **input:** Preserve native wheel semantics
- **precision-scroll:** Preserve accepted wheel motion
- **precision-scroll:** Apply settings immediately
- **input:** Close capture device registration race
- **input:** Harden wheel observation correlation
- **scripts:** Page maintenance results with mouse wheel
- **scripts:** Keep command grid visible during actions

## [0.14.1](https://github.com/louisfifre/deckle/compare/v0.14.0...v0.14.1) — 2026-07-20

### Fixed

- **release:** Materialize tags before finalizing drafts

## [0.14.0](https://github.com/louisfifre/deckle/compare/v0.13.7...v0.14.0) — 2026-07-20

### Added

- **llm:** Gate the paragraph rewrite behind a mechanical diff validator
- **llm:** Pose the paragraph retaille prompt in its single home
- **benchmark:** Measure the retaille service and gate on a prompt sample

### Changed

- **llm:** Put the inference engine behind the rewrite service seam

### Fixed

- **install:** Isolate app releases from native bundles
- **release:** Stage verified releases before publication

## [0.13.7](https://github.com/louisfifre/deckle/compare/v0.13.4...v0.13.7) — 2026-07-19

### Fixed

- **release:** Preserve changes between public releases
- **app:** Exclude duplicate DirectML publish payload
- **logging:** Restore structured filter labels

## [0.13.4](https://github.com/louisfifre/deckle/compare/v0.8.0...v0.13.4) — 2026-07-19

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
- **scripts:** Add record version workflow
- **autocorrect-probe:** Select execution provider and stream batch progress
- **audio:** Decode audio files to pipeline PCM via Media Foundation
- **transcription:** Add transcript writer and destination-folder setting
- **transcription:** Run picked audio files through the monolithic pipeline
- **hud:** Add the saved-transcript success message
- **traymenu:** Add the transcribe-a-file command item
- **app:** Wire the file-transcription flow from tray to HUD
- **anytype:** Add schema admin MCP surface
- **anytype:** Support project-to-epic links
- **catalog:** Add an application-log consent slot to the telemetry registry
- **autocorrect:** Collect the typed corpus on every editable surface
- **autocorrect-lab:** Guard replay intake and overlay maintainer ground truth
- **autocorrect:** Gate rare forms out of the sentence candidate set
- **autocorrect-onnx:** Abstain below a four-word context floor
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
- **transcription:** Default to ggml-base with installed-model fallback
- **setup:** Update pipeline — silent check, download page, deploy update mode
- **settings:** Update opt-out and version row on the General page
- **app:** Wire the in-app updater end to end
- **setup:** Data-root relocation page
- **settings:** Move action on the data-folder card
- **app:** Wire the data-root relocation end to end
- **autocorrect:** Fall back the rarity gate to the slot's best variant
- **autocorrect:** Wire the sentence judge into the live stage, margin 1.0
- **logging:** Refine live log interaction
- **autocorrect:** Capture the typing stream on enrolled surfaces
- **autocorrect:** Ventilate surface profiles from the typed corpus
- **autocorrect:** Mine mistouch families from the typed corpus
- **autocorrect:** Route approved mistouch families to the commit stage
- **autocorrect:** Anticipate the sentence stage on a typing pause
- **diagnostics:** Gate input activity and relocate wheel capture
- **hud:** Honor motion preferences across overlay feedback
- **app:** Make log transfers explicit and rows readable

### Changed

- **diagnostics:** Extract the shared settings-UX log source
- **audio:** Host the Recording settings page in its module
- **transcription:** Host the HUD overlay and auto-paste settings on the Dictation page
- **lighting:** Host the ambient capture-log toggle on the Ambient page
- **audio:** Host the microphone-telemetry opt-in on the Recording page
- **transcription:** Host the dictation observability opt-ins on the Dictation page
- **autocorrect:** Host the autocorrect observability opt-ins on the Autocorrect page
- **diagnostics:** Host the Diagnostics page in Deckle.Diagnostics.Logging
- **shell:** Share the TitleBar caption-inset correction
- **install:** Extract the Windows integration primitives
- **logging:** Streamline live log filtering
- **install:** Move the release resolver into Deckle.Install
- **transcription:** Avoid redundant PCM copies
- **logging:** Batch live log dispatch
- **setup:** Move provisioning into owning backends
- **setup:** Keep payload measurement off the UI thread
- **diagnostics:** Separate admission from projections
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

### Fixed

- **lighting:** Label ambient heartbeat push latency
- **vision:** Reduce capture recovery warning noise
- **lighting:** Prepare hue entertainment startup
- **lighting:** Keep Hue Entertainment stream alive
- **autocorrect-onnx:** Score in one forward and read fp16 logits for DirectML
- **anytype:** Align schema admin with live type contract
- **autocorrect:** Keep fragment tails out of corpus sentence starts
- **settings:** Honour display scale in caption reserve and window minimums
- **settings:** Pack the collapsed search icon against the title
- **app:** Honour display scale in the log window
- **playground:** Honour display scale in the playground window
- **settings:** Scale the initial window size
- **app:** Scale the log window's initial size
- **playground:** Scale the playground's initial size
- **trackpad:** Yield active drag to four-finger gestures
- **setup:** Persist the chosen speech model into engine settings
- **anytype:** Bound schema preview retention
- **lighting:** Surface Hue discovery failures
- **installer:** Keep cancellation aligned with setup state
- **anytype:** Make preview lookup null-safe
- **autocorrect-onnx:** Absorb the transient DML flake at model construction
- **input:** Coalesce duplicate focus events
- **app:** Gate rewrite pipeline and hotkeys on module presence
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
- **app:** Resolve DirectML publish collision

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

## [0.4.4](https://github.com/louisfifre/deckle/compare/v0.4.1...v0.4.4) — 2026-06-07

### Added

- **transcription:** Dynamic hangover ramp + observable streaming pipeline
- **transcription:** Detect AB-AB period-2 repetition loops
- **inference:** Silero VAD v5 ONNX module
- **transcription:** Trim streaming utterances with the external Silero VAD
- **transcription:** Surface an untrimmed streaming take; test Reset
- **transcription:** Make the external Silero VAD the speech-detection toggle
- **transcription:** Expose the Silero VAD parameters and log span counts
- **scripts:** Add a GitHub Release action to the dev menu

### Changed

- **hud:** Decouple the chrono lifecycle from the paint states
- **playground:** Drive the chrono clock explicitly in HUD previews
- **hud:** Split HudChrono into per-concern partials
- **diagnostics:** Name ETW providers Deckle-<component>, not Deckle.<module>
- **audio:** Sharpen the capture-lag probe to attribute a cause
- **vad:** Autonomous module, kill the dead whisper-internal VAD

### Fixed

- **scripts:** Serialize the publish build to avoid the WinAppSDK PRI race
- **app:** Switch the HUD to Transcribing the instant Stop is pressed
- **hud:** Suppress stale delayed z-order probes
- **hud:** Serialize the chrono stroke lifecycle against the RMS pump
- **build:** Dedupe self-contained project references
- **inference:** Dispose SessionOptions when the Silero session fails to construct
- **transcription:** Checksum-verify the Silero VAD download and self-heal a corrupt model
- **inference:** Run the v6.2 Silero VAD model and verify the on-disk build
- **app:** Keep the streaming firehose log gate whole across the VAD split
- **transcription:** Start chrono and duration on real capture start

## [0.4.1](https://github.com/louisfifre/deckle/compare/v0.4.0...v0.4.1) — 2026-06-04

### Added

- **installer:** Add NativeAOT download-stub installer
- **diagnostics:** Roll app journal by line count into a kept archive
- **telemetry:** Route the post-DSP distribution to its own channel

### Fixed

- **scripts:** Mark 0.x releases as pre-release

## [0.4.0](https://github.com/louisfifre/deckle/releases/tag/v0.4.0) — 2026-06-04

### Added

- **settings:** Frame+Page navigation + CommunityToolkit SettingsCard
- **settings:** Persistance JSON + WhisperPage câblée (6 sections)
- **hud:** Click-through natif + fade plancher réglable
- **settings:** Passe contenu EN + réordonnancement pages
- **settings:** GeneralPage fonctionnelle + descriptions WhisperPage auditées + restart ciblé
- **settings:** Câblage complet GeneralPage + thème live + overlay configurable
- **logwindow:** Refonte ListView native + clic-to-copy + UX copy Microsoft
- **llm:** Système de réécriture multi-profils Ollama complet
- **llm:** Paramètres de génération, OllamaService, import GGUF et modèles custom
- **ui:** Titre LogWindow, restart tray, shortcut rewrite, suppression override caption buttons
- **llm:** Refonte LlmPage en sections + mode RAW Ollama + détection micro
- **settings:** ComboBox modèle Ollama dans profils, UX import GGUF, titre Models
- **benchmark:** Add autoresearch.py — autonomous prompt optimization loop
- **llm:** Prompt Nettoyage optimisé par autoresearch v2
- **whisper:** Activate beam search, carry_initial_prompt, and richer initial prompt
- **engine:** Lazy load + idle unload whisper model to free VRAM
- **benchmark:** Suite restructuration — corpus, scripts, boucle 40 itérations
- **llm:** Prompt restructuration optimisé (benchmark 0.0000), suppression nettoyage
- **llm:** Restaure Nettoyage, prépare Restructuration pour benchmark
- **engine:** Silent warmup transcription at launch
- **shell:** Autostart via HKCU\Run registry key
- **hud:** Audio-level coupled recording outline (WIP, render to validate)
- **telemetry:** Unify envelope + routing sinks
- **benchmark:** Rewrite runner + autoresearch around 6-criteria grid
- **settings:** Per-section reset + clearer Latency copy
- **telemetry:** Layout refonte + ApplicationLog opt-in + Telemetry section
- **dev:** Scaffold HudPlayground standalone dev tool
- **dev:** HudPlayground tuning panel + simulated RMS pump
- **hud:** Swipe = critical flash on changed digits only (no disabled)
- **hud:** OKLCh palette, anti-moiré, RMS window + Recording accent
- **playground:** Promote HudPlayground to first-party WhispUI window
- **hud:** Message stack with proximity fade + gated low-audio warning
- **playground:** Persistence (auto-hydrate + Save + Reset all), responsive layout
- **engine,hud:** Mic telemetry + tail RMS fix + ephemeral playground + tuned defaults
- **engine,settings:** Microphone.jsonl + level window calibration defaults
- **settings,engine:** Voice level window UI + auto-calibration heuristic
- **benchmark:** Whisper initial prompt tuning + folder layout refacto
- **rewrite:** Aligner les profils par défaut sur les 4 brackets de cleanup
- **robustness:** Hardening sweep — Ollama startup race, dispatcher safety, settings mutex, lifecycle guards
- **settings:** Add SettingsBackupService and BackupDirectory
- **settings/general:** Add Backup section (snapshot and restore)
- **telemetry:** Per-stage latency instrumentation + Ollama metrics
- **hud:** Soften swipe animation defaults and widen playground sliders
- **llm:** Drop default Relecture profile, blank prompts, realign auto-rules
- **settings/general:** Add Application data section with Open data folder
- **setup:** Add SpeechModels catalog, Downloader, SetupContext (B.1)
- **setup:** Add SetupWindow shell (B.2)
- **setup:** Add wizard pages — Choices, Installing, Summary (B.3-B.5)
- **setup:** Wire wizard from App.OnLaunched + Re-run button (B.6, B.7)
- **telemetry:** Rolling history buffer + Replay() for late-registered sinks
- **localization:** Introduce .resw + Loc helper, migrate CorpusConsentDialog
- **localization:** Migrate remaining 14 surfaces to .resw + Loc
- **hud:** Warm-show HudWindow at boot to skip first-hotkey cold path
- **scripts:** Unify setup-assets, drop restore-assets and setup-userdata
- **scripts:** Add interactive launcher with two-step menu
- **rename:** Migrate %LOCALAPPDATA%\WhispUI\ to %LOCALAPPDATA%\Deckle\
- **hud:** Fade-in 150ms on Hidden→visible transition
- **native-runtime:** Add publish script + recompile recipe doc
- **setup:** Auto-download native runtime in the first-run wizard
- **paths:** Add AppPaths.GetModuleDirectory(moduleId)
- **vision:** Screen capture pump + Playground Ambient lighting page (J1)
- **lighting:** Hue REST driver scaffolding + Playground Discover/Pair (J2 step 1)
- **lighting:** Hue group listing + colour push + Playground rotation (J2 step 2)
- **ambient:** AmbientEngine scaffolding + Playground pipeline card (J3 step 1)
- **ambient:** J3 step 2 — real frame analysis + HDR + Playground polish
- **ambient:** Persist Hue bridge state — no more re-pair per session
- **ambient:** Clamp near-black averages to off so dark screens dim the lights
- **hue:** Force transitiontime=1 (100ms) so the lamp keeps up with the screen
- **vision:** Wire optional target monitor through ScreenCaptureService.Start
- **ambient:** J4 multi-light zones with Hue entertainment auto-fill
- **diagnostics:** Logging section + brightness/ComboBox UX rework
- **ambient:** Canonical engine ownership + Settings page + tray toggle + HDR tuning
- **ambient:** Color science pipeline — gamut C clip + linear-light averaging + OKLCh saturation
- **vision:** Switch capture backend to DXGI Output Duplication (removes Windows yellow capture border)
- **scripts:** Add bootstrap-dev-env.ps1 + global.json rollForward for multi-machine dev
- **ambient:** Promote BrightnessCurveGamma to AmbientSettings
- **ambient:** Extract HuePairingService
- **ambient:** Add BrightnessCurveCanvas UserControl
- **ambient:** Wire gamma slider + curve viz in Settings AmbientPage
- **deckle:** Add HDR tuning live card in Playground ambient page
- **settings:** Migrate Hue pairing UI to AmbientPage Configuration section
- **scripts:** UAC warning + per-item result tracking + post-install recap
- **scripts:** Handle pre-existing VS installs via setup.exe modify
- **scripts:** Add clean worker + Clean sub-menu in launcher
- **ambient:** Add temporal smoothing and live emitted-colour swatches
- **ambient:** Brightness curve types, smoothing slider, threshold slider
- **ambient:** Game/Movie/Ambient/Custom mode presets with auto-Custom
- **playground:** Minimal Home page with section picker
- **playground:** Preview follows the canonical engine + free the Pipeline toggle
- **playground, ambient:** Multi-curve canvas + light-zones toggle
- **ambient:** Allow inverted gamma (γ<1) and inverted S-curve (k<0)
- **ambient:** Resilient pipeline against transient interruptions
- **ambient:** Expose zone-sampling thickness as a user setting
- **agent:** Add tdd skill for red-green-refactor workflow
- **transcription:** Ajouter CorpusAsr/RewriteRecorded au provider Whisp
- **diagnostics:** RoutedJsonlEventListener pour paths résolus par event
- **diagnostics:** Câbler les destinations routées du corpus normalisé
- **bench:** Add Voxtral smoke test script
- **bench:** Add Voxtral POC bench — 6 configs + judge + métriques
- **transcription:** Brancher le pipeline sur le corpus normalisé
- **scripts:** Introduire TREE.md auto-update via hook pre-commit
- **bench:** Versionner les prompts par scope
- **bench:** Scénario voxtral-poc orchestrateur des régimes
- **bench:** Joindre events × monitor pour exposer les peaks system par row
- **bench:** Juge Gemini multimodal écoutant le WAV brut
- **bench:** Câbler --judge gemini dans le scénario voxtral-poc
- **bench:** Retry GeminiJudge sur 429 free tier Gemini
- **diagnostics:** Ouvrir les sub-providers transverses
- **diagnostics:** Instrumenter les transitions reseau
- **hud:** Instrumenter state machine, transitions, proximity
- **vision:** Ajouter heartbeat capture (fps + percentiles)
- **diagnostics:** Instrumenter cycle de vie ressources natives
- **diagnostics:** Instrumenter les annulations applicatives
- **diagnostics:** Instrumenter le positionnement des fenêtres
- **diagnostics:** Instrumenter le marshalling dispatcher
- **diagnostics:** Instrumenter les changements de thème
- **skills:** Introduire save-context et spawn-tasks pour la discipline de session
- **diagnostics:** Instrumenter le positionnement de PlaygroundWindow
- **diagnostics:** Exposer priority sur les wrappers Threading
- **settings:** Câbler les marshallings Settings sur le wrapper Threading
- **hud:** Câbler le warm pass tail sur le wrapper Threading
- **ambient:** Câbler les marshallings UI d'AmbientPage sur le wrapper Threading
- **playground:** Câbler les marshallings UI du Playground sur le wrapper Threading
- **playground:** Instrumenter les changements de thème de PlaygroundWindow
- **diagnostics:** Étendre Cancellation au pipeline ambient
- **hud:** Émettre le proximity rollup en récap fin de session
- **bench:** Corpus voxtral-val-30 stratifié + script de construction
- **bench:** Source Voxtral via llama-mtmd-cli (Vulkan) + régimes T1-T6
- **bench:** Source Gemini audio + pré-gen ground truth
- **bench:** Bench voxtral-validation — WER vs Gemini ground truth
- **bench:** Viewer HTML — comparaison Voxtral vs Whisper vs Gemini
- **bench:** Viewer HTML générique sous viewers/ — auto-discovery
- **bench:** Sanity check Voxtral Mini 3B en BF16 sur Transformers
- **bench:** Mesure perf RTF Voxtral 3B BF16 sur 3 durées audio
- **bench:** Backend Voxtral Transformers BF16 sur voxtral-validation
- **bench:** Comparaison BF16 vs Q4_K_M sur T1_baseline
- **bench:** Router voxtral-transformers vers apply_chat_template pour T2-T5
- **bench:** Outiller la lecture des verdicts judge
- **shell:** Introduire Deckle.Shell.TrayMenu pour le menu tray WinUI 3
- **shell:** Bascule l'item Ambient sur un ToggleSwitch via Style custom
- **bench:** Scaffold PhiBench C# tool for Phi-4 ONNX/DirectML POC
- **bench:** Smoke test voxtral ONNX DirectML — voie 2
- **whisp:** Warm up the model on first hotkey instead of at boot
- **vision:** Add capture stall detector with unit tests
- **observability:** Self-describing, bounded app.jsonl persistence
- **observability:** Tier the LogWindow registers by level
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

- **ui:** Migrate LogWindow to native TitleBar control
- **ui:** Migrate SettingsWindow to native TitleBar with NavigationView integration
- **logs:** Bind log entry colors via ThemeResource
- **hud:** Nettoyage XAML natif + backdrop transient
- **settings:** Burger dans TitleBar native + purge styles custom
- **logs:** Structured LogEntry model, CommandBar cleanup, FR→EN comments
- **logs:** Modularise logging — LogService singleton, ILogSink, sources typées
- Réorganisation dossiers — Interop/, Llm/, Shell/, DebugLog → Logging/
- **settings:** WhisperPage MVVM — ViewModel + x:Bind TwoWay, suppression handlers manuels
- **settings:** LlmPage MVVM — ProfilesSection et RulesSection en XAML déclaratif
- **benchmark:** V2 — scoring robuste, designer ciblé, observabilité
- **benchmark:** Config.ini + gitignore artefacts
- Restructure repo — flatten layout, decouple native deps, English comments
- **hud:** Unify recording outline into ProcessingStroke pipeline
- **benchmark:** Extract shared lib/ for ollama, corpus, metrics, judge
- **hud:** Expose Recording* overrides on ConicArcStrokeConfig
- **hud:** Expose tunables + RebuildStroke seam for HudPlayground
- **logging:** Progressive SelectorBar filter + two-register casing
- **playground:** Flat Expander list + resizable sash + rounded steps
- **telemetry:** Single Microphone event carries text + payload
- **paths:** Centralize filesystem resolution in AppPaths
- **settings/general:** Reorder sections — Appearance / Startup before Recording
- **settings/general:** Collapse Backup section into a PowerToys-style expander
- **logwindow:** Drop Narrative tab, default to All, fold narrative into Activity
- **record:** Rephrase Tail-600 ms log as plain-English signal at Stop
- **engine:** Hotkey-driven pipeline state machine + dispose worker join
- **paths:** Centralize user data under %LOCALAPPDATA%\<AppFolderName>\
- **setup:** Encapsulate whisper native runtime in Setup/NativeRuntime
- **paths:** Move settings.json to UserDataRoot root
- **app:** Lazy LogWindow + SettingsWindow + theme broadcast helper
- **logging:** Split Info/Verbose for SETTINGS load complete
- **hud:** Preload Bitcount Single via Win2D at boot
- **claude-md:** Drop redundant conventions section
- **scripts:** Three-mode native runtime provisioning
- **structure:** Scaffold empty Deckle.Core class library
- **structure:** Move Loc into Deckle.Core
- **structure:** Move AppPaths into Deckle.Core + GetModuleDirectory
- **structure:** Extract Loc into a dedicated Deckle.Localization project
- **structure:** Extract Logging into a dedicated Deckle.Logging project
- **structure:** Extract Llm into a dedicated Deckle.Llm project
- **structure:** Scaffold Deckle.Whisp + extract libwhisper P/Invokes
- **structure:** Move Win32 Interop layer into Deckle.Core
- **structure:** Extract WhispEngine + Whisp/Recording/Telemetry POCOs
- **llm:** Align LlmSettings namespace Deckle.Settings → Deckle.Llm
- **capture:** Scaffold Deckle.Capture project + extract CaptureSettings POCO
- **capture:** Extract MicrophoneCapture engine into Deckle.Capture
- **composition:** Scaffold Deckle.Composition + extract pure primitives
- **composition:** Move HudComposition + extract SwipeWaveAnimator
- **capture:** Migrate audio level mapping HudChrono → AudioLevelMapper
- **chrono:** Extract Deckle.Chrono — timer + formatter primitives
- **chrono:** Extract HudChrono UserControl into Deckle.Chrono.Hud
- **shell:** Extract Deckle.Shell — Hotkey + Tray + Autostart + MessageOnlyHost
- **settings:** Extract Deckle.Settings — slice A (core service)
- **settings:** Extract Deckle.Settings — slice B (UI surface)
- **core:** Extract JsonSettingsStore<T> from SettingsService
- **settings:** Per-module persistence (slice C2b)
- **settings:** Cross-module page split (slice C1)
- **settings:** Unified FolderPickerCard for storage paths (slice S1)
- **settings:** Fix FolderPicker nesting + restore button labels (slice S1)
- **settings:** Canonical Set/Open text-only buttons (slice S1)
- **settings:** Extract Diagnostics page from General (slice S2)
- **settings:** Extract Recording page from General (slice S3)
- **settings:** Rename Shortcuts → Hotkeys, Transcribe → Principal hotkey (slice S4)
- **whisp:** Wrap Decoding and Confidence sliders in SettingsExpander parents (slice S5)
- **settings:** Move Behaviour section from Recording to General (pass2)
- **whisp:** Reorder Transcription section and fold storage into model expander (pass2)
- **diagnostics:** Set microphone glyph on LogMicrophone card (pass2)
- **audio:** Rename Deckle.Capture to Deckle.Audio
- **deckle:** Route Playground pairing through HuePairingService
- **ambient:** UX pass on AmbientPage + Playground tuning copy
- **ambient:** Hue bridge layout + CriticalAccentButtonStyle + HDR descriptions back
- **ambient:** HDR slider layout + Forget confirm dialog
- **ambient:** Align HDR tuning reset pattern with WhisperPage
- **ambient:** Drop hardcoded red Forget + horizontal slider layout
- **ambient:** Value text left of slider in HDR tuning cards
- **ambient:** UX copy pass + full i18n of AmbientPage code-behind
- **playground:** Extract into Deckle.Playground module
- **ambient:** Slim Settings page down to mode + Playground link
- **ambient:** Reorder sections + bulb glyph for Ambient + open-in-new for Fine tuning
- **playground:** States/Primitive sections + native Play/Pause toggle
- **playground:** Ambient preview toolbar out of the frame + live zone fills
- **playground:** Tooltips + Hue auto-list + constant-scale overlays
- **catalog:** Unify Localization into Catalog + central Fluent Icons library
- **playground:** Split monolithic Window into Frame-navigated pages + MVVM
- **scripts:** Unify launcher into deckle.ps1 + lib/ split
- **observability:** Introduce EventSource pipeline
- **playground:** Update stale Deckle.Localization comment
- **audio:** Migrate to DeckleAudioSource EventSource provider
- **llm:** Remove in-app GGUF import (Ollama CLI handles it)
- **vision:** Migrate to DeckleVisionSource EventSource provider
- **lighting:** Migrate to DeckleLightingSource EventSource provider
- **transcription:** Rename Deckle.Whisp -> Deckle.Transcription
- **shell:** Migrate to DeckleShellSource EventSource provider
- **llm:** Migrate to DeckleLlmSource EventSource provider
- **app:** Rename host module Deckle -> Deckle.App
- **settings:** Migrate to DeckleSettingsSource EventSource provider
- **setup:** Extract first-run wizard into Deckle.Setup
- **hud:** Extract Deckle.Hud and dissolve Deckle.Chrono.Hud
- **llm:** Split Deckle.Llm into engine + Deckle.Llm.Rewrite consumer
- **modules:** Normalize Engine/Ui layout in Llm.Rewrite + Lighting.Ambient
- **core:** Homogenize Deckle.Core namespaces under Deckle.Core.*
- **whisp:** Migrate to DeckleWhispSource EventSource provider
- **ambient:** Migrate to DeckleAmbientSource EventSource provider
- **playground:** Migrate to DecklePlaygroundSource EventSource provider
- **host:** Migrate App and Setup to DeckleAppSource/DeckleSetupSource
- **observability:** Relocalize legacy POCOs ahead of Deckle.Logging removal
- **observability:** Dismantle UserFeedback legacy on HUD path
- **observability:** Wire LogWindow to EventSource pipeline direct
- **observability:** Wire user gates and ambient filter on EventSource listeners
- **observability:** Switch JSONL to canonical paths, retire JsonlFileSink
- **observability:** Drop dead using Deckle.Logging across modules
- **observability:** Drop Deckle.Logging module entirely
- **transcription:** Extract Whisper backend behind IAsrBackend
- **transcription:** Retirer l'event legacy CorpusRecorded
- **bench:** Pivot Voxtral POC stack de transformers+ROCm vers llama.cpp+Vulkan+GGUF
- **bench:** Basculer le squelette POC sur le layout modulaire v2
- **bench:** Étendre Judge.score_row avec audio_path optionnel
- **bench:** Désactiver le thinking sur GeminiJudge
- **bench:** Refondre le monitor système
- **bench:** Durcir les sources Voxtral DML contre l'inflation VRAM
- **bench:** Externaliser corpora et runs vers AppData via lib/paths.py
- **bench:** Centraliser les paths modèles GGUF vers paths.VOXTRAL_DIR
- **agent:** Aplatir la structure du skill ux-designer
- **shell:** Bascule le menu tray sur TrayContextMenuHost WinUI 3
- **shell:** Réordonner les items du menu tray avec Ambient en tête
- **shell:** Laisser le rendu natif WinUI 3 décider de la hauteur des items
- **shell:** WIP — polir le rendu du tray menu sur les rails Win11
- **shell:** WIP — pillule custom + helper réutilisable pour switch tray menu
- **ambient:** Isolate engine lifecycle and push loop
- **whisp:** Split transcription event source
- **settings:** Remove the dead warmup-on-launch toggle
- **whisp:** Drop the dead warmup observability events
- **core:** Add verified Win32Clipboard writer
- **whisp:** Route clipboard write through Win32Clipboard
- **core:** Co-locate clipboard P/Invokes in Win32Clipboard
- **transcription:** Extract pipeline seam and shared finalize
- **composition:** Split oversized host and HUD files
- **transcription:** Readable per-take streaming logs
- Split hue and vision oversized files

### Removed

- **llm:** Restore default SystemPrompts as the shipped example
- **perf:** Re-enable Mica — disabling didn't fix the lag

### Fixed

- **settings:** WhisperPage nav débloquée + refacto Settings canonique
- **settings:** Plages sliders confiance alignées sur rapport whisper.cpp + descriptions corrigées
- **settings:** Persistence cassée au restart + refacto GeneralPage MVVM
- **settings:** Import GGUF sans freeze UI + dirty detection immédiate profils
- **settings:** Defaults profils réécriture — températures basses, ctx 2K, modèle vide
- **paste:** Figer la cible paste au Start, supprimer la re-capture au Stop
- **benchmark:** Force UTF-8 stdout on Windows to prevent CP1252 crash in redirected output
- **benchmark:** Sanitize LLM output against terminal escape sequences
- **settings:** Click-to-unfocus on WhisperPage and LlmPage, fix Language ComboBox initial display
- **llm:** Augmenter NumCtxK par défaut (Restructuration 2→8K, Prompt 2→4K)
- Route startup milestones through LogService
- Gate PublishSingleFile on _IsPublishing, add SelfContained
- **diag:** Surface Silero-missing and LLM-empty-model errors
- **logs:** Downgrade 'no GPU found' on VAD backend init to Verbose
- **shell:** Make autostart owner-aware per install
- **hud:** Flush processing stroke at fractional DPI + center Recording lobes
- **playground:** Sash cursor, display rounding, focus dismiss, RMS step
- **playground:** SashThumb derives from ContentControl (Thumb is sealed)
- **hud:** Complementary digit opacities (primary + accent = 1)
- **playground:** Moiré, composition freeze, ArcMask in light theme
- **hud:** Live low-audio tracker + tighter stack gap + animations override
- **hud:** Kill DWM Shell dropshadow on overlays + tune low-audio tracker
- **playground,hud:** Responsive preview + badge semantic/centering
- **logwindow,hud:** Drop blank Microphone row + bump palette chroma
- **settings/llm:** Align rewrite rules, drop Prompt profile, recalibrate word thresholds
- **engine:** Tooltip stuck on "Loading model…", warmup pollutes logs
- **whisper:** Pass UTF-8 to whisper.cpp instead of CP-1252 ANSI
- **llm:** Keep rules independent of profiles, add UX-copy reset dialogs
- **llm:** Retry ProfileCombo selection on dispatcher tick after Reset Rules
- **settings/llm:** Rebuild auto-rewrite rules section without ItemsRepeater
- **setup:** Throttle download progress reporting to avoid UI freeze
- **setup:** Tolerant gate + retire Silero v5.1.2
- **playground:** Pause by default + reset to Pause on each show
- **hud:** Pre-init digit reference arrays at HudChrono ctor
- **hud:** Virtualize swipe head domain to drop last-digit stall
- **localization:** Drop Common_Browse.Content scope conflict
- **localization:** Split shared x:Uid on Storage folder buttons
- **hud:** Route warm pass through SetState to preserve topmost
- **engine:** Resolve model path from user-selected setting
- **hud:** Invisible warm pass via layered alpha=0
- **engine:** Scope warmup flag per-thread + cancel on toggle/dispose
- **build-run:** Launch Deckle via cmd /c start so it gets foreground promotion
- **core:** Add Microsoft.WindowsAppSDK reference to Deckle.Core
- **whisp:** Track Pinvoke files (Native/ collided with .gitignore)
- **whisp:** Align namespace Native → Pinvoke in WhisperPInvoke.cs
- **llm:** Make Ollama types public for cross-assembly visibility
- **setup:** DPI-aware wizard sizing + skip whisper re-download
- **settings:** Add Resources.resw to Deckle.Settings for x:Uid resolution
- **vision:** Manual HSTRING marshalling in RoGetActivationFactory
- **playground:** Use per-button IsEnabled instead of StackPanel attribute
- **lighting:** Decouple Hue brightness from RGB luminance
- **scripts:** Resolve Deckle.exe by glob, not hardcoded TPV segment
- **scripts:** Resolve Deckle.exe by glob, not hardcoded TPV segment
- **playground:** Nav layers + InvalidCastException on pipeline start
- **vision:** Use MarshalInspectable.FromManaged for the WinRT ABI extract
- **vision:** Copy mip 0 with CopySubresourceRegion, not CopyResource
- **vision:** Switch to CPU stride sampling — GPU mip path was returning zeros
- **playground:** Move RestoreHueFromSettings out of the constructor body
- **playground:** Allocate one SolidColorBrush per preview cell
- **diagnostics:** Logging toggle is per-module Ambient, not verbose level
- **playground+diagnostics:** Revert light zones row + flip Log Ambient default off
- **playground:** Zone ComboBox via static ItemsSource (fixes 3-click bug)
- **playground:** Zone picker via DropDownButton + MenuFlyout; pane closed; tooltip + width
- **ui+logging:** Focus stealing, dropdown UX, Verbose×source filter, Info promotions
- **logging:** Per-loop capture toggle + Verbose/Info doctrine enforcement
- **logging:** Central filter w/ capture-active flag; default OFF
- **diagnostics:** Trim Log ambient capture activity description to one factual sentence
- **ambient:** Audit remediation — Critical UI desync + Major transient feedback + IP validation + pre-publication docs
- **ambient:** GammaSlider default Value + Padding warning
- **ambient:** GammaSlider XAML attribute order Max/Value/Min
- **ambient:** Move GammaSlider range to code-behind
- **ambient:** Force OFF at boot + downgrade DXGI retry log
- **ambient:** Critical brushes + slider sizing + gamma expander + reset
- **ambient:** Mirror Loc.Get keys in main Deckle .resw
- **ambient:** Correct Forget dialog wording + flag mirror caveat
- **ambient:** Widen zones, restore exposure on SDR, raise change threshold
- **playground:** Seed Smoothing slider Minimum in code-behind
- **playground, ambient:** Home page overflow + clean Settings card
- **ambient:** Per-curve param + hide Gamma canvas for the other curves
- **playground:** Preview timer runs for the window's lifetime
- **ambient:** Drop duplicate LatestSample property
- **playground:** Zones toggle pattern + SCurve range + light names
- **scripts:** Stats.ps1 path split — use -split regex over String.Split overload
- **integration:** Resolve cross-layer EventSource refs post-merge
- **integration:** Rewrite App.xaml.cs refs after moved Diagnostics namespaces
- **playground:** Restore capture preview placeholder on ambient stop
- **ambient:** Release DXGI duplication eagerly on engine stop
- **bench:** Commentaires .gitignore sur des lignes dédiées
- **bench:** Dtype= au lieu de torch_dtype= pour charger Voxtral en fp16
- **bench:** Élargir pauses model_load à 10 s pour stabilité monitor
- **bench:** Bumper max_output_tokens GeminiJudge à 4096
- **bench:** Aligner la fenêtre idle_baseline sur la pause pré-load
- **diagnostics:** Garder TryEnqueueObserved contre la récursion synchrone du listener
- **bench:** Aligner le prompt judge sur les régimes T1-T6 actuels
- **bench:** Empêcher l'écrasement silencieux des runs avec model composé
- **bench:** Relever max_new_tokens Voxtral à 8 tokens/s d'audio
- **shell:** Déplace les strings TrayMenu dans le .resw de Deckle.App
- **shell:** Scaler le menu tray via le DPI du moniteur sous le curseur
- **shell:** Ancrer le menu tray sur le rect de l'icône, pas le curseur
- **shell:** Forcer FlyoutPlacementMode.Full pour aligner le popup
- **shell:** Aligner le tray menu sur la densité Win11 narrow
- **observability:** Drop per-frame capture firehose before buffering
- **observability:** Stop LogWindow observing its own log-append
- **ambient:** Classify Hue echoes by pushed state
- **shell:** Aligner le dimensionnement du tray menu sur le rendu réel
- **vision:** Make the duplication recreate format-aware
- **ambient:** Rebuild the frame sampler on capture format change
- **app:** Make LogWindow Copy reliable on full selection
- **observability:** Align app journal with log window
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
