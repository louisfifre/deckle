# Arborescence — Deckle
_Mise à jour : 2026-05-25 21:24 — source : `git ls-files`_

```
├── .claude/
│   ├── agents/
│   │   ├── expert-dotnet-software-engineer.agent.md  — Provide expert .NET software engineering guidance using modern software design…
│   │   ├── plan.agent.md  — Strategic planning and architecture assistant focused on thoughtful analysis be…
│   │   └── winui3-expert.agent.md  — Expert agent for WinUI 3 and Windows App SDK development. Prevents common UWP-t…
│   └── skills/
│       ├── deckle-commits/
│       │   └── SKILL.md  — Commit doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries the…
│       ├── deckle-logging/
│       │   ├── SKILL.md  — Observability doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carr…
│       │   └── taxonomy.md
│       ├── deckle-modularite/
│       │   └── SKILL.md  — Modularity and splitting doctrine for the Deckle project (Windows .NET 10 / Win…
│       ├── deckle-nomenclature/
│       │   ├── SKILL.md  — Naming doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries cas…
│       │   └── taxonomie.md
│       ├── deckle-refonte/
│       │   └── SKILL.md  — Panoramic refonte coordination skill for the Deckle project (Windows .NET 10 /…
│       ├── deckle-settings-ux/
│       │   └── SKILL.md  — User experience doctrine for the settings surfaces of the Deckle project (Windo…
│       ├── deckle-testing/
│       │   └── SKILL.md  — Testing doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries th…
│       ├── deckle-workflow/
│       │   └── SKILL.md  — Workflow doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries h…
│       ├── microsoft-docs/
│       │   └── SKILL.md  — Query official Microsoft documentation to find concepts, tutorials, and code ex…
│       ├── save-context/
│       │   ├── format.md
│       │   └── SKILL.md  — [skill] When in-session information has durable value (a tranched decision, learned tec…
│       ├── spawn-tasks/
│       │   └── SKILL.md  — [skill] When Louis invokes you to spin off parallel topics that surfaced in conversatio…
│       ├── tdd/
│       │   ├── deep-modules.md
│       │   ├── interface-design.md
│       │   ├── mocking.md
│       │   ├── refactoring.md
│       │   ├── SKILL.md  — Test-driven development with red-green-refactor loop. Use when user wants to bu…
│       │   └── tests.md
│       └── write-a-skill/
│           └── SKILL.md  — Create new agent skills with proper structure, progressive disclosure, and bund…
├── .vscode/
│   └── launch.json
├── benchmark/
│   ├── benches/
│   │   └── voxtral-poc/
│   │       ├── bench.py
│   │       └── README.md
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
│   │   │   ├── voxtral_chat.py
│   │   │   ├── voxtral_transcribe.py
│   │   │   └── whisper_cpp.py
│   │   ├── __init__.py
│   │   ├── _base_compat.py
│   │   ├── corpus.py
│   │   ├── env.py
│   │   └── event_log.py
│   ├── prompts/
│   │   ├── judges/
│   │   │   ├── claude_per_row.md
│   │   │   ├── gemini_per_row.md
│   │   │   └── legacy_ollama_judge.md
│   │   ├── transcription/
│   │   │   ├── voxtral_chat.toml
│   │   │   └── voxtral_transcribe.toml
│   │   └── whisper_initial.txt
│   ├── CLAUDE.md
│   └── README.md
├── docs/
│   ├── adr/
│   │   ├── 0001-record-architecture-decisions.md
│   │   ├── 0002-reporter-msix-rester-unpackaged.md
│   │   ├── 0003-distribution-source-only-pour-l-app.md
│   │   ├── 0004-lazy-windows-pour-stabilite-au-boot.md
│   │   ├── 0005-adoption-eventsource-pour-l-observabilite.md
│   │   ├── 0006-structure-diagnostics-parent-logging-telemetry-enfants.md
│   │   ├── 0007-rester-sur-whisper-cpp-surveiller-voxtral.md
│   │   ├── 0008-rester-sur-vulkan-pour-backends-gpu-natifs.md
│   │   ├── 0009-assets-resolus-via-userdataroot.md
│   │   ├── 0010-backend-asr-pluggable-via-iasrbackend.md
│   │   ├── 0011-corpus-normalise-comme-dataset-ml.md
│   │   ├── 0012-adoption-de-dotnet-build-et-dotnet-test.md
│   │   ├── 0013-format-canonique-des-artefacts-agents.md  — [adr] Acte le format normatif des artefacts agents Deckle : langue anglaise par défau…
│   │   └── 0014-poc-evaluation-voxtral.md
│   └── research/
│       ├── research--hdr-graphics-capture--2026-05-15.md
│       ├── research--hue-entertainment-v2--2026-05-15.md
│       ├── research--hyperhdr-interpolators--2026-05-15.md
│       └── research--inventaire-observabilite-eventsource--2026-05-24.md
├── scripts/
│   ├── hooks/
│   │   └── pre-commit
│   ├── lib/
│   │   ├── _menu.psm1
│   │   ├── bootstrap-dev-env.ps1
│   │   ├── build-run.ps1
│   │   ├── clean.ps1
│   │   ├── publish-native-runtime.ps1
│   │   ├── setup-assets.ps1
│   │   └── stats.ps1
│   ├── deckle.ps1
│   ├── install-hooks.ps1
│   ├── README.md
│   └── update-tree.ps1
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
│   │   │   └── LogEntryTemplateSelector.cs
│   │   ├── Engine/
│   │   │   ├── AppAmbientEngineHost.cs
│   │   │   └── AppTranscriptionEngineHost.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── app.manifest
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.App, the WinUI 3 host module that composes all Deckle.* mod…
│   │   ├── Deckle.App.csproj
│   │   ├── global.json
│   │   ├── LogWindow.xaml
│   │   └── LogWindow.xaml.cs
│   ├── Deckle.Audio/
│   │   ├── Internal/
│   │   │   ├── PcmConversion.cs
│   │   │   └── WaveInLoop.cs
│   │   ├── Telemetry/
│   │   │   ├── MicrophoneCalibrationCalculator.cs
│   │   │   ├── MicrophoneTelemetryCalculator.cs
│   │   │   └── MicrophoneTelemetryPayload.cs
│   │   ├── AudioLevelMapper.cs
│   │   ├── CaptureResult.cs
│   │   ├── CaptureSettings.cs
│   │   ├── CaptureSettingsService.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Audio, the microphone capture and audio telemetry module. R…
│   │   ├── Deckle.Audio.csproj
│   │   ├── DeckleAudioSource.cs
│   │   ├── IAudioRecordingHost.cs
│   │   ├── MicrophoneCapture.cs
│   │   └── ProbeResult.cs
│   ├── Deckle.Catalog/
│   │   ├── Themes/
│   │   │   └── Icons.xaml
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Catalog, the UI resource catalog module (localized strings…
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
│   │   │   ├── HudComposition.cs
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
│   │   │   ├── LogWindowEventListener.cs
│   │   │   └── RoutedJsonlEventListener.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Diagnostics, the observability foundation module. Read befo…
│   │   ├── Deckle.Diagnostics.csproj
│   │   ├── DeckleEventSource.cs
│   │   ├── EventEntry.cs
│   │   ├── IHudFeedbackSink.cs
│   │   ├── ILogWindowSink.cs
│   │   └── Keywords.cs
│   ├── Deckle.Diagnostics.Logging/
│   │   ├── AmbientCaptureGate.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Diagnostics.Logging (live logging settings and AmbientCaptu…
│   │   ├── Deckle.Diagnostics.Logging.csproj
│   │   ├── LoggingSettings.cs
│   │   └── LoggingSettingsService.cs
│   ├── Deckle.Diagnostics.Telemetry/
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Diagnostics.Telemetry (JSONL telemetry listeners and user g…
│   │   ├── Deckle.Diagnostics.Telemetry.csproj
│   │   ├── TelemetryListenerBootstrap.cs
│   │   ├── TelemetrySettings.cs
│   │   └── TelemetrySettingsService.cs
│   ├── Deckle.Hud/
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Hud, the HUD window, overlay manager, and composite shadow…
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
│   │   └── WindowSlideAnimator.cs
│   ├── Deckle.Lighting/
│   │   ├── Hue/
│   │   │   ├── HueBridge.cs
│   │   │   ├── HueBridgeClient.cs
│   │   │   ├── HueColorMath.cs
│   │   │   ├── HueDiscovery.cs
│   │   │   ├── HueEntertainmentArea.cs
│   │   │   ├── HueEventStreamModels.cs
│   │   │   ├── HueGroup.cs
│   │   │   ├── HueLight.cs
│   │   │   └── HueRestLightOutput.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Lighting, the Philips Hue driver (REST CLIP v1/v2) and colo…
│   │   ├── Deckle.Lighting.csproj
│   │   ├── DeckleLightingSource.cs
│   │   ├── ILightOutput.cs
│   │   ├── LightColor.cs
│   │   └── LightDescriptor.cs
│   ├── Deckle.Lighting.Ambient/
│   │   ├── Engine/
│   │   │   ├── AmbientEngine.cs
│   │   │   ├── AmbientEngineState.cs
│   │   │   ├── AmbientModePresets.cs
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
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Playground, the dev-only tuning and diagnostics sandbox sur…
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
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Settings, the settings UI shell and per-module persistence…
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
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Setup, the first-run wizard and provisioning primitives (na…
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
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Shell, the system shell module (message-only host, tray, gl…
│   │   ├── Deckle.Shell.csproj
│   │   ├── DeckleShellSource.cs
│   │   ├── DispatcherQueueExtensions.cs
│   │   ├── HotkeyManager.cs
│   │   ├── IconAssets.cs
│   │   ├── MessageOnlyHost.cs
│   │   └── TrayIconManager.cs
│   ├── Deckle.Transcription/
│   │   ├── Corpus/
│   │   │   ├── CorpusTier.cs
│   │   │   ├── PromptTemplateHash.cs
│   │   │   └── WavCorpusWriter.cs
│   │   ├── Engine/
│   │   │   ├── IAsrBackend.cs
│   │   │   ├── TextMetrics.cs
│   │   │   ├── TranscriptionEngine.cs
│   │   │   ├── TranscriptionEngine.Lifecycle.cs
│   │   │   ├── TranscriptionEngine.Pipeline.cs
│   │   │   └── TranscriptionEngine.StateMachine.cs
│   │   ├── Setup/
│   │   │   ├── Downloader.cs
│   │   │   └── ModelEntry.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── ViewModels/
│   │   │   └── WhisperViewModel.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Transcription, the backend-agnostic transcription orchestra…
│   │   ├── Deckle.Transcription.csproj
│   │   ├── DeckleWhispSource.cs
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
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Transcription.Whisper, the IAsrBackend implementation via w…
│   │   ├── Deckle.Transcription.Whisper.csproj
│   │   ├── RepetitionDetector.cs
│   │   └── WhisperBackend.cs
│   └── Deckle.Vision/
│       ├── CapturedFrame.cs
│       ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Vision, the screen capture and frame analysis module (Windo…
│       ├── Deckle.Vision.csproj
│       ├── DeckleVisionSource.cs
│       ├── FrameAnalysisHint.cs
│       ├── FrameSampler.cs
│       ├── IFrameAnalyzer.cs
│       ├── SampledFrame.cs
│       ├── ScreenCaptureInterop.cs
│       └── ScreenCaptureService.cs
├── tests/
│   └── Deckle.Tests/
│       ├── Chrono/
│       │   ├── ChronoFormatterTests.cs
│       │   └── DeckleChronoSourceTests.cs
│       ├── Shared/
│       │   └── TestEventListener.cs
│       └── Deckle.Tests.csproj
├── .gitattributes
├── .gitignore
├── CLAUDE.md  — [agent-instructions] Master agent-instructions for the Deckle project (Windows utility, .NET 10 / Wi…
├── CONTEXT.md  — [agent-instructions] Project glossary for Deckle — shared vocabulary, term-of-art definitions, namin…
├── CONTRIBUTING.md
├── deckle.code-workspace
├── LICENSE
├── NOTICE.md
├── README.md
├── SECURITY.md
└── TREE.md
```