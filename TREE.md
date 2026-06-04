# Arborescence — Deckle
_Généré depuis `git ls-files` — ne pas éditer à la main._

```
├── .claude/
│   ├── agents/
│   │   ├── expert-dotnet-software-engineer.agent.md  — Expert .NET software engineer mode instructions Provide expert .NET software engineering guidance using modern software design…
│   │   ├── plan.agent.md  — Plan Mode - Strategic Planning & Architecture Strategic planning and architecture assistant focused on thoughtful analysis be…
│   │   └── winui3-expert.agent.md  — WinUI 3 Expert Expert agent for WinUI 3 and Windows App SDK development. Prevents common UWP-t…
│   └── skills/
│       ├── deckle-commits/
│       │   └── SKILL.md  — deckle-commits [skill] Commit grain and the few deviations from the universal convention. Invoke befor…
│       ├── deckle-journal/
│       │   ├── examples.md
│       │   └── SKILL.md  — deckle-journal [skill] How to write the JOURNAL.md. Invoke when recording a finding, or a small decisi…
│       ├── deckle-logging/
│       │   ├── SKILL.md  — deckle-logging [skill] What to observe in code, and how to write it readable and actionable. Invoke be…
│       │   └── taxonomy.md
│       ├── deckle-modularite/
│       │   └── SKILL.md  — deckle-modularite [skill] When and along which lines to separate code into modules and files. Invoke befo…
│       ├── deckle-nomenclature/
│       │   ├── SKILL.md  — deckle-nomenclature [skill] Naming doctrine for Deckle: casing and prefixes, accepted vs fuzzy suffixes, na…
│       │   └── taxonomie.md
│       ├── deckle-settings-ux/
│       │   └── SKILL.md  — deckle-settings-ux [skill] UX doctrine for Deckle's settings surfaces: what to expose and how to organize…
│       ├── deckle-testing/
│       │   └── SKILL.md  — deckle-testing [skill] Testing posture — test behavior not implementation, stay sober, grow coverage p…
│       ├── deckle-versioning/
│       │   └── SKILL.md  — deckle-versioning [skill] How versions are numbered and the changelog written. Invoke before cutting a ve…
│       ├── deckle-workflow/
│       │   └── SKILL.md
│       ├── deckle-xaml/
│       │   └── SKILL.md  — deckle-xaml [skill] Transverse XAML rendering doctrine for Deckle (.NET 10 / WinUI 3): native primi…
│       ├── microsoft-docs/
│       │   └── SKILL.md  — microsoft-docs Query official Microsoft documentation to find concepts, tutorials, and code ex…
│       ├── senior-architect/
│       │   ├── references/
│       │   │   ├── architecture_patterns.md
│       │   │   ├── system_design_workflows.md
│       │   │   └── tech_decision_guide.md
│       │   ├── scripts/
│       │   │   ├── architecture_diagram_generator.py
│       │   │   ├── dependency_analyzer.py
│       │   │   └── project_architect.py
│       │   └── SKILL.md  — senior-architect Complete toolkit for senior architect with modern tools and best practices.
│       ├── senior-frontend/
│       │   ├── references/
│       │   │   ├── frontend_best_practices.md
│       │   │   ├── nextjs_optimization_guide.md
│       │   │   └── react_patterns.md
│       │   ├── scripts/
│       │   │   ├── bundle_analyzer.py
│       │   │   ├── component_generator.py
│       │   │   └── frontend_scaffolder.py
│       │   └── SKILL.md  — senior-frontend Frontend development skill for React, Next.js, TypeScript, and Tailwind CSS app…
│       ├── senior-fullstack/
│       │   ├── references/
│       │   │   ├── architecture_patterns.md
│       │   │   ├── development_workflows.md
│       │   │   └── tech_stack_guide.md
│       │   ├── scripts/
│       │   │   ├── code_quality_analyzer.py
│       │   │   ├── fullstack_scaffolder.py
│       │   │   └── project_scaffolder.py
│       │   └── SKILL.md  — senior-fullstack Complete toolkit for senior fullstack with modern tools and best practices.
│       ├── tdd/
│       │   ├── deep-modules.md
│       │   ├── interface-design.md
│       │   ├── mocking.md
│       │   ├── refactoring.md
│       │   ├── SKILL.md  — tdd Test-driven development with red-green-refactor loop. Use when user wants to bu…
│       │   └── tests.md
│       ├── ux-copy/
│       │   └── SKILL.md  — ux-copy Write or review UX copy — microcopy, error messages, empty states, CTAs. Trigge…
│       ├── ux-designer/
│       │   ├── rules/
│       │   │   ├── accessibility.md
│       │   │   ├── information-architecture.md
│       │   │   ├── interaction-design.md
│       │   │   ├── research.md
│       │   │   └── visual-design.md
│       │   ├── AGENTS.md
│       │   └── SKILL.md  — ux-designer Expert UX design assistance for user research, wireframing, prototyping, and de…
│       ├── winui-app/
│       │   ├── agents/
│       │   │   └── openai.yaml
│       │   ├── assets/
│       │   │   └── winui.png
│       │   ├── references/
│       │   │   ├── _sections.md
│       │   │   ├── accessibility-input-and-localization.md
│       │   │   ├── build-run-and-launch-verification.md
│       │   │   ├── community-toolkit-controls-and-helpers.md
│       │   │   ├── controls-layout-and-adaptive-ui.md
│       │   │   ├── foundation-environment-audit-and-remediation.md
│       │   │   ├── foundation-setup-and-project-selection.md
│       │   │   ├── foundation-template-first-recovery.md
│       │   │   ├── foundation-winui-app-structure.md
│       │   │   ├── motion-animations-and-polish.md
│       │   │   ├── performance-diagnostics-and-responsiveness.md
│       │   │   ├── sample-source-map.md
│       │   │   ├── shell-navigation-and-windowing.md
│       │   │   ├── styling-theming-materials-and-icons.md
│       │   │   ├── testing-debugging-and-review-checklists.md
│       │   │   └── windows-app-sdk-lifecycle-notifications-and-deployment.md
│       │   ├── config.yaml
│       │   ├── LICENSE.txt
│       │   └── SKILL.md  — winui-app Bootstrap, develop, and design modern WinUI 3 desktop applications with C# and…
│       └── write-a-skill/
│           └── SKILL.md  — write-a-skill Create new agent skills with proper structure, progressive disclosure, and bund…
├── .vscode/
│   └── launch.json
├── benchmark/
│   ├── benches/
│   │   ├── voxtral-onnx-poc/
│   │   │   ├── debug_kv.py
│   │   │   ├── debug_tokens.py
│   │   │   └── smoke_test.py
│   │   ├── voxtral-poc/
│   │   │   ├── bench.py
│   │   │   └── README.md  — readme-bench-voxtral-poc [module-readme] Bench scenario evaluating Voxtral Mini 3B as a Whisper alternative in the Deckl…
│   │   ├── voxtral-transformers/
│   │   │   ├── compare_bf16_vs_q4.py
│   │   │   ├── inspect_smoke_palier3.py
│   │   │   ├── perf_rtf.py
│   │   │   ├── sandbox_sampling.py
│   │   │   ├── sanity_check.py
│   │   │   ├── smoke_chat_regimes.py
│   │   │   └── summary_validation_0001.py
│   │   └── voxtral-validation/
│   │       ├── aggregate_verdicts.py
│   │       ├── bench.py
│   │       ├── README.md  — bench-voxtral-validation [bench-scenario] Bench de validation Voxtral 24B Q4_K_M comme remplacement de Whisper, ground tr…
│   │       └── validate_judge_prompt.py
│   ├── cs/
│   │   └── PhiBench/
│   │       ├── Models/
│   │       │   ├── Regime.cs
│   │       │   ├── Sample.cs
│   │       │   └── TranscriptionResult.cs
│   │       ├── CorpusLoader.cs
│   │       ├── CorpusRunner.cs
│   │       ├── JsonlWriter.cs
│   │       ├── Phi4Transcriber.cs
│   │       ├── PhiBench.csproj
│   │       ├── Program.cs
│   │       ├── RegimesLoader.cs
│   │       ├── SingleRunner.cs
│   │       └── WavHeader.cs
│   ├── lib/
│   │   ├── judges/
│   │   │   ├── __init__.py
│   │   │   ├── _base.py
│   │   │   ├── claude.py
│   │   │   └── gemini.py
│   │   ├── metrics/
│   │   │   ├── __init__.py
│   │   │   ├── leak.py
│   │   │   ├── looping.py
│   │   │   └── wer.py
│   │   ├── monitor/
│   │   │   ├── gpu_monitor.ps1
│   │   │   └── joiner.py
│   │   ├── sources/
│   │   │   ├── __init__.py
│   │   │   ├── _base.py
│   │   │   ├── _voxtral_common.py
│   │   │   ├── gemini_audio.py
│   │   │   ├── voxtral_chat.py
│   │   │   ├── voxtral_llamacpp.py
│   │   │   ├── voxtral_transcribe.py
│   │   │   ├── voxtral_transformers.py
│   │   │   └── whisper_cpp.py
│   │   ├── __init__.py
│   │   ├── _base_compat.py
│   │   ├── corpus.py
│   │   ├── env.py
│   │   ├── event_log.py
│   │   └── paths.py
│   ├── perf-cap/
│   │   ├── debug-mini-3b-f16-2026-05-27.ps1
│   │   ├── debug-mini-3b-q8-2026-05-27.ps1
│   │   ├── debug-samples-difficiles-2026-05-27.ps1
│   │   ├── debug-transcribe-token-2026-05-27.ps1
│   │   ├── download-models.ps1
│   │   ├── parse_vulkan_log.py
│   │   ├── profile-config.ps1
│   │   ├── profile-server-text.ps1
│   │   ├── run-all.ps1
│   │   ├── session-2026-05-26-prompts.ps1
│   │   └── session-2026-05-26-reruns.ps1
│   ├── prompts/
│   │   ├── judges/
│   │   │   ├── claude_per_row.md
│   │   │   ├── gemini_per_row.md
│   │   │   └── legacy_ollama_judge.md
│   │   ├── transcription/
│   │   │   ├── gemini_audio.toml
│   │   │   ├── voxtral_chat.toml
│   │   │   ├── voxtral_transcribe.toml
│   │   │   └── voxtral_validation.toml
│   │   └── whisper_initial.txt
│   ├── viewers/
│   │   ├── __init__.py
│   │   └── build_html.py
│   ├── build_corpus_voxtral_val_30.py
│   ├── CLAUDE.md  — claude-benchmark [agent-instructions] Doctrine for the benchmark/ suite — an autonomous box that measures quality and…
│   ├── Directory.Build.props
│   ├── JOURNAL.md  — journal-benchmark [module-journal] Journal daté du module benchmark : décisions intermédiaires, hypothèses, learni…
│   ├── pregenerate_groundtruth_gemini.py
│   └── README.md  — readme-benchmark [module-readme] Human-facing entry point for the benchmark/ suite — what it is, how a bench is…
├── docs/
│   ├── adr/
│   │   ├── 0000-template.md  — adr-0000-template [adr] Fill-in template and worked example for Deckle ADRs. Not a decision — copy it t…
│   │   ├── 0001-lazy-secondary-windows.md  — adr-0001-lazy-secondary-windows [adr] Records that Deckle builds its secondary WinUI 3 windows (Settings, Logs, Playg…
│   │   ├── 0002-resolve-assets-via-userdataroot.md  — adr-0002-resolve-assets-via-userdataroot [adr] Records that Deckle resolves native runtime assets and speech models from UserD…
│   │   ├── 0003-adopt-eventsource-for-observability.md  — adr-0003-adopt-eventsource-for-observability [adr] Records the move of Deckle's observability pillar from a home-grown TelemetrySe…
│   │   ├── 0004-diagnostics-parent-logging-telemetry-children.md  — adr-0004-diagnostics-parent-logging-telemetry-children [adr] Records the three-module split of observability: a Deckle.Diagnostics parent ca…
│   │   ├── 0005-pluggable-asr-backend-via-iasrbackend.md  — adr-0005-pluggable-asr-backend-via-iasrbackend [adr] Records the split of transcription into a backend-agnostic Deckle.Transcription…
│   │   ├── 0006-normalized-corpus-as-ml-dataset.md  — adr-0006-normalized-corpus-as-ml-dataset [adr] Records the corpus redesign into a normalized ML dataset: separate ASR and rewr…
│   │   ├── 0007-self-describing-app-journal-with-rotation.md  — adr-0007-self-describing-app-journal-with-rotation [adr] Records that the persisted app journal (app.jsonl) becomes the self-describing…
│   │   └── 0008-raw-capture-and-in-house-dsp.md  — adr-0008-raw-capture-and-in-house-dsp [adr] Records that Deckle keeps raw mic capture (waveInOpen) and conditions the signa…
│   └── reviews/
│       └── review--commentaires-code--2026-06-01.md
├── scripts/
│   ├── hooks/
│   │   ├── pre-commit
│   │   └── update-tree.ps1
│   ├── lib/
│   │   ├── _menu.psm1
│   │   ├── bootstrap-dev-env.ps1
│   │   ├── build-run.ps1
│   │   ├── clean.ps1
│   │   ├── install-hooks.ps1
│   │   ├── publish-app.ps1
│   │   ├── publish-native-runtime.ps1
│   │   ├── setup-assets.ps1
│   │   └── stats.ps1
│   ├── deckle.ps1
│   └── README.md  — readme-scripts [module-readme] Dev workflows entry point for Deckle: the deckle.ps1 menu, the worker scripts u…
├── src/
│   ├── Deckle.App/
│   │   ├── Assets/
│   │   │   ├── Fonts/
│   │   │   │   └── BitcountSingle.ttf
│   │   │   ├── Icons/
│   │   │   │   ├── recording--indicator--false--16px.ico
│   │   │   │   ├── recording--indicator--false--16px.png
│   │   │   │   ├── recording--indicator--false--32px.ico
│   │   │   │   ├── recording--indicator--false--32px.png
│   │   │   │   ├── recording--indicator--true--16px.ico
│   │   │   │   ├── recording--indicator--true--16px.png
│   │   │   │   ├── recording--indicator--true--32px.ico
│   │   │   │   └── recording--indicator--true--32px.png
│   │   │   └── Sounds/
│   │   │       └── speech.wav
│   │   ├── Diagnostics/
│   │   │   ├── AppDiagnosticsBootstrap.cs
│   │   │   ├── AppHudFeedbackSink.cs
│   │   │   ├── DeckleAppSource.cs
│   │   │   ├── LogEntry.cs
│   │   │   ├── LogEntryTemplateSelector.cs
│   │   │   └── NetworkStatusEmitter.cs
│   │   ├── Engine/
│   │   │   ├── AppAmbientEngineHost.cs
│   │   │   └── AppTranscriptionEngineHost.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── App.Ambient.cs
│   │   ├── App.Hotkeys.cs
│   │   ├── App.Lifetime.cs
│   │   ├── app.manifest
│   │   ├── App.Theme.cs
│   │   ├── App.Windows.cs
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── CLAUDE.md  — claude-deckle-app [agent-instructions] Doctrine for Deckle.App, the WinUI 3 host module that composes all Deckle.* mod…
│   │   ├── Deckle.App.csproj
│   │   ├── global.json
│   │   ├── LogWindow.xaml
│   │   ├── LogWindow.xaml.cs
│   │   └── SecondaryWindowPlacement.cs
│   ├── Deckle.Audio/
│   │   ├── Internal/
│   │   │   ├── PcmConversion.cs
│   │   │   └── WaveInLoop.cs
│   │   ├── Preprocessing/
│   │   │   ├── Compressor.cs
│   │   │   ├── HighPassFilter.cs
│   │   │   ├── Limiter.cs
│   │   │   ├── MicLevelCheck.cs
│   │   │   ├── NoiseGate.cs
│   │   │   ├── PreprocessingSettings.cs
│   │   │   └── TranscriptionPreprocessor.cs
│   │   ├── Telemetry/
│   │   │   ├── MicrophoneCalibrationCalculator.cs
│   │   │   ├── MicrophoneTelemetryCalculator.cs
│   │   │   └── MicrophoneTelemetryPayload.cs
│   │   ├── AudioLevelMapper.cs
│   │   ├── CaptureFrame.cs
│   │   ├── CaptureResult.cs
│   │   ├── CaptureSettings.cs
│   │   ├── CaptureSettingsService.cs
│   │   ├── CLAUDE.md  — claude-deckle-audio [agent-instructions] Doctrine for Deckle.Audio, the microphone capture and audio telemetry module. R…
│   │   ├── Deckle.Audio.csproj
│   │   ├── DeckleAudioSource.cs
│   │   ├── IAudioRecordingHost.cs
│   │   ├── MicLevelTester.cs
│   │   ├── MicrophoneCapture.cs
│   │   └── ProbeResult.cs
│   ├── Deckle.Catalog/
│   │   ├── Themes/
│   │   │   └── Icons.xaml
│   │   ├── CLAUDE.md  — claude-deckle-catalog [agent-instructions] Doctrine for Deckle.Catalog, the UI resource catalog module (localized strings…
│   │   ├── Deckle.Catalog.csproj
│   │   ├── Glyphs.cs
│   │   └── Loc.cs
│   ├── Deckle.Chrono/
│   │   ├── ChronoFormatter.cs
│   │   ├── ChronoTimer.cs
│   │   ├── Deckle.Chrono.csproj
│   │   └── DeckleChronoSource.cs
│   ├── Deckle.Composition/
│   │   ├── Core/
│   │   │   ├── HudComposition.Animations.cs
│   │   │   ├── HudComposition.Config.cs
│   │   │   ├── HudComposition.cs
│   │   │   ├── HudComposition.Factories.cs
│   │   │   ├── HudComposition.NakedPreview.cs
│   │   │   ├── HudComposition.Painting.cs
│   │   │   ├── HudComposition.ProcessingStroke.cs
│   │   │   └── ProcessingVariant.cs
│   │   ├── Primitives/
│   │   │   ├── ColorSpace.cs
│   │   │   ├── Easing.cs
│   │   │   └── SwipeWaveAnimator.cs
│   │   └── Deckle.Composition.csproj
│   ├── Deckle.Core/
│   │   ├── Interop/
│   │   │   ├── NativeMethods.cs
│   │   │   ├── Structs.cs
│   │   │   ├── UIAutomation.cs
│   │   │   ├── Win32Clipboard.cs
│   │   │   └── Win32Util.cs
│   │   ├── Paths/
│   │   │   └── AppPaths.cs
│   │   ├── CorpusPaths.cs
│   │   ├── Deckle.Core.csproj
│   │   └── JsonSettingsStore.cs
│   ├── Deckle.Diagnostics/
│   │   ├── Listeners/
│   │   │   ├── HudFeedbackEventListener.cs
│   │   │   ├── JsonlEventListener.cs
│   │   │   ├── JsonlRotationPolicy.cs
│   │   │   ├── JsonlSchema.cs
│   │   │   ├── LogWindowEventListener.cs
│   │   │   └── RoutedJsonlEventListener.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Observability foundation — EventSource providers, levels, sinks, JSONL contract.
│   │   ├── Deckle.Diagnostics.csproj
│   │   ├── DeckleCancellationSource.cs
│   │   ├── DeckleEventSource.cs
│   │   ├── DeckleNetworkSource.cs
│   │   ├── DeckleResourceSource.cs
│   │   ├── DeckleThemeSource.cs
│   │   ├── DeckleThreadingSource.cs
│   │   ├── DeckleWindowingSource.cs
│   │   ├── EventEntry.cs
│   │   ├── IHudFeedbackSink.cs
│   │   ├── ILogWindowSink.cs
│   │   ├── Keywords.cs
│   │   ├── LogLineFormatter.cs
│   │   └── WindowingProbe.cs
│   ├── Deckle.Diagnostics.Logging/
│   │   ├── AmbientCaptureGate.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Live LogWindow settings and the ambient capture noise gate.
│   │   ├── Deckle.Diagnostics.Logging.csproj
│   │   ├── LoggingSettings.cs
│   │   ├── LoggingSettingsService.cs
│   │   ├── LogWindowFilter.cs
│   │   └── LogWindowVisibilityMode.cs
│   ├── Deckle.Diagnostics.Telemetry/
│   │   ├── CLAUDE.md  — [agent-instructions] Structured JSONL persistence and consent gates.
│   │   ├── Deckle.Diagnostics.Telemetry.csproj
│   │   ├── TelemetryListenerBootstrap.cs
│   │   ├── TelemetrySettings.cs
│   │   └── TelemetrySettingsService.cs
│   ├── Deckle.Hud/
│   │   ├── CLAUDE.md  — claude-deckle-hud [agent-instructions] Doctrine for Deckle.Hud, the HUD window, overlay manager, and composite shadow…
│   │   ├── Deckle.Hud.csproj
│   │   ├── DeckleHudSource.cs
│   │   ├── HudChrono.xaml
│   │   ├── HudChrono.xaml.cs
│   │   ├── HudMessage.xaml
│   │   ├── HudMessage.xaml.cs
│   │   ├── HudOverlayManager.cs
│   │   ├── HudOverlayWindow.xaml
│   │   ├── HudOverlayWindow.xaml.cs
│   │   ├── HudPalette.cs
│   │   ├── HudState.cs
│   │   ├── HudWindow.xaml
│   │   ├── HudWindow.xaml.cs
│   │   ├── MessageKind.cs
│   │   ├── ProximityRollupAggregator.cs
│   │   └── WindowSlideAnimator.cs
│   ├── Deckle.Installer/
│   │   ├── Install/
│   │   │   ├── CliArgs.cs
│   │   │   ├── InstallFlow.cs
│   │   │   ├── InstallPaths.cs
│   │   │   └── Uninstaller.cs
│   │   ├── Io/
│   │   │   └── Downloader.cs
│   │   ├── Platform/
│   │   │   ├── Shortcut.cs
│   │   │   ├── UninstallEntry.cs
│   │   │   └── UserEnvironment.cs
│   │   ├── Release/
│   │   │   └── ReleaseResolver.cs
│   │   ├── Ui/
│   │   │   └── ConsoleUi.cs
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Installer.csproj
│   │   └── Program.cs
│   ├── Deckle.Lighting/
│   │   ├── Hue/
│   │   │   ├── HueBridge.cs
│   │   │   ├── HueBridgeClient.cs
│   │   │   ├── HueBridgeClient.Dtos.cs
│   │   │   ├── HueBridgeClient.EventStream.cs
│   │   │   ├── HueBridgeClient.Pairing.cs
│   │   │   ├── HueBridgeClient.V2.cs
│   │   │   ├── HueColorMath.cs
│   │   │   ├── HueDiscovery.cs
│   │   │   ├── HueEntertainmentArea.cs
│   │   │   ├── HueEventStreamModels.cs
│   │   │   ├── HueGroup.cs
│   │   │   ├── HueLight.cs
│   │   │   ├── HueProjectedState.cs
│   │   │   └── HueRestLightOutput.cs
│   │   ├── CLAUDE.md  — claude-deckle-lighting [agent-instructions] Doctrine for Deckle.Lighting, the Philips Hue driver (REST CLIP v1/v2) and colo…
│   │   ├── Deckle.Lighting.csproj
│   │   ├── DeckleLightingSource.cs
│   │   ├── ILightOutput.cs
│   │   ├── LightColor.cs
│   │   └── LightDescriptor.cs
│   ├── Deckle.Lighting.Ambient/
│   │   ├── Engine/
│   │   │   ├── AmbientColorPipeline.cs
│   │   │   ├── AmbientEngine.cs
│   │   │   ├── AmbientEngine.Lifecycle.cs
│   │   │   ├── AmbientEngine.PushLoop.cs
│   │   │   ├── AmbientEngineState.cs
│   │   │   ├── AmbientHueEchoClassifier.cs
│   │   │   ├── AmbientModePresets.cs
│   │   │   ├── AmbientZoneSampler.cs
│   │   │   ├── HuePairingService.cs
│   │   │   ├── LightZone.cs
│   │   │   └── LightZoneSuggester.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── Controls/
│   │   │   │   ├── BrightnessCurveCanvas.xaml
│   │   │   │   └── BrightnessCurveCanvas.xaml.cs
│   │   │   ├── AmbientPage.xaml
│   │   │   └── AmbientPage.xaml.cs
│   │   ├── AmbientSettings.cs
│   │   ├── AmbientSettingsService.cs
│   │   ├── Deckle.Lighting.Ambient.csproj
│   │   ├── DeckleAmbientSource.cs
│   │   └── IAmbientEngineHost.cs
│   ├── Deckle.Llm/
│   │   ├── Deckle.Llm.csproj
│   │   ├── DeckleLlmSource.cs
│   │   └── OllamaService.cs
│   ├── Deckle.Llm.Rewrite/
│   │   ├── Engine/
│   │   │   ├── LlmService.cs
│   │   │   └── PromptTemplates.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── LlmGeneralSection.xaml
│   │   │   ├── LlmGeneralSection.xaml.cs
│   │   │   ├── LlmModelsSection.xaml
│   │   │   ├── LlmModelsSection.xaml.cs
│   │   │   ├── LlmOllamaContext.cs
│   │   │   ├── LlmPage.xaml
│   │   │   ├── LlmPage.xaml.cs
│   │   │   ├── LlmProfilesSection.xaml
│   │   │   ├── LlmProfilesSection.xaml.cs
│   │   │   ├── LlmRulesSection.xaml
│   │   │   ├── LlmRulesSection.xaml.cs
│   │   │   ├── LlmShortcutSlotsSection.xaml
│   │   │   ├── LlmShortcutSlotsSection.xaml.cs
│   │   │   └── ProfileViewModel.cs
│   │   ├── Deckle.Llm.Rewrite.csproj
│   │   ├── LlmSettings.cs
│   │   ├── LlmSettingsMigrations.cs
│   │   └── LlmSettingsService.cs
│   ├── Deckle.Playground/
│   │   ├── Models/
│   │   │   └── TuningModel.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── ViewModels/
│   │   │   ├── AmbientViewModel.cs
│   │   │   └── HudViewModel.cs
│   │   ├── Views/
│   │   │   ├── AmbientPage.HdrTuning.cs
│   │   │   ├── AmbientPage.Hue.cs
│   │   │   ├── AmbientPage.LightZones.cs
│   │   │   ├── AmbientPage.Preview.cs
│   │   │   ├── AmbientPage.ScreenCapture.cs
│   │   │   ├── AmbientPage.xaml
│   │   │   ├── AmbientPage.xaml.cs
│   │   │   ├── HomePage.xaml
│   │   │   ├── HomePage.xaml.cs
│   │   │   ├── HudPage.Expanders.cs
│   │   │   ├── HudPage.RowFactories.cs
│   │   │   ├── HudPage.xaml
│   │   │   ├── HudPage.xaml.cs
│   │   │   ├── PlaygroundWindow.xaml
│   │   │   └── PlaygroundWindow.xaml.cs
│   │   ├── CLAUDE.md  — claude-deckle-playground [agent-instructions] Doctrine for Deckle.Playground, the dev-only tuning and diagnostics sandbox sur…
│   │   ├── Deckle.Playground.csproj
│   │   ├── DecklePlaygroundSource.cs
│   │   └── PlaygroundShell.cs
│   ├── Deckle.Settings/
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── ViewModels/
│   │   │   ├── DiagnosticsViewModel.cs
│   │   │   ├── GeneralViewModel.cs
│   │   │   └── RecordingViewModel.cs
│   │   ├── ApplicationLogConsentDialog.cs
│   │   ├── AppSettings.cs
│   │   ├── AudioCorpusConsentDialog.cs
│   │   ├── CLAUDE.md  — claude-deckle-settings [agent-instructions] Doctrine for Deckle.Settings, the settings UI shell and per-module persistence…
│   │   ├── CorpusConsentDialog.cs
│   │   ├── Deckle.Settings.csproj
│   │   ├── DeckleSettingsSource.cs
│   │   ├── DiagnosticsPage.xaml
│   │   ├── DiagnosticsPage.xaml.cs
│   │   ├── FolderPickerCard.xaml
│   │   ├── FolderPickerCard.xaml.cs
│   │   ├── FolderPickerEditableCard.xaml
│   │   ├── FolderPickerEditableCard.xaml.cs
│   │   ├── GeneralPage.xaml
│   │   ├── GeneralPage.xaml.cs
│   │   ├── MicrophoneTelemetryConsentDialog.cs
│   │   ├── RecordingPage.xaml
│   │   ├── RecordingPage.xaml.cs
│   │   ├── SettingsBackupService.cs
│   │   ├── SettingsBootstrap.cs
│   │   ├── SettingsHost.cs
│   │   ├── SettingsService.cs
│   │   ├── SettingsWindow.xaml
│   │   └── SettingsWindow.xaml.cs
│   ├── Deckle.Setup/
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── ChoicesPage.xaml
│   │   ├── ChoicesPage.xaml.cs
│   │   ├── CLAUDE.md  — claude-deckle-setup [agent-instructions] Doctrine for Deckle.Setup, the first-run wizard and provisioning primitives (na…
│   │   ├── Deckle.Setup.csproj
│   │   ├── DeckleSetupSource.cs
│   │   ├── InstallingPage.xaml
│   │   ├── InstallingPage.xaml.cs
│   │   ├── SetupContext.cs
│   │   ├── SetupWindow.xaml
│   │   ├── SetupWindow.xaml.cs
│   │   ├── SummaryPage.xaml
│   │   └── SummaryPage.xaml.cs
│   ├── Deckle.Shell/
│   │   ├── AutostartService.cs
│   │   ├── CLAUDE.md  — claude-deckle-shell [agent-instructions] Doctrine for Deckle.Shell, the system shell module (message-only host, tray, gl…
│   │   ├── Deckle.Shell.csproj
│   │   ├── DeckleShellSource.cs
│   │   ├── DispatcherQueueExtensions.cs
│   │   ├── HotkeyManager.cs
│   │   ├── IconAssets.cs
│   │   ├── MessageOnlyHost.cs
│   │   └── TrayIconManager.cs
│   ├── Deckle.Shell.TrayMenu/
│   │   ├── Interop/
│   │   │   └── TrayMenuNativeMethods.cs
│   │   ├── Themes/
│   │   │   └── TrayMenu.xaml
│   │   ├── CLAUDE.md  — [agent-instructions] WinUI 3 tray context menu — the carrier-window pattern and the DWM pitfalls it…
│   │   ├── Deckle.Shell.TrayMenu.csproj
│   │   ├── DeckleShellTrayMenuSource.cs
│   │   ├── JOURNAL.md  — [module-journal] Dated journal for Deckle.Shell.TrayMenu — in-flight diagnostics, observations m…
│   │   ├── TrayContextMenuHost.cs
│   │   └── TraySwitchMenuItem.cs
│   ├── Deckle.Transcription/
│   │   ├── Corpus/
│   │   │   ├── CorpusTier.cs
│   │   │   ├── PromptTemplateHash.cs
│   │   │   └── WavCorpusWriter.cs
│   │   ├── Engine/
│   │   │   ├── IAsrBackend.cs
│   │   │   ├── PipelineProduction.cs
│   │   │   ├── TextMetrics.cs
│   │   │   ├── TranscriptionEngine.cs
│   │   │   ├── TranscriptionEngine.Lifecycle.cs
│   │   │   ├── TranscriptionEngine.MonolithicPipeline.cs
│   │   │   ├── TranscriptionEngine.Pipeline.cs
│   │   │   ├── TranscriptionEngine.StateMachine.cs
│   │   │   └── TranscriptionEngine.StreamingPipeline.cs
│   │   ├── Setup/
│   │   │   ├── Downloader.cs
│   │   │   └── ModelEntry.cs
│   │   ├── Streaming/
│   │   │   ├── EnergySegmenter.cs
│   │   │   ├── EnergySegmenterSettings.cs
│   │   │   └── Utterance.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── ViewModels/
│   │   │   └── WhisperViewModel.cs
│   │   ├── CLAUDE.md  — claude-deckle-transcription [agent-instructions] Doctrine for Deckle.Transcription, the backend-agnostic transcription orchestra…
│   │   ├── Deckle.Transcription.csproj
│   │   ├── DeckleWhispSource.cs
│   │   ├── DeckleWhispSource.Delivery.cs
│   │   ├── DeckleWhispSource.PipelineCompletion.cs
│   │   ├── DeckleWhispSource.Preprocessing.cs
│   │   ├── DeckleWhispSource.Telemetry.cs
│   │   ├── DeckleWhispSource.Transcribe.cs
│   │   ├── DeckleWhispSource.Ui.cs
│   │   ├── DeckleWhispSource.WarmupModel.cs
│   │   ├── ITranscriptionEngineHost.cs
│   │   ├── TranscriptionSettings.cs
│   │   ├── TranscriptionSettingsService.cs
│   │   ├── WhisperPage.xaml
│   │   └── WhisperPage.xaml.cs
│   ├── Deckle.Transcription.Whisper/
│   │   ├── Engine/
│   │   │   └── WhisperParamsMapper.cs
│   │   ├── Pinvoke/
│   │   │   ├── WhisperPInvoke.cs
│   │   │   └── WhisperStructs.cs
│   │   ├── Setup/
│   │   │   ├── NativeRuntime.cs
│   │   │   └── SpeechModels.cs
│   │   ├── CLAUDE.md  — claude-deckle-transcription-whisper [agent-instructions] Doctrine for Deckle.Transcription.Whisper, the IAsrBackend implementation via w…
│   │   ├── Deckle.Transcription.Whisper.csproj
│   │   ├── RepetitionDetector.cs
│   │   └── WhisperBackend.cs
│   └── Deckle.Vision/
│       ├── CapturedFrame.cs
│       ├── CLAUDE.md  — claude-deckle-vision [agent-instructions] Doctrine for Deckle.Vision, the DXGI screen capture and frame analysis module (…
│       ├── Deckle.Vision.csproj
│       ├── DeckleVisionSource.cs
│       ├── FrameAnalysisHint.cs
│       ├── FrameSampler.cs
│       ├── FrameSampler.Process.cs
│       ├── FrameSampler.Readback.cs
│       ├── FrameSampler.Resources.cs
│       ├── IFrameAnalyzer.cs
│       ├── JOURNAL.md  — journal-vision [module-journal] Journal daté du module Deckle.Vision — diagnostics de la capture DXGI Output Du…
│       ├── SampledFrame.cs
│       ├── ScreenCaptureInterop.cs
│       ├── ScreenCaptureInterop.D3D11.cs
│       ├── ScreenCaptureInterop.Direct3D.cs
│       ├── ScreenCaptureInterop.Duplication.cs
│       ├── ScreenCaptureInterop.Hdr.cs
│       ├── ScreenCaptureService.cs
│       ├── ScreenCaptureService.Dispose.cs
│       ├── ScreenCaptureService.Lifecycle.cs
│       ├── ScreenCaptureService.Loop.cs
│       └── ScreenCaptureService.Recovery.cs
├── tests/
│   ├── Deckle.Audio.Tests/
│   │   ├── Deckle.Audio.Tests.csproj
│   │   ├── MicLevelCheckTests.cs
│   │   ├── MicrophoneTelemetryCalculatorTests.cs
│   │   └── TranscriptionPreprocessorTests.cs
│   ├── Deckle.Chrono.Tests/
│   │   ├── ChronoFormatterTests.cs
│   │   ├── Deckle.Chrono.Tests.csproj
│   │   └── DeckleChronoSourceTests.cs
│   ├── Deckle.Diagnostics.Telemetry.Tests/
│   │   ├── Deckle.Diagnostics.Telemetry.Tests.csproj
│   │   └── TelemetryListenerBootstrapTests.cs
│   ├── Deckle.Diagnostics.Tests/
│   │   ├── Deckle.Diagnostics.Tests.csproj
│   │   ├── DeckleCancellationSourceTests.cs
│   │   ├── DeckleNetworkSourceTests.cs
│   │   ├── DeckleResourceSourceTests.cs
│   │   ├── DeckleThemeSourceTests.cs
│   │   ├── DeckleThreadingSourceTests.cs
│   │   ├── DeckleWindowingSourceTests.cs
│   │   └── JsonlEventListenerRotationTests.cs
│   ├── Deckle.Hud.Tests/
│   │   ├── Deckle.Hud.Tests.csproj
│   │   ├── DeckleHudSourceTests.cs
│   │   └── ProximityRollupAggregatorTests.cs
│   ├── Deckle.Lighting.Ambient.Tests/
│   │   ├── AmbientHueEchoClassifierTests.cs
│   │   ├── Deckle.Lighting.Ambient.Tests.csproj
│   │   └── DeckleAmbientSourceTests.cs
│   ├── Deckle.Shell.Tests/
│   │   ├── Deckle.Shell.Tests.csproj
│   │   └── DispatcherQueueExtensionsTests.cs
│   ├── Deckle.TestSupport/
│   │   ├── Deckle.TestSupport.csproj
│   │   ├── EventArgsExtensions.cs
│   │   ├── TestEventListener.cs
│   │   ├── WindowsAppSdkBootstrap.cs
│   │   └── WindowsAppSdkModuleInitializer.cs
│   ├── Deckle.Transcription.Tests/
│   │   ├── Deckle.Transcription.Tests.csproj
│   │   ├── EnergySegmenterTests.cs
│   │   └── StreamingBackendAudioTests.cs
│   ├── Deckle.Vision.Tests/
│   │   ├── Deckle.Vision.Tests.csproj
│   │   └── DeckleVisionSourceTests.cs
│   └── Directory.Build.props
├── .editorconfig
├── .gitattributes
├── .gitignore
├── AGENTS.md  — codex-deckle-bridge [agent-instructions] Minimal Codex bridge for Deckle. Claude-maintained files remain the source of t…
├── CHANGELOG.md
├── CLAUDE.md  — claude-deckle-root [agent-instructions] Root agent-instructions for Deckle (local Windows utility, .NET 10 / WinUI 3).…
├── CONTEXT.md  — context-deckle [agent-instructions] Project glossary for Deckle — shared vocabulary, term-of-art definitions, namin…
├── CONTRIBUTING.md
├── deckle.code-workspace
├── Deckle.Tests.sln
├── Directory.Build.props
├── Directory.Packages.props
├── INSTALL.md
├── JOURNAL.md  — journal-deckle [project-journal] Journal daté du projet Deckle : avancées techniques validées, observations en c…
├── LICENSE
├── NOTICE.md
├── README.md
├── SECURITY.md
└── TREE.md
```