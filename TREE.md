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
│       │   ├── references/
│       │   │   └── controls-and-behaviour.md
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
├── .github/
│   └── workflows/
│       └── update-readme-stats.yml
├── .vscode/
│   └── launch.json
├── benchmark/
│   ├── asr/
│   │   ├── lib/
│   │   │   ├── judges/
│   │   │   │   ├── __init__.py
│   │   │   │   ├── _base.py
│   │   │   │   ├── claude.py
│   │   │   │   └── gemini.py
│   │   │   ├── metrics/
│   │   │   │   ├── __init__.py
│   │   │   │   ├── leak.py
│   │   │   │   ├── looping.py
│   │   │   │   └── wer.py
│   │   │   ├── sources/
│   │   │   │   ├── __init__.py
│   │   │   │   ├── _base.py
│   │   │   │   ├── _voxtral_common.py
│   │   │   │   ├── gemini_audio.py
│   │   │   │   ├── voxtral_chat.py
│   │   │   │   ├── voxtral_llamacpp.py
│   │   │   │   ├── voxtral_transcribe.py
│   │   │   │   ├── voxtral_transformers.py
│   │   │   │   └── whisper_cpp.py
│   │   │   ├── __init__.py
│   │   │   └── corpus.py
│   │   ├── prompts/
│   │   │   ├── judges/
│   │   │   │   ├── claude_per_row.md
│   │   │   │   ├── gemini_per_row.md
│   │   │   │   └── legacy_ollama_judge.md
│   │   │   ├── transcription/
│   │   │   │   ├── gemini_audio.toml
│   │   │   │   ├── voxtral_chat.toml
│   │   │   │   ├── voxtral_transcribe.toml
│   │   │   │   └── voxtral_validation.toml
│   │   │   └── whisper_initial.txt
│   │   ├── studies/
│   │   │   ├── perf-cap/
│   │   │   │   ├── download-models.ps1
│   │   │   │   ├── parse_vulkan_log.py
│   │   │   │   ├── profile-config.ps1
│   │   │   │   ├── profile-server-text.ps1
│   │   │   │   ├── README.md  — readme-study-perf-cap [study] Frozen Voxtral GGUF performance-characterization session (2026-05-26) over llam…
│   │   │   │   └── run-all.ps1
│   │   │   ├── PhiBench/
│   │   │   │   ├── Models/
│   │   │   │   │   ├── Regime.cs
│   │   │   │   │   ├── Sample.cs
│   │   │   │   │   └── TranscriptionResult.cs
│   │   │   │   ├── CorpusLoader.cs
│   │   │   │   ├── CorpusRunner.cs
│   │   │   │   ├── JsonlWriter.cs
│   │   │   │   ├── Phi4Transcriber.cs
│   │   │   │   ├── PhiBench.csproj
│   │   │   │   ├── Program.cs
│   │   │   │   ├── README.md  — readme-study-phibench [study] Suspended C# bench for Phi-4 multimodal audio via ONNX Runtime GenAI (OGA). Blo…
│   │   │   │   ├── RegimesLoader.cs
│   │   │   │   ├── SingleRunner.cs
│   │   │   │   └── WavHeader.cs
│   │   │   ├── tts-audition/
│   │   │   │   ├── _harness.py
│   │   │   │   ├── .gitignore
│   │   │   │   ├── build_player.py
│   │   │   │   ├── chatterbox_synth.py
│   │   │   │   ├── f5_synth.py
│   │   │   │   ├── orpheus_synth.py
│   │   │   │   ├── README.md  — readme-bench-tts-audition [bench-scenario] Local French TTS audition — a by-ear comparison of ONNX-local TTS engines on th…
│   │   │   │   ├── supertonic_synth.py
│   │   │   │   └── synth_onnx.py
│   │   │   ├── voxtral-onnx-poc/
│   │   │   │   ├── README.md  — readme-study-voxtral-onnx [study] Completed POC — Voxtral Mini 3B via ONNX Runtime + DirectML. Smoke pipeline kep…
│   │   │   │   └── smoke_test.py
│   │   │   ├── voxtral-poc/
│   │   │   │   ├── bench.py
│   │   │   │   └── README.md  — readme-bench-voxtral-poc [module-readme] Bench scenario evaluating Voxtral Mini 3B as a Whisper alternative in the Deckl…
│   │   │   ├── voxtral-transformers/
│   │   │   │   ├── perf_rtf.py
│   │   │   │   ├── README.md  — readme-study-voxtral-transformers [study] Completed study — Voxtral Mini 3B BF16 via Transformers + torch-ROCm on Windows…
│   │   │   │   ├── sanity_check.py
│   │   │   │   └── smoke_chat_regimes.py
│   │   │   ├── voxtral-validation/
│   │   │   │   ├── aggregate_verdicts.py
│   │   │   │   ├── bench.py
│   │   │   │   ├── README.md  — bench-voxtral-validation [bench-scenario] Bench de validation Voxtral 24B Q4_K_M comme remplacement de Whisper, ground tr…
│   │   │   │   └── validate_judge_prompt.py
│   │   │   └── README.md  — readme-studies [module-readme] Index of frozen benchmark studies — completed or abandoned ASR/TTS spikes kept…
│   │   ├── __init__.py
│   │   ├── AGENTS.md  — [agent-instructions] ASR-specific benchmark workspace — sources, judges, corpora, metrics, and froze…
│   │   ├── build_corpus.py
│   │   ├── CLAUDE.md
│   │   ├── JOURNAL.md  — [module-journal] Dated findings from the Voxtral/ASR benchmark spike — backends, quantization, a…
│   │   └── README.md  — readme-benchmark-asr [module-readme] Human-facing entry point for benchmark/asr — ASR-specific harness pieces and fr…
│   ├── autoresearch/
│   │   ├── campaigns/
│   │   │   └── README.md  — readme-autoresearch-campaigns [module-readme] How to store individual autoresearch campaign folders.
│   │   ├── judges/
│   │   │   └── README.md  — readme-autoresearch-judges [module-readme] Generic judge rubrics and wrappers for autoresearch loops.
│   │   ├── metrics/
│   │   │   └── README.md  — readme-autoresearch-metrics [module-readme] Generic metric wrappers for autoresearch loops.
│   │   ├── prompts/
│   │   │   └── README.md  — readme-autoresearch-prompts [module-readme] Prompt templates owned by generic autoresearch campaigns.
│   │   ├── runners/
│   │   │   └── README.md  — readme-autoresearch-runners [module-readme] Runner helpers for autoresearch campaigns.
│   │   ├── AGENTS.md  — [agent-instructions] Autoresearch benchmark workspace — reusable iterative optimization loops.
│   │   ├── CLAUDE.md
│   │   └── README.md  — readme-autoresearch [module-readme] Generic autoresearch workspace for measurable iterative generation, editing, ju…
│   ├── lib/
│   │   ├── monitor/
│   │   │   ├── gpu_monitor.ps1
│   │   │   └── joiner.py
│   │   ├── __init__.py
│   │   ├── _base_compat.py
│   │   ├── env.py
│   │   ├── event_log.py
│   │   └── paths.py
│   ├── viewers/
│   │   ├── __init__.py
│   │   └── build_html.py
│   ├── AGENTS.md  — [agent-instructions] Benchmark workspace router — choose the right benchmark family before touching…
│   ├── CLAUDE.md
│   ├── Directory.Build.props
│   ├── Directory.Packages.props
│   ├── HANDOFF.md  — handoff-benchmark-reorg [handoff] Session handoff for the benchmark workspace reorganization.
│   └── README.md  — readme-benchmark [module-readme] Index for Deckle benchmark workspaces — routes ASR evaluation and generic autor…
├── docs/
│   ├── adr/
│   │   ├── 0000-template.md  — [adr] Fill-in template for a Deckle ADR — copy it to start one, record no decision he…
│   │   ├── 0001-anytype-headless-service-single-http-mcp-host.md  — [adr] Anytype runs as a Deckle-orchestrated headless service behind one HTTP MCP host…
│   │   ├── AGENTS.md  — [agent-instructions] Why Deckle keeps ADRs and the questions that gate one. Read before writing or p…
│   │   └── CLAUDE.md
│   ├── research/
│   │   ├── 2026-06-12--notifications-catalogue.md
│   │   ├── 2026-06-15--mouse-wheel-to-virtual-touchpad-spec.md
│   │   ├── research--correcteur-evaluation--2026-07-02.md  — [research-report] Recherche vérifiée (deep-research, 2026-07-02) — comment mesurer un correcteur…
│   │   ├── research--globish-seed-sources--2026-07-02.md  — [research-report] Recherche vérifiée (deep-research, 2026-07-02) — d'où tirer le lexique globish…
│   │   ├── research--onnx-judge-runtime--2026-07-02.md  — [research-report] Recherche vérifiée (deep-research, 2026-07-02) — faisabilité du juge ONNX de l'…
│   │   └── research--system-autocorrect--2026-06-12.md
│   └── inventaire-settings.md
├── scripts/
│   ├── hooks/
│   │   ├── pre-commit
│   │   └── update-tree.ps1
│   ├── lib/
│   │   ├── launcher/
│   │   │   ├── actions.ps1
│   │   │   ├── context.ps1
│   │   │   └── menus.ps1
│   │   ├── menu/
│   │   │   ├── chrome.ps1
│   │   │   ├── grid-picker.ps1
│   │   │   ├── list-picker.ps1
│   │   │   └── session.ps1
│   │   ├── _menu.psm1
│   │   ├── action-summary.ps1
│   │   ├── bootstrap-dev-env.ps1
│   │   ├── build-run.ps1
│   │   ├── build-server-cleanup.ps1
│   │   ├── changelog.ps1
│   │   ├── clean.ps1
│   │   ├── cut-version.ps1
│   │   ├── deckle-process.ps1
│   │   ├── fetch-autocorrect-data.ps1
│   │   ├── install-hooks.ps1
│   │   ├── launch-app.ps1
│   │   ├── publish-app.ps1
│   │   ├── publish-native-runtime.ps1
│   │   ├── setup-assets.ps1
│   │   ├── stats.ps1
│   │   ├── stop-build-servers.ps1
│   │   └── update-readme-stats.ps1
│   ├── deckle.ps1
│   └── README.md  — readme-scripts [module-readme] Dev workflows entry point for Deckle: the deckle.ps1 menu, the worker scripts u…
├── src/
│   ├── Deckle.Anytype/
│   │   ├── Api/
│   │   │   ├── AnytypeApiClient.Chats.cs
│   │   │   ├── AnytypeApiClient.cs
│   │   │   ├── AnytypeCredentials.cs
│   │   │   └── SpaceWriteLock.cs
│   │   ├── Backend/
│   │   │   ├── BackendHealthProbe.cs
│   │   │   ├── BackendInstallation.cs
│   │   │   ├── BackendProcess.cs
│   │   │   ├── BackendProcessSpec.cs
│   │   │   └── BackendSupervisor.cs
│   │   ├── Dialogues/
│   │   │   └── DialogueGestures.cs
│   │   ├── Gestures/
│   │   │   ├── LiveTagResolver.cs
│   │   │   ├── ManagementGestures.cs
│   │   │   ├── MarkdownBody.cs
│   │   │   ├── ProjectGestures.cs
│   │   │   ├── QueryGestures.cs
│   │   │   ├── Resolution.cs
│   │   │   ├── SessionGestures.cs
│   │   │   └── TaskGestures.cs
│   │   ├── Schema/
│   │   │   ├── DevSpace.cs
│   │   │   └── SCHEMA.md
│   │   ├── Deckle.Anytype.csproj
│   │   ├── DeckleAnytypeSource.cs
│   │   └── JOURNAL.md  — [module-journal] Dated decisions and findings for the Anytype MCP server — founding grilling, AP…
│   ├── Deckle.Anytype.Mcp/
│   │   ├── Http/
│   │   │   ├── McpClients.cs
│   │   │   ├── McpClientTokens.cs
│   │   │   ├── McpHttpHost.cs
│   │   │   └── McpSession.cs
│   │   ├── JsonRpc/
│   │   │   └── McpServer.cs
│   │   ├── Tools/
│   │   │   ├── DialogueToolCatalog.cs
│   │   │   ├── ManagementToolCatalog.cs
│   │   │   ├── ToolCatalog.cs
│   │   │   └── ToolDescriptor.cs
│   │   ├── Deckle.Anytype.Mcp.csproj
│   │   ├── DeckleAnytypeMcpSource.cs
│   │   ├── McpToolset.cs
│   │   └── ToolProfile.cs
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
│   │   ├── AGENTS.md  — [agent-instructions] WinUI 3 host composing the Deckle.* modules — the composition boundary, the OnL…
│   │   ├── App.Ambient.cs
│   │   ├── App.Anytype.cs
│   │   ├── App.Autocorrect.cs
│   │   ├── App.Hotkeys.cs
│   │   ├── App.Input.cs
│   │   ├── App.Lifetime.cs
│   │   ├── app.manifest
│   │   ├── App.MouseWheel.cs
│   │   ├── App.TaskbarCover.cs
│   │   ├── App.Theme.cs
│   │   ├── App.Trackpad.cs
│   │   ├── App.Windows.cs
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── AutocorrectNotifications.cs
│   │   ├── CLAUDE.md
│   │   ├── Deckle.App.csproj
│   │   ├── global.json
│   │   ├── LogWindow.Chrome.cs
│   │   ├── LogWindow.Interaction.cs
│   │   ├── LogWindow.Model.cs
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
│   │   ├── AGENTS.md  — [agent-instructions] Audio module — the home for capturing and analyzing sound: microphone capture,…
│   │   ├── AudioLevelMapper.cs
│   │   ├── CaptureFrame.cs
│   │   ├── CaptureResult.cs
│   │   ├── CaptureSettings.cs
│   │   ├── CaptureSettingsService.cs
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Audio.csproj
│   │   ├── DeckleAudioSource.cs
│   │   ├── IAudioRecordingHost.cs
│   │   ├── MicLevelTester.cs
│   │   ├── MicrophoneCapture.cs
│   │   ├── ProbeResult.cs
│   │   └── SpeakerOutput.cs
│   ├── Deckle.Autocorrect/
│   │   ├── Data/
│   │   │   ├── lexicon-en-globish.tsv.gz
│   │   │   ├── lexicon-en.tsv.gz
│   │   │   ├── lexicon-fr.tsv.gz
│   │   │   ├── pair-bigrams-fr.tsv.gz
│   │   │   └── verbs-fr.tsv.gz
│   │   ├── Engine/
│   │   │   ├── AutocorrectEngine.cs
│   │   │   ├── BackgroundRerankLane.cs
│   │   │   ├── BigramPairDisambiguator.cs
│   │   │   ├── CasePattern.cs
│   │   │   ├── CompositeCorrectionPolicy.cs
│   │   │   ├── ConservativeTypoCorrector.cs
│   │   │   ├── CorrectionDecision.cs
│   │   │   ├── CorrectionTrace.cs
│   │   │   ├── DiacriticsRestorer.cs
│   │   │   ├── ElisionCorrector.cs
│   │   │   ├── FrenchSentenceReranker.cs
│   │   │   ├── GrammarCorrector.cs
│   │   │   ├── IAmbiguityProbe.cs
│   │   │   ├── IPairDisambiguator.cs
│   │   │   ├── ISentenceReranker.cs
│   │   │   ├── ISentenceScorer.cs
│   │   │   ├── QwertyAdjacency.cs
│   │   │   ├── RestorerOptions.cs
│   │   │   ├── SentenceCorpus.cs
│   │   │   ├── SentenceRerankCoordinator.cs
│   │   │   ├── TrainerReport.cs
│   │   │   ├── TypoOptions.cs
│   │   │   └── WordShape.cs
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
│   │   │   ├── AutocorrectLexiconArtifacts.cs
│   │   │   ├── FrequencyLexicon.cs
│   │   │   ├── IFrequencyLexicon.cs
│   │   │   └── VerbMorphology.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
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
│   │   ├── Ui/
│   │   │   ├── AutocorrectPage.xaml
│   │   │   └── AutocorrectPage.xaml.cs
│   │   ├── ViewModels/
│   │   │   ├── AutocorrectAppRow.cs
│   │   │   ├── AutocorrectViewModel.cs
│   │   │   └── AutocorrectViewModel.Settings.cs
│   │   ├── AGENTS.md  — [agent-instructions] Machine-wide autocorrect domain module — typed-word tracking, conservative corr…
│   │   ├── AutocorrectSettings.cs
│   │   ├── AutocorrectSettingsService.cs
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Autocorrect.csproj
│   │   ├── DeckleAutocorrectSource.cs
│   │   └── JOURNAL.md  — [module-journal] Dated decisions and findings for Deckle.Autocorrect — founding choices, measure…
│   ├── Deckle.Autocorrect.Lab/
│   │   ├── Replay/
│   │   │   ├── CorpusReader.cs
│   │   │   ├── MarginCalibration.cs
│   │   │   ├── ReplayRunner.cs
│   │   │   ├── SentenceAlignment.cs
│   │   │   └── SentenceReplay.cs
│   │   ├── DataSet.cs
│   │   ├── Deckle.Autocorrect.Lab.csproj
│   │   ├── HarvestData.cs
│   │   ├── HarvestFilter.cs
│   │   ├── HarvestStore.cs
│   │   ├── LexiconBuilder.cs
│   │   ├── MorphalouReader.cs
│   │   ├── PairModelTrainer.cs
│   │   ├── RestorationEvaluator.cs
│   │   └── RestorationReport.cs
│   ├── Deckle.Autocorrect.Mlm/
│   │   ├── CamembertMlmScorer.cs
│   │   ├── CamembertReranker.cs
│   │   ├── CamembertSentenceReranker.cs
│   │   ├── Deckle.Autocorrect.Mlm.csproj
│   │   └── MlmProbe.cs
│   ├── Deckle.Autocorrect.Onnx/
│   │   ├── CandidateCompletionPlan.cs
│   │   ├── Deckle.Autocorrect.Onnx.csproj
│   │   ├── OnnxSentenceScorer.cs
│   │   └── OnnxSlotReranker.cs
│   ├── Deckle.Autocorrect.Probe/
│   │   ├── CorrectionBenchmarkCase.cs
│   │   ├── CorrectionBenchmarkCommand.cs
│   │   ├── CorrectionBenchmarkCorpus.cs
│   │   ├── CorrectionBenchmarkResult.cs
│   │   ├── CorrectionBenchmarkSummary.cs
│   │   ├── Deckle.Autocorrect.Probe.csproj
│   │   ├── ModelPathResolver.cs
│   │   ├── ProbeArguments.cs
│   │   ├── Program.cs
│   │   └── SingleProbeCommand.cs
│   ├── Deckle.Catalog/
│   │   ├── Composer/
│   │   │   ├── IPathControl.cs
│   │   │   ├── Setting.cs
│   │   │   ├── SettingArgs.cs
│   │   │   ├── SettingDescriptor.cs
│   │   │   ├── SettingKind.cs
│   │   │   └── SettingsComposer.cs
│   │   ├── Themes/
│   │   │   └── Icons.xaml
│   │   ├── AGENTS.md  — [agent-instructions] Shared WinUI floor — the UI primitives every module depends on and that depend…
│   │   ├── CLAUDE.md
│   │   ├── ConfirmationService.cs
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
│   │   │   ├── HudComposition.ConicClonePreview.cs
│   │   │   ├── HudComposition.cs
│   │   │   ├── HudComposition.DigitReveal.cs
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
│   │   ├── Sinks/
│   │   │   ├── HudFeedbackSink.cs
│   │   │   ├── JsonlRotationPolicy.cs
│   │   │   ├── JsonlSchema.cs
│   │   │   ├── JsonlSink.cs
│   │   │   ├── LogWindowSink.cs
│   │   │   └── RoutedJsonlSink.cs
│   │   ├── AGENTS.md  — [agent-instructions] Observability foundation — EventSource providers, levels, sinks, JSONL contract.
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Diagnostics.csproj
│   │   ├── DeckleCancellationSource.cs
│   │   ├── DeckleEventSource.cs
│   │   ├── DeckleNetworkSource.cs
│   │   ├── DeckleResourceSource.cs
│   │   ├── DeckleThemeSource.cs
│   │   ├── DeckleThreadingSource.cs
│   │   ├── DeckleWindowingSource.cs
│   │   ├── DispatchEventListener.cs
│   │   ├── EventEntry.cs
│   │   ├── IHudFeedbackSink.cs
│   │   ├── ILogSink.cs
│   │   ├── ILogWindowSink.cs
│   │   ├── Keywords.cs
│   │   ├── LogLineFormatter.cs
│   │   └── WindowingProbe.cs
│   ├── Deckle.Diagnostics.Logging/
│   │   ├── AGENTS.md  — [agent-instructions] Live LogWindow settings and the ambient capture noise gate.
│   │   ├── AmbientCaptureGate.cs
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Diagnostics.Logging.csproj
│   │   ├── LoggingSettings.cs
│   │   ├── LoggingSettingsService.cs
│   │   ├── LogWindowFilter.cs
│   │   ├── LogWindowVisibilityMode.cs
│   │   └── StreamingCaptureGate.cs
│   ├── Deckle.Diagnostics.Telemetry/
│   │   ├── AGENTS.md  — [agent-instructions] Structured JSONL persistence and consent gates.
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Diagnostics.Telemetry.csproj
│   │   ├── TelemetryListenerBootstrap.cs
│   │   ├── TelemetrySettings.cs
│   │   └── TelemetrySettingsService.cs
│   ├── Deckle.Hud/
│   │   ├── Chrono/
│   │   │   ├── CONTEXT.md  — context-deckle-hud-chrono [agent-instructions] Chrono HUD element vocabulary and the per-phase colour-state matrix (digits, do…
│   │   │   ├── HudChrono.Clock.cs
│   │   │   ├── HudChrono.Reveal.cs
│   │   │   ├── HudChrono.RevealMask.cs
│   │   │   ├── HudChrono.Stroke.cs
│   │   │   ├── HudChrono.xaml
│   │   │   └── HudChrono.xaml.cs
│   │   ├── Model/
│   │   │   ├── HudPalette.cs
│   │   │   ├── HudState.cs
│   │   │   └── MessageKind.cs
│   │   ├── Windows/
│   │   │   ├── HudOverlayManager.cs
│   │   │   ├── HudOverlayWindow.xaml
│   │   │   ├── HudOverlayWindow.xaml.cs
│   │   │   ├── HudWindow.Fade.cs
│   │   │   ├── HudWindow.Proximity.cs
│   │   │   ├── HudWindow.State.cs
│   │   │   ├── HudWindow.Windowing.cs
│   │   │   ├── HudWindow.xaml
│   │   │   ├── HudWindow.xaml.cs
│   │   │   └── WindowSlideAnimator.cs
│   │   ├── AGENTS.md  — [agent-instructions] HUD overlay surface — non-focusable, click-through, always-on-top windows that…
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Hud.csproj
│   │   ├── DeckleHudSource.cs
│   │   ├── HudMessage.xaml
│   │   ├── HudMessage.xaml.cs
│   │   ├── JOURNAL.md  — [module-journal] Diagnosis notes, render doctrine, and deferred work for Deckle.Hud — read on de…
│   │   └── ProximityRollupAggregator.cs
│   ├── Deckle.Inference.Onnx/
│   │   ├── AGENTS.md  — [agent-instructions] ONNX Runtime CPU inference substrate — isolates the OnnxRuntime dependency behi…
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Inference.Onnx.csproj
│   │   └── OnnxModelSession.cs
│   ├── Deckle.Input/
│   │   ├── Injection/
│   │   │   └── MouseInjector.cs
│   │   ├── Interop/
│   │   │   ├── HidInterop.cs
│   │   │   ├── LowLevelMouseHookInterop.cs
│   │   │   ├── RawInputInterop.cs
│   │   │   ├── SendInputInterop.cs
│   │   │   └── WinEventInterop.cs
│   │   ├── Keyboard/
│   │   │   ├── IKeyboardInputHost.cs
│   │   │   ├── KeyboardInputHost.cs
│   │   │   ├── KeyboardKeyEvent.cs
│   │   │   └── MouseWheelEvent.cs
│   │   ├── Telemetry/
│   │   │   ├── ContactFrameRecorder.cs
│   │   │   └── WheelEventRecorder.cs
│   │   ├── Touchpad/
│   │   │   ├── ContactFrame.cs
│   │   │   ├── ContactFrameAssembler.cs
│   │   │   ├── TouchpadCapabilities.cs
│   │   │   ├── TouchpadContact.cs
│   │   │   ├── TouchpadParser.cs
│   │   │   └── TouchpadReport.cs
│   │   ├── AGENTS.md  — [agent-instructions] Input support module — Raw Input host, Precision Touchpad HID parsing, the Send…
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Input.csproj
│   │   ├── DeckleInputSource.cs
│   │   ├── JOURNAL.md  — [module-journal] Dated decisions and findings for Deckle.Input — founding choices, measurements,…
│   │   ├── MouseWheelSettings.cs
│   │   ├── MouseWheelSettingsService.cs
│   │   └── RawInputHost.cs
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
│   │   │   ├── TrackpadViewModel.cs
│   │   │   └── TrackpadViewModel.Settings.cs
│   │   ├── AGENTS.md  — [agent-instructions] Trackpad domain module — three-finger drag recognizer and engine, module settin…
│   │   ├── CLAUDE.md
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
│   │   │   ├── RunningProcesses.cs
│   │   │   ├── Shortcut.cs
│   │   │   ├── UninstallEntry.cs
│   │   │   └── UserEnvironment.cs
│   │   ├── Release/
│   │   │   └── ReleaseResolver.cs
│   │   ├── Ui/
│   │   │   └── ConsoleUi.cs
│   │   ├── AGENTS.md  — [agent-instructions] NativeAOT console stub that downloads, installs, and uninstalls Deckle per-user…
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
│   │   ├── AGENTS.md  — [agent-instructions] Generalist light-output driver — the ILightOutput abstraction and the REST Hue…
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Lighting.csproj
│   │   ├── DeckleLightingSource.cs
│   │   ├── ILightOutput.cs
│   │   ├── JOURNAL.md  — [module-journal] Color-science decisions and the Night Owl gamut bug for Deckle.Lighting — read…
│   │   ├── LightColor.cs
│   │   └── LightDescriptor.cs
│   ├── Deckle.Lighting.Ambient/
│   │   ├── Engine/
│   │   │   ├── AmbientColorPipeline.cs
│   │   │   ├── AmbientEngine.CaptureEvents.cs
│   │   │   ├── AmbientEngine.cs
│   │   │   ├── AmbientEngine.HueEvents.cs
│   │   │   ├── AmbientEngine.Lifecycle.cs
│   │   │   ├── AmbientEngine.PushLoop.cs
│   │   │   ├── AmbientEngineState.cs
│   │   │   ├── AmbientHueChangeAttributor.cs
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
│   │   │   ├── PromptTemplates.cs
│   │   │   └── RewriteService.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   ├── BoolToVisibilityConverter.cs
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
│   │   ├── ViewModels/
│   │   │   ├── LlmGeneralViewModel.cs
│   │   │   └── LlmGeneralViewModel.Settings.cs
│   │   ├── Deckle.Llm.Rewrite.csproj
│   │   ├── LlmSettings.cs
│   │   ├── LlmSettingsMigrations.cs
│   │   └── LlmSettingsService.cs
│   ├── Deckle.Notifications/
│   │   ├── Catalog/
│   │   │   ├── NotificationAction.cs
│   │   │   ├── NotificationCatalog.cs
│   │   │   └── NotificationDescriptor.cs
│   │   ├── Channels/
│   │   │   └── Toast/
│   │   │       ├── ToastActivation.cs
│   │   │       └── ToastChannel.cs
│   │   ├── Dispatch/
│   │   │   ├── INotificationChannel.cs
│   │   │   ├── NotificationDispatcher.cs
│   │   │   └── NotificationResponse.cs
│   │   ├── AGENTS.md  — [agent-instructions] Notification catalogue, dispatcher, and delivery channels — modules declare des…
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Notifications.csproj
│   │   └── DeckleNotificationsSource.cs
│   ├── Deckle.Playground/
│   │   ├── Models/
│   │   │   └── TuningModel.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── ViewModels/
│   │   │   ├── AmbientViewModel.cs
│   │   │   ├── HudViewModel.cs
│   │   │   └── SegmentationViewModel.cs
│   │   ├── Views/
│   │   │   ├── Ambient/
│   │   │   │   ├── AmbientPage.HdrTuning.cs
│   │   │   │   ├── AmbientPage.Hue.cs
│   │   │   │   ├── AmbientPage.LightZones.cs
│   │   │   │   ├── AmbientPage.Preview.cs
│   │   │   │   ├── AmbientPage.ScreenCapture.cs
│   │   │   │   ├── AmbientPage.xaml
│   │   │   │   └── AmbientPage.xaml.cs
│   │   │   ├── Hud/
│   │   │   │   ├── HudPage.Expanders.cs
│   │   │   │   ├── HudPage.RowFactories.cs
│   │   │   │   ├── HudPage.xaml
│   │   │   │   └── HudPage.xaml.cs
│   │   │   ├── Segmentation/
│   │   │   │   ├── SegmentationPage.xaml
│   │   │   │   └── SegmentationPage.xaml.cs
│   │   │   ├── HomePage.xaml
│   │   │   ├── HomePage.xaml.cs
│   │   │   ├── PlaygroundWindow.xaml
│   │   │   └── PlaygroundWindow.xaml.cs
│   │   ├── AGENTS.md  — [agent-instructions] Dev-only tuning sandbox — live-adjust the running pipelines without a rebuild,…
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Playground.csproj
│   │   ├── DecklePlaygroundSource.cs
│   │   ├── PlaygroundNotifications.cs
│   │   └── PlaygroundShell.cs
│   ├── Deckle.Security/
│   │   ├── Deckle.Security.csproj
│   │   ├── ISecretVault.cs
│   │   ├── SecretVault.cs
│   │   └── SecretVaultException.cs
│   ├── Deckle.Settings/
│   │   ├── Controls/
│   │   │   ├── FolderPickerCard.xaml
│   │   │   ├── FolderPickerCard.xaml.cs
│   │   │   ├── FolderPickerEditableCard.xaml
│   │   │   ├── FolderPickerEditableCard.xaml.cs
│   │   │   ├── TunableRow.xaml
│   │   │   ├── TunableRow.xaml.cs
│   │   │   ├── TuningCard.xaml
│   │   │   └── TuningCard.xaml.cs
│   │   ├── Dialogs/
│   │   │   ├── ApplicationLogConsentDialog.cs
│   │   │   ├── AudioCorpusConsentDialog.cs
│   │   │   ├── AutocorrectDecisionsConsentDialog.cs
│   │   │   ├── AutocorrectTextConsentDialog.cs
│   │   │   ├── CorpusConsentDialog.cs
│   │   │   └── MicrophoneTelemetryConsentDialog.cs
│   │   ├── Modules/
│   │   │   ├── SettingsModuleDescriptor.cs
│   │   │   └── SettingsModuleRegistry.cs
│   │   ├── Pages/
│   │   │   ├── DiagnosticsPage.xaml
│   │   │   ├── DiagnosticsPage.xaml.cs
│   │   │   ├── GeneralPage.xaml
│   │   │   ├── GeneralPage.xaml.cs
│   │   │   ├── RecordingPage.xaml
│   │   │   └── RecordingPage.xaml.cs
│   │   ├── Persistence/
│   │   │   ├── AppSettings.cs
│   │   │   ├── SettingsBackupService.cs
│   │   │   ├── SettingsBootstrap.cs
│   │   │   └── SettingsService.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── ViewModels/
│   │   │   ├── DiagnosticsViewModel.cs
│   │   │   ├── DiagnosticsViewModel.Settings.cs
│   │   │   ├── GeneralViewModel.cs
│   │   │   ├── GeneralViewModel.Settings.cs
│   │   │   ├── RecordingViewModel.cs
│   │   │   └── RecordingViewModel.Settings.cs
│   │   ├── AGENTS.md  — [agent-instructions] Settings shell — aggregates module-owned pages in a NavigationView, owns non-mo…
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Settings.csproj
│   │   ├── DeckleSettingsSource.cs
│   │   ├── SettingsHost.cs
│   │   ├── SettingsWindow.xaml
│   │   └── SettingsWindow.xaml.cs
│   ├── Deckle.Setup/
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── AGENTS.md  — [agent-instructions] First-run wizard provisioning ASR runtimes and models — owns the flow, delegate…
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
│   │   ├── AGENTS.md  — [agent-instructions] System shell module — the low-level OS primitives (hotkeys, tray, autostart, me…
│   │   ├── AutostartService.cs
│   │   ├── CLAUDE.md
│   │   ├── CursorMovementSignal.cs
│   │   ├── Deckle.Shell.csproj
│   │   ├── DeckleShellSource.cs
│   │   ├── DispatcherQueueExtensions.cs
│   │   ├── ElevatedStartupService.cs
│   │   ├── HotkeyManager.cs
│   │   ├── IconAssets.cs
│   │   ├── MessageOnlyHost.cs
│   │   ├── ResizeCoalescer.cs
│   │   ├── ResizeGesture.cs
│   │   ├── StartupService.cs
│   │   └── TrayIconManager.cs
│   ├── Deckle.Shell.TaskbarCover/
│   │   ├── Interop/
│   │   │   └── TaskbarCoverNativeMethods.cs
│   │   ├── AGENTS.md  — [agent-instructions] Taskbar cover module — an opaque topmost band that masks the taskbar until the…
│   │   ├── CLAUDE.md
│   │   ├── CoverGeometry.cs
│   │   ├── Deckle.Shell.TaskbarCover.csproj
│   │   ├── DeckleShellTaskbarCoverSource.cs
│   │   ├── TaskbarCoverHost.cs
│   │   ├── TaskbarCoverSettings.cs
│   │   └── TaskbarCoverSettingsService.cs
│   ├── Deckle.Shell.TrayMenu/
│   │   ├── Interop/
│   │   │   └── TrayMenuNativeMethods.cs
│   │   ├── Themes/
│   │   │   └── TrayMenu.xaml
│   │   ├── AGENTS.md  — [agent-instructions] WinUI 3 tray context menu — the carrier-window pattern and the DWM pitfalls it…
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Shell.TrayMenu.csproj
│   │   ├── DeckleShellTrayMenuSource.cs
│   │   ├── JOURNAL.md  — [module-journal] Dated diagnostics for Deckle.Shell.TrayMenu — the tray-menu density, gap, and f…
│   │   ├── TrayContextMenuHost.cs
│   │   ├── TrayContextMenuHost.Flyout.cs
│   │   ├── TrayContextMenuHost.Measure.cs
│   │   ├── TrayContextMenuHost.Show.cs
│   │   ├── TrayContextMenuHost.Window.cs
│   │   └── TraySwitchMenuItem.cs
│   ├── Deckle.Speech/
│   │   ├── AGENTS.md  — [agent-instructions] Read-aloud (TTS) output module — the ISpeechBackend boundary, the placeholder s…
│   │   ├── ChatterboxSpeechBackend.cs
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Speech.csproj
│   │   ├── DeckleSpeechSource.cs
│   │   ├── ISpeechBackend.cs
│   │   ├── JOURNAL.md  — [module-journal] Diagnosis notes and kept decisions for Deckle.Speech — read on demand when chas…
│   │   ├── SpeechEngine.cs
│   │   ├── SpeechSettings.cs
│   │   └── SpeechSettingsService.cs
│   ├── Deckle.Transcription/
│   │   ├── Corpus/
│   │   │   ├── CorpusTier.cs
│   │   │   ├── PromptTemplateHash.cs
│   │   │   └── WavCorpusWriter.cs
│   │   ├── Curves/
│   │   │   └── UnitBezier.cs
│   │   ├── Engine/
│   │   │   ├── IAsrBackend.cs
│   │   │   ├── PipelineProduction.cs
│   │   │   ├── TextMetrics.cs
│   │   │   ├── TranscriptionEngine.cs
│   │   │   ├── TranscriptionEngine.Finalize.cs
│   │   │   ├── TranscriptionEngine.Lifecycle.cs
│   │   │   ├── TranscriptionEngine.MonolithicPipeline.cs
│   │   │   ├── TranscriptionEngine.Pipeline.cs
│   │   │   ├── TranscriptionEngine.StateMachine.cs
│   │   │   ├── TranscriptionEngine.StreamingPipeline.cs
│   │   │   └── TranscriptionEngine.Telemetry.cs
│   │   ├── Setup/
│   │   │   └── ModelEntry.cs
│   │   ├── Streaming/
│   │   │   ├── EnergySegmenter.cs
│   │   │   ├── EnergySegmenterSettings.cs
│   │   │   └── Utterance.cs
│   │   ├── Strings/
│   │   │   └── en-US/
│   │   │       └── Resources.resw
│   │   ├── Ui/
│   │   │   └── Controls/
│   │   │       ├── HangoverCurveCanvas.xaml
│   │   │       └── HangoverCurveCanvas.xaml.cs
│   │   ├── ViewModels/
│   │   │   ├── WhisperViewModel.cs
│   │   │   └── WhisperViewModel.Settings.cs
│   │   ├── AGENTS.md  — [agent-instructions] Backend-agnostic transcription orchestrator — the IAsrBackend boundary, the mod…
│   │   ├── CLAUDE.md
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
│   │   ├── AGENTS.md  — [agent-instructions] whisper.cpp ASR backend (IAsrBackend) — native log compaction, the whisper repe…
│   │   ├── CLAUDE.md
│   │   ├── Deckle.Transcription.Whisper.csproj
│   │   ├── KnownHallucinations.cs
│   │   ├── RepetitionDetector.cs
│   │   ├── WhisperBackend.cs
│   │   └── WhisperNativeLogCompactor.cs
│   ├── Deckle.Vad/
│   │   ├── AGENTS.md
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
│       ├── AGENTS.md  — [agent-instructions] Screen-capture and frame-analysis module — DXGI Output Duplication, the transie…
│       ├── CapturedFrame.cs
│       ├── CLAUDE.md
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
│   │   ├── DialogueToolCatalogTests.cs
│   │   ├── FakeSecretVault.cs
│   │   ├── ManagementToolCatalogTests.cs
│   │   ├── McpClientTokensTests.cs
│   │   ├── McpHttpHostTests.cs
│   │   ├── McpServerTests.cs
│   │   ├── McpToolsetTests.cs
│   │   └── ToolCatalogTests.cs
│   ├── Deckle.Anytype.Tests/
│   │   ├── AnytypeCredentialsTests.cs
│   │   ├── BackendSupervisorTests.cs
│   │   ├── Deckle.Anytype.Tests.csproj
│   │   ├── DevSpaceTests.cs
│   │   ├── DialogueGesturesTests.cs
│   │   ├── LiveTagResolverTests.cs
│   │   ├── ManagementGesturesTests.cs
│   │   ├── MarkdownBodyTests.cs
│   │   ├── ProjectGesturesTests.cs
│   │   ├── QueryGesturesTests.cs
│   │   ├── SessionGesturesTests.cs
│   │   ├── SpaceWriteLockTests.cs
│   │   └── TaskGesturesTests.cs
│   ├── Deckle.Audio.Tests/
│   │   ├── Deckle.Audio.Tests.csproj
│   │   ├── MicLevelCheckTests.cs
│   │   ├── MicrophoneTelemetryCalculatorTests.cs
│   │   ├── PcmConversionTests.cs
│   │   └── TranscriptionPreprocessorTests.cs
│   ├── Deckle.Autocorrect.Tests/
│   │   ├── AccentFoldingTests.cs
│   │   ├── AccentIndexTests.cs
│   │   ├── AssemblyInfo.cs
│   │   ├── AutocorrectDecisionMapTests.cs
│   │   ├── AutocorrectEngineBackspaceTests.cs
│   │   ├── AutocorrectEngineCorrectionTests.cs
│   │   ├── AutocorrectEngineGateTests.cs
│   │   ├── AutocorrectEngineHarness.cs
│   │   ├── AutocorrectEngineLearningTests.cs
│   │   ├── AutocorrectEngineLifecycleTests.cs
│   │   ├── AutocorrectEngineObservabilityTests.cs
│   │   ├── AutocorrectLexiconArtifactsTests.cs
│   │   ├── AutocorrectSettingsTests.cs
│   │   ├── BigramPairDisambiguatorTests.cs
│   │   ├── BuildDataGestureTests.cs
│   │   ├── CamembertRerankerIntegrationTests.cs
│   │   ├── CandidateCompletionPlanTests.cs
│   │   ├── CasePatternTests.cs
│   │   ├── ConservativeTypoCorrectorTests.cs
│   │   ├── CorpusReaderTests.cs
│   │   ├── CorrectionBenchmarkSummaryTests.cs
│   │   ├── CorrectionTraceTests.cs
│   │   ├── Deckle.Autocorrect.Tests.csproj
│   │   ├── DiacriticsRestorerTests.cs
│   │   ├── ElisionCorrectorTests.cs
│   │   ├── FrenchSentenceRerankerTests.cs
│   │   ├── FrequencyLexiconTests.cs
│   │   ├── GrammarCorrectorTests.cs
│   │   ├── HarvestDataTests.cs
│   │   ├── HarvestFilterTests.cs
│   │   ├── InjectionPlanTests.cs
│   │   ├── KeyDecoderTests.cs
│   │   ├── MarginCalibrationTests.cs
│   │   ├── MorphalouReaderTests.cs
│   │   ├── OnnxSentenceScorerTests.cs
│   │   ├── OnnxSlotRerankerTests.cs
│   │   ├── PairModelTrainerTests.cs
│   │   ├── PersonalDictionaryTests.cs
│   │   ├── ReplayRunnerTests.cs
│   │   ├── RestorationEvaluatorTests.cs
│   │   ├── SentenceAlignmentTests.cs
│   │   ├── SentenceCorpusTests.cs
│   │   ├── SentenceReplayGestureTests.cs
│   │   ├── SentenceReplayTests.cs
│   │   ├── SentenceRerankCoordinatorTests.cs
│   │   ├── TypedWordTrackerTests.cs
│   │   ├── VerbMorphologyTests.cs
│   │   └── WordBoundariesTests.cs
│   ├── Deckle.Catalog.Tests/
│   │   ├── Deckle.Catalog.Tests.csproj
│   │   └── SettingFactoryTests.cs
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
│   │   └── JsonlSinkRotationTests.cs
│   ├── Deckle.Hud.Tests/
│   │   ├── Deckle.Hud.Tests.csproj
│   │   ├── DeckleHudSourceTests.cs
│   │   └── ProximityRollupAggregatorTests.cs
│   ├── Deckle.Input.Tests/
│   │   ├── ContactFrameAssemblerTests.cs
│   │   ├── Deckle.Input.Tests.csproj
│   │   └── LowLevelMouseHookInteropTests.cs
│   ├── Deckle.Input.Trackpad.Tests/
│   │   ├── Deckle.Input.Trackpad.Tests.csproj
│   │   └── ThreeFingerDragRecognizerTests.cs
│   ├── Deckle.Lighting.Ambient.Tests/
│   │   ├── AmbientHueChangeAttributorTests.cs
│   │   ├── Deckle.Lighting.Ambient.Tests.csproj
│   │   └── DeckleAmbientSourceTests.cs
│   ├── Deckle.Notifications.Tests/
│   │   ├── Deckle.Notifications.Tests.csproj
│   │   ├── DeckleNotificationsSourceTests.cs
│   │   ├── Descriptors.cs
│   │   ├── FakeNotificationChannel.cs
│   │   ├── NotificationCatalogTests.cs
│   │   └── NotificationDispatcherTests.cs
│   ├── Deckle.Security.Tests/
│   │   ├── Deckle.Security.Tests.csproj
│   │   └── SecretVaultTests.cs
│   ├── Deckle.Shell.TaskbarCover.Tests/
│   │   ├── CoverGeometryTests.cs
│   │   └── Deckle.Shell.TaskbarCover.Tests.csproj
│   ├── Deckle.Shell.Tests/
│   │   ├── Deckle.Shell.Tests.csproj
│   │   ├── DispatcherQueueExtensionsTests.cs
│   │   └── ResizeGestureTests.cs
│   ├── Deckle.Speech.Tests/
│   │   ├── Deckle.Speech.Tests.csproj
│   │   ├── SpeechEngineTests.cs
│   │   └── SpeechSettingsTests.cs
│   ├── Deckle.TestSupport/
│   │   ├── Deckle.TestSupport.csproj
│   │   ├── EventArgsExtensions.cs
│   │   ├── TestEventListener.cs
│   │   ├── WindowsAppSdkBootstrap.cs
│   │   └── WindowsAppSdkModuleInitializer.cs
│   ├── Deckle.Transcription.Tests/
│   │   ├── Deckle.Transcription.Tests.csproj
│   │   ├── EnergySegmenterTests.cs
│   │   ├── StreamingBackendAudioTests.cs
│   │   ├── TranscriptionSettingsMigrationTests.cs
│   │   └── UnitBezierTests.cs
│   ├── Deckle.Transcription.Whisper.Tests/
│   │   ├── Deckle.Transcription.Whisper.Tests.csproj
│   │   ├── KnownHallucinationsTests.cs
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
├── AGENTS.md  — [agent-instructions] Root agent-instructions for Deckle — identity, hard rules, posture, and where t…
├── CHANGELOG.md
├── CLAUDE.md
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