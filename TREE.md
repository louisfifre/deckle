# Arborescence — Deckle
_Mise à jour : 2026-05-31 19:10 — source : `git ls-files`_

```
├── .claude/
│   ├── agents/
│   │   ├── expert-dotnet-software-engineer.agent.md  — Provide expert .NET software engineering guidance using modern software design…
│   │   ├── plan.agent.md  — Strategic planning and architecture assistant focused on thoughtful analysis be…
│   │   └── winui3-expert.agent.md  — Expert agent for WinUI 3 and Windows App SDK development. Prevents common UWP-t…
│   └── skills/
│       ├── deckle-commits/
│       │   └── SKILL.md  — Commit doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries the…
│       ├── deckle-journal/
│       │   └── SKILL.md  — [skill] Journal doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries th…
│       ├── deckle-logging/
│       │   ├── SKILL.md  — Observability doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carr…
│       │   └── taxonomy.md
│       ├── deckle-modularite/
│       │   └── SKILL.md  — Modularity and splitting doctrine for the Deckle project (Windows .NET 10 / Win…
│       ├── deckle-nomenclature/
│       │   ├── SKILL.md  — Naming doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries cas…
│       │   └── taxonomie.md
│       ├── deckle-settings-ux/
│       │   └── SKILL.md  — User experience doctrine for the settings surfaces of the Deckle project (Windo…
│       ├── deckle-testing/
│       │   └── SKILL.md  — Testing doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries th…
│       ├── deckle-workflow/
│       │   └── SKILL.md  — Workflow doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries h…
│       ├── microsoft-docs/
│       │   └── SKILL.md  — Query official Microsoft documentation to find concepts, tutorials, and code ex…
│       ├── senior-architect/
│       │   ├── references/
│       │   │   ├── architecture_patterns.md
│       │   │   ├── system_design_workflows.md
│       │   │   └── tech_decision_guide.md
│       │   ├── scripts/
│       │   │   ├── architecture_diagram_generator.py
│       │   │   ├── dependency_analyzer.py
│       │   │   └── project_architect.py
│       │   └── SKILL.md  — Complete toolkit for senior architect with modern tools and best practices.
│       ├── senior-frontend/
│       │   ├── references/
│       │   │   ├── frontend_best_practices.md
│       │   │   ├── nextjs_optimization_guide.md
│       │   │   └── react_patterns.md
│       │   ├── scripts/
│       │   │   ├── bundle_analyzer.py
│       │   │   ├── component_generator.py
│       │   │   └── frontend_scaffolder.py
│       │   └── SKILL.md  — Frontend development skill for React, Next.js, TypeScript, and Tailwind CSS app…
│       ├── senior-fullstack/
│       │   ├── references/
│       │   │   ├── architecture_patterns.md
│       │   │   ├── development_workflows.md
│       │   │   └── tech_stack_guide.md
│       │   ├── scripts/
│       │   │   ├── code_quality_analyzer.py
│       │   │   ├── fullstack_scaffolder.py
│       │   │   └── project_scaffolder.py
│       │   └── SKILL.md  — Complete toolkit for senior fullstack with modern tools and best practices.
│       ├── tdd/
│       │   ├── deep-modules.md
│       │   ├── interface-design.md
│       │   ├── mocking.md
│       │   ├── refactoring.md
│       │   ├── SKILL.md  — Test-driven development with red-green-refactor loop. Use when user wants to bu…
│       │   └── tests.md
│       ├── ux-copy/
│       │   └── SKILL.md  — Write or review UX copy — microcopy, error messages, empty states, CTAs. Trigge…
│       ├── ux-designer/
│       │   ├── rules/
│       │   │   ├── accessibility.md
│       │   │   ├── information-architecture.md
│       │   │   ├── interaction-design.md
│       │   │   ├── research.md
│       │   │   └── visual-design.md
│       │   ├── AGENTS.md
│       │   └── SKILL.md  — |
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
│       │   └── SKILL.md  — Bootstrap, develop, and design modern WinUI 3 desktop applications with C# and…
│       └── write-a-skill/
│           └── SKILL.md  — Create new agent skills with proper structure, progressive disclosure, and bund…
├── .vscode/
│   └── launch.json
├── audits/
│   ├── prompts/
│   │   ├── claude.md  — [agent-instructions] Claude prompt for the weekly Deckle audit routine, emphasizing doctrine, archit…
│   │   └── codex.md  — [agent-instructions] Codex prompt for the weekly Deckle audit routine, emphasizing implementation ri…
│   ├── runs/
│   │   └── 2026/
│   │       └── .gitkeep
│   ├── templates/
│   │   └── weekly-audit.md
│   ├── CLAUDE.md  — [agent-instructions] Shared instructions for recurring Deckle audits run by Codex, Claude, or future…
│   ├── index.csv
│   ├── README.md  — [module-readme] Explains the root audits workspace: recurring agent reviews, shared prompts, sc…
│   └── schema.md  — [agent-instructions] Canonical schema for Deckle recurring audit reports: frontmatter, section names…
├── benchmark/
│   ├── benches/
│   │   ├── voxtral-onnx-poc/
│   │   │   ├── debug_kv.py
│   │   │   ├── debug_tokens.py
│   │   │   └── smoke_test.py
│   │   ├── voxtral-poc/
│   │   │   ├── bench.py
│   │   │   └── README.md  — [module-readme] Bench scenario evaluating Voxtral Mini 3B as a Whisper alternative in the Deckl…
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
│   │       ├── README.md  — [bench-scenario] Bench de validation Voxtral 24B Q4_K_M comme remplacement de Whisper, ground tr…
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
│   ├── CLAUDE.md  — [agent-instructions] Doctrine for the benchmark/ suite — an autonomous box that measures quality and…
│   ├── JOURNAL.md  — [module-journal] Journal daté du module benchmark : décisions intermédiaires, hypothèses, learni…
│   ├── pregenerate_groundtruth_gemini.py
│   ├── README.md  — [module-readme] Human-facing entry point for the benchmark/ suite — what it is, how a bench is…
│   └── voxtral-val-30-notes.json
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
│   │   ├── 0014-poc-evaluation-voxtral.md
│   │   ├── 0015-attendre-le-merge-mmvq-vulkan-q3-k-q6-k.md
│   │   └── 0016-inference-safetensors-native-pour-voxtral.md  — [adr] Acte l'adoption de l'inférence safetensors-native via Transformers + torch ROCm…
│   ├── reference/
│   │   ├── reference--build-onnxruntime-genai-amd-windows--1.0.md  — [reference] Recette de build local de Microsoft `onnxruntime-genai` sur Windows AMD avec Vi…
│   │   └── reference--eventsource-convention--1.2.md
│   └── research/
│       ├── phi4-oga-lora-activation--2026-05-28.patch
│       ├── research--asr-benchmarks-voxtral-vs-whisper-fr--2026-05-27.md  — [research] Comparaison ASR français au 2026-05-27 entre Voxtral Mini 3B / Small 24B, Whisp…
│       ├── research--asr-native-windows-amd-routes--2026-05-28.md  — [research] Cartographie des voies réalistes pour exécuter un modèle ASR multimodal (Phi-4,…
│       ├── research--hdr-graphics-capture--2026-05-15.md
│       ├── research--hue-entertainment-v2--2026-05-15.md
│       ├── research--hyperhdr-interpolators--2026-05-15.md
│       ├── research--inventaire-observabilite-eventsource--2026-05-24.md
│       ├── research--phi-4-multimodal-state-of-the-art--2026-05-27.md  — [research] Synthèse à l'état de l'art (2026-05-27) du modèle Phi-4-Multimodal de Microsoft…
│       ├── research--whisper-alternatives-fine-windowing--2026-05-27.md  — [research] Alternatives à whisper.cpp pour réduire la fenêtre d'inférence Whisper de 30s à…
│       ├── research--whisper-dynamic-vad-distil-fr--2026-05-28.md  — [research] Cartographie de la voie composite « Whisper dynamic windowing + VAD énergie + d…
│       └── voxtral-val-30-notes-louis--2026-05-27.json
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
│   ├── README.md  — [module-readme] Dev workflows entry point for Deckle: the deckle.ps1 menu, the worker scripts u…
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
│   │   ├── ProximityRollupAggregator.cs
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
│   │   │   ├── HueProjectedState.cs
│   │   │   └── HueRestLightOutput.cs
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Lighting, the Philips Hue driver (REST CLIP v1/v2) and colo…
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
│   ├── Deckle.Shell.TrayMenu/
│   │   ├── Interop/
│   │   │   └── TrayMenuNativeMethods.cs
│   │   ├── Themes/
│   │   │   └── TrayMenu.xaml
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Shell.TrayMenu.csproj
│   │   ├── DeckleShellTrayMenuSource.cs
│   │   ├── JOURNAL.md  — [module-journal] Journal daté du module Deckle.Shell.TrayMenu — diagnostics en cours, observatio…
│   │   ├── TrayContextMenuHost.cs
│   │   └── TraySwitchMenuItem.cs
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
│   │   ├── DeckleWhispSource.Delivery.cs
│   │   ├── DeckleWhispSource.PipelineCompletion.cs
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
│   │   ├── CLAUDE.md  — [agent-instructions] Doctrine for Deckle.Transcription.Whisper, the IAsrBackend implementation via w…
│   │   ├── Deckle.Transcription.Whisper.csproj
│   │   ├── RepetitionDetector.cs
│   │   └── WhisperBackend.cs
│   └── Deckle.Vision/
│       ├── CapturedFrame.cs
│       ├── CaptureStallDetector.cs
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
│       ├── Diagnostics/
│       │   ├── DeckleCancellationSourceTests.cs
│       │   ├── DeckleNetworkSourceTests.cs
│       │   ├── DeckleResourceSourceTests.cs
│       │   ├── DeckleThemeSourceTests.cs
│       │   ├── DeckleThreadingSourceTests.cs
│       │   ├── DeckleWindowingSourceTests.cs
│       │   └── TelemetryListenerBootstrapTests.cs
│       ├── Hud/
│       │   ├── DeckleHudSourceTests.cs
│       │   └── ProximityRollupAggregatorTests.cs
│       ├── Lighting/
│       │   ├── AmbientHueEchoClassifierTests.cs
│       │   └── DeckleAmbientSourceTests.cs
│       ├── Shared/
│       │   ├── EventArgsExtensions.cs
│       │   ├── TestEventListener.cs
│       │   └── WindowsAppSdkBootstrap.cs
│       ├── Shell/
│       │   └── DispatcherQueueExtensionsTests.cs
│       ├── Vision/
│       │   ├── CaptureStallDetectorTests.cs
│       │   └── DeckleVisionSourceTests.cs
│       └── Deckle.Tests.csproj
├── .gitattributes
├── .gitignore
├── AGENTS.md  — [agent-instructions] Minimal Codex bridge for Deckle. Claude-maintained files remain the source of t…
├── CLAUDE.md  — [agent-instructions] Master agent-instructions for the Deckle project (Windows utility, .NET 10 / Wi…
├── CONTEXT.md  — [agent-instructions] Project glossary for Deckle — shared vocabulary, term-of-art definitions, namin…
├── CONTRIBUTING.md
├── deckle.code-workspace
├── JOURNAL.md  — [project-journal] Journal daté du projet Deckle : avancées techniques validées, observations en c…
├── LICENSE
├── NOTICE.md
├── README.md
├── SECURITY.md
└── TREE.md
```