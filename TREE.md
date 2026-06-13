# Arborescence — Deckle
_Généré depuis `git ls-files` — ne pas éditer à la main._

```
├── .claude/
│   └── skills/
│       ├── deckle-commits/
│       │   └── SKILL.md  — deckle-commits [skill] Commit grain and the few deviations from the universal convention. Invoke befor…
│       ├── deckle-interface/
│       │   └── SKILL.md  — deckle-interface [skill] Render the visual interface at Microsoft first-party level — native primitives,…
│       ├── deckle-journal/
│       │   ├── examples.md
│       │   └── SKILL.md  — deckle-journal [skill] How to write the JOURNAL.md. Invoke when recording a finding, or a small decisi…
│       ├── deckle-logging/
│       │   └── SKILL.md  — deckle-logging [skill] What to observe in code, and how to write it readable and actionable. Invoke be…
│       ├── deckle-modularite/
│       │   └── SKILL.md  — deckle-modularite [skill] When and along which lines to separate code into modules and files. Invoke befo…
│       ├── deckle-nomenclature/
│       │   └── SKILL.md  — deckle-nomenclature [skill] One normalized way to name files, folders, symbols, resources and providers. In…
│       ├── deckle-settings-ux/
│       │   └── SKILL.md  — deckle-settings-ux [skill] What to expose in settings surfaces and how to organize it. Invoke before expos…
│       ├── deckle-testing/
│       │   └── SKILL.md  — deckle-testing [skill] Testing posture — test behavior not implementation, stay sober, grow coverage p…
│       ├── deckle-versioning/
│       │   └── SKILL.md  — deckle-versioning [skill] How versions are numbered and the changelog written. Invoke before cutting a ve…
│       ├── microsoft-docs/
│       │   └── SKILL.md  — microsoft-docs Query official Microsoft documentation to find concepts, tutorials, and code ex…
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
│       └── winui-app/
│           ├── agents/
│           │   └── openai.yaml
│           ├── assets/
│           │   └── winui.png
│           ├── references/
│           │   ├── _sections.md
│           │   ├── accessibility-input-and-localization.md
│           │   ├── build-run-and-launch-verification.md
│           │   ├── community-toolkit-controls-and-helpers.md
│           │   ├── controls-layout-and-adaptive-ui.md
│           │   ├── foundation-environment-audit-and-remediation.md
│           │   ├── foundation-setup-and-project-selection.md
│           │   ├── foundation-template-first-recovery.md
│           │   ├── foundation-winui-app-structure.md
│           │   ├── motion-animations-and-polish.md
│           │   ├── performance-diagnostics-and-responsiveness.md
│           │   ├── sample-source-map.md
│           │   ├── shell-navigation-and-windowing.md
│           │   ├── styling-theming-materials-and-icons.md
│           │   ├── testing-debugging-and-review-checklists.md
│           │   └── windows-app-sdk-lifecycle-notifications-and-deployment.md
│           ├── config.yaml
│           ├── LICENSE.txt
│           └── SKILL.md  — winui-app Bootstrap, develop, and design modern WinUI 3 desktop applications with C# and…
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
│   ├── CLAUDE.md  — [agent-instructions] Autonomous box measuring ASR backend quality and performance on private corpora…
│   ├── Directory.Build.props
│   ├── JOURNAL.md  — [module-journal] Dated findings from the Voxtral/ASR benchmark spike — backends, quantization, a…
│   ├── pregenerate_groundtruth_gemini.py
│   └── README.md  — readme-benchmark [module-readme] Human-facing entry point for the benchmark/ suite — what it is, how a bench is…
├── docs/
│   ├── adr/
│   │   ├── 0000-template.md  — [adr] Fill-in template for a Deckle ADR — copy it to start one, record no decision he…
│   │   └── CLAUDE.md  — [agent-instructions] Why Deckle keeps ADRs and the questions that gate one. Read before writing or p…
│   ├── research/
│   │   ├── 2026-06-12--notifications-catalogue.md
│   │   └── research--system-autocorrect--2026-06-12.md
│   └── pipeline-hud-sync.md  — [reference] Carte du pipeline de transcription et de la synchronisation HUD — étapes, threa…
├── scripts/
│   ├── hooks/
│   │   ├── pre-commit
│   │   └── update-tree.ps1
│   ├── lib/
│   │   ├── _menu.psm1
│   │   ├── bootstrap-dev-env.ps1
│   │   ├── build-run.ps1
│   │   ├── changelog.ps1
│   │   ├── clean.ps1
│   │   ├── fetch-autocorrect-data.ps1
│   │   ├── install-hooks.ps1
│   │   ├── publish-app.ps1
│   │   ├── publish-native-runtime.ps1
│   │   ├── setup-assets.ps1
│   │   └── stats.ps1
│   ├── deckle.ps1
│   └── README.md  — readme-scripts [module-readme] Dev workflows entry point for Deckle: the deckle.ps1 menu, the worker scripts u…
├── src/
│   ├── Deckle.Anytype/
│   │   ├── Api/
│   │   │   ├── AnytypeApiClient.cs
│   │   │   └── AnytypeCredentials.cs
│   │   ├── Gestures/
│   │   │   ├── ProjectGestures.cs
│   │   │   ├── QueryGestures.cs
│   │   │   ├── Resolution.cs
│   │   │   ├── SessionGestures.cs
│   │   │   └── TaskGestures.cs
│   │   ├── Schema/
│   │   │   └── DevSpace.cs
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Anytype.csproj
│   │   ├── DeckleAnytypeSource.cs
│   │   └── JOURNAL.md  — [module-journal] Dated decisions and findings for the Anytype MCP server — founding grilling, AP…
│   ├── Deckle.Anytype.Mcp/
│   │   ├── JsonRpc/
│   │   │   ├── JsonRpcEndpoint.cs
│   │   │   └── McpServer.cs
│   │   ├── Tools/
│   │   │   ├── ToolCatalog.cs
│   │   │   └── ToolDescriptor.cs
│   │   ├── Deckle.Anytype.Mcp.csproj
│   │   ├── Program.cs
│   │   └── StderrEventListener.cs
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
│   │   ├── App.Trackpad.cs
│   │   ├── App.Windows.cs
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── CLAUDE.md  — [agent-instructions] WinUI 3 host composing the Deckle.* modules — the composition boundary, the OnL…
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
│   │   ├── CLAUDE.md  — [agent-instructions] Audio module — the home for capturing and analyzing sound: microphone capture,…
│   │   ├── Deckle.Audio.csproj
│   │   ├── DeckleAudioSource.cs
│   │   ├── IAudioRecordingHost.cs
│   │   ├── MicLevelTester.cs
│   │   ├── MicrophoneCapture.cs
│   │   └── ProbeResult.cs
│   ├── Deckle.Catalog/
│   │   ├── Themes/
│   │   │   └── Icons.xaml
│   │   ├── CLAUDE.md  — [agent-instructions] UI resource catalog — localized strings (Loc / x:Uid) and Segoe Fluent glyphs,…
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
│   │   ├── Io/
│   │   │   └── Downloader.cs
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
│   │   ├── LogWindowVisibilityMode.cs
│   │   └── StreamingCaptureGate.cs
│   ├── Deckle.Diagnostics.Telemetry/
│   │   ├── CLAUDE.md  — [agent-instructions] Structured JSONL persistence and consent gates.
│   │   ├── Deckle.Diagnostics.Telemetry.csproj
│   │   ├── TelemetryListenerBootstrap.cs
│   │   ├── TelemetrySettings.cs
│   │   └── TelemetrySettingsService.cs
│   ├── Deckle.Hud/
│   │   ├── CLAUDE.md  — [agent-instructions] HUD overlay surface — non-focusable, click-through, always-on-top windows that…
│   │   ├── Deckle.Hud.csproj
│   │   ├── DeckleHudSource.cs
│   │   ├── HudChrono.Clock.cs
│   │   ├── HudChrono.Reveal.cs
│   │   ├── HudChrono.Stroke.cs
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
│   │   ├── JOURNAL.md  — [module-journal] Diagnosis notes, render doctrine, and deferred work for Deckle.Hud — read on de…
│   │   ├── MessageKind.cs
│   │   ├── ProximityRollupAggregator.cs
│   │   └── WindowSlideAnimator.cs
│   ├── Deckle.Inference.Onnx/
│   │   ├── CLAUDE.md  — [agent-instructions] ONNX Runtime CPU inference substrate — isolates the OnnxRuntime dependency behi…
│   │   ├── Deckle.Inference.Onnx.csproj
│   │   └── OnnxModelSession.cs
│   ├── Deckle.Input/
│   │   ├── Injection/
│   │   │   └── MouseInjector.cs
│   │   ├── Interop/
│   │   │   ├── HidInterop.cs
│   │   │   ├── RawInputInterop.cs
│   │   │   ├── SendInputInterop.cs
│   │   │   └── WinEventInterop.cs
│   │   ├── Keyboard/
│   │   │   ├── IKeyboardInputHost.cs
│   │   │   ├── KeyboardInputHost.cs
│   │   │   └── KeyboardKeyEvent.cs
│   │   ├── Telemetry/
│   │   │   └── ContactFrameRecorder.cs
│   │   ├── Touchpad/
│   │   │   ├── ContactFrame.cs
│   │   │   ├── ContactFrameAssembler.cs
│   │   │   ├── TouchpadCapabilities.cs
│   │   │   ├── TouchpadContact.cs
│   │   │   ├── TouchpadParser.cs
│   │   │   └── TouchpadReport.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Input support module — Raw Input host, Precision Touchpad HID parsing, contact…
│   │   ├── Deckle.Input.csproj
│   │   ├── DeckleInputSource.cs
│   │   └── RawInputHost.cs
│   ├── Deckle.Input.Autocorrect/
│   │   ├── Data/
│   │   │   ├── lexicon-en.tsv.gz
│   │   │   ├── lexicon-fr.tsv.gz
│   │   │   └── pair-bigrams-fr.tsv.gz
│   │   ├── Engine/
│   │   │   ├── AutocorrectEngine.cs
│   │   │   ├── BigramPairDisambiguator.cs
│   │   │   ├── CasePattern.cs
│   │   │   ├── CorrectionDecision.cs
│   │   │   ├── DiacriticsRestorer.cs
│   │   │   ├── IPairDisambiguator.cs
│   │   │   ├── PairModelTrainer.cs
│   │   │   └── RestorerOptions.cs
│   │   ├── Evaluation/
│   │   │   ├── RestorationEvaluator.cs
│   │   │   └── RestorationReport.cs
│   │   ├── Injection/
│   │   │   ├── InjectionPlan.cs
│   │   │   ├── ITextInjector.cs
│   │   │   └── TextInjector.cs
│   │   ├── Interop/
│   │   │   └── KeyboardStateInterop.cs
│   │   ├── Learning/
│   │   │   ├── IPersonalLexicon.cs
│   │   │   ├── PersonalDictionary.cs
│   │   │   └── PersonalDictionaryData.cs
│   │   ├── Lexicon/
│   │   │   ├── AccentFolding.cs
│   │   │   ├── AccentIndex.cs
│   │   │   ├── AccentVariant.cs
│   │   │   └── FrequencyLexicon.cs
│   │   ├── Surfaces/
│   │   │   ├── FocusedSurface.cs
│   │   │   ├── ISurfaceProber.cs
│   │   │   └── SurfaceProber.cs
│   │   ├── Tracking/
│   │   │   ├── KeyDecoder.cs
│   │   │   ├── Keystroke.cs
│   │   │   ├── TypedWordTracker.cs
│   │   │   ├── WordBoundaries.cs
│   │   │   └── WordCommit.cs
│   │   ├── AutocorrectSettings.cs
│   │   ├── AutocorrectSettingsService.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Machine-wide autocorrect domain module — typed-word tracking, conservative corr…
│   │   ├── Deckle.Input.Autocorrect.csproj
│   │   ├── DeckleAutocorrectSource.cs
│   │   └── JOURNAL.md  — [module-journal] Dated decisions and findings for Deckle.Input.Autocorrect — founding choices, c…
│   ├── Deckle.Input.Autocorrect.Cli/
│   │   ├── Commands/
│   │   │   ├── BuildDataCommand.cs
│   │   │   ├── CliArgs.cs
│   │   │   ├── DataSet.cs
│   │   │   ├── DictCommand.cs
│   │   │   ├── EnrollCommand.cs
│   │   │   ├── EvalCommand.cs
│   │   │   ├── InjectCommand.cs
│   │   │   ├── RepoPaths.cs
│   │   │   ├── RunCommand.cs
│   │   │   ├── TrainPairsCommand.cs
│   │   │   └── WatchCommand.cs
│   │   ├── Deckle.Input.Autocorrect.Cli.csproj
│   │   └── Program.cs
│   ├── Deckle.Input.Trackpad/
│   │   ├── Acts/
│   │   │   ├── ConnectionRepair.cs
│   │   │   ├── repair-trackpad-connection.ps1
│   │   │   └── WindowsGestureNeutralizer.cs
│   │   ├── Engine/
│   │   │   ├── ThreeFingerDragRecognizer.cs
│   │   │   └── TrackpadEngine.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── TrackpadPage.xaml
│   │   │   └── TrackpadPage.xaml.cs
│   │   ├── ViewModels/
│   │   │   └── TrackpadViewModel.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Trackpad domain module — three-finger drag recognizer and engine, module settin…
│   │   ├── Deckle.Input.Trackpad.csproj
│   │   ├── DeckleTrackpadSource.cs
│   │   ├── TrackpadSettings.cs
│   │   └── TrackpadSettingsService.cs
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
│   │   ├── CLAUDE.md  — [agent-instructions] NativeAOT console stub that downloads, installs, and uninstalls Deckle per-user…
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
│   │   ├── CLAUDE.md  — [agent-instructions] Generalist light-output driver — the ILightOutput abstraction and the REST Hue…
│   │   ├── Deckle.Lighting.csproj
│   │   ├── DeckleLightingSource.cs
│   │   ├── ILightOutput.cs
│   │   ├── JOURNAL.md  — [module-journal] Color-science decisions and the Night Owl gamut bug for Deckle.Lighting — read…
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
│   │   ├── CLAUDE.md  — [agent-instructions] Dev-only tuning sandbox — live-adjust the running pipelines without a rebuild,…
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
│   │   ├── CLAUDE.md  — [agent-instructions] Settings shell — aggregates module-owned pages in a NavigationView, owns non-mo…
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
│   │   ├── CLAUDE.md  — [agent-instructions] First-run wizard provisioning ASR runtimes and models — owns the flow, delegate…
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
│   │   ├── CLAUDE.md  — [agent-instructions] System shell module — the low-level OS primitives (hotkeys, tray, autostart, me…
│   │   ├── Deckle.Shell.csproj
│   │   ├── DeckleShellSource.cs
│   │   ├── DispatcherQueueExtensions.cs
│   │   ├── ElevatedStartupService.cs
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
│   │   ├── JOURNAL.md  — [module-journal] Dated diagnostics for Deckle.Shell.TrayMenu — the tray-menu density, gap, and f…
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
│   │   ├── CLAUDE.md  — [agent-instructions] Backend-agnostic transcription orchestrator — the IAsrBackend boundary, the mod…
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
│   │   ├── JOURNAL.md  — [module-journal] Diagnosis notes and kept decisions for Deckle.Transcription — read on demand wh…
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
│   │   ├── CLAUDE.md  — [agent-instructions] whisper.cpp ASR backend (IAsrBackend) — native log compaction, the whisper repe…
│   │   ├── Deckle.Transcription.Whisper.csproj
│   │   ├── RepetitionDetector.cs
│   │   └── WhisperBackend.cs
│   ├── Deckle.Vad/
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Vad.csproj
│   │   ├── DeckleVadSource.cs
│   │   ├── SileroSpeechTimestamps.cs
│   │   ├── SileroVad.cs
│   │   ├── SileroVadModel.cs
│   │   ├── SileroVadOptions.cs
│   │   ├── SpeechSegment.cs
│   │   ├── SpeechTrimResult.cs
│   │   └── VadService.cs
│   └── Deckle.Vision/
│       ├── CapturedFrame.cs
│       ├── CLAUDE.md  — [agent-instructions] Screen-capture and frame-analysis module — DXGI Output Duplication, the transie…
│       ├── Deckle.Vision.csproj
│       ├── DeckleVisionSource.cs
│       ├── FrameAnalysisHint.cs
│       ├── FrameSampler.cs
│       ├── FrameSampler.Process.cs
│       ├── FrameSampler.Readback.cs
│       ├── FrameSampler.Resources.cs
│       ├── IFrameAnalyzer.cs
│       ├── JOURNAL.md  — [module-journal] Dated diagnostics for Deckle.Vision — the DXGI capture freeze on an HDR toggle…
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
│   ├── Deckle.Anytype.Mcp.Tests/
│   │   ├── Deckle.Anytype.Mcp.Tests.csproj
│   │   ├── McpServerTests.cs
│   │   └── ToolCatalogTests.cs
│   ├── Deckle.Anytype.Tests/
│   │   ├── Deckle.Anytype.Tests.csproj
│   │   ├── DevSpaceTests.cs
│   │   ├── ProjectGesturesTests.cs
│   │   ├── SessionGesturesTests.cs
│   │   └── TaskGesturesTests.cs
│   ├── Deckle.Audio.Tests/
│   │   ├── Deckle.Audio.Tests.csproj
│   │   ├── MicLevelCheckTests.cs
│   │   ├── MicrophoneTelemetryCalculatorTests.cs
│   │   └── TranscriptionPreprocessorTests.cs
│   ├── Deckle.Chrono.Tests/
│   │   ├── ChronoFormatterTests.cs
│   │   ├── ChronoTimerTests.cs
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
│   ├── Deckle.Input.Autocorrect.Tests/
│   │   ├── AccentFoldingTests.cs
│   │   ├── AccentIndexTests.cs
│   │   ├── BigramPairDisambiguatorTests.cs
│   │   ├── CasePatternTests.cs
│   │   ├── Deckle.Input.Autocorrect.Tests.csproj
│   │   ├── DiacriticsRestorerTests.cs
│   │   ├── FrequencyLexiconTests.cs
│   │   ├── InjectionPlanTests.cs
│   │   ├── KeyDecoderTests.cs
│   │   ├── PairModelTrainerTests.cs
│   │   ├── PersonalDictionaryTests.cs
│   │   ├── RestorationEvaluatorTests.cs
│   │   ├── TypedWordTrackerTests.cs
│   │   └── WordBoundariesTests.cs
│   ├── Deckle.Input.Tests/
│   │   ├── ContactFrameAssemblerTests.cs
│   │   └── Deckle.Input.Tests.csproj
│   ├── Deckle.Input.Trackpad.Tests/
│   │   ├── Deckle.Input.Trackpad.Tests.csproj
│   │   └── ThreeFingerDragRecognizerTests.cs
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
│   ├── Deckle.Transcription.Whisper.Tests/
│   │   ├── Deckle.Transcription.Whisper.Tests.csproj
│   │   └── RepetitionDetectorTests.cs
│   ├── Deckle.Vad.Tests/
│   │   ├── Deckle.Vad.Tests.csproj
│   │   └── SileroSpeechTimestampsTests.cs
│   ├── Deckle.Vision.Tests/
│   │   ├── Deckle.Vision.Tests.csproj
│   │   └── DeckleVisionSourceTests.cs
│   └── Directory.Build.props
├── .editorconfig
├── .gitattributes
├── .gitignore
├── AGENTS.md  — [agent-instructions] Minimal Codex bridge for Deckle — Claude-maintained files remain the source of…
├── CHANGELOG.md
├── CLAUDE.md  — [agent-instructions] Root agent-instructions for Deckle — identity, hard rules, posture, and where t…
├── CONTEXT.md  — context-deckle [agent-instructions] Project glossary for Deckle — shared vocabulary, term-of-art definitions, namin…
├── deckle.code-workspace
├── Deckle.Tests.sln
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── JOURNAL.md  — [project-journal] Dated project notes for Deckle — cross-cutting findings too dated for a CLAUDE.…
├── LICENSE
├── NOTICE.md
├── README.md
└── TREE.md
```