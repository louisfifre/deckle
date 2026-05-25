# Arborescence — Deckle
_Mise à jour : 2026-05-25 22:15 — source : `git ls-files`_

```
├── .claude/
│   ├── agents/
│   │   ├── expert-dotnet-software-engineer.agent.md
│   │   ├── plan.agent.md
│   │   └── winui3-expert.agent.md
│   └── skills/
│       ├── deckle-commits/
│       │   └── SKILL.md
│       ├── deckle-docs/
│       │   └── SKILL.md
│       ├── deckle-logging/
│       │   ├── SKILL.md
│       │   └── taxonomy.md
│       ├── deckle-modularite/
│       │   └── SKILL.md
│       ├── deckle-nomenclature/
│       │   ├── SKILL.md
│       │   └── taxonomie.md
│       ├── deckle-refonte/
│       │   └── SKILL.md
│       ├── deckle-settings-ux/
│       │   └── SKILL.md
│       ├── deckle-testing/
│       │   └── SKILL.md
│       ├── deckle-workflow/
│       │   └── SKILL.md
│       ├── microsoft-docs/
│       │   └── SKILL.md
│       ├── tdd/
│       │   ├── deep-modules.md
│       │   ├── interface-design.md
│       │   ├── mocking.md
│       │   ├── refactoring.md
│       │   ├── SKILL.md
│       │   └── tests.md
│       └── write-a-skill/
│           └── SKILL.md
├── .vscode/
│   └── launch.json
├── benchmark/
│   ├── config/
│   │   ├── prompts/
│   │   │   ├── affinage_system_prompt.txt
│   │   │   ├── arrangement_system_prompt.txt
│   │   │   ├── judge_system_prompt.txt
│   │   │   ├── lissage_system_prompt.txt
│   │   │   ├── relecture_system_prompt.txt
│   │   │   ├── system_prompt.txt
│   │   │   └── whisper_initial_prompt.txt
│   │   └── config.ini
│   ├── lib/
│   │   ├── __init__.py
│   │   ├── corpus.py
│   │   ├── judge_claude.py
│   │   ├── judge_ollama.py
│   │   ├── judge.py
│   │   ├── metrics.py
│   │   └── ollama.py
│   ├── _template_bench.py
│   ├── autoresearch.py
│   ├── benchmark.py
│   ├── compare_runs.py
│   ├── launch.ps1
│   ├── README.md
│   ├── refresh_corpus.py
│   ├── rewrite_bench.py
│   ├── segment_corpus.py
│   ├── whisper_bench.py
│   └── wpm_stats.py
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
│   │   └── 0012-adoption-de-dotnet-build-et-dotnet-test.md
│   ├── reference/
│   │   ├── reference--eventsource-convention--1.0.md
│   │   ├── reference--eventsource-convention--1.1.md
│   │   └── reference--eventsource-convention--1.2.md
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
│   │   │   ├── LogEntryTemplateSelector.cs
│   │   │   └── NetworkStatusEmitter.cs
│   │   ├── Engine/
│   │   │   ├── AppAmbientEngineHost.cs
│   │   │   └── AppTranscriptionEngineHost.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── app.manifest
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── CLAUDE.md
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
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Audio.csproj
│   │   ├── DeckleAudioSource.cs
│   │   ├── IAudioRecordingHost.cs
│   │   ├── MicrophoneCapture.cs
│   │   └── ProbeResult.cs
│   ├── Deckle.Catalog/
│   │   ├── Themes/
│   │   │   └── Icons.xaml
│   │   ├── CLAUDE.md
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
│   │   ├── CLAUDE.md
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
│   │   └── WindowingProbe.cs
│   ├── Deckle.Diagnostics.Logging/
│   │   ├── AmbientCaptureGate.cs
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Diagnostics.Logging.csproj
│   │   ├── LoggingSettings.cs
│   │   └── LoggingSettingsService.cs
│   ├── Deckle.Diagnostics.Telemetry/
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Diagnostics.Telemetry.csproj
│   │   ├── TelemetryListenerBootstrap.cs
│   │   ├── TelemetrySettings.cs
│   │   └── TelemetrySettingsService.cs
│   ├── Deckle.Hud/
│   │   ├── CLAUDE.md
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
│   │   ├── CLAUDE.md
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
│   │   ├── CLAUDE.md
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
│   │   ├── CLAUDE.md
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
│   │   ├── CLAUDE.md
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
│   │   ├── CLAUDE.md
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
│   │   ├── CLAUDE.md
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
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Transcription.Whisper.csproj
│   │   ├── RepetitionDetector.cs
│   │   └── WhisperBackend.cs
│   └── Deckle.Vision/
│       ├── CapturedFrame.cs
│       ├── CLAUDE.md
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
│       ├── Diagnostics/
│       │   ├── DeckleCancellationSourceTests.cs
│       │   ├── DeckleNetworkSourceTests.cs
│       │   ├── DeckleResourceSourceTests.cs
│       │   ├── DeckleThemeSourceTests.cs
│       │   ├── DeckleThreadingSourceTests.cs
│       │   └── DeckleWindowingSourceTests.cs
│       ├── Shared/
│       │   ├── EventArgsExtensions.cs
│       │   ├── TestEventListener.cs
│       │   └── WindowsAppSdkBootstrap.cs
│       ├── Shell/
│       │   └── DispatcherQueueExtensionsTests.cs
│       ├── Vision/
│       │   └── DeckleVisionSourceTests.cs
│       └── Deckle.Tests.csproj
├── .gitattributes
├── .gitignore
├── CLAUDE.md
├── CONTEXT.md
├── CONTRIBUTING.md
├── deckle.code-workspace
├── LICENSE
├── NOTICE.md
├── README.md
├── SECURITY.md
└── TREE.md
```