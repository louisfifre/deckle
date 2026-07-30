# Inventaire des exposables Deckle

> Support de consultation pour l'arbitrage — 30 juillet 2026.

**Portée.** Toutes les valeurs déclarées dans le code des 41 modules balayés : réglages persistés, constantes de tuning, paramètres Playground, valeurs figées par construction. Chaque module a été relu à la source, ligne par ligne, par un agent dédié : les faux positifs du balayage automatique ont été retirés, les entrées manquées ont été ajoutées à la main, les entrées douteuses ont été conservées et signalées.

**Ce document ne tranche rien.** Il expose et il signale. Exposer, geler, déplacer ou supprimer reste une décision de Louis. La colonne « Juin » indique si l'entrée figurait déjà dans `docs/inventaire-settings.md` (juin 2026). La colonne « Doute » porte la réserve de l'agent qui a vérifié l'entrée — c'est là que se lisent les arbitrages à rendre.

**Hors périmètre.** Les littéraux XAML (bornes `Minimum`/`Maximum`/`StepFrequency` des sliders), les tables lexicales préfixées `_`, et tout ce qui vit sous `tests/`.

Total : **693 entrées** réparties sur **41 modules**.

| Module | Entrées |
|---|---|
| Deckle.Anytype | 12 |
| Deckle.Anytype.Mcp | 3 |
| Deckle.App | 4 |
| Deckle.Audio | 45 |
| Deckle.Autocorrect | 53 |
| Deckle.Autocorrect.Lab | 20 |
| Deckle.Autocorrect.Mlm | 4 |
| Deckle.Autocorrect.Onnx | 1 |
| Deckle.Autocorrect.Probe | 41 |
| Deckle.Catalog | 2 |
| Deckle.Composition | 62 |
| Deckle.Core | 9 |
| Deckle.Diagnostics | 5 |
| Deckle.Diagnostics.Logging | 7 |
| Deckle.Diagnostics.Telemetry | 8 |
| Deckle.Home | 3 |
| Deckle.Hud | 26 |
| Deckle.Input | 12 |
| Deckle.Input.PrecisionScroll | 12 |
| Deckle.Input.Trackpad | 10 |
| Deckle.Install | 4 |
| Deckle.Installer | 1 |
| Deckle.Lighting | 8 |
| Deckle.Lighting.Ambient | 40 |
| Deckle.Llm | 1 |
| Deckle.Llm.Rewrite | 54 |
| Deckle.Modules | 6 |
| Deckle.Notifications | 1 |
| Deckle.Playground | 63 |
| Deckle.Security | 1 |
| Deckle.Settings | 17 |
| Deckle.Setup | 9 |
| Deckle.Shell | 3 |
| Deckle.Shell.TaskbarCover | 3 |
| Deckle.Shell.TrayMenu | 2 |
| Deckle.Speech | 5 |
| Deckle.Transcription | 62 |
| Deckle.Transcription.Whisper | 1 |
| Deckle.Travel | 59 |
| Deckle.Vad | 4 |
| Deckle.Vision | 10 |

---

## Deckle.Anytype

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| MaxRetries | tuning-constant | `1` | src/Deckle.Anytype/Api/AnytypeApiClient.cs:39 | non |  |
| DefaultBackoff | tuning-constant | `TimeSpan.FromSeconds(1)` | src/Deckle.Anytype/Api/AnytypeApiClient.cs:40 | non | Fallback only: used when the response omits Retry-After. |
| InitialBackoff | tuning-constant | `TimeSpan.FromMilliseconds(25)` | src/Deckle.Anytype/Api/SpaceWriteLock.cs:29 | non | Cross-process lock poll cadence; perceivable only as write latency under contention. |
| MaxBackoff | tuning-constant | `TimeSpan.FromMilliseconds(200)` | src/Deckle.Anytype/Api/SpaceWriteLock.cs:30 | non | Same doubt as InitialBackoff; the pair moves together. |
| DefaultBaseUrl | tuning-constant | `"http://127.0.0.1:31012"` | src/Deckle.Anytype/Backend/BackendHealthProbe.cs:22 | non |  |
| ProbeTimeout | tuning-constant | `TimeSpan.FromSeconds(2)` | src/Deckle.Anytype/Backend/BackendHealthProbe.cs:28 | non |  |
| ExecutablePath | tuning-constant | `Path.Combine(InstallDirectory, "anytype.exe")` | src/Deckle.Anytype/Backend/BackendInstallation.cs:26 | non | Derived, not a constant: the real exposable is InstallDirectory (same file, line 22) which the sweep missed. Kept as the pointer to it. |
| ReadinessTimeout | tuning-constant | `TimeSpan.FromSeconds(20)` | src/Deckle.Anytype/Backend/BackendSupervisor.cs:45 | non |  |
| ProbeInterval | tuning-constant | `TimeSpan.FromMilliseconds(500)` | src/Deckle.Anytype/Backend/BackendSupervisor.cs:46 | non |  |
| StableUptime | tuning-constant | `TimeSpan.FromMinutes(5)` | src/Deckle.Anytype/Backend/BackendSupervisor.cs:53 | non | Reset threshold of the restart ladder; its sibling RestartBackoff array (line 51) was missed by the sweep and belongs with it. |
| MaxPreviews | tuning-constant | `32` | src/Deckle.Anytype/Gestures/SchemaPreviewStore.cs:5 | non |  |
| PreviewLifetime | tuning-constant | `TimeSpan.FromMinutes(15)` | src/Deckle.Anytype/Gestures/SchemaPreviewStore.cs:6 | non | Perceivable: a schema preview silently expires past this window. |

## Deckle.Anytype.Mcp

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| DefaultPort | tuning-constant | `33255` | src/Deckle.Anytype.Mcp/Http/McpHttpHost.cs:34 | non |  |
| EndpointPath | tuning-constant | `"/mcp"` | src/Deckle.Anytype.Mcp/Http/McpHttpHost.cs:36 | non | Protocol path, but it is half of the BaseUrl a user pastes into an MCP client config; kept as the port's companion, drop if the port alone suffices. |
| SessionIdleLimit | tuning-constant | `TimeSpan.FromHours(24)` | src/Deckle.Anytype.Mcp/Http/McpHttpHost.cs:51 | non |  |

## Deckle.App

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| SentenceJudgeMargin | tuning-constant | `1.0` | src/Deckle.App/App.Autocorrect.cs:37 | non | Strongest keeper of the batch. Comment documents a maintainer-decided precision/coverage tradeoff (92.2%/20.8% at 1.0 vs 90.8%/41.0% at 0.5) and says it is to be relaxed as the corpus grows. Per-export: must be recalibrated if the DML int4 export changes — so exposing it needs a per-export guard. |
| EntryDrainBatchSize | tuning-constant | `256` | src/Deckle.App/LogWindow.Model.cs:15 | non | UI drain batching for the log window. Perceivable only as responsiveness under load; likely frozen-in-code rather than a setting. |
| MaxEntries | tuning-constant | `5000` | src/Deckle.App/LogWindow.Model.cs:96 | non | Sweep reports declaration 'field'; it is actually a method-local const inside the enqueue loop, so exposing it means hoisting it first. Value verified. User-perceivable: it caps log window scrollback. |
| SearchCollapseThreshold | tuning-constant | `520.0` | src/Deckle.App/LogWindow.xaml.cs:72 | non | Responsive breakpoint (DIPs) at which the log window SearchBox collapses to an icon. Comment pins it to the Windows 11 Task Manager pattern — a design constant, probably not exposable. |

## Deckle.Audio

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| AudioFileDecoder.TargetSampleRate | tuning-constant | `16000` | src/Deckle.Audio/AudioFileDecoder.cs:29 | non | Fixed pipeline format, locked to MicrophoneCapture; frozen contract, not a knob. Keep only as inventory trace. |
| AudioFileDecoder.TargetChannels | tuning-constant | `1` | src/Deckle.Audio/AudioFileDecoder.cs:30 | non | Same frozen format contract. |
| AudioFileDecoder.TargetBitsPerSample | tuning-constant | `32` | src/Deckle.Audio/AudioFileDecoder.cs:31 | non | Same frozen format contract. |
| AudioLevelMapper.EmaAlpha | tuning-constant | `0.25f` | src/Deckle.Audio/AudioLevelMapper.cs:34 | non | Genuine unexposed tuning: HUD stroke smoothing tau ~0.15s. Public static mutable field, nothing writes it today. |
| AudioLevelMapper.MinDbfs | tuning-constant | `-55f` | src/Deckle.Audio/AudioLevelMapper.cs:72 | oui | Runtime mirror of LevelWindowSettings.MinDbfs (pushed at startup and on change). Same knob, second declaration site - dedupe against CaptureSettings.cs:58. |
| AudioLevelMapper.MaxDbfs | tuning-constant | `-32f` | src/Deckle.Audio/AudioLevelMapper.cs:73 | oui | Runtime mirror of LevelWindowSettings.MaxDbfs. |
| AudioLevelMapper.DbfsCurveExponent | tuning-constant | `1.0f` | src/Deckle.Audio/AudioLevelMapper.cs:74 | oui | Runtime mirror of LevelWindowSettings.DbfsCurveExponent. Note the file comment still documents an older -40/-22/exp2.0 calibration - comment is stale vs the code. |
| CaptureSettings.AudioInputDeviceId | persisted-setting | `-1` | src/Deckle.Audio/CaptureSettings.cs:15 | oui | June marks it bespoke (runtime waveIn enumeration). -1 = WAVE_MAPPER. |
| CaptureSettings.MaxRecordingDurationSeconds | persisted-setting | `20 * 60 (1200 s)` | src/Deckle.Audio/CaptureSettings.cs:23 | non | Persisted, no UI found in the sweep scope. Sweep value string was the unevaluated expression; real default 1200. 0 = no cap. |
| CaptureSettings.LevelWindow | persisted-setting | `new LevelWindowSettings()` | src/Deckle.Audio/CaptureSettings.cs:25 | oui | Container property, not a knob - its five members are the exposables. |
| CaptureSettings.Preprocessing | persisted-setting | `new PreprocessingSettings()` | src/Deckle.Audio/CaptureSettings.cs:34 | oui | Container property; the opt-in toggle is Preprocessing.Enabled, fine params live in the Playground. |
| LevelWindowSettings.MinDbfs | persisted-setting | `-55f` | src/Deckle.Audio/CaptureSettings.cs:58 | oui |  |
| LevelWindowSettings.MaxDbfs | persisted-setting | `-32f` | src/Deckle.Audio/CaptureSettings.cs:59 | oui |  |
| LevelWindowSettings.DbfsCurveExponent | persisted-setting | `1.0f` | src/Deckle.Audio/CaptureSettings.cs:60 | oui |  |
| LevelWindowSettings.AutoCalibrationEnabled | persisted-setting | `false` | src/Deckle.Audio/CaptureSettings.cs:61 | oui |  |
| LevelWindowSettings.AutoCalibrationSamples | persisted-setting | `5` | src/Deckle.Audio/CaptureSettings.cs:62 | oui | June lists the LevelWindow block but not this member explicitly; rolling-window size for the auto-calibration heuristic. |
| WaveInLoop.NormalVoiceDbfsThreshold | tuning-constant | `-45.0` | src/Deckle.Audio/Internal/WaveInLoop.cs:102 | non | Method-local const. Drives the user-facing low-audio warning - user-perceivable, so kept. |
| WaveInLoop.NormalVoiceSustainedMs | tuning-constant | `200` | src/Deckle.Audio/Internal/WaveInLoop.cs:103 | non | Method-local const gating the same warning. |
| WaveInLoop.WarnAfterSilenceMs | tuning-constant | `5000` | src/Deckle.Audio/Internal/WaveInLoop.cs:104 | non | Method-local const: delay before the low-audio warning fires. |
| WaveInLoop.SubWindowMs | tuning-constant | `50` | src/Deckle.Audio/Internal/WaveInLoop.cs:382 | non | Method-local const. Structural cadence: telemetry and MicrophoneTelemetryCalculator.SubWindowSamples=800 assume it. Not independently tunable. |
| MicrophoneCapture.N_BUFFERS | tuning-constant | `4` | src/Deckle.Audio/MicrophoneCapture.cs:40 | non | waveIn buffer count - implementation detail, but it sets capture latency/robustness. Kept under doubt. |
| MicLevelTester.DefaultMeasureSeconds | tuning-constant | `5` | src/Deckle.Audio/MicLevelTester.cs:23 | non | Duration of the Settings mic check - directly user-perceivable. |
| MicLevelCheck.RecommendDeltaDb | tuning-constant | `6.0` | src/Deckle.Audio/Preprocessing/MicLevelCheck.cs:26 | non | Self-declared provisional engineer guess; decides the 'enable the lift' recommendation shown to the user. |
| MicLevelCheck.MarginalDeltaDb | tuning-constant | `2.0` | src/Deckle.Audio/Preprocessing/MicLevelCheck.cs:30 | non | Same provisional advice thresholds. |
| PreprocessingSettings.Enabled | persisted-setting | `false` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:27 | oui | The only Preprocessing knob in Settings > Recording; the rest are Playground-only per CaptureSettings comment. |
| PreprocessingSettings.HighPassEnabled | persisted-setting | `true` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:33 | oui |  |
| PreprocessingSettings.HighPassHz | persisted-setting | `90f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:34 | oui |  |
| PreprocessingSettings.GateEnabled | persisted-setting | `false` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:40 | oui |  |
| PreprocessingSettings.GateThresholdDbfs | persisted-setting | `-55f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:41 | oui |  |
| PreprocessingSettings.GateRatio | persisted-setting | `2f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:42 | oui |  |
| PreprocessingSettings.GateAttackMs | persisted-setting | `5f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:43 | oui |  |
| PreprocessingSettings.GateReleaseMs | persisted-setting | `150f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:44 | oui |  |
| PreprocessingSettings.CompressorEnabled | persisted-setting | `true` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:50 | oui |  |
| PreprocessingSettings.CompThresholdDbfs | persisted-setting | `-24f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:51 | oui |  |
| PreprocessingSettings.CompRatio | persisted-setting | `2f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:52 | oui |  |
| PreprocessingSettings.CompKneeDb | persisted-setting | `6f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:53 | oui |  |
| PreprocessingSettings.CompAttackMs | persisted-setting | `8f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:54 | oui |  |
| PreprocessingSettings.CompReleaseMs | persisted-setting | `150f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:55 | oui |  |
| PreprocessingSettings.TargetRmsDbfs | persisted-setting | `-20f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:64 | oui | Makeup-gain pair; June flags the whole DSP block as frozen/Playground-only - confirm. |
| PreprocessingSettings.MaxMakeupGainDb | persisted-setting | `24f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:65 | oui | Makeup-gain pair; June flags the whole DSP block as frozen/Playground-only - confirm. |
| PreprocessingSettings.LimiterEnabled | persisted-setting | `true` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:70 | oui |  |
| PreprocessingSettings.LimiterCeilingDbfs | persisted-setting | `-1f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:71 | oui |  |
| PreprocessingSettings.LimiterReleaseMs | persisted-setting | `50f` | src/Deckle.Audio/Preprocessing/PreprocessingSettings.cs:72 | oui |  |
| TranscriptionPreprocessor.SampleRate | tuning-constant | `16000.0` | src/Deckle.Audio/Preprocessing/TranscriptionPreprocessor.cs:42 | non | Frozen module-wide capture format the DSP time constants depend on. Not a knob. |
| MicrophoneTelemetryCalculator.SubWindowSamples | tuning-constant | `800` | src/Deckle.Audio/Telemetry/MicrophoneTelemetryCalculator.cs:135 | non | 800 samples = 50 ms @16 kHz; must stay in lock-step with WaveInLoop.SubWindowMs. Structural, not tunable. |

## Deckle.Autocorrect

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| AutocorrectSettings.Enabled | persisted-setting | `true` | src/Deckle.Autocorrect/AutocorrectSettings.cs:12 | oui |  |
| AutocorrectSettings.Apps | persisted-setting | `new(StringComparer.OrdinalIgnoreCase) { ["notepad"] = true }` | src/Deckle.Autocorrect/AutocorrectSettings.cs:17 | oui | MISSED BY SWEEP (multi-line initializer). Non-composable per June: runtime-enumerated process list. |
| AutocorrectSettings.DomainPacks | persisted-setting | `new(StringComparer.Ordinal)` | src/Deckle.Autocorrect/AutocorrectSettings.cs:26 | non | New since June. Empty map = undecided; DomainActivation falls back to the Windows language list. Bespoke UI (LexicalDomainsPage). |
| AutocorrectSettings.ExcludedWords | persisted-setting | `new()` | src/Deckle.Autocorrect/AutocorrectSettings.cs:31 | non | New since June. Bespoke list editor (AutocorrectPage). |
| AutocorrectSettings.EnrolledProcesses | legacy-deserialization | `null` | src/Deckle.Autocorrect/AutocorrectSettings.cs:37 | non | Legacy v1 allow-list, read once at OnDeserialized then nulled and never written back. Kind corrected from persisted-setting; never surfaced. |
| RestorerOptions.MinWordLength | tuning-constant | `2` | src/Deckle.Autocorrect/Engine/RestorerOptions.cs:15 | non | Kind corrected: NOT persisted. RestorerOptions is constructed with defaults in the live engine (DiacriticsRestorer.cs:48); only the offline eval overrides it. |
| RestorerOptions.DominanceRatio | tuning-constant | `20.0` | src/Deckle.Autocorrect/Engine/RestorerOptions.cs:19 | non | Not persisted. |
| RestorerOptions.MinDominantFrequencyPerMillion | tuning-constant | `1.0` | src/Deckle.Autocorrect/Engine/RestorerOptions.cs:23 | non | Not persisted. |
| RestorerOptions.MinCandidateFrequencyPerMillion | tuning-constant | `0.0` | src/Deckle.Autocorrect/Engine/RestorerOptions.cs:27 | non | Not persisted. |
| RestorerOptions.MinCandidateFrequencyRatio | tuning-constant | `0.01` | src/Deckle.Autocorrect/Engine/RestorerOptions.cs:41 | non | Not persisted. Sentence-stage rarity gate, empirically grounded (100x floor over the maintainer's corpus). |
| RestorerOptions.CorrectValidFormsWithContext | tuning-constant | `false` | src/Deckle.Autocorrect/Engine/RestorerOptions.cs:46 | non | Eval-only flag by doctrine; Lab/Playground territory, not Settings. |
| RestorerOptions.GuardCapitalizedMidSentence | tuning-constant | `false` | src/Deckle.Autocorrect/Engine/RestorerOptions.cs:54 | non | Proper-noun guard, off by default. Plausibly a real user-perceivable knob. |
| TypoOptions.MinWordLength | tuning-constant | `3` | src/Deckle.Autocorrect/Engine/TypoOptions.cs:19 | non | Kind corrected: NOT persisted. Defaults-only in ConservativeTypoCorrector.cs:62. |
| TypoOptions.MinFrequencyPerMillion | tuning-constant | `2.0` | src/Deckle.Autocorrect/Engine/TypoOptions.cs:23 | non | Not persisted. |
| TypoOptions.DominanceRatio | tuning-constant | `5.0` | src/Deckle.Autocorrect/Engine/TypoOptions.cs:27 | non | Not persisted. |
| TypoOptions.MaxEditDistance | tuning-constant | `2` | src/Deckle.Autocorrect/Engine/TypoOptions.cs:31 | non | Not persisted. Closest thing to a user-facing correction-aggressiveness dial. |
| TypoOptions.Edits2MinWordLength | tuning-constant | `6` | src/Deckle.Autocorrect/Engine/TypoOptions.cs:35 | non | Not persisted. |
| TypoOptions.Edits2MinFrequencyPerMillion | tuning-constant | `30.0` | src/Deckle.Autocorrect/Engine/TypoOptions.cs:39 | non | Not persisted. |
| TypoOptions.Edits2DominanceRatio | tuning-constant | `12.0` | src/Deckle.Autocorrect/Engine/TypoOptions.cs:43 | non | Not persisted. |
| DisambiguatorOptions.MarginRatio | tuning-constant | `10.0` | src/Deckle.Autocorrect/Engine/BigramPairDisambiguator.cs:262 | non | Kind corrected: NOT persisted. Only Deckle.Autocorrect.Lab/DataSet.cs:37 overrides it. Measured optimum of the 2026-06-13 eval matrix. |
| DisambiguatorOptions.LiteralBias | tuning-constant | `2.0` | src/Deckle.Autocorrect/Engine/BigramPairDisambiguator.cs:266 | non | Not persisted. |
| DisambiguatorOptions.MinEvidence | tuning-constant | `5` | src/Deckle.Autocorrect/Engine/BigramPairDisambiguator.cs:269 | non | Not persisted. |
| DisambiguatorOptions.MaxContextOrder | tuning-constant | `3` | src/Deckle.Autocorrect/Engine/BigramPairDisambiguator.cs:274 | non | Not persisted. |
| ConservativeTypoCorrector.SentenceCandidateCap | tuning-constant | `4` | src/Deckle.Autocorrect/Engine/ConservativeTypoCorrector.cs:27 | non | Perf/ambiguity bound; borderline exposable. |
| ConservativeTypoCorrector.ContextualFarMaxWordLength | tuning-constant | `5` | src/Deckle.Autocorrect/Engine/ConservativeTypoCorrector.cs:28 | non |  |
| ConservativeTypoCorrector.Edits2MaxWordLength | tuning-constant | `14` | src/Deckle.Autocorrect/Engine/ConservativeTypoCorrector.cs:209 | non | Cost bound on the far tier; kept because it changes what gets corrected. |
| SentenceProposalGate.MaxTextLength | tuning-constant | `512` | src/Deckle.Autocorrect/Engine/SentenceProposalGate.cs:10 | non | Safety budget for whole-sentence proposals; arguably frozen-by-doctrine rather than a knob. |
| SentenceProposalGate.MaxBackspaces | tuning-constant | `160` | src/Deckle.Autocorrect/Engine/SentenceProposalGate.cs:11 | non | Safety budget. |
| SentenceProposalGate.MaxInsertedChars | tuning-constant | `192` | src/Deckle.Autocorrect/Engine/SentenceProposalGate.cs:12 | non | Safety budget. |
| SentenceProposalGate.AbsoluteEditCap | tuning-constant | `24` | src/Deckle.Autocorrect/Engine/SentenceProposalGate.cs:13 | non | Safety budget. |
| SentenceProposalGate.RelativeEditCap | tuning-constant | `0.15` | src/Deckle.Autocorrect/Engine/SentenceProposalGate.cs:14 | non | Safety budget. |
| SentenceRerankCoordinator.BufferCap | tuning-constant | `40` | src/Deckle.Autocorrect/Engine/SentenceRerankCoordinator.cs:65 | non |  |
| SentenceRerankCoordinator.ContextWindow | tuning-constant | `12` | src/Deckle.Autocorrect/Engine/SentenceRerankCoordinator.cs:69 | non | Words each side handed to the masked-LM; latency/quality trade. |
| SentenceRerankCoordinator.SeparatorRunCap | tuning-constant | `8` | src/Deckle.Autocorrect/Engine/SentenceRerankCoordinator.cs:73 | non | Deliberate duplicate of TypedWordTracker.SeparatorRunCap (comment says 'mirrors'). Two declaration sites, one knob. |
| SentenceRerankCoordinator.MaxRewriteTailChars | tuning-constant | `256` | src/Deckle.Autocorrect/Engine/SentenceRerankCoordinator.cs:78 | non | Intrusiveness bound; user-perceivable. |
| SentenceRerankCoordinator.MaxSentenceEditCandidates | tuning-constant | `12` | src/Deckle.Autocorrect/Engine/SentenceRerankCoordinator.cs:83 | non |  |
| SentenceRerankCoordinator.SentenceEnders | tuning-constant | `{ '.', '!', '?', '…' }` | src/Deckle.Autocorrect/Engine/SentenceRerankCoordinator.cs:87 | non | Linguistic table; kept in doubt, arguably frozen. |
| AutocorrectEngine.RollupPeriodMs | tuning-constant | `30_000` | src/Deckle.Autocorrect/Engine/AutocorrectEngine.cs:25 | non | Telemetry roll-up period. Diagnostic, not user-facing. |
| PersonalDictionary.RequiredCleanOccurrences | tuning-constant | `3` | src/Deckle.Autocorrect/Learning/PersonalDictionary.cs:14 | non | How fast a typed literal is adopted into the personal lexicon; strongly user-perceivable. |
| PersonalDictionary.RequiredDistinctDays | tuning-constant | `2` | src/Deckle.Autocorrect/Learning/PersonalDictionary.cs:15 | non | Same adoption gate. |
| PersonalDictionary.MaxWords | tuning-constant | `5000` | src/Deckle.Autocorrect/Learning/PersonalDictionary.cs:16 | non | Personal dictionary capacity. |
| PersonalWordAdmission.FrenchCollisionFloorPerMillion | tuning-constant | `1.0` | src/Deckle.Autocorrect/Learning/PersonalWordAdmission.cs:15 | non | Mirrors RestorerOptions.MinDominantFrequencyPerMillion by intent; coupled constants that would have to move together. |
| GlobalEnglishLexicon.OverlayFrequency | tuning-constant | `1.0` | src/Deckle.Autocorrect/Lexicon/GlobalEnglishLexicon.cs:11 | non | Weight of the restricted English literal overlay, i.e. the 'lexique anglais restreint' of the arbitrated correcteur contract. |
| CaretParagraphContext.MaxParagraphLength | tuning-constant | `4096` | src/Deckle.Autocorrect/Recovery/CaretParagraphContext.cs:8 | non |  |
| CaretSentenceContext.MaxSentenceLength | tuning-constant | `512` | src/Deckle.Autocorrect/Recovery/CaretSentenceContext.cs:10 | non |  |
| UIAutomationCaretTextReader.MaxCharacters | tuning-constant | `1024` | src/Deckle.Autocorrect/Recovery/CaretTextReader.cs:15 | non |  |
| UIAutomationCaretTextReader.InitialSettle | tuning-constant | `TimeSpan.FromMilliseconds(35)` | src/Deckle.Autocorrect/Recovery/CaretTextReader.cs:16 | non | Latency knob for UIA sampling; perceivable as responsiveness. |
| UIAutomationCaretTextReader.VerificationGap | tuning-constant | `TimeSpan.FromMilliseconds(75)` | src/Deckle.Autocorrect/Recovery/CaretTextReader.cs:17 | non | Latency knob. |
| TypedWordTracker.BufferCap | tuning-constant | `64` | src/Deckle.Autocorrect/Tracking/TypedWordTracker.cs:25 | non |  |
| TypedWordTracker.SeparatorRunCap | tuning-constant | `8` | src/Deckle.Autocorrect/Tracking/TypedWordTracker.cs:55 | non | Canonical site; SentenceRerankCoordinator.SeparatorRunCap duplicates it. |
| TypingStream.RunCap | tuning-constant | `512` | src/Deckle.Autocorrect/Tracking/TypingStream.cs:47 | non |  |
| WordBoundaries.RightSingleQuote | tuning-constant | `'’'` | src/Deckle.Autocorrect/Tracking/WordBoundaries.cs:19 | non | Typographic apostrophe; frozen linguistic fact, kept only for doubt. |
| QwertyAdjacency.Rows | tuning-constant | `["qwertyuiop", "asdfghjkl", "zxcvbnm"]` | src/Deckle.Autocorrect/Engine/QwertyAdjacency.cs:10 | non | DOUBT: a hardcoded QWERTY layout drives the physical-slip model. An AZERTY typist gets a mismatched adjacency map. Either a real exposable (keyboard layout) or a defect. |

## Deckle.Autocorrect.Lab

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| DataSet.FrenchFile | tuning-constant | `"lexicon-fr.tsv.gz"` | src/Deckle.Autocorrect.Lab/DataSet.cs:13 | non | Dataset artifact filename, not a tuning knob; kept because it names which lexicon the Lab reads. |
| DataSet.EnglishFile | tuning-constant | `"lexicon-en.tsv.gz"` | src/Deckle.Autocorrect.Lab/DataSet.cs:14 | non | Dataset artifact filename, not a tuning knob; kept because it names which lexicon the Lab reads. |
| DataSet.PairFile | tuning-constant | `"pair-bigrams-fr.tsv.gz"` | src/Deckle.Autocorrect.Lab/DataSet.cs:15 | non | Dataset artifact filename, not a tuning knob; kept because it names which pair model the Lab reads. |
| DataSet.VerbsFile | tuning-constant | `"verbs-fr.tsv.gz"` | src/Deckle.Autocorrect.Lab/DataSet.cs:16 | non | Dataset artifact filename, not a tuning knob; kept because it names which verb table the Lab reads. |
| DomainPackBuilder.FloorFrequencyPerMillion | tuning-constant | `0.2` | src/Deckle.Autocorrect.Lab/DomainPackBuilder.cs:32 | non |  |
| DomainPackBuilder.MaskingExclusionPerMillion | tuning-constant | `20.0` | src/Deckle.Autocorrect.Lab/DomainPackBuilder.cs:38 | non |  |
| DomainPackBuilder.MaskingGrayZonePerMillion | tuning-constant | `1.0` | src/Deckle.Autocorrect.Lab/DomainPackBuilder.cs:39 | non |  |
| HarvestFilter.MaxTokenLength | tuning-constant | `24` | src/Deckle.Autocorrect.Lab/HarvestFilter.cs:20 | non |  |
| HarvestStore.DebounceMs | tuning-constant | `1000` | src/Deckle.Autocorrect.Lab/HarvestStore.cs:21 | non |  |
| LexiconBuilder.MorphalouEpsilonPerMillion | tuning-constant | `0.001` | src/Deckle.Autocorrect.Lab/LexiconBuilder.cs:25 | non |  |
| MiningResult.ExamplesPerFamily | tuning-constant | `8` | src/Deckle.Autocorrect.Lab/Mining/MistouchMiner.cs:71 | non |  |
| PairModelTrainer.SentenceBreaks | tuning-constant | `{ '.', '!', '?', ';', ':', '…' }` | src/Deckle.Autocorrect.Lab/PairModelTrainer.cs:29 | non |  |
| TrainerOptions.MinPairCount | default-value | `3` | src/Deckle.Autocorrect.Lab/PairModelTrainer.cs:234 | non |  |
| TrainerOptions.MaxPrevPerSlot | default-value | `64` | src/Deckle.Autocorrect.Lab/PairModelTrainer.cs:239 | non |  |
| TrainerOptions.MaxOrder | default-value | `3` | src/Deckle.Autocorrect.Lab/PairModelTrainer.cs:243 | non |  |
| SurfaceProfiler.minTimedSentences | tuning-constant | `30` | src/Deckle.Autocorrect.Lab/Profiling/SurfaceProfiler.cs:45 | non | Method-local const, not a field as the sweep claimed; declaration corrected to local. |
| ReplayRunner.DefaultThresholds | tuning-constant | `{ 0.0, 0.25, 0.5, 1.0, 1.5, 2.0, 3.0, 5.0 }` | src/Deckle.Autocorrect.Lab/Replay/ReplayRunner.cs:27 | non |  |
| TruthOverlay.FileName | tuning-constant | `"autocorrect.truth-review.md"` | src/Deckle.Autocorrect.Lab/Replay/TruthOverlay.cs:33 | non | Output artifact filename of the truth-review overlay; borderline, no tuning effect. |
| RestorationEvaluator.SentenceBreaks | tuning-constant | `{ '.', '!', '?', ';', ':', '…' }` | src/Deckle.Autocorrect.Lab/RestorationEvaluator.cs:24 | non |  |
| EvaluatorOptions.MaxTokens | default-value | `0` | src/Deckle.Autocorrect.Lab/RestorationEvaluator.cs:414 | non |  |

## Deckle.Autocorrect.Mlm

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| CamembertAssets.DirectoryName | tuning-constant | `"camembert-base"` | src/Deckle.Autocorrect.Mlm/CamembertAssets.cs:22 | non |  |
| AssetFile.BaseUrl | tuning-constant | `"https://huggingface.co/Xenova/camembert-base/resolve/main"` | src/Deckle.Autocorrect.Mlm/CamembertAssets.cs:30 | non | Remote asset origin (HuggingFace). Network-facing default, relevant to the local-compute doctrine. |
| CamembertSentenceReranker.FreqFloor | tuning-constant | `0.01` | src/Deckle.Autocorrect.Mlm/CamembertSentenceReranker.cs:28 | non |  |
| MlmProbe.SentenceBreaks | tuning-constant | `{ '.', '!', '?', ';', ':', '…', '\n' }` | src/Deckle.Autocorrect.Mlm/MlmProbe.cs:38 | non |  |

## Deckle.Autocorrect.Onnx

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| OnnxSlotReranker.MinSentenceWordTokens | tuning-constant | `4` | src/Deckle.Autocorrect.Onnx/OnnxSlotReranker.cs:26 | non |  |

## Deckle.Autocorrect.Probe

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| AnticipationLeadOracle.DecisionBudgetsMilliseconds | tuning-constant | `[50, 100, 150, 250, 500, 945]` | src/Deckle.Autocorrect.Probe/AnticipationLeadOracleCommand.cs:79 | non |  |
| AnticipationLeadOracle.TriggerDelaysMilliseconds | tuning-constant | `[0, 50, 100, 150, 250]` | src/Deckle.Autocorrect.Probe/AnticipationLeadOracleCommand.cs:80 | non |  |
| AnticipationTransactionJoinCommand.TriggerDelaysMilliseconds | tuning-constant | `[0, 50, 100]` | src/Deckle.Autocorrect.Probe/AnticipationTransactionJoinCommand.cs:17 | non |  |
| AnticipationTransactionJoinCommand.TerminalBranches | tuning-constant | `['.', '?', '!', '…']` | src/Deckle.Autocorrect.Probe/AnticipationTransactionJoinCommand.cs:18 | non | Terminal punctuation set driving branch policy; linguistic, shared with the Mlm/Lab sentence-break sets. |
| CorrectionBenchmarkCommand.PrimaryThreshold | tuning-constant | `0.25` | src/Deckle.Autocorrect.Probe/CorrectionBenchmarkCommand.cs:9 | non |  |
| ProbeArguments.Provider | default-value | `"dml"` | src/Deckle.Autocorrect.Probe/ProbeArguments.cs:46 | non |  |
| QwenAdapterCompatibilityPlanReader.FrozenExperimentId | tuning-constant | `"ACX-0023"` | src/Deckle.Autocorrect.Probe/QwenAdapterCompatibilityPlan.cs:91 | non | Frozen experiment identity pin (ACX-0023), not a knob; kept because it pins what the probe runs. |
| QwenAdapterCompatibilityPlanReader.FrozenPhase | tuning-constant | `"A"` | src/Deckle.Autocorrect.Probe/QwenAdapterCompatibilityPlan.cs:92 | non | Frozen experiment identity pin, not a knob. |
| QwenAdapterCompatibilityPlanReader.FrozenProvider | tuning-constant | `"cpu"` | src/Deckle.Autocorrect.Probe/QwenAdapterCompatibilityPlan.cs:93 | non | Frozen execution provider (cpu) for the compatibility plan; a real choice, frozen by protocol. |
| QwenAdapterCompatibilityPlanReader.FrozenRepository | tuning-constant | `"Qwen/Qwen3-0.6B"` | src/Deckle.Autocorrect.Probe/QwenAdapterCompatibilityPlan.cs:94 | non | Frozen base model repository; a real choice, frozen by protocol. |
| QwenAdapterCompatibilityPlanReader.FrozenRevision | tuning-constant | `"c1899de289a04d12100db370d81485cdf75e47ca"` | src/Deckle.Autocorrect.Probe/QwenAdapterCompatibilityPlan.cs:95 | non | Frozen base model revision hash; a real choice, frozen by protocol. |
| QwenAdapterResourceSampler.SamplePeriodMilliseconds | tuning-constant | `50` | src/Deckle.Autocorrect.Probe/QwenAdapterResourceSampler.cs:31 | non |  |
| QwenAdapterResourceSampler.QuiescenceMilliseconds | tuning-constant | `250` | src/Deckle.Autocorrect.Probe/QwenAdapterResourceSampler.cs:32 | non |  |
| SentenceBatchExperimentCommand.StabilityThresholds | tuning-constant | `[0.0, 0.5, 1.0]` | src/Deckle.Autocorrect.Probe/SentenceBatchExperimentCommand.cs:9 | non |  |
| SentenceBatchExperimentFixture.WarmupPairs | tuning-constant | `2` | src/Deckle.Autocorrect.Probe/SentenceBatchExperimentFixture.cs:5 | non |  |
| SentenceBatchExperimentFixture.LatencyBlocks | tuning-constant | `5` | src/Deckle.Autocorrect.Probe/SentenceBatchExperimentFixture.cs:6 | non |  |
| SentenceBatchExperimentFixture.MaximumMedianBlockRatio | tuning-constant | `0.75` | src/Deckle.Autocorrect.Probe/SentenceBatchExperimentFixture.cs:7 | non |  |
| SentenceBatchExperimentFixture.MinimumFasterBlocks | tuning-constant | `4` | src/Deckle.Autocorrect.Probe/SentenceBatchExperimentFixture.cs:8 | non |  |
| SentenceBatchExperimentFixture.SecondaryLatencyReferenceMilliseconds | tuning-constant | `300.0` | src/Deckle.Autocorrect.Probe/SentenceBatchExperimentFixture.cs:9 | non |  |
| SentenceCalibrationFixture.OrdinaryRounds | tuning-constant | `20` | src/Deckle.Autocorrect.Probe/SentenceCalibrationFixture.cs:5 | non |  |
| SentenceCalibrationFixture.CalibrationBlocksPerStratum | tuning-constant | `16` | src/Deckle.Autocorrect.Probe/SentenceCalibrationFixture.cs:6 | non |  |
| SentenceCalibrationFixture.CalibrationCandidateCounts | tuning-constant | `[2]` | src/Deckle.Autocorrect.Probe/SentenceCalibrationFixture.cs:8 | non |  |
| SentenceCanonicalLatencyCommand.Rounds | tuning-constant | `20` | src/Deckle.Autocorrect.Probe/SentenceCanonicalLatencyCommand.cs:9 | non |  |
| SentenceDecisionInventory.SourceCorpus | tuning-constant | `"public_visible_development"` | src/Deckle.Autocorrect.Probe/SentenceDecisionInventory.cs:8 | non | Corpus selector string; identifies which corpus the inventory measures. |
| SentenceDecisionInventoryCommand.Seed | tuning-constant | `20260730` | src/Deckle.Autocorrect.Probe/SentenceDecisionInventoryCommand.cs:9 | non |  |
| SentenceDecisionInventoryCommand.WarmupEvaluationCount | tuning-constant | `1_000` | src/Deckle.Autocorrect.Probe/SentenceDecisionInventoryCommand.cs:10 | non |  |
| SentenceDecisionInventoryCommand.MeasuredEvaluationCount | tuning-constant | `10_000` | src/Deckle.Autocorrect.Probe/SentenceDecisionInventoryCommand.cs:11 | non |  |
| SentenceOrderAblationCommand.StabilityThresholds | tuning-constant | `[0.0, 0.5, 1.0]` | src/Deckle.Autocorrect.Probe/SentenceOrderAblationCommand.cs:9 | non |  |
| SentenceOrderAblationFixture.Seed | tuning-constant | `20260730` | src/Deckle.Autocorrect.Probe/SentenceOrderAblationFixture.cs:5 | non |  |
| SentenceOrderAblationFixture.WarmupCycles | tuning-constant | `2` | src/Deckle.Autocorrect.Probe/SentenceOrderAblationFixture.cs:6 | non |  |
| SentenceOrderAblationFixture.LatencyBlocks | tuning-constant | `20` | src/Deckle.Autocorrect.Probe/SentenceOrderAblationFixture.cs:7 | non |  |
| SentenceOrderAblationFixture.ContinuousHotForwardP95ReferenceMilliseconds | tuning-constant | `300.0` | src/Deckle.Autocorrect.Probe/SentenceOrderAblationFixture.cs:8 | non |  |
| SentenceProfileCommand.OverheadPairs | tuning-constant | `5` | src/Deckle.Autocorrect.Probe/SentenceProfileCommand.cs:9 | non |  |
| SentenceProfileFixture.Seed | tuning-constant | `20260730` | src/Deckle.Autocorrect.Probe/SentenceProfileFixture.cs:5 | non |  |
| SentenceProfileFixture.CandidateCounts | tuning-constant | `[2, 4, 8, 13]` | src/Deckle.Autocorrect.Probe/SentenceProfileFixture.cs:7 | non |  |
| SentenceProfileFixture.Transaction | tuning-constant | `CreateTransaction()` | src/Deckle.Autocorrect.Probe/SentenceProfileFixture.cs:9 | non | Static fixture object built by CreateTransaction(), not a scalar value; the sweep read it as a default. Kept as the experiment input. |
| SentenceUnanimityBundleCommand.Seed | tuning-constant | `20260730` | src/Deckle.Autocorrect.Probe/SentenceUnanimityBundleCommand.cs:9 | non |  |
| SentenceUnanimityBundleCommand.WarmupEvaluationCount | tuning-constant | `1_000` | src/Deckle.Autocorrect.Probe/SentenceUnanimityBundleCommand.cs:10 | non |  |
| SentenceUnanimityBundleCommand.MeasuredEvaluationCount | tuning-constant | `10_000` | src/Deckle.Autocorrect.Probe/SentenceUnanimityBundleCommand.cs:11 | non |  |
| SentenceUnanimityBundleCommand.WarmP95ReferenceMilliseconds | tuning-constant | `1.0` | src/Deckle.Autocorrect.Probe/SentenceUnanimityBundleCommand.cs:12 | non |  |
| StaleWorkProbeCommand.StaleHoldMilliseconds | tuning-constant | `250` | src/Deckle.Autocorrect.Probe/StaleWorkProbeCommand.cs:14 | non |  |

## Deckle.Catalog

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| SettingsModuleDescriptor.LabelKey | default | `"SettingsModuleNavLabel"` | src/Deckle.Catalog/SettingsModuleDescriptor.cs:61 | non | Sweep called it persisted-setting; it is an authoring default on a declaration record. Perceivable only through the nav label a module inherits. |
| SettingsModuleDescriptor.Tier | default | `SettingsNavTier.Main` | src/Deckle.Catalog/SettingsModuleDescriptor.cs:71 | non | Same misread. Decides which nav band an unqualified module lands in — visible in the rail, but authored per module, not by the user. |

## Deckle.Composition

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| OklchLightness | default-value | `0.75f` | src/Deckle.Composition/Core/HudComposition.Config.cs:146 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| OklchChroma | default-value | `0.3f` | src/Deckle.Composition/Core/HudComposition.Config.cs:147 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| HueStart | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:148 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| HueRange | default-value | `1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:149 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| WedgeCount | default-value | `360` | src/Deckle.Composition/Core/HudComposition.Config.cs:150 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| HuePeriodSeconds | default-value | `14.0` | src/Deckle.Composition/Core/HudComposition.Config.cs:154 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| HueDirection | default-value | `1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:155 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| HuePhaseTurns | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:156 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| HueEaseP1X | default-value | `0.125f` | src/Deckle.Composition/Core/HudComposition.Config.cs:166 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| HueEaseP1Y | default-value | `0.375f` | src/Deckle.Composition/Core/HudComposition.Config.cs:167 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| HueEaseP2X | default-value | `0.875f` | src/Deckle.Composition/Core/HudComposition.Config.cs:168 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| HueEaseP2Y | default-value | `0.625f` | src/Deckle.Composition/Core/HudComposition.Config.cs:169 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| HueMinSpeedFraction | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:174 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ConicSpanTurns | default-value | `0.5f` | src/Deckle.Composition/Core/HudComposition.Config.cs:177 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ConicLeadFadeTurns | default-value | `1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:178 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ConicTailFadeTurns | default-value | `1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:179 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ConicFadeCurve | default-value | `4f` | src/Deckle.Composition/Core/HudComposition.Config.cs:180 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ArcMirror | default-value | `true` | src/Deckle.Composition/Core/HudComposition.Config.cs:181 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ArcPeriodSeconds | default-value | `8.0` | src/Deckle.Composition/Core/HudComposition.Config.cs:186 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ArcDirection | default-value | `1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:187 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ArcPhaseTurns | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:188 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ArcEaseP1X | default-value | `0.125f` | src/Deckle.Composition/Core/HudComposition.Config.cs:191 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ArcEaseP1Y | default-value | `0.375f` | src/Deckle.Composition/Core/HudComposition.Config.cs:192 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ArcEaseP2X | default-value | `0.875f` | src/Deckle.Composition/Core/HudComposition.Config.cs:193 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ArcEaseP2Y | default-value | `0.625f` | src/Deckle.Composition/Core/HudComposition.Config.cs:194 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| ArcMinSpeedFraction | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:195 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| CloneCentreXFraction | default-value | `196f / 272f (= 0.7206f)` | src/Deckle.Composition/Core/HudComposition.Config.cs:214 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting \| computed literal expression, sweep kept it verbatim |
| CloneCentreYFraction | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:215 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| CloneOklchLightness | default-value | `0.9f` | src/Deckle.Composition/Core/HudComposition.Config.cs:225 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| CloneOklchChroma | default-value | `0.3f` | src/Deckle.Composition/Core/HudComposition.Config.cs:226 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| CloneHuePeriodSeconds | default-value | `7.0` | src/Deckle.Composition/Core/HudComposition.Config.cs:238 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| CloneHueDirection | default-value | `-1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:239 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| CloneArcPeriodSeconds | default-value | `4.0` | src/Deckle.Composition/Core/HudComposition.Config.cs:240 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| CloneArcDirection | default-value | `-1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:241 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RewritingSaturation | default-value | `1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:245 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RewritingHueShiftTurns | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:246 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RewritingExposure | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:247 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RewritingOpacity | default-value | `1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:248 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RewritingBlendSeconds | default-value | `2 (double 2.0)` | src/Deckle.Composition/Core/HudComposition.Config.cs:249 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| TranscribingSaturationDark | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:258 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| TranscribingSaturationLight | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:259 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| TranscribingHueShiftTurns | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:260 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| TranscribingExposureDark | default-value | `0.7f` | src/Deckle.Composition/Core/HudComposition.Config.cs:261 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| TranscribingExposureLight | default-value | `-1.2f` | src/Deckle.Composition/Core/HudComposition.Config.cs:262 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| TranscribingOpacity | default-value | `1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:263 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| TranscribingBlendSeconds | default-value | `2 (double 2.0)` | src/Deckle.Composition/Core/HudComposition.Config.cs:264 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingConicSpanTurns | default-value | `0.5f` | src/Deckle.Composition/Core/HudComposition.Config.cs:292 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingConicLeadFadeTurns | default-value | `1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:293 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingConicTailFadeTurns | default-value | `1f` | src/Deckle.Composition/Core/HudComposition.Config.cs:294 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingConicFadeCurve | default-value | `2f` | src/Deckle.Composition/Core/HudComposition.Config.cs:295 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingArcMirror | default-value | `true` | src/Deckle.Composition/Core/HudComposition.Config.cs:296 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingArcPhaseTurns | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:297 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingSaturationDark | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:298 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingSaturationLight | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:299 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingHueShiftTurns | default-value | `0f` | src/Deckle.Composition/Core/HudComposition.Config.cs:300 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingExposureDark | default-value | `0.7f` | src/Deckle.Composition/Core/HudComposition.Config.cs:301 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingExposureLight | default-value | `-1.2f` | src/Deckle.Composition/Core/HudComposition.Config.cs:302 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingBlendSeconds | default-value | `2 (double 2.0)` | src/Deckle.Composition/Core/HudComposition.Config.cs:303 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| RecordingHuePeriodSeconds | default-value | `0` | src/Deckle.Composition/Core/HudComposition.Config.cs:313 | non | ConicArcStrokeConfig default; mirrored in Deckle.Playground/Models/TuningModel.cs — dedupe by name before counting |
| StrokeThickness | tuning-constant | `4f` | src/Deckle.Composition/Core/HudComposition.cs:52 | non | private const HUD stroke geometry; no UI, no Playground mirror |
| InsetDip | playground-parameter | `-2f` | src/Deckle.Composition/Core/HudComposition.cs:59 | non | public static mutable field, tuned live by HudPlayground (not const) — value -2f |
| CornerRadiusDip | tuning-constant | `8f` | src/Deckle.Composition/Core/HudComposition.cs:60 | non | private const HUD stroke geometry; no UI, no Playground mirror |

## Deckle.Core

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| SC_LEFT_OF_ONE | tuning-constant | `0x29` | src/Deckle.Core/Interop/NativeMethods.cs:29 | non | Scancode of the key left of '1' — the physical key bound to the transcription hotkey. User-perceivable (which key triggers Deckle) but frozen in interop code; hotkey rebinding would live here or above. Kept as a borderline exposable. |
| ProgressReportThrottleMs | tuning-constant | `200` | src/Deckle.Core/Io/Downloader.cs:48 | non | Perceptible: governs download progress-bar smoothness (~5 updates/s). Perf-motivated, likely frozen, but user-visible in effect. |
| BufferSize | tuning-constant | `81920` | src/Deckle.Core/Io/Downloader.cs:39 | non | Pure implementation constant (Stream.CopyToAsync default). Kept only because of the in-doubt rule; nothing user-perceivable. |
| AppFolderName | tuning-constant | `"Deckle"` | src/Deckle.Core/Paths/AppPaths.cs:36 | non | Identity constant: names %LOCALAPPDATA%\Deckle and seeds the settings mutex name. Not exposable as a setting, but it is the single source of truth for the data root. |
| DataRootEnvVar | tuning-constant | `"DECKLE_DATA_ROOT"` | src/Deckle.Core/Paths/AppPaths.cs:45 | non | Env-var name allowing relocation of the whole user data root. Acts as an out-of-band setting (dev override); no UI, probably should stay env-only. |
| SettingsMutexName | tuning-constant | `$"{AppFolderName}-Settings-Save"` | src/Deckle.Core/Paths/AppPaths.cs:41 | non | Missed by the sweep (interpolated const excluded). Cross-process write serialization name; infrastructure, not user-facing. |
| SettingsSaveDebounceMs | tuning-constant | `300` | src/Deckle.Core/JsonSettingsStore.cs:162 | non | Missed by the sweep (inline literal, no named const). Delay between Save() and the actual disk write for every settings.json in the app — governs how fast a UI change reaches disk. |
| SettingsMutexWaitTimeout | tuning-constant | `TimeSpan.FromSeconds(2)` | src/Deckle.Core/JsonSettingsStore.cs:198 | non | Missed by the sweep (inline expression). How long a save waits for the cross-process mutex before giving up — a settings write can be silently skipped past it. Worth naming. |
| DownloaderHttpTimeout | tuning-constant | `Timeout.InfiniteTimeSpan` | src/Deckle.Core/Io/Downloader.cs:157 | non | Missed by the sweep (assignment, not a declaration). Deliberate override of the 100 s HttpClient default for multi-GB model downloads; cancellation is the escape hatch. Deliberately frozen. |

## Deckle.Diagnostics

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| LogWindowSink.BufferCapacity | tuning-constant | `5000` | src/Deckle.Diagnostics/Sinks/LogWindowSink.cs:24 | non | Verified. Ring-buffer depth of the in-app log window — user-perceivable (how much history the log viewer can show). Closest thing to a real exposable in this module; still private const, no UI. |
| JsonlSink.DefaultQueueCapacity | tuning-constant | `1024` | src/Deckle.Diagnostics/Sinks/JsonlSink.cs:16 | non | Verified. Bounded write queue; overflow drops events. Implementation detail, but it silently governs log completeness under load — kept per in-doubt rule. |
| RoutedJsonlSink.DefaultQueueCapacity | tuning-constant | `1024` | src/Deckle.Diagnostics/Sinks/RoutedJsonlSink.cs:12 | non | Verified. Same rationale as JsonlSink; duplicate knob at a second declaration site. |
| RoutedJsonlSink.DefaultMaxOpenFiles | tuning-constant | `16` | src/Deckle.Diagnostics/Sinks/RoutedJsonlSink.cs:13 | non | Verified. LRU cap on concurrently open log files. Pure implementation; kept only because it bounds a resource, not because it tunes anything the user sees. |
| OperationalLogActivity (Ambient, Transcription, Autocorrect, Input, Windowing) | default-value | `5-member enum; admission fails closed until Logging injects its reader` | src/Deckle.Diagnostics/OperationalLogAdmission.cs:8-38 | oui | Added from June inventory (Diagnostics page: logging toggles by subsystem). Sweep missed it — an enum, not an initialized member. This module owns the closed vocabulary of the toggles; the persisted values live in Deckle.Diagnostics.Logging. Not itself a value, but the definitive list of what the logging toggles can be. |

## Deckle.Diagnostics.Logging

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| ApplicationLogToDisk | persisted-setting | `false` | src/Deckle.Diagnostics.Logging/LoggingSettings.cs:14 | oui | June inventory locates it on TelemetrySettings.cs:30; it now lives on LoggingSettings. Same user-facing setting, moved module POCO — verify no duplicate persisted key survives in telemetry.json. |
| LogAmbientCaptureActivity | persisted-setting | `false` | src/Deckle.Diagnostics.Logging/LoggingSettings.cs:19 | oui |  |
| LogTranscriptionActivity | persisted-setting | `false` | src/Deckle.Diagnostics.Logging/LoggingSettings.cs:26 | oui | June inventory names it LogStreamingTranscriptionActivity; that identifier no longer exists in source. Treated as a rename, not a new exposable. |
| LogAutocorrectActivity | persisted-setting | `false` | src/Deckle.Diagnostics.Logging/LoggingSettings.cs:38 | oui |  |
| LogInputActivity | persisted-setting | `false` | src/Deckle.Diagnostics.Logging/LoggingSettings.cs:44 | non | Absent from the June inventory — added since. Gates the Raw Input contact-frame rollup and per-gesture Trackpad detail. |
| LogWindowingActivity | persisted-setting | `false` | src/Deckle.Diagnostics.Logging/LoggingSettings.cs:54 | oui |  |
| LogWindowVisibilityMode | removed-setting | `LogWindowVisibilityMode.All (June); no longer present in source` | src/Deckle.Diagnostics.Logging/LoggingSettings.cs (June: :54) | oui | June-only. Grep over src/ returns zero hits for the identifier; line 54 is now LogWindowingActivity. Kept as an added-from-June row so the deletion is visible, not silently lost. Not an exposable today. |

## Deckle.Diagnostics.Telemetry

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| LatencyEnabled | persisted-setting | `false` | src/Deckle.Diagnostics.Telemetry/TelemetrySettings.cs:13 | oui |  |
| MicrophoneTelemetry | persisted-setting | `false` | src/Deckle.Diagnostics.Telemetry/TelemetrySettings.cs:14 | oui |  |
| CorpusEnabled | persisted-setting | `false` | src/Deckle.Diagnostics.Telemetry/TelemetrySettings.cs:15 | oui |  |
| RecordAudioCorpus | persisted-setting | `false` | src/Deckle.Diagnostics.Telemetry/TelemetrySettings.cs:16 | oui |  |
| AudioCorpusContent | persisted-setting | `AudioCorpusContent.MatchTranscription` | src/Deckle.Diagnostics.Telemetry/TelemetrySettings.cs:25 | oui |  |
| StorageDirectory | persisted-setting | `""` | src/Deckle.Diagnostics.Telemetry/TelemetrySettings.cs:29 | oui | June cites :31; actual line is :29 (drift after ApplicationLogToDisk left this POCO). Empty string resolves to AppPaths.TelemetryDirectory. |
| AutocorrectDecisions | persisted-setting | `false` | src/Deckle.Diagnostics.Telemetry/TelemetrySettings.cs:36 | oui | June cites :38; actual line is :36. |
| AutocorrectText | persisted-setting | `false` | src/Deckle.Diagnostics.Telemetry/TelemetrySettings.cs:44 | oui | June cites :46; actual line is :44. |

## Deckle.Home

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| MaxBatchSize | tuning-constant | `100` | src/Deckle.Home/HomeGestures.cs:10 | non | Anytype batch-write cap. Borderline implementation constant; kept only because it bounds a remote call. |
| PageLimit | tuning-constant | `1000` | src/Deckle.Home/HomeObjects.cs:97 | non | Anytype object-index page size. Borderline implementation constant. |
| limit | tuning-constant | `100` | src/Deckle.Home/HomePropertyWriter.cs:126 | non | Method-local const (tag pagination), lowercase name confirms it is a local, not a field as the sweep reports. Almost certainly a false positive; kept per the doubt rule. |

## Deckle.Hud

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| HudChrono.DigitCount | tuning-constant | `6` | src/Deckle.Hud/Chrono/HudChrono.Reveal.cs:14 | non | Structural: the chrono face has six digits. Likely pure implementation, kept per doubt rule. |
| HudChrono.MaxRevealBuildAttempts | tuning-constant | `90` | src/Deckle.Hud/Chrono/HudChrono.RevealMask.cs:63 | non | Retry window in vsync frames before latching a reveal-build failure; robustness bound, not user-facing. |
| HudChrono.ToneCharging | tuning-constant | `"TextFillColorDisabledBrush"` | src/Deckle.Hud/Chrono/HudChrono.xaml.cs:237 | non | Theme-resource key, not a numeric knob. Source says the authoritative mapping lives in Chrono/CONTEXT.md and these are its code mirror - design token rather than exposable. |
| HudChrono.ToneRecording | tuning-constant | `"TextFillColorSecondaryBrush"` | src/Deckle.Hud/Chrono/HudChrono.xaml.cs:238 | non | Same doubt as ToneCharging. |
| HudChrono.ToneStopped | tuning-constant | `"TextFillColorTertiaryBrush"` | src/Deckle.Hud/Chrono/HudChrono.xaml.cs:239 | non | Same doubt as ToneCharging. |
| ProximityRollupAggregator.Capacity | tuning-constant | `2048` | src/Deckle.Hud/ProximityRollupAggregator.cs:15 | non | Ring-buffer depth for the proximity rollup; perceivable only in log-window statistics. |
| HudOverlayManager.GapDip | tuning-constant | `12` | src/Deckle.Hud/Windows/HudOverlayManager.cs:32 | non |  |
| HudOverlayWindow.HUD_WIDTH | tuning-constant | `272` | src/Deckle.Hud/Windows/HudOverlayWindow.xaml.cs:30 | non | Duplicated verbatim in HudWindow.xaml.cs:49 - one exposable, two declaration sites. |
| HudOverlayWindow.HUD_HEIGHT | tuning-constant | `78` | src/Deckle.Hud/Windows/HudOverlayWindow.xaml.cs:31 | non | Duplicated verbatim in HudWindow.xaml.cs:50. |
| HudOverlayWindow.NEAR_RADIUS_DIP | tuning-constant | `10` | src/Deckle.Hud/Windows/HudOverlayWindow.xaml.cs:39 | non |  |
| HudOverlayWindow.FAR_RADIUS_DIP | tuning-constant | `256` | src/Deckle.Hud/Windows/HudOverlayWindow.xaml.cs:40 | non | Deliberately wider than the HudWindow value of 128 (source comment); the two are tuned as a pair. |
| HudOverlayWindow.MAX_ALPHA | tuning-constant | `255` | src/Deckle.Hud/Windows/HudOverlayWindow.xaml.cs:41 | non |  |
| HudOverlayWindow.MIN_ALPHA | tuning-constant | `40` | src/Deckle.Hud/Windows/HudOverlayWindow.xaml.cs:42 | non | Floor alpha of the proximity fade - the strongest user-perceivable knob of the group; gated by the persisted Overlay FadeOnProximity toggle listed in June. |
| HudWindow.HUD_WIDTH | tuning-constant | `272` | src/Deckle.Hud/Windows/HudWindow.xaml.cs:49 | non | Duplicate of HudOverlayWindow.HUD_WIDTH. |
| HudWindow.HUD_HEIGHT | tuning-constant | `78` | src/Deckle.Hud/Windows/HudWindow.xaml.cs:50 | non | Duplicate of HudOverlayWindow.HUD_HEIGHT. |
| HudWindow.HUD_BOTTOM_MARGIN | tuning-constant | `96` | src/Deckle.Hud/Windows/HudWindow.xaml.cs:51 | non | Screen-edge offset of the HUD; adjacent to the persisted Overlay Position choice (TopCenter/BottomCenter). |
| HudWindow.NEAR_RADIUS_DIP | tuning-constant | `10` | src/Deckle.Hud/Windows/HudWindow.xaml.cs:58 | non |  |
| HudWindow.FAR_RADIUS_DIP | tuning-constant | `128` | src/Deckle.Hud/Windows/HudWindow.xaml.cs:59 | non |  |
| HudWindow.MAX_ALPHA | tuning-constant | `255` | src/Deckle.Hud/Windows/HudWindow.xaml.cs:60 | non |  |
| HudWindow.MIN_ALPHA | tuning-constant | `40` | src/Deckle.Hud/Windows/HudWindow.xaml.cs:61 | non | Floor alpha of the main-HUD proximity fade. |
| HudWindow.FADE_IN_MS | tuning-constant | `150` | src/Deckle.Hud/Windows/HudWindow.xaml.cs:103 | non | Animation duration; gated by the persisted Overlay Animations toggle. |
| HudWindow.SuccessDuration | tuning-constant | `TimeSpan.FromSeconds(2)` | src/Deckle.Hud/Windows/HudWindow.xaml.cs:246 | non | How long the Copied/Pasted confirmation stays up - directly user-perceivable dwell time. |
| WindowSlideAnimator.FrameIntervalMs | tuning-constant | `16` | src/Deckle.Hud/Windows/WindowSlideAnimator.cs:24 | non | ~60 Hz tick; implementation cadence rather than a knob. |
| WindowSlideAnimator.Duration | tuning-constant | `TimeSpan.FromMilliseconds(150)` | src/Deckle.Hud/Windows/WindowSlideAnimator.cs:25 | non |  |
| LayeredAlphaAnimator.FrameIntervalMs | tuning-constant | `16` | src/Deckle.Hud/Windows/WindowSlideAnimator.cs:153 | non | Second declaration in the same file (LayeredAlphaAnimator, not WindowSlideAnimator); same doubt as above. |
| LayeredAlphaAnimator.Duration | tuning-constant | `TimeSpan.FromMilliseconds(150)` | src/Deckle.Hud/Windows/WindowSlideAnimator.cs:154 | non | Matches HudWindow.FADE_IN_MS by intent (source comment) - the three fade durations move together. |

## Deckle.Input

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| MouseWheelSettings.RecordEvents | persisted-setting | `false` | src/Deckle.Input/MouseWheelSettings.cs:20 | oui | Verified: only persisted member of the module, written to <UserDataRoot>/modules/mousewheel/settings.json via MouseWheelSettingsService. Diagnostic-category, surfaced today only in Playground per June inventory. |
| PrecisionTouchpadInjector.DeviceWidth | tuning-constant | `10000` | src/Deckle.Input/Injection/PrecisionTouchpadInjector.cs:12 | non | Himetric (1/100 mm) physical size of the synthetic gesture-only touchpad. Not user-perceivable as a knob, but it sets the coordinate scale every injected gesture is expressed in — keep, likely frozen. |
| PrecisionTouchpadInjector.DeviceHeight | tuning-constant | `6000` | src/Deckle.Input/Injection/PrecisionTouchpadInjector.cs:13 | non | Same as DeviceWidth — synthetic device physical height, sets gesture coordinate scale. |
| FocusEventCoalescer.WindowMilliseconds | tuning-constant | `50` | src/Deckle.Input/Keyboard/FocusEventCoalescer.cs:10 | non | Burst window collapsing duplicate WinEvent focus callbacks. Perceivable only indirectly (password-gate reactivity). Borderline implementation constant — kept per doubt rule. |
| WheelObservationBuffer.RetentionMs | tuning-constant | `40` | src/Deckle.Input/Keyboard/WheelObservationBuffer.cs:9 | non | How long a hook/RawInput wheel observation is held awaiting its pair before publishing. Directly governs wheel-event latency and correlation quality — the most plausible genuine tuning knob of the wheel path. |
| WheelObservationBuffer.CapacityPerSource | tuning-constant | `64` | src/Deckle.Input/Keyboard/WheelObservationBuffer.cs:10 | non | Fixed per-source ring size. Pure implementation sizing; overflow forces an early publish, so it has a faint behavioral edge. Likely drop on arbitration. |
| WheelEventQueue.Capacity | tuning-constant | `256` | src/Deckle.Input/Keyboard/WheelEventQueue.cs:7 | non | Sweep reported line 8; actual declaration is line 7 (line 8 is the derived Mask). Hook→pump handoff ring size, implementation sizing. Likely drop. |
| KeyboardInputHost.WheelObservationTimerMs | tuning-constant | `10` | src/Deckle.Input/Keyboard/KeyboardInputHost.WheelObservations.cs:6 | non | Pump timer cadence draining wheel observations — pairs with RetentionMs to set wheel latency. Keep alongside it. |
| KeyboardInputHost.RollupPeriodMs | tuning-constant | `30000` | src/Deckle.Input/Keyboard/KeyboardInputHost.cs:47 | non | Logging rollup period, diagnostic cadence only. Kept but a strong drop candidate. |
| RawInputHost.RollupPeriodMs | tuning-constant | `5000` | src/Deckle.Input/RawInputHost.cs:30 | non | Same nature as the KeyboardInputHost one, different value (5 s vs 30 s) on the sibling host — the inconsistency itself may be worth a look. Diagnostic cadence only. |
| ContactFrameRecorder.FlushPeriodMs | tuning-constant | `500` | src/Deckle.Input/Telemetry/ContactFrameRecorder.cs:28 | non | JSONL flush cadence of the contact-frame diagnostic recorder. Affects data-loss window on crash, nothing user-facing. |
| WheelEventRecorder.FlushPeriodMs | tuning-constant | `500` | src/Deckle.Input/Telemetry/WheelEventRecorder.cs:33 | non | Same as ContactFrameRecorder — flush cadence of the wheel JSONL driven by MouseWheelSettings.RecordEvents. |

## Deckle.Input.PrecisionScroll

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| Enabled | persisted-setting | `false` | src/Deckle.Input.PrecisionScroll/PrecisionScrollSettings.cs:5 | non |  |
| Tuning | persisted-setting | `new PrecisionScrollTuning()` | src/Deckle.Input.PrecisionScroll/PrecisionScrollSettings.cs:7 | non | Container property, not a knob itself — its three members are the real exposables. Kept so the persisted shape stays visible. |
| DistancePerDetentMm | default-value | `1.5` | src/Deckle.Input.PrecisionScroll/PrecisionScrollTuning.cs:14 | non |  |
| InitialStepDurationMs | default-value | `60` | src/Deckle.Input.PrecisionScroll/PrecisionScrollTuning.cs:15 | non |  |
| QuietPeriodScale | default-value | `2` | src/Deckle.Input.PrecisionScroll/PrecisionScrollTuning.cs:16 | non |  |
| DistancePerDetentMinimum | tuning-constant | `0.25` | src/Deckle.Input.PrecisionScroll/PrecisionScrollTuning.cs:7 | non | Clamp bound consumed by Normalize() — a slider range endpoint rather than a knob. |
| DistancePerDetentMaximum | tuning-constant | `6` | src/Deckle.Input.PrecisionScroll/PrecisionScrollTuning.cs:8 | non | Clamp bound consumed by Normalize() — a slider range endpoint rather than a knob. |
| InitialStepDurationMinimum | tuning-constant | `20` | src/Deckle.Input.PrecisionScroll/PrecisionScrollTuning.cs:9 | non | Clamp bound consumed by Normalize() — a slider range endpoint rather than a knob. |
| InitialStepDurationMaximum | tuning-constant | `180` | src/Deckle.Input.PrecisionScroll/PrecisionScrollTuning.cs:10 | non | Clamp bound consumed by Normalize() — a slider range endpoint rather than a knob. |
| QuietPeriodScaleMinimum | tuning-constant | `1` | src/Deckle.Input.PrecisionScroll/PrecisionScrollTuning.cs:11 | non | Clamp bound consumed by Normalize() — a slider range endpoint rather than a knob. |
| QuietPeriodScaleMaximum | tuning-constant | `4` | src/Deckle.Input.PrecisionScroll/PrecisionScrollTuning.cs:12 | non | Clamp bound consumed by Normalize() — a slider range endpoint rather than a knob. |
| FrameIntervalMs | tuning-constant | `10` | src/Deckle.Input.PrecisionScroll/Engine/PrecisionScrollGesture.cs:27 | non | Synthetic-frame cadence (100 Hz) of the injected scroll — perceptible as smoothness, but plausibly a frozen implementation choice. |

## Deckle.Input.Trackpad

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| Enabled | persisted-setting | `false` | src/Deckle.Input.Trackpad/TrackpadSettings.cs:8 | oui |  |
| DragSpeed | persisted-setting | `1.0` | src/Deckle.Input.Trackpad/TrackpadSettings.cs:16 | oui |  |
| RecordFrames | persisted-setting | `false` | src/Deckle.Input.Trackpad/TrackpadSettings.cs:23 | oui |  |
| MaxFrameDeltaRatio | tuning-constant | `0.08` | src/Deckle.Input.Trackpad/Engine/TrackpadEngine.cs:19 | non | Source comment states it is an in-engine constant by decision (smoothing is never exposed) — an exposable already arbitrated shut. |
| BaseScale | tuning-constant | `0.25` | src/Deckle.Input.Trackpad/Engine/TrackpadEngine.cs:24 | non | Source comment states sensitivity is the DragSpeed slider alone — arbitrated shut. |
| GraceDelayMs | tuning-constant | `0` | src/Deckle.Input.Trackpad/Engine/TrackpadEngine.cs:28 | non | Frozen 2026-06-12 after hands-on calibration; TrackpadSettings comment says a stale persisted Tuning object is ignored. |
| StartThresholdRatio | tuning-constant | `0.001` | src/Deckle.Input.Trackpad/Engine/TrackpadEngine.cs:35 | non | Frozen 2026-06-12; deliberately non-zero (framing decision: a three-finger tap must do nothing). |
| StartThresholdUnits | default-value | `50` | src/Deckle.Input.Trackpad/Engine/ThreeFingerDragRecognizer.cs:43 | non | Placeholder default — TrackpadEngine overwrites it at runtime from StartThresholdRatio times the device X range. Never effective as 50. |
| MaxFrameDeltaUnits | default-value | `double.MaxValue` | src/Deckle.Input.Trackpad/Engine/ThreeFingerDragRecognizer.cs:46 | non | Placeholder default (clamp disabled) — TrackpadEngine overwrites it at runtime from MaxFrameDeltaRatio. |
| GraceDelayMs | default-value | `0 (implicit, no initializer)` | src/Deckle.Input.Trackpad/Engine/ThreeFingerDragRecognizer.cs:40 | non | Missed by the sweep (no initializer, hence no value token). Settable for tests; the engine writes the frozen value. Kept for symmetry with its two sibling recognizer knobs. |

## Deckle.Install

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| Repo | tuning-constant | `"louisfifre/deckle"` | src/Deckle.Install/ReleaseResolver.cs:30 | non | Verified at source. GitHub owner/repo the update stub resolves releases from. Frozen distribution contract, not a knob today — but it is the single lever for pointing the updater elsewhere (fork, self-host). Kept under doubt. |
| DataRootVariable | tuning-constant | `"DECKLE_DATA_ROOT"` | src/Deckle.Install/UserEnvironment.cs:14 | non | Verified at source. Name of the HKCU user env var carrying the data-root override; written by the installer only when the user picks a non-default folder. The variable name is a contract, not a value — but it is the documented user-actionable relocation lever. Kept under doubt. |
| DefaultInstallDir | default-value | `Path.Combine(LocalAppData, "Programs", "Deckle")` | src/Deckle.Install/InstallPaths.cs:22 | non | MISSED BY SWEEP — added from source. The real binaries default (%LOCALAPPDATA%\Programs\Deckle); the sweep only caught SetupContext.InstallDirectory which forwards to it. User-changeable on the Folders page. |
| DefaultDataDir | default-value | `Path.Combine(LocalAppData, "Deckle")` | src/Deckle.Install/InstallPaths.cs:24 | non | MISSED BY SWEEP — added from source. The real data default (%LOCALAPPDATA%\Deckle, what AppPaths uses); the sweep only caught SetupContext.DataDirectory which forwards to it. User-changeable on the Folders page, relocatable via DECKLE_DATA_ROOT. |

## Deckle.Installer

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| ProgressThrottleMs | tuning-constant | `100` | src/Deckle.Installer/Io/Downloader.cs:22 | non | Verified at source, comment reads "~10 updates/sec, no flicker". Governs the perceived smoothness of the download progress bar — user-perceivable, but a rendering-hygiene constant with no plausible exposure. Kept under doubt; nearest thing to an exposable in the whole Installer module. |

## Deckle.Lighting

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| AmbientTransitionDeciseconds | tuning-constant | `1` | src/Deckle.Lighting/Hue/HueBridgeClient.cs:141 | non | Strong keeper. Comment documents the perceptual tradeoff explicitly (Hue factory default 4 = 400ms reads sluggish; 1 = 100ms is responsive without strobing at 15 Hz). Directly user-perceivable on the lamp. |
| StopStreamingTimeout | tuning-constant | `TimeSpan.FromSeconds(3)` | src/Deckle.Lighting/Hue/HueEntertainmentLightOutput.cs:10 | non | Bounds how long stopping the Entertainment stream can hang. Perceivable as a stall when Ambient is turned off. |
| RestPrePrimeColor | tuning-constant | `new(1, 1, 1)` | src/Deckle.Lighting/Hue/HueEntertainmentLightOutput.cs:11 | non | Near-black priming colour pushed to lights at stream start — visible on the lamp for one frame. Plausibly a frozen implementation detail. |
| ConnectTimeout | tuning-constant | `TimeSpan.FromSeconds(5)` | src/Deckle.Lighting/Hue/HueEntertainmentTransport.cs:12 | non | DTLS connect timeout. Perceivable as how long the user waits before a connection failure surfaces. |
| IncidentDelay | tuning-constant | `TimeSpan.FromSeconds(5)` | src/Deckle.Lighting/Hue/HueEventStreamEpisode.cs:8 | non | Grace window before an event-stream drop is declared an incident. Debounce constant — user-perceivable only through notification/status noise. |
| BrowseWindow | tuning-constant | `TimeSpan.FromSeconds(3)` | src/Deckle.Lighting/Hue/HueLocalDiscovery.cs:20 | non | mDNS browse duration — directly the length of the bridge-discovery wait in the pairing UI. |
| ResolveWindow | tuning-constant | `TimeSpan.FromSeconds(2)` | src/Deckle.Lighting/Hue/HueLocalDiscovery.cs:21 | non | mDNS resolve duration, same discovery wait as BrowseWindow. The two would arbitrate together, not separately. |
| CloudDiscoveryUrl | tuning-constant | `"https://discovery.meethue.com/"` | src/Deckle.Lighting/Hue/HueDiscovery.cs:10 | non | The only cloud endpoint in the batch — an explicitly-requested fallback to Philips' hosted discovery. Not a tuning value, but it is the one exposable here that touches the emancipation-from-cloud goal: a user might want it disabled outright rather than retargeted. |

## Deckle.Lighting.Ambient

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| AmbientSettings.Enabled | persisted-setting | `false` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:20 | oui | Comment says persisted but had no consumer until J3 wiring; verify the runtime toggle is now wired. |
| AmbientSettings.HueBridgeIp | persisted-setting | `null` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:36 | oui | June lists the pairing trio (HueBridgeId/HueUsername/HueLastGroupId) as bespoke; HueBridgeIp is a fourth member of the same pairing block, not named explicitly. |
| AmbientSettings.HueBridgeId | persisted-setting | `null` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:39 | oui |  |
| AmbientSettings.HueUsername | persisted-setting | `null` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:45 | oui | Credential-ish (CLIP API key). Written in clear JSON by design; the DTLS clientkey lives in the DPAPI vault instead. |
| AmbientSettings.HueLastGroupId | persisted-setting | `null` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:50 | oui |  |
| AmbientSettings.SelectedMonitorDeviceName | persisted-setting | `null` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:63 | non | Absent from the June inventory. Bespoke like CaptureSettings.AudioInputDeviceId: values come from a runtime monitor enumeration; null = follow primary. |
| AmbientSettings.Mode | persisted-setting | `AmbientMode.Game` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:80 | oui | Enum Game/Movie/Ambient/Custom; setting it applies a whole preset bundle to the other knobs, and touching any tuning knob flips it to Custom. Not a plain choice descriptor. |
| AmbientSettings.UseMultiLight | persisted-setting | `false` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:103 | oui |  |
| AmbientSettings.LightZones | persisted-setting | `new Dictionary<string, LightZone>() (empty)` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:112 | oui | Sweep value 'new()' truncated; real default is an empty dictionary keyed by opaque light id. June flags it non-composable (per-light dynamic grid). |
| AmbientSettings.LightBrightness | persisted-setting | `new Dictionary<string, double>() (empty; missing key = 1.0)` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:123 | oui | Sweep value truncated. Per-light implicit default 1.0 lives in consumer code, not in the POCO. |
| AmbientSettings.BorderMode | persisted-setting | `BorderThicknessMode.Share` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:134 | oui |  |
| AmbientSettings.BorderDepth | persisted-setting | `0.33` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:146 | oui | Only meaningful when BorderMode == Share. Practical range [0.05, 0.5] clamped by the UI, not by the POCO. |
| AmbientSettings.BorderCells | persisted-setting | `8` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:162 | oui | Only meaningful when BorderMode == Cells. Range [4,24] step 2, enforced by the slider. AmbientEngine.cs comment still calls the mode 'Pixels' while the enum member is Cells - stale wording. |
| AmbientSettings.ExposureEv | persisted-setting | `0.0` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:199 | oui | POCO default 0.0 but the shipping Mode default (Game) preset sets 0.5 - the effective out-of-box value is 0.5. |
| AmbientSettings.SaturationBoost | persisted-setting | `1.0` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:205 | oui | POCO 1.0 vs Game preset 1.3. Also: the class-level comment says OKLCh, the property comment says HSV-S - the actual colour space is not settled by this file. |
| AmbientSettings.MinBrightnessEnabled | persisted-setting | `true` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:211 | non | June lists MinBrightness but not its enabling switch. |
| AmbientSettings.MinBrightness | persisted-setting | `180` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:221 | oui | POCO 180 (matches June) but Game preset sets 100, Movie 60, Ambient 40 - 180 is only reached in Custom mode. |
| AmbientSettings.BrightnessCurveX1 | persisted-setting | `0.42` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:228 | oui | Game preset overrides to 0.33. |
| AmbientSettings.BrightnessCurveY1 | persisted-setting | `0.00` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:232 | oui | Game preset overrides to 0.33. |
| AmbientSettings.BrightnessCurveX2 | persisted-setting | `1.00` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:235 | oui | Game preset overrides to 0.67. |
| AmbientSettings.BrightnessCurveY2 | persisted-setting | `1.00` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:238 | oui | Game preset overrides to 0.67. The four curve values are one exposable (a bezier editor), not four sliders - BrightnessCurveCanvas edits them jointly. |
| AmbientSettings.ChangeThreshold | persisted-setting | `6` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:247 | oui |  |
| AmbientSettings.SmoothingAlpha | persisted-setting | `0.30` | src/Deckle.Lighting.Ambient/AmbientSettings.cs:259 | oui | POCO 0.30 vs Game preset 0.40. |
| AmbientEngine.FrameProcessingIncidentDelayMs | tuning-constant | `1_000` | src/Deckle.Lighting.Ambient/Engine/AmbientEngine.CaptureEvents.cs:12 | non | Error/backoff timing on the frame path. Probably pure implementation; kept because it surfaces as recovery latency after a capture incident. |
| AmbientEngine.FrameProcessingFatalDelayMs | tuning-constant | `5_000` | src/Deckle.Lighting.Ambient/Engine/AmbientEngine.CaptureEvents.cs:13 | non | Same family as the incident delay; likely frozen-in-code. |
| AmbientEngine.FrameProcessingGapCapMs | tuning-constant | `250` | src/Deckle.Lighting.Ambient/Engine/AmbientEngine.CaptureEvents.cs:14 | non | Telemetry/gap accounting cap; likely pure implementation. |
| AmbientEngine.GroupPushHz | tuning-constant | `15` | src/Deckle.Lighting.Ambient/Engine/AmbientEngine.cs:89 | non | Directly perceivable (reactivity vs bridge load) and coupled to ScreenCaptureService ThrottleIntervalMs=66 in Deckle.Vision - moving one without the other desynchronises the pipeline. |
| AmbientEngine.MultiPushHz | tuning-constant | `10` | src/Deckle.Lighting.Ambient/Engine/AmbientEngine.cs:97 | non | At the Philips-documented ceiling (10 Hz per light); raising it risks bridge rate-limiting. Exposable only with a hard clamp. |
| AmbientEngine.OffThreshold | tuning-constant | `8` | src/Deckle.Lighting.Ambient/Engine/AmbientEngine.cs:114 | non | Code comment explicitly plans to surface it in the Playground tuning panel (J5). Strong exposable candidate, unlisted in June. |
| AmbientEngine.HeartbeatIntervalMs | tuning-constant | `5000` | src/Deckle.Lighting.Ambient/Engine/AmbientEngine.cs:137 | non | Log-cadence only; perceivable solely in the LogWindow. Borderline diagnostic exposable. |
| AmbientHueChangeAttributor.PendingEchoWindow | tuning-constant | `TimeSpan.FromSeconds(10)` | src/Deckle.Lighting.Ambient/Engine/AmbientHueChangeAttributor.cs:23 | non | Attribution heuristic (is this bridge update our own echo). Internal; perceivable only through diagnostic wording. |
| AmbientHueChangeAttributor.BrightnessTolerance | tuning-constant | `1` | src/Deckle.Lighting.Ambient/Engine/AmbientHueChangeAttributor.cs:25 | non | Echo-matching tolerance; likely a pure implementation constant. |
| AmbientHueChangeAttributor.XyTolerance | tuning-constant | `0.002f` | src/Deckle.Lighting.Ambient/Engine/AmbientHueChangeAttributor.cs:26 | non | Echo-matching tolerance; likely a pure implementation constant. |
| LightZoneSuggester.NeutralDeadband | tuning-constant | `0.15` | src/Deckle.Lighting.Ambient/Engine/LightZoneSuggester.cs:35 | non | Shapes the zone auto-suggestion the user sees when assigning lamps - perceivable through a wrong suggestion, but odd to expose. |
| BrightnessCurveCanvas.SampleCount | tuning-constant | `80` | src/Deckle.Lighting.Ambient/Ui/Controls/BrightnessCurveCanvas.xaml.cs:16 | non | Curve-editor rendering resolution. Almost certainly a false positive (pure UI implementation); kept per the in-doubt rule. |
| BrightnessCurveCanvas.PlotPadding | tuning-constant | `6.0` | src/Deckle.Lighting.Ambient/Ui/Controls/BrightnessCurveCanvas.xaml.cs:17 | non | Pure layout constant of the curve editor; drop candidate. |
| BrightnessCurveCanvas.HandleSize | tuning-constant | `24.0` | src/Deckle.Lighting.Ambient/Ui/Controls/BrightnessCurveCanvas.xaml.cs:18 | non | Hit-target size of the bezier handles; accessibility-relevant but not a setting. Drop candidate. Sibling HandleRadius (line 19, = HandleSize/2) was skipped by the sweep as a computed value. |
| AmbientModePresets.Game | default-value | `ExposureEv 0.5, SaturationBoost 1.3, MinBrightnessEnabled true, MinBrightness 100, Curve 0.33/0.33/0.67/0.67, SmoothingAlpha 0.40, ChangeThreshold 6` | src/Deckle.Lighting.Ambient/Engine/AmbientModePresets.cs:21-33 | non | Missed by the sweep (assignments inside a switch, not initializers). This is the effective out-of-box tuning since Mode defaults to Game - it overrides several AmbientSettings POCO defaults. |
| AmbientModePresets.Movie | default-value | `ExposureEv 0.0, SaturationBoost 0.9, MinBrightnessEnabled true, MinBrightness 60, Curve 0.42/0.08/0.58/0.92, SmoothingAlpha 0.15, ChangeThreshold 8` | src/Deckle.Lighting.Ambient/Engine/AmbientModePresets.cs:35-47 | non | Missed by the sweep; a full tuning bundle behind one enum value. |
| AmbientModePresets.Ambient | default-value | `ExposureEv -0.5, SaturationBoost 0.7, MinBrightnessEnabled true, MinBrightness 40, Curve 0.18/0.55/0.40/0.90, SmoothingAlpha 0.08, ChangeThreshold 10` | src/Deckle.Lighting.Ambient/Engine/AmbientModePresets.cs:49-62 | non | Missed by the sweep; a full tuning bundle behind one enum value. |

## Deckle.Llm

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| DefaultBaseUrl | tuning-constant | `"http://localhost:11434"` | src/Deckle.Llm/OllamaService.cs:160 | non | Fallback used only when the persisted endpoint is unparseable or its scheme is rejected. June lists the persisted LlmSettings.OllamaEndpoint = http://localhost:11434/api/generate (Deckle.Llm.Rewrite) — same host, different path shape. |

## Deckle.Llm.Rewrite

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| Model | tuning-constant | `"ministral-3:14b"` | src/Deckle.Llm.Rewrite/Engine/ParagraphRewrite.cs:26 | non | Hardcoded model for the interactive paragraph retaille, distinct from the user-facing RewriteProfile.Model. |
| Deadline | tuning-constant | `TimeSpan.FromSeconds(3)` | src/Deckle.Llm.Rewrite/Engine/ParagraphRewrite.cs:30 | non | Interactive offer budget; user-perceivable (offer appears or not). |
| NumCtx | tuning-constant | `4096` | src/Deckle.Llm.Rewrite/Engine/ParagraphRewrite.cs:35 | non |  |
| KeepAlive | tuning-constant | `"2m"` | src/Deckle.Llm.Rewrite/Engine/ParagraphRewrite.cs:40 | non | VRAM residency hint; perceivable as warm/cold latency. |
| SystemPrompt | tuning-constant | `(multi-line raw string, French few-shot prompt)` | src/Deckle.Llm.Rewrite/Engine/ParagraphRewrite.cs:52 | non | MISSED BY SWEEP (multi-line). Largest exposable of the module; source comment declares this file the prompts' single home. |
| Label | tuning-constant | `"paragraph"` | src/Deckle.Llm.Rewrite/Engine/ParagraphRewrite.cs:21 | non | Observability label only. Borderline false positive, kept per doubt rule. |
| Label | tuning-constant | `"sentence_correction_proposal"` | src/Deckle.Llm.Rewrite/Engine/SentenceRewrite.cs:9 | non | Observability label only; same borderline call. |
| Model | tuning-constant | `ParagraphRewrite.Model (= "ministral-3:14b")` | src/Deckle.Llm.Rewrite/Engine/SentenceRewrite.cs:10 | non | Alias, not an independent knob. |
| Deadline | tuning-constant | `TimeSpan.FromSeconds(5)` | src/Deckle.Llm.Rewrite/Engine/SentenceRewrite.cs:11 | non |  |
| NumCtx | tuning-constant | `2048` | src/Deckle.Llm.Rewrite/Engine/SentenceRewrite.cs:13 | non |  |
| KeepAlive | tuning-constant | `"2m"` | src/Deckle.Llm.Rewrite/Engine/SentenceRewrite.cs:14 | non |  |
| SystemPrompt | tuning-constant | `(multi-line raw string, French sentence-correction prompt)` | src/Deckle.Llm.Rewrite/Engine/SentenceRewrite.cs:16 | non | MISSED BY SWEEP (multi-line). |
| REWRITE_HARD_CAP | tuning-constant | `TimeSpan.FromMinutes(15)` | src/Deckle.Llm.Rewrite/Engine/RewriteService.cs:48 | non | Hard cap on a single rewrite call. |
| HttpClient.Timeout | tuning-constant | `Timeout.InfiniteTimeSpan` | src/Deckle.Llm.Rewrite/Engine/OllamaEngine.cs:34 | non | MISSED BY SWEEP. Deliberate disabling of the 100 s default; caller token is the only deadline. |
| POLL_INTERVAL | tuning-constant | `TimeSpan.FromSeconds(60)` | src/Deckle.Llm.Rewrite/Engine/OllamaEngine.cs:37 | non | MISSED BY SWEEP. /api/ps busy-probe cadence. |
| ReplaceGroupMax | tuning-constant | `3` | src/Deckle.Llm.Rewrite/Gate/DiffAlignment.cs:34 | non | Gate alignment horizon; gate knobs are code-level by doctrine. |
| FormDistanceCap | tuning-constant | `3` | src/Deckle.Llm.Rewrite/Gate/RewriteDiffGate.cs:27 | non | Source comment explicitly: the gate's only knobs, code-level, never exposed as settings. |
| FormDistancePercent | tuning-constant | `25` | src/Deckle.Llm.Rewrite/Gate/RewriteDiffGate.cs:34 | non | Same; tightened from 34 % on the 2026-07-19 eval. |
| MaxFillerPhraseLength | derived-value | `ComputeMaxPhraseLength()` | src/Deckle.Llm.Rewrite/Gate/GateLexicon.cs:88 | non | Kind fixed: computed property, not a constant. Real frozen data is the lexicon tables (_functionWords:21, _fillers:46, _fillerPhrases:54, _insertablePunctuation:69), excluded by sweep design. |
| CurlyApostrophe | tuning-constant | `'’'` | src/Deckle.Llm.Rewrite/Gate/GateToken.cs:44 | non | DROP-candidate: tokenizer character-class literal, tunes nothing. Sibling Apostrophe (line 43) likewise. Kept flagged. |
| Capacity | tuning-constant | `4096` | src/Deckle.Llm.Rewrite/Interaction/ParagraphDraft.cs:10 | non | Max observed draft length before invalidation; perceivable on very long paragraphs. |
| RewriteProfile.Id | persisted-setting | `""` | src/Deckle.Llm.Rewrite/LlmSettings.cs:17 | oui | June excludes it from the settings total as a read-only id. |
| RewriteProfile.Name | persisted-setting | `""` | src/Deckle.Llm.Rewrite/LlmSettings.cs:19 | oui | Part of the bespoke Profiles list editor. |
| RewriteProfile.Model | persisted-setting | `""` | src/Deckle.Llm.Rewrite/LlmSettings.cs:20 | oui | Bespoke: runtime Ollama model enumeration. |
| RewriteProfile.SystemPrompt | persisted-setting | `""` | src/Deckle.Llm.Rewrite/LlmSettings.cs:21 | oui | Bespoke multi-line text editor. |
| RewriteProfile.Temperature | persisted-setting | `null (double?)` | src/Deckle.Llm.Rewrite/LlmSettings.cs:24 | oui | null = Ollama default, not sent. |
| RewriteProfile.NumCtxK | persisted-setting | `null (int?)` | src/Deckle.Llm.Rewrite/LlmSettings.cs:25 | oui | In K, x1024 when sent. |
| RewriteProfile.TopP | persisted-setting | `null (double?)` | src/Deckle.Llm.Rewrite/LlmSettings.cs:26 | oui |  |
| RewriteProfile.RepeatPenalty | persisted-setting | `null (double?)` | src/Deckle.Llm.Rewrite/LlmSettings.cs:27 | oui |  |
| AutoRewriteRule.MinDurationSeconds | persisted-setting | `0` | src/Deckle.Llm.Rewrite/LlmSettings.cs:34 | oui | Kind fixed: default-value -> persisted-setting. Legacy shape retained for deserialization, no longer evaluated. |
| AutoRewriteRule.ProfileId | persisted-setting | `""` | src/Deckle.Llm.Rewrite/LlmSettings.cs:39 | oui | Legacy, deserialization only. |
| AutoRewriteRule.ProfileName | persisted-setting | `""` | src/Deckle.Llm.Rewrite/LlmSettings.cs:40 | oui | Legacy, deserialization only. |
| AutoRewriteRuleByWords.MinWordCount | persisted-setting | `0` | src/Deckle.Llm.Rewrite/LlmSettings.cs:47 | oui | Legacy, deserialization only. |
| AutoRewriteRuleByWords.ProfileId | persisted-setting | `""` | src/Deckle.Llm.Rewrite/LlmSettings.cs:48 | oui | Legacy, deserialization only. |
| AutoRewriteRuleByWords.ProfileName | persisted-setting | `""` | src/Deckle.Llm.Rewrite/LlmSettings.cs:49 | oui | Legacy, deserialization only. |
| LlmSettings.Enabled | persisted-setting | `true` | src/Deckle.Llm.Rewrite/LlmSettings.cs:54 | oui |  |
| LlmSettings.OllamaEndpoint | persisted-setting | `"http://localhost:11434/api/generate"` | src/Deckle.Llm.Rewrite/LlmSettings.cs:55 | oui |  |
| LlmSettings.PrimaryRewriteProfileName | persisted-setting | `null` | src/Deckle.Llm.Rewrite/LlmSettings.cs:61 | oui | Bespoke runtime-dependent ComboBox; Shift+Win+backtick. |
| LlmSettings.SecondaryRewriteProfileName | persisted-setting | `null` | src/Deckle.Llm.Rewrite/LlmSettings.cs:65 | oui | Bespoke; Ctrl+Win+backtick. |
| LlmSettings.PrimaryRewriteProfileId | persisted-setting | `null` | src/Deckle.Llm.Rewrite/LlmSettings.cs:70 | oui | Stable companion id filled by migrations; not directly edited. |
| LlmSettings.SecondaryRewriteProfileId | persisted-setting | `null` | src/Deckle.Llm.Rewrite/LlmSettings.cs:71 | oui | Same. |
| LlmSettings.Profiles | persisted-setting | `3 seeded profiles Lissage/Affinage/Arrangement, Model="", Temperature 0.30, NumCtxK 8/16/16, tuned French SystemPrompts` | src/Deckle.Llm.Rewrite/LlmSettings.cs:83 | oui | MISSED BY SWEEP entirely (multi-line collection initializer). June lists it bespoke. The three shipped SystemPrompts are themselves substantial exposables, restored by Reset Profiles. |
| LlmSettings.AutoRewriteRules | persisted-setting | `[600s->Arrangement, 300s->Affinage, 60s->Lissage]` | src/Deckle.Llm.Rewrite/LlmSettings.cs:248 | oui | MISSED BY SWEEP (multi-line). Legacy, deserialization only. |
| LlmSettings.RuleMetric | persisted-setting | `"Duration"` | src/Deckle.Llm.Rewrite/LlmSettings.cs:251 | oui | Verified at line 251. June gap #10 flags it as driving sibling-panel visibility; source comment marks it legacy. |
| LlmSettings.AutoRewriteRulesByWords | persisted-setting | `[1200->Arrangement, 600->Affinage, 150->Lissage]` | src/Deckle.Llm.Rewrite/LlmSettings.cs:254 | oui | MISSED BY SWEEP (multi-line). Legacy, deserialization only. |
| OllamaAdminTimeout | tuning-constant | `TimeSpan.FromSeconds(5)` | src/Deckle.Llm.Rewrite/Ui/LlmPage.xaml.cs:39 | non | Bounds Ollama admin calls (list/show) so the Settings page cannot freeze. |
| DeleteModelTimeout | tuning-constant | `TimeSpan.FromSeconds(30)` | src/Deckle.Llm.Rewrite/Ui/LlmModelsSection.xaml.cs:132 | non | MISSED BY SWEEP (inline literal, not a named field). Name coined here for the inventory. |
| NoneSentinel | tuning-constant | `"(None)"` | src/Deckle.Llm.Rewrite/Ui/LlmShortcutSlotsSection.xaml.cs:25 | non | UI wording sentinel, stored as null. Wording exposable, not a tuning knob. |
| CtxKSteps | tuning-constant | `{1,2,4,8,16,32,64,128,256}` | src/Deckle.Llm.Rewrite/Ui/ProfileViewModel.cs:14 | non | Discrete ladder the NumCtxK slider snaps to; bounds what the user can pick. |
| DefaultTemperature | default-value | `0.5` | src/Deckle.Llm.Rewrite/Ui/ProfileViewModel.cs:15 | non | Kind fixed -> default-value (VM fallback when POCO is null). CONFLICT: seeded profiles ship 0.30. |
| DefaultNumCtxK | default-value | `2` | src/Deckle.Llm.Rewrite/Ui/ProfileViewModel.cs:16 | non | Kind fixed -> default-value. CONFLICT: seeded profiles ship 8/16/16. |
| WidthDip | tuning-constant | `440` | src/Deckle.Llm.Rewrite/Ui/RewriteOfferWindow.xaml.cs:18 | non | Rewrite-offer window geometry. |
| HeightDip | tuning-constant | `280` | src/Deckle.Llm.Rewrite/Ui/RewriteOfferWindow.xaml.cs:19 | non |  |
| AnchorGapDip | tuning-constant | `8` | src/Deckle.Llm.Rewrite/Ui/RewriteOfferWindow.xaml.cs:20 | non |  |

## Deckle.Modules

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| Transcription | tuning-constant | `"transcription"` | src/Deckle.Modules/ModuleIds.cs:17 | non | Identity string, not a tunable value. Kept because the ModuleIds set IS the module catalogue the setup Modules page and the presence file arbitrate over — the vocabulary matters even if no single id is settable. |
| Rewrite | tuning-constant | `"rewrite"` | src/Deckle.Modules/ModuleIds.cs:18 | non | Identity string, not a tunable value — same rationale as ModuleIds.Transcription. |
| Autocorrect | tuning-constant | `"autocorrect"` | src/Deckle.Modules/ModuleIds.cs:19 | non | Identity string, not a tunable value — same rationale as ModuleIds.Transcription. Note: June inventory has an AutocorrectEnabled setting, but that is Deckle.Autocorrect's own persisted toggle, not this id. |
| Ambient | tuning-constant | `"ambient"` | src/Deckle.Modules/ModuleIds.cs:20 | non | Identity string, not a tunable value — same rationale as ModuleIds.Transcription. |
| Trackpad | tuning-constant | `"trackpad"` | src/Deckle.Modules/ModuleIds.cs:21 | non | Identity string, not a tunable value — same rationale as ModuleIds.Transcription. |
| Anytype | tuning-constant | `"anytype"` | src/Deckle.Modules/ModuleIds.cs:22 | non | Identity string, not a tunable value — same rationale as ModuleIds.Transcription. |

## Deckle.Notifications

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| PromptLifetime | tuning-constant | `TimeSpan.FromMinutes(15)` | src/Deckle.Notifications/Channels/Toast/ToastChannel.cs:29 | non | How long a toast prompt stays answerable before settling as unanswered. The code comment states outright 'Module constant, not configurable' — it bounds both the await and the toast Expiration so neither outlives the other. User-perceivable duration, but the source has already argued against exposing it. |

## Deckle.Playground

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| OklchLightness | playground-parameter | `0.75f` | src/Deckle.Playground/Models/TuningModel.cs:28 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| OklchChroma | playground-parameter | `0.3f` | src/Deckle.Playground/Models/TuningModel.cs:29 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| HueStart | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:30 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| HueRange | playground-parameter | `1f` | src/Deckle.Playground/Models/TuningModel.cs:31 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| WedgeCount | playground-parameter | `360` | src/Deckle.Playground/Models/TuningModel.cs:32 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| HuePeriodSeconds | playground-parameter | `14.0` | src/Deckle.Playground/Models/TuningModel.cs:35 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| HueDirection | playground-parameter | `1f` | src/Deckle.Playground/Models/TuningModel.cs:36 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| HuePhaseTurns | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:37 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| HueEaseP1X | playground-parameter | `0.125f` | src/Deckle.Playground/Models/TuningModel.cs:41 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| HueEaseP1Y | playground-parameter | `0.375f` | src/Deckle.Playground/Models/TuningModel.cs:42 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| HueEaseP2X | playground-parameter | `0.875f` | src/Deckle.Playground/Models/TuningModel.cs:43 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| HueEaseP2Y | playground-parameter | `0.625f` | src/Deckle.Playground/Models/TuningModel.cs:44 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| HueMinSpeedFraction | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:45 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ConicSpanTurns | playground-parameter | `0.5f` | src/Deckle.Playground/Models/TuningModel.cs:48 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ConicLeadFadeTurns | playground-parameter | `1f` | src/Deckle.Playground/Models/TuningModel.cs:49 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ConicTailFadeTurns | playground-parameter | `1f` | src/Deckle.Playground/Models/TuningModel.cs:50 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ConicFadeCurve | playground-parameter | `4f` | src/Deckle.Playground/Models/TuningModel.cs:51 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ArcMirror | playground-parameter | `true` | src/Deckle.Playground/Models/TuningModel.cs:52 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ArcPeriodSeconds | playground-parameter | `8.0` | src/Deckle.Playground/Models/TuningModel.cs:55 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ArcDirection | playground-parameter | `1f` | src/Deckle.Playground/Models/TuningModel.cs:56 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ArcPhaseTurns | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:57 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ArcEaseP1X | playground-parameter | `0.125f` | src/Deckle.Playground/Models/TuningModel.cs:58 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ArcEaseP1Y | playground-parameter | `0.375f` | src/Deckle.Playground/Models/TuningModel.cs:59 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ArcEaseP2X | playground-parameter | `0.875f` | src/Deckle.Playground/Models/TuningModel.cs:60 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ArcEaseP2Y | playground-parameter | `0.625f` | src/Deckle.Playground/Models/TuningModel.cs:61 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| ArcMinSpeedFraction | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:62 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| CloneCentreXFraction | playground-parameter | `196f / 272f` | src/Deckle.Playground/Models/TuningModel.cs:67 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| CloneCentreYFraction | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:68 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| CloneOklchLightness | playground-parameter | `0.9f` | src/Deckle.Playground/Models/TuningModel.cs:73 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| CloneOklchChroma | playground-parameter | `0.3f` | src/Deckle.Playground/Models/TuningModel.cs:74 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| CloneHuePeriodSeconds | playground-parameter | `7.0` | src/Deckle.Playground/Models/TuningModel.cs:78 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| CloneHueDirection | playground-parameter | `-1f` | src/Deckle.Playground/Models/TuningModel.cs:79 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| CloneArcPeriodSeconds | playground-parameter | `4.0` | src/Deckle.Playground/Models/TuningModel.cs:80 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| CloneArcDirection | playground-parameter | `-1f` | src/Deckle.Playground/Models/TuningModel.cs:81 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RewritingSaturation | playground-parameter | `1f` | src/Deckle.Playground/Models/TuningModel.cs:84 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RewritingHueShiftTurns | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:85 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RewritingExposure | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:86 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RewritingOpacity | playground-parameter | `1f` | src/Deckle.Playground/Models/TuningModel.cs:87 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RewritingBlendSeconds | playground-parameter | `2` | src/Deckle.Playground/Models/TuningModel.cs:88 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| TranscribingSaturationDark | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:91 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| TranscribingSaturationLight | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:92 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| TranscribingHueShiftTurns | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:93 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| TranscribingExposureDark | playground-parameter | `1.0f` | src/Deckle.Playground/Models/TuningModel.cs:94 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| TranscribingExposureLight | playground-parameter | `-1.0f` | src/Deckle.Playground/Models/TuningModel.cs:95 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| TranscribingOpacity | playground-parameter | `1f` | src/Deckle.Playground/Models/TuningModel.cs:96 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| TranscribingBlendSeconds | playground-parameter | `2` | src/Deckle.Playground/Models/TuningModel.cs:97 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingConicSpanTurns | playground-parameter | `0.5f` | src/Deckle.Playground/Models/TuningModel.cs:100 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingConicLeadFadeTurns | playground-parameter | `1f` | src/Deckle.Playground/Models/TuningModel.cs:101 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingConicTailFadeTurns | playground-parameter | `1f` | src/Deckle.Playground/Models/TuningModel.cs:102 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingConicFadeCurve | playground-parameter | `2f` | src/Deckle.Playground/Models/TuningModel.cs:103 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingArcMirror | playground-parameter | `true` | src/Deckle.Playground/Models/TuningModel.cs:104 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingArcPhaseTurns | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:105 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingSaturationDark | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:108 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingSaturationLight | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:109 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingHueShiftTurns | playground-parameter | `0f` | src/Deckle.Playground/Models/TuningModel.cs:110 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingExposureDark | playground-parameter | `1.0f` | src/Deckle.Playground/Models/TuningModel.cs:111 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingExposureLight | playground-parameter | `-1.0f` | src/Deckle.Playground/Models/TuningModel.cs:112 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingBlendSeconds | playground-parameter | `2` | src/Deckle.Playground/Models/TuningModel.cs:113 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| RecordingHuePeriodSeconds | playground-parameter | `0` | src/Deckle.Playground/Models/TuningModel.cs:114 | oui | Mirror of the shipping HudComposition.ConicArcStrokeConfig default; memory-only in the Playground. The real exposable is the Deckle.Composition default it shadows. |
| IdentifyFlashDuration | tuning-constant | `TimeSpan.FromSeconds(3)` | src/Deckle.Playground/Views/Ambient/AmbientPage.LightZones.cs:53 | non | User-perceivable: caps the Hue Identify strobe. Playground-only today; would belong to Deckle.Lighting.Ambient if the Identify affordance ships in Settings. |
| PreviewCellSize | tuning-constant | `16` | src/Deckle.Playground/Views/Ambient/AmbientPage.xaml.cs:90 | non | Dip size of one preview-grid cell; the Viewbox rescales it, so visual impact is near-nil. Borderline implementation constant — kept per doubt rule. |
| NakedHudSize | tuning-constant | `new Vector2(272f, 78f)` | src/Deckle.Playground/Views/Hud/HudPage.xaml.cs:112 | non | Comment says 'Constants — never change at runtime'; it restates the shipping HUD stroke surface. Not a knob, kept as a traceable geometry fact. |
| NakedHostDim | tuning-constant | `300f` | src/Deckle.Playground/Views/Hud/HudPage.xaml.cs:113 | non | Same as NakedHudSize — host box sized to avoid clipping the 270 dip cone. Frozen by construction. |

## Deckle.Security

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| MutexTimeout | tuning-constant | `TimeSpan.FromSeconds(5)` | src/Deckle.Security/SecretVault.cs:43 | non | Cross-process vault lock timeout. User-perceivable only as a save failure under contention — borderline implementation constant, kept under the doubt rule. |

## Deckle.Settings

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| AppearanceSettings.Theme | persisted-setting | `"System"` | src/Deckle.Settings/Persistence/AppSettings.cs:55 | oui |  |
| OverlaySettings.Enabled | persisted-setting | `true` | src/Deckle.Settings/Persistence/AppSettings.cs:62 | oui |  |
| OverlaySettings.FadeOnProximity | persisted-setting | `true` | src/Deckle.Settings/Persistence/AppSettings.cs:63 | oui |  |
| OverlaySettings.Position | persisted-setting | `"BottomCenter"` | src/Deckle.Settings/Persistence/AppSettings.cs:64 | oui | Closed vocabulary documented in-code: BottomCenter \| BottomRight \| TopCenter. |
| OverlaySettings.Animations | persisted-setting | `true` | src/Deckle.Settings/Persistence/AppSettings.cs:69 | oui |  |
| PasteSettings.AutoPasteEnabled | persisted-setting | `false` | src/Deckle.Settings/Persistence/AppSettings.cs:49 | oui |  |
| UpdatesSettings.AutoCheckEnabled | persisted-setting | `true` | src/Deckle.Settings/Persistence/AppSettings.cs:40 | non | Composed card GeneralUpdateAutoCheckCard exists (GeneralViewModel.Settings.cs:72-78); June inventory has no Updates category — likely post-June addition. |
| PathsSettings.BackupDirectory | persisted-setting | `""` | src/Deckle.Settings/Persistence/AppSettings.cs:85 | non | Empty = auto-resolve via AppPaths.SettingsBackupDirectory. June lists a diagnostics 'Storage directory' but not this backup path — confirm they are distinct. |
| AutostartEnabled | persisted-setting | `false (StartupService.DefaultEnabled)` | src/Deckle.Settings/ViewModels/GeneralViewModel.cs:47 | oui | ADDED FROM JUNE — sweep missed it. Not backed by AppSettings: source of truth is the OS (HKCU\Run + elevated scheduled task) via StartupService; default read from src/Deckle.Shell/AutostartService.cs:42. |
| SettingsBackupService.FilenamePrefix | tuning-constant | `"settings-"` | src/Deckle.Settings/Persistence/SettingsBackupService.cs:44 | non | Borderline: user-perceivable only as the snapshot file name on disk. Kept per keep-when-in-doubt. |
| SettingsBackupService.FilenameExtension | tuning-constant | `".json"` | src/Deckle.Settings/Persistence/SettingsBackupService.cs:45 | non | Same as FilenamePrefix; realistically frozen. |
| SettingsBackupService.FilenameTimestampFormat | tuning-constant | `"yyyyMMdd-HHmmss"` | src/Deckle.Settings/Persistence/SettingsBackupService.cs:46 | non | Same family; frozen candidate. |
| SettingsWindow.SearchIconZoneWidth | tuning-constant | `180.0` | src/Deckle.Settings/SettingsWindow.Search.cs:39 | non | Layout threshold (DIPs) for search box collapse to icon. Chrome tuning, not a user knob — frozen candidate. |
| SettingsWindow.SearchInlineZoneWidth | tuning-constant | `204.0` | src/Deckle.Settings/SettingsWindow.Search.cs:40 | non | Paired restore threshold; the 24 DIP gap is deliberate hysteresis — the two must move together. |
| SettingsWindow.SearchDebounceMs | tuning-constant | `300` | src/Deckle.Settings/SettingsWindow.Search.cs:50 | non | Perceptible (search responsiveness) but a craft value, not a setting. |
| SettingsWindow.MaxSuggestions | tuning-constant | `7` | src/Deckle.Settings/SettingsWindow.Search.cs:54 | non | Rendered-hit cap; overflow becomes a '+N more — refine' notice. Perceptible. |
| SettingsWindow.TitleBarProbeDebounceMs | tuning-constant | `300` | src/Deckle.Settings/SettingsWindow.TitleBarProbe.cs:22 | non | Diagnostics-only (trace emission on settled resize). Weakest exposable of the set — arguably a drop. |

## Deckle.Setup

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| NativeRuntimeItemId | tuning-constant | `"native-runtime"` | src/Deckle.Setup/InstallPlan.cs:30 | non | Identity string for an installable plan item. Kept for the same reason as ModuleIds: the item set is what the wizard shows and installs, so the vocabulary is arbitration-relevant even though no single id is settable. |
| SileroItemId | tuning-constant | `"silero-vad"` | src/Deckle.Setup/InstallPlan.cs:31 | non | Installable plan item id — same rationale as NativeRuntimeItemId. |
| CamembertItemId | tuning-constant | `"camembert-base"` | src/Deckle.Setup/InstallPlan.cs:32 | non | Installable plan item id — same rationale as NativeRuntimeItemId. |
| AnytypeItemId | tuning-constant | `"anytype-cli"` | src/Deckle.Setup/InstallPlan.cs:33 | non | Installable plan item id — same rationale as NativeRuntimeItemId. |
| Location | default-value | `AppPaths.UserDataRoot` | src/Deckle.Setup/SetupContext.cs:31 | non |  |
| InstallDirectory | default-value | `Deckle.Install.InstallPaths.DefaultInstallDir` | src/Deckle.Setup/SetupContext.cs:81 | non |  |
| DataDirectory | default-value | `Deckle.Install.InstallPaths.DefaultDataDir` | src/Deckle.Setup/SetupContext.cs:82 | non |  |
| SelectedModules | default-value | `new HashSet<string>(StringComparer.Ordinal)` | src/Deckle.Setup/SetupContext.cs:89 | non | MISSED BY SWEEP — added from source. The module selection the install plan is built from: seeded from the recorded presence choice (or the full catalogue when none), then overwritten by the Modules page. Directly user-chosen; arguably the most consequential exposable in Deckle.Setup. |
| UpdateAvailable | default-value | `NotificationDescriptor(Id: "setup.update_available", Category: "setup", Severity: Info, Channel: Toast, Actions: install/later)` | src/Deckle.Setup/SetupNotifications.cs:18 | non | MISSED BY SWEEP — added from source. The sweep caught the wrapper collection SetupNotifications.All instead. This is the real descriptor: setup's one user-facing toast. Severity/Channel and the notification's on/off are plausible exposables via the notifications catalogue. |

## Deckle.Shell

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| FileIdle | tuning-constant | `"recording--indicator--false--32px.ico"` | src/Deckle.Shell/IconAssets.cs:16 | non | Tray icon asset filename. Kept only under the doubt rule — a user could conceivably want a different tray icon, but this is an asset path, not a value. |
| FileRecording | tuning-constant | `"recording--indicator--true--32px.ico"` | src/Deckle.Shell/IconAssets.cs:17 | non | Same as FileIdle — recording-state tray icon asset filename. |
| AutostartService.DefaultEnabled | default-value | `false` | src/Deckle.Shell/AutostartService.cs:42 | oui | Added from the June inventory — the sweep missed it. June lists it as 'Start with Windows' / 'Autostart Enabled', hand-authored, registry-backed (HKCU Run). Expression-bodied static property, which is likely why the sweep's accessor filter dropped it; that same filter may have swallowed other real defaults repo-wide. |

## Deckle.Shell.TaskbarCover

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| CoverGeometry.RevealZoneDepth | tuning-constant | `192` | src/Deckle.Shell.TaskbarCover/CoverGeometry.cs:26 | non | Source comment states it outright: ported from the standalone utility (EDGE_ZONE), calibrated in daily use, deliberately a constant and not a setting. TaskbarCoverSettings.cs:10-12 restates the freeze. Still an exposable per the glossary, but arbitration is already written into the code. |
| TaskbarCoverHost.RecoverDelayMs | tuning-constant | `5000` | src/Deckle.Shell.TaskbarCover/TaskbarCoverHost.cs:40 | non | Same explicit freeze: ported HIDE_DELAY, calibrated in daily use, a constant and not a setting. Highly user-perceivable - how long the taskbar stays revealed. |
| TaskbarCoverSettings.Enabled | persisted-setting | `false` | src/Deckle.Shell.TaskbarCover/TaskbarCoverSettings.cs:8 | oui |  |

## Deckle.Shell.TrayMenu

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| CursorExcludeHalfExtent | tuning-constant | `18` | src/Deckle.Shell.TrayMenu/TrayContextMenuHost.cs:54 | non | Physical-pixel exclusion margin keeping the tray flyout off the tray icon (36px slot at 100% DPI). Derived from a system metric, not a taste knob. |
| FlyoutFrameMargin | tuning-constant | `4.0` | src/Deckle.Shell.TrayMenu/TrayContextMenuHost.cs:61 | non | Fallback-path chrome margin for MeasureFlyout. Comment says it is imprecise by nature and no longer drives the nominal path (which reads the real presenter DesiredSize). Likely dead-weight rather than exposable. |

## Deckle.Speech

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| SpeechSettings.Enabled | persisted-setting | `false` | src/Deckle.Speech/SpeechSettings.cs:18 | oui | Verified. Persisted to <UserDataRoot>/modules/speech/settings.json via SpeechSettingsService. No consumer today: the read-aloud hotkey (Alt+Win+`) fires regardless of Enabled, per the class comment. Dormant skeleton — June lacuna #7. |
| SpeechSettings.Voice | persisted-setting | `SpeechVoice.Pierre` | src/Deckle.Speech/SpeechSettings.cs:22 | oui | Verified. Closed enum {Pierre, Jessica} (FR reference clips). Wired through to ISpeechBackend.SynthesizeAsync but ignored by the Chatterbox stub. Enum set flagged in code as revisitable at the ONNX palier. |
| SpeechSettings.Temperature | persisted-setting | `0.6` | src/Deckle.Speech/SpeechSettings.cs:27 | oui | Verified. Comment gives practical range [0.5, 0.7] (below 0.5 robotic) — a UI range hint the sweep does not carry. Ignored by the stub backend. |
| SpeechEngine.OutputSampleRate | tuning-constant | `24000` | src/Deckle.Speech/SpeechEngine.cs:22 | non | Kept under doubt. private const, output render rate fixed at Chatterbox S3Gen's 24 kHz — dictated by the model, not a taste knob. Likely frozen-in-code, not exposable. |
| ChatterboxSpeechBackend.SampleRate | tuning-constant | `24000` | src/Deckle.Speech/ChatterboxSpeechBackend.cs:17 | non | Kept under doubt. Duplicate of OutputSampleRate (same 24 kHz, two declaration sites). Model-imposed format constant; dedup by value before any count. |

## Deckle.Transcription

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| CorpusTier.ShortLowerBound | tuning-constant | `30` | src/Deckle.Transcription/Corpus/CorpusTier.cs:17 | non | Corpus telemetry tiering bucket; analysis-only, not user-perceivable — kept as borderline. |
| CorpusTier.MediumLowerBound | tuning-constant | `200` | src/Deckle.Transcription/Corpus/CorpusTier.cs:18 | non | Corpus telemetry tiering bucket; analysis-only — kept as borderline. |
| CorpusTier.LongLowerBound | tuning-constant | `1000` | src/Deckle.Transcription/Corpus/CorpusTier.cs:19 | non | Corpus telemetry tiering bucket; analysis-only — kept as borderline. |
| CorpusTier.VeryLongLowerBound | tuning-constant | `3000` | src/Deckle.Transcription/Corpus/CorpusTier.cs:20 | non | Corpus telemetry tiering bucket; analysis-only — kept as borderline. |
| WavCorpusWriter.SampleRate | tuning-constant | `16_000` | src/Deckle.Transcription/Corpus/WavCorpusWriter.cs:32 | non | WAV corpus format, dictated by Whisper's 16 kHz mono contract; not tunable in practice. |
| WavCorpusWriter.BitsPerSample | tuning-constant | `16` | src/Deckle.Transcription/Corpus/WavCorpusWriter.cs:33 | non | WAV corpus format constant; not tunable in practice. |
| WavCorpusWriter.NumChannels | tuning-constant | `1` | src/Deckle.Transcription/Corpus/WavCorpusWriter.cs:34 | non | WAV corpus format constant; not tunable in practice. |
| WavCorpusWriter.AudioSubfolder | tuning-constant | `"audio"` | src/Deckle.Transcription/Corpus/WavCorpusWriter.cs:35 | non | Corpus folder name; path convention, not a tuning knob. |
| TranscriptionEngine.PrimingTailWords | tuning-constant | `30` | src/Deckle.Transcription/Engine/TranscriptionEngine.StreamingPipeline.cs:513 | non | Comment says explicitly 'not exposed and not yet tuned' — a genuine untuned exposable. |
| RecordingHostAdapter.DISPOSE_WORKER_JOIN_TIMEOUT_MS | tuning-constant | `30_000` | src/Deckle.Transcription/Engine/TranscriptionEngine.cs:428 | non | Shutdown robustness timeout; borderline implementation constant. |
| EnergySegmenter.FrameMs | tuning-constant | `50.0` | src/Deckle.Transcription/Streaming/EnergySegmenter.cs:57 | non | Hard-coupled to WaveInLoop's 800-sample sub-window — cannot be changed alone. |
| EnergySegmenterSettings.ThresholdDbfs | persisted-setting | `-45.0` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:48 | oui |  |
| EnergySegmenterSettings.HangoverMaxMs | persisted-setting | `5_000` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:49 | oui |  |
| EnergySegmenterSettings.HangoverMinMs | persisted-setting | `500` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:50 | oui |  |
| EnergySegmenterSettings.HangoverRampStartMs | persisted-setting | `15_000` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:51 | oui |  |
| EnergySegmenterSettings.HangoverRampEndMs | persisted-setting | `120_000` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:52 | oui |  |
| EnergySegmenterSettings.HangoverCurveX1 | persisted-setting | `0.85` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:53 | non | Persisted with no UI anywhere — June inventory gap #5 flags it, no table row. |
| EnergySegmenterSettings.HangoverCurveY1 | persisted-setting | `0.10` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:54 | non | Persisted with no UI anywhere — June inventory gap #5 only. |
| EnergySegmenterSettings.HangoverCurveX2 | persisted-setting | `0.90` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:55 | non | Persisted with no UI anywhere — June inventory gap #5 only. |
| EnergySegmenterSettings.HangoverCurveY2 | persisted-setting | `0.25` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:56 | non | Persisted with no UI anywhere — June inventory gap #5 only. |
| EnergySegmenterSettings.MarginMs | persisted-setting | `150` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:57 | oui |  |
| EnergySegmenterSettings.MinUtteranceMs | persisted-setting | `250` | src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:58 | oui |  |
| TranscriptionSettings.ModelsDirectory | persisted-setting | `""` | src/Deckle.Transcription/TranscriptionSettings.cs:16 | oui |  |
| TranscriptionSettings.FileTranscriptionOutputDirectory | persisted-setting | `""` | src/Deckle.Transcription/TranscriptionSettings.cs:22 | non | Persisted, empty sentinel = Desktop. Absent from the June inventory — likely added after it. |
| TranscriptionSettings.Engine | persisted-setting | `new()` | src/Deckle.Transcription/TranscriptionSettings.cs:24 | oui | Section container (nested POCO), not a knob itself. |
| TranscriptionSettings.Confidence | persisted-setting | `new()` | src/Deckle.Transcription/TranscriptionSettings.cs:25 | oui | Section container, not a knob. |
| TranscriptionSettings.OutputFilters | persisted-setting | `new()` | src/Deckle.Transcription/TranscriptionSettings.cs:26 | oui | Section container, not a knob. |
| TranscriptionSettings.Decoding | persisted-setting | `new()` | src/Deckle.Transcription/TranscriptionSettings.cs:27 | oui | Section container, not a knob. |
| TranscriptionSettings.Context | persisted-setting | `new()` | src/Deckle.Transcription/TranscriptionSettings.cs:28 | oui | Section container, not a knob. |
| TranscriptionSettings.Streaming | persisted-setting | `new()` | src/Deckle.Transcription/TranscriptionSettings.cs:29 | oui | Section container, not a knob. |
| StreamingSettings.Strategy | persisted-setting | `PipelineStrategyKind.Monolithic` | src/Deckle.Transcription/TranscriptionSettings.cs:45 | oui |  |
| StreamingSettings.Segmenter | persisted-setting | `new()` | src/Deckle.Transcription/TranscriptionSettings.cs:46 | oui | Section container, not a knob. |
| StreamingSettings.SpeechTrim | persisted-setting | `new()` | src/Deckle.Transcription/TranscriptionSettings.cs:47 | oui | Section container, not a knob. |
| SpeechTrimSettings.Enabled | persisted-setting | `true` | src/Deckle.Transcription/TranscriptionSettings.cs:64 | oui |  |
| SpeechTrimSettings.Threshold | persisted-setting | `0.5f` | src/Deckle.Transcription/TranscriptionSettings.cs:69 | oui |  |
| SpeechTrimSettings.MinSpeechDurationMs | persisted-setting | `250` | src/Deckle.Transcription/TranscriptionSettings.cs:72 | oui |  |
| SpeechTrimSettings.MinSilenceDurationMs | persisted-setting | `100` | src/Deckle.Transcription/TranscriptionSettings.cs:76 | oui |  |
| SpeechTrimSettings.SpeechPadMs | persisted-setting | `30` | src/Deckle.Transcription/TranscriptionSettings.cs:80 | oui |  |
| EngineSettings.Model | persisted-setting | `"ggml-base.bin"` | src/Deckle.Transcription/TranscriptionSettings.cs:88 | oui |  |
| EngineSettings.UseGpu | persisted-setting | `true` | src/Deckle.Transcription/TranscriptionSettings.cs:89 | oui |  |
| EngineSettings.Language | persisted-setting | `"fr"` | src/Deckle.Transcription/TranscriptionSettings.cs:90 | oui |  |
| EngineSettings.CarryInitialPrompt | persisted-setting | `true` | src/Deckle.Transcription/TranscriptionSettings.cs:101 | oui |  |
| ConfidenceSettings.EntropyThreshold | persisted-setting | `2.4` | src/Deckle.Transcription/TranscriptionSettings.cs:109 | oui |  |
| ConfidenceSettings.LogprobThreshold | persisted-setting | `-1.0` | src/Deckle.Transcription/TranscriptionSettings.cs:110 | oui |  |
| ConfidenceSettings.NoSpeechThreshold | persisted-setting | `0.6` | src/Deckle.Transcription/TranscriptionSettings.cs:111 | oui |  |
| OutputFilterSettings.SuppressNonSpeechTokens | persisted-setting | `true` | src/Deckle.Transcription/TranscriptionSettings.cs:116 | oui |  |
| OutputFilterSettings.SuppressBlank | persisted-setting | `true` | src/Deckle.Transcription/TranscriptionSettings.cs:117 | oui |  |
| OutputFilterSettings.SuppressRegex | persisted-setting | `""` | src/Deckle.Transcription/TranscriptionSettings.cs:118 | oui |  |
| DecodingSettings.Temperature | persisted-setting | `0.0` | src/Deckle.Transcription/TranscriptionSettings.cs:123 | oui |  |
| DecodingSettings.TemperatureIncrement | persisted-setting | `0.2` | src/Deckle.Transcription/TranscriptionSettings.cs:124 | oui |  |
| DecodingSettings.UseBeamSearch | persisted-setting | `true` | src/Deckle.Transcription/TranscriptionSettings.cs:129 | oui |  |
| DecodingSettings.BeamSize | persisted-setting | `5` | src/Deckle.Transcription/TranscriptionSettings.cs:130 | oui |  |
| ContextSettings.UseContext | persisted-setting | `true` | src/Deckle.Transcription/TranscriptionSettings.cs:138 | oui |  |
| ContextSettings.MaxTokens | persisted-setting | `-1` | src/Deckle.Transcription/TranscriptionSettings.cs:139 | oui |  |
| HangoverCurveCanvas.SampleCount | tuning-constant | `96` | src/Deckle.Transcription/Ui/Controls/HangoverCurveCanvas.xaml.cs:45 | non | HangoverCurveCanvas render resolution; visual quality, not a setting. |
| HangoverCurveCanvas.GutterLeft | tuning-constant | `46.0` | src/Deckle.Transcription/Ui/Controls/HangoverCurveCanvas.xaml.cs:50 | non | Canvas layout geometry; belongs to the control's design, not settings. |
| HangoverCurveCanvas.GutterBottom | tuning-constant | `24.0` | src/Deckle.Transcription/Ui/Controls/HangoverCurveCanvas.xaml.cs:51 | non | Canvas layout geometry. |
| HangoverCurveCanvas.PadTop | tuning-constant | `12.0` | src/Deckle.Transcription/Ui/Controls/HangoverCurveCanvas.xaml.cs:52 | non | Canvas layout geometry. |
| HangoverCurveCanvas.PadRight | tuning-constant | `14.0` | src/Deckle.Transcription/Ui/Controls/HangoverCurveCanvas.xaml.cs:53 | non | Canvas layout geometry. |
| HangoverCurveCanvas.TickLength | tuning-constant | `4.0` | src/Deckle.Transcription/Ui/Controls/HangoverCurveCanvas.xaml.cs:54 | non | Canvas layout geometry. |
| HangoverCurveCanvas.HandleSize | tuning-constant | `24.0` | src/Deckle.Transcription/Ui/Controls/HangoverCurveCanvas.xaml.cs:57 | non | Canvas hit-target size; accessibility-relevant but design-owned. |
| EngineSettings.InitialPrompt | persisted-setting | `"Bon. Je suis en train de coder une application Windows, ..." (7-line French seed prompt)` | src/Deckle.Transcription/TranscriptionSettings.cs:91 | oui | Missed by the sweep (multi-line string concat initializer). Real persisted setting, hand-authored in WhisperPage. |

## Deckle.Transcription.Whisper

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| DefaultModelFileName | tuning-constant | `"ggml-base.bin"` | src/Deckle.Transcription.Whisper/Setup/SpeechModels.cs:29 | non | June records the Whisper model default as EngineSettings.Model = ggml-large-v3.bin (Deckle.Transcription). Two defaults coexist: this one is the no-override install target, base on purpose so the first-run path is not a 3 GB download. |

## Deckle.Travel

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| language | json-default | `"fr"` | src/Deckle.Travel/Terms/terms.fr.json:2 | non |  |
| properties.start_date | json-default | `"Début"` | src/Deckle.Travel/Terms/terms.fr.json:13 | non |  |
| properties.end_date | json-default | `"Fin"` | src/Deckle.Travel/Terms/terms.fr.json:14 | non |  |
| properties.date | json-default | `"Date"` | src/Deckle.Travel/Terms/terms.fr.json:15 | non |  |
| properties.appointment | json-default | `"RDV"` | src/Deckle.Travel/Terms/terms.fr.json:16 | non |  |
| properties.arrival | json-default | `"Arrivée"` | src/Deckle.Travel/Terms/terms.fr.json:17 | non |  |
| properties.departure | json-default | `"Départ"` | src/Deckle.Travel/Terms/terms.fr.json:18 | non |  |
| properties.duration | json-default | `"Durée"` | src/Deckle.Travel/Terms/terms.fr.json:19 | non |  |
| properties.visit_duration | json-default | `"Durée de visite"` | src/Deckle.Travel/Terms/terms.fr.json:20 | non |  |
| properties.accessibility | json-default | `"Accessibilité"` | src/Deckle.Travel/Terms/terms.fr.json:21 | non |  |
| properties.address | json-default | `"Adresse"` | src/Deckle.Travel/Terms/terms.fr.json:22 | non |  |
| properties.official_site | json-default | `"Site officiel"` | src/Deckle.Travel/Terms/terms.fr.json:23 | non |  |
| properties.confirmation | json-default | `"Référence"` | src/Deckle.Travel/Terms/terms.fr.json:24 | non |  |
| properties.files | json-default | `"Fichiers"` | src/Deckle.Travel/Terms/terms.fr.json:25 | non |  |
| properties.amount | json-default | `"Montant"` | src/Deckle.Travel/Terms/terms.fr.json:26 | non |  |
| properties.activity_category | json-default | `"Catégorie"` | src/Deckle.Travel/Terms/terms.fr.json:27 | non |  |
| properties.expense_category | json-default | `"Catégorie"` | src/Deckle.Travel/Terms/terms.fr.json:28 | non |  |
| properties.place_category | json-default | `"Catégorie"` | src/Deckle.Travel/Terms/terms.fr.json:29 | non |  |
| properties.mode | json-default | `"Mode"` | src/Deckle.Travel/Terms/terms.fr.json:30 | non |  |
| properties.stay | json-default | `"Séjour"` | src/Deckle.Travel/Terms/terms.fr.json:31 | non |  |
| properties.stage | json-default | `"Étape"` | src/Deckle.Travel/Terms/terms.fr.json:32 | non |  |
| properties.place | json-default | `"Lieu"` | src/Deckle.Travel/Terms/terms.fr.json:33 | non |  |
| properties.expense | json-default | `"Dépense"` | src/Deckle.Travel/Terms/terms.fr.json:34 | non |  |
| options.activity_category.walk | json-default | `"Marche"` | src/Deckle.Travel/Terms/terms.fr.json:38 | non |  |
| options.activity_category.visit | json-default | `"Visite"` | src/Deckle.Travel/Terms/terms.fr.json:39 | non |  |
| options.activity_category.evening | json-default | `"Soirée"` | src/Deckle.Travel/Terms/terms.fr.json:40 | non |  |
| options.activity_category.sport | json-default | `"Sport"` | src/Deckle.Travel/Terms/terms.fr.json:41 | non |  |
| options.activity_category.meal | json-default | `"Repas"` | src/Deckle.Travel/Terms/terms.fr.json:42 | non |  |
| options.activity_category.other | json-default | `"Autre"` | src/Deckle.Travel/Terms/terms.fr.json:43 | non |  |
| options.expense_category.transport | json-default | `"Transport"` | src/Deckle.Travel/Terms/terms.fr.json:46 | non |  |
| options.expense_category.lodging | json-default | `"Hébergement"` | src/Deckle.Travel/Terms/terms.fr.json:47 | non |  |
| options.expense_category.food | json-default | `"Restauration"` | src/Deckle.Travel/Terms/terms.fr.json:48 | non |  |
| options.expense_category.activity | json-default | `"Activité"` | src/Deckle.Travel/Terms/terms.fr.json:49 | non |  |
| options.expense_category.purchase | json-default | `"Achat"` | src/Deckle.Travel/Terms/terms.fr.json:50 | non |  |
| options.expense_category.fees | json-default | `"Frais"` | src/Deckle.Travel/Terms/terms.fr.json:51 | non |  |
| options.expense_category.other | json-default | `"Autre"` | src/Deckle.Travel/Terms/terms.fr.json:52 | non |  |
| options.mode.plane | json-default | `"Avion"` | src/Deckle.Travel/Terms/terms.fr.json:55 | non |  |
| options.mode.train | json-default | `"Train"` | src/Deckle.Travel/Terms/terms.fr.json:56 | non |  |
| options.mode.bus | json-default | `"Bus"` | src/Deckle.Travel/Terms/terms.fr.json:57 | non |  |
| options.mode.ferry | json-default | `"Ferry"` | src/Deckle.Travel/Terms/terms.fr.json:58 | non |  |
| options.mode.car | json-default | `"Voiture"` | src/Deckle.Travel/Terms/terms.fr.json:59 | non |  |
| MaxBatchSize | tuning-constant | `100` | src/Deckle.Travel/TravelGestures.cs:14 | non | caps a single batched gesture; user-perceivable only at the ceiling |
| PageLimit | tuning-constant | `1000` | src/Deckle.Travel/TravelObjects.cs:105 | non | pagination page size; perf-only unless a ceiling becomes user-perceivable |
| ReadTagsAsync.limit | tuning-constant | `100` | src/Deckle.Travel/TravelPropertyWriter.cs:135 | non | local pagination page size inside ReadTagsAsync; perf-only, kept per doubt rule |
| DefaultLanguage | tuning-constant | `"fr"` | src/Deckle.Travel/TravelTerms.cs:13 | non |  |
| types.stay.name | json-default | `"Séjour"` | src/Deckle.Travel/Terms/terms.fr.json:4 | non | missed by the sweep, which flattened the nested types block |
| types.stay.plural | json-default | `"Séjours"` | src/Deckle.Travel/Terms/terms.fr.json:4 | non | missed by the sweep, which flattened the nested types block |
| types.stage.name | json-default | `"Étape"` | src/Deckle.Travel/Terms/terms.fr.json:5 | non | missed by the sweep, which flattened the nested types block |
| types.stage.plural | json-default | `"Étapes"` | src/Deckle.Travel/Terms/terms.fr.json:5 | non | missed by the sweep, which flattened the nested types block |
| types.place.name | json-default | `"Lieu"` | src/Deckle.Travel/Terms/terms.fr.json:6 | non | missed by the sweep, which flattened the nested types block |
| types.place.plural | json-default | `"Lieux"` | src/Deckle.Travel/Terms/terms.fr.json:6 | non | missed by the sweep, which flattened the nested types block |
| types.activity.name | json-default | `"Activité"` | src/Deckle.Travel/Terms/terms.fr.json:7 | non | missed by the sweep, which flattened the nested types block |
| types.activity.plural | json-default | `"Activités"` | src/Deckle.Travel/Terms/terms.fr.json:7 | non | missed by the sweep, which flattened the nested types block |
| types.transfer.name | json-default | `"Déplacement"` | src/Deckle.Travel/Terms/terms.fr.json:8 | non | missed by the sweep, which flattened the nested types block |
| types.transfer.plural | json-default | `"Déplacements"` | src/Deckle.Travel/Terms/terms.fr.json:8 | non | missed by the sweep, which flattened the nested types block |
| types.lodging.name | json-default | `"Hébergement"` | src/Deckle.Travel/Terms/terms.fr.json:9 | non | missed by the sweep, which flattened the nested types block |
| types.lodging.plural | json-default | `"Hébergements"` | src/Deckle.Travel/Terms/terms.fr.json:9 | non | missed by the sweep, which flattened the nested types block |
| types.expense.name | json-default | `"Dépense"` | src/Deckle.Travel/Terms/terms.fr.json:10 | non | missed by the sweep, which flattened the nested types block |
| types.expense.plural | json-default | `"Dépenses"` | src/Deckle.Travel/Terms/terms.fr.json:10 | non | missed by the sweep, which flattened the nested types block |

## Deckle.Vad

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| Threshold | default-value | `0.5f` | src/Deckle.Vad/SileroVadOptions.cs:11 | non | Sweep said persisted-setting; SileroVadOptions is not itself persisted. June holds the mirrored persisted knob as SpeechTrimSettings.Threshold (0.5f) under Deckle.Transcription — same number, different home. |
| MinSpeechDurationMs | default-value | `250` | src/Deckle.Vad/SileroVadOptions.cs:14 | non | Sweep said persisted-setting; not persisted here. Mirrors SpeechTrimSettings.MinSpeechDurationMs (250), listed in June under Deckle.Transcription. |
| MinSilenceDurationMs | default-value | `100` | src/Deckle.Vad/SileroVadOptions.cs:18 | non | Sweep said persisted-setting; not persisted here. Mirrors SpeechTrimSettings.MinSilenceDurationMs (100), listed in June under Deckle.Transcription. |
| SpeechPadMs | default-value | `30` | src/Deckle.Vad/SileroVadOptions.cs:22 | non | Sweep said persisted-setting; not persisted here. Mirrors SpeechTrimSettings.SpeechPadMs (30), listed in June under Deckle.Transcription. |

## Deckle.Vision

| Nom | Type | Valeur actuelle | Fichier:ligne | Juin | Doute |
|---|---|---|---|---|---|
| FrameSampler.ContentPeakReleaseDecay | tuning-constant | `0.97f` | src/Deckle.Vision/FrameSampler.cs:96 | non | Per-frame release of the HDR content-peak follower; directly shapes ambient-light responsiveness. Strongest Vision candidate, sits next to the June-listed Ambient HDR tuning block. |
| HdrState.SdrReferenceNits | tuning-constant | `80f` | src/Deckle.Vision/ScreenCaptureInterop.Hdr.cs:78 | non | Method-local const inside DetectHdrState, not a type member - the sweep reported it as a field of HdrState. SDR reference-white fallback; a display-calibration value, arguably exposable. |
| ScreenCaptureService.ThrottleIntervalMs | tuning-constant | `66` | src/Deckle.Vision/ScreenCaptureService.cs:56 | non | ~15 Hz capture cadence, deliberately coupled to the AmbientEngine push rate; not independently tunable without moving the engine too. |
| ScreenCaptureService.AcquireTimeoutMs | tuning-constant | `200` | src/Deckle.Vision/ScreenCaptureService.cs:61 | non | Implementation robustness bound; kept per doubt rule. |
| ScreenCaptureService.ErrorBackoffMs | tuning-constant | `500` | src/Deckle.Vision/ScreenCaptureService.cs:67 | non | Implementation robustness bound. |
| ScreenCaptureService.MaxInvalidCallRecoveryAttempts | tuning-constant | `10` | src/Deckle.Vision/ScreenCaptureService.cs:73 | non | Implementation robustness bound; decides when Ambient gives up - user-perceivable only as a failure. |
| ScreenCaptureService.RecreateInitialBackoffMs | tuning-constant | `2000` | src/Deckle.Vision/ScreenCaptureService.cs:78 | non | Source literal is 2_000. Implementation robustness bound. |
| ScreenCaptureService.RecreateExtendedBackoffMs | tuning-constant | `5000` | src/Deckle.Vision/ScreenCaptureService.cs:79 | non | Source literal is 5_000. Implementation robustness bound. |
| ScreenCaptureService.MaxUnexpectedRecreateAttempts | tuning-constant | `5` | src/Deckle.Vision/ScreenCaptureService.cs:80 | non | Implementation robustness bound; terminal-error threshold. |
| ScreenCaptureService.HeartbeatIntervalMs | tuning-constant | `5000` | src/Deckle.Vision/ScreenCaptureService.cs:88 | non | Source literal is 5_000. Log rollup cadence - diagnostic exposable, twin of the AmbientEngine.HeartbeatIntervalMs already flagged in the exposables inventory. |

---

## Lacunes de juin — non résolues

Reprises **verbatim** de la section « Lacunes / à recouper » de `docs/inventaire-settings.md` (juin 2026). Aucune n'est tranchée ici ; elles restent à l'arbitrage de Louis. Note : la section en compte onze, pas neuf.

- LevelWindowSettings defaults: verified source is MinDbfs=-55f, MaxDbfs=-32f, DbfsCurveExponent=1.0f, AutoCalibrationEnabled=false (src/Deckle.Audio/CaptureSettings.cs:58-61). Two zone reports gave conflicting figures (-90/-36, AutoCalibration default true) — those are wrong; treat -55/-32/1.0/false as canonical.
- EnergySegmenter defaults: verified source is ThresholdDbfs=-45.0, HangoverMaxMs=5000, HangoverMinMs=500, HangoverRampStartMs=15000, HangoverRampEndMs=120000, MarginMs=150, MinUtteranceMs=250 (src/Deckle.Transcription/Streaming/EnergySegmenterSettings.cs:48-58). Several reports cited -38/4000/60000/180000/40/100/300 — those appear stale (an older worktree?); reconcile before quoting defaults anywhere.
- SpeechTrimSettings (VAD) defaults: reports disagree (Threshold 0.5 agreed; MinSpeech 250 vs 100; MinSilence 100 vs 300; SpeechPad 30 vs 0). SpeechTrimSettings.cs was not located by glob — verify the actual POCO before quoting VAD defaults.
- totalSettings=132 is a deduplicated estimate across seven overlapping zone reports that recount the same POCOs; it counts persisted category=='setting' entries (incl. frozen DSP params and dormant Speech skeleton) and excludes category=='diagnostic'/'command' and read-only ids (RewriteProfile.Id). A line-by-line POCO census would firm the exact figure.
- EnergySegmenter HangoverCurve (X1/Y1/X2/Y2, defaults 0.85/0.10/0.90/0.25) is persisted but has NO UI anywhere (not Settings, not the composed fold, not Playground) — confirm it is intentionally unexposed (cubic-bezier easing) and whether it should ever surface.
- Frozen Preprocessing DSP params (HighPassEnabled/Hz, GateEnabled, Compressor bundle, LimiterEnabled) are persisted but exposed only in Playground/never — confirm they stay frozen and are not migration candidates.
- SpeechSettings (Enabled/Voice/Temperature) are fully persisted to modules/speech/settings.json with NO UI — dormant skeleton. Confirm they stay out of any Settings page until the ONNX backend lands.
- TaskbarCoverSettings.Enabled and MouseWheelSettings.RecordEvents are persisted but live only in the tray menu / Playground respectively — confirm these are deliberately not in the Settings UI.
- TemperatureIncrement (InfoBar warning when =0) and Logprob/NoSpeech sliders (Min/Max set in code-behind for a WinUI trimming bug) carry chrome the plain Slider/Number descriptor does not reproduce — confirm the composer can host that auxiliary chrome before migrating, or accept its loss.
- AudioCorpusContent and LlmSettings.RuleMetric drive the VISIBILITY of sibling panels (not just their own value). The radio-Choice variant alone does not make them composable — they also need cross-sibling visibleWhen, which folds-within-folds and the current flat model may not support. Verify the composer's VisibleWhen reach before counting them as migrate-now.
- StreamingSettings.Strategy enum→bool projection (Monolithic/Streaming): one report shows it composed (WhisperViewModel.Settings.cs Group master), another lists it hand-authored RadioButtons in WhisperPage. Verified composed in the manifest I read — confirm no stale hand-authored duplicate remains in WhisperPage.xaml.

Éléments du présent inventaire qui recoupent ces lacunes :

- Lacune 1 (LevelWindow) — `Deckle.Audio` confirme -55 / -32 / 1.0 à la source, et identifie la source probable des chiffres divergents : un commentaire périmé au-dessus de `AudioLevelMapper.MinDbfs` documentant une calibration -40 / -22 / exposant 2.0 que le code ne tient plus.
- Lacune 2 (EnergySegmenter) — `Deckle.Transcription` confirme -45.0 / 5000 / 500 / 15000 / 120000 / 150 / 250, et note que la section consolidée de juin (lignes 206-212) porte encore les anciens chiffres alors que sa table par module porte les bons.
- Lacune 5 (HangoverCurve) — probablement périmée : le dépôt a depuis un `HangoverCurveCanvas` (éditeur de bézier Win2D dans `Ui/Controls/`). Son câblage dans WhisperPage n'a pas été vérifié.
- Lacune 7 (SpeechSettings) — `Deckle.Speech` confirme les trois valeurs (false / Pierre / 0.6), sans dérive. `Enabled` n'a aujourd'hui aucun consommateur.
- Lacune 8 (MouseWheelSettings.RecordEvents) — `Deckle.Input` confirme que c'est le seul réglage persisté du module.

---

## Limites du balayage

Ce que la passe automatique ne voit pas, confirmé par les quarante-et-une vérifications :

- **Initialiseurs multi-lignes.** Toute valeur écrite sur plusieurs lignes est invisible. Cas confirmés et rattrapés à la main : `EngineSettings.InitialPrompt` (concaténation `+`), `AutocorrectSettings.Apps`, les deux `SystemPrompt` de `Deckle.Llm.Rewrite` (chaînes brutes multi-lignes), `LlmSettings.Profiles` / `AutoRewriteRules` / `AutoRewriteRulesByWords`, les trois `AmbientModePresets` (affectations dans un `switch`).
- **Membres à corps d'expression.** Le regex de ligne lit `=> _champ` comme un initialiseur. C'est la source la plus abondante de faux positifs — une soixantaine sur l'ensemble, reconnaissables à une valeur commençant par `>`. Tous retirés.
- **Littéraux inline et constantes calculées.** Rien qui ne soit pas une déclaration nommée n'est vu : `SettingsSaveDebounceMs = 300` et l'attente de mutex de 2 s de `JsonSettingsStore`, qui gouvernent pourtant toute écriture de `settings.json`, n'apparaissent que parce qu'un agent les a ajoutés.
- **Réglages sans initialiseur.** Un réglage dont la source de vérité est l'OS échappe par construction : `AutostartEnabled` n'a pas d'initialiseur, n'est dans aucun `settings.json`, et vit dans `HKCU\Run` + la tâche planifiée. Toute méthode indexée sur les initialiseurs de POCO ratera cette classe entière.
- **Heuristique `kind` peu fiable.** Le suffixe du nom de type décide seul : n'importe quel type finissant par `Settings`/`Options`/`Source` est marqué `persisted-setting`. Faux positifs corrigés en masse (18 dans `Deckle.Autocorrect`, plus les `*SettingsService.Current`/`.Path` de six modules). L'inverse existe aussi : des DTO de résultats de recherche dans `Deckle.Settings` comptés comme persistés.
- **XAML non balayé.** Les bornes des sliders (`Minimum`, `Maximum`, `StepFrequency`) définissent l'amplitude admissible de chaque paramètre et vivent hors de cette liste. Si l'arbitrage a besoin des plages, c'est une passe séparée.
- **Champs préfixés `_` exclus par conception.** Les tables lexicales figées de `GateLexicon.cs` (`_functionWords`, `_fillers`, `_fillerPhrases`, `_insertablePunctuation`) sont de vraies données de tuning, hors périmètre.
- **Encodage.** Les valeurs non-ASCII reviennent en mojibake du balayage : les 42 libellés de `Deckle.Travel/Terms/terms.fr.json` (`"D�but"`), et le `'…'` des jeux de terminateurs de phrase de `Deckle.Autocorrect.Lab`/`.Mlm`. Relues à la source et corrigées ici, mais toute reprise directe du balayage les réintroduit.
- **Blocs JSON imbriqués aplatis.** Le balayage ne descend pas dans un objet imbriqué : les 14 feuilles `types.<clé>.name` / `.plural` de `terms.fr.json` ont disparu, dont `transfer`, `lodging` et `activity` qui n'ont pas de jumeau dans le bloc `properties`. Rattrapées à la main.
- **Initialiseurs de collection et propriétés statiques calculées.** Même angle mort que les initialiseurs multi-lignes : `BackendSupervisor.RestartBackoff` (l'échelle de redémarrage), `McpServer.SupportedProtocols`, `McpHttpHost.MaxBodyBytes` sont absents alors que leurs voisins scalaires sont pris.
- **Défauts écrits en corps d'expression.** Le filtre qui retire (à raison) les accesseurs `=> _champ` retire aussi les vrais défauts écrits sous la même forme : `AutostartService.DefaultEnabled => false`, réglage réel de l'inventaire de juin, n'apparaît que parce qu'un agent l'a rajouté. Un `=> false` / `=> true` littéral est une valeur, pas un accesseur.
- **Indirection.** Quand un membre transfère vers une constante, le balayage prend le passe-plat et rate la constante : `SetupContext.InstallDirectory`/`DataDirectory` pris, `InstallPaths.DefaultInstallDir`/`DefaultDataDir` — où vivent les vrais défauts — ratés. Idem `BackendInstallation.ExecutablePath` devant `InstallDirectory`.
- **Collections-enveloppes.** Le balayage prend l'enveloppe et rate l'élément décrit : `SetupNotifications.All` pris, le descripteur `UpdateAvailable` qui porte la surface réelle (sévérité, canal, actions) raté.
- **`owner` mal attribué.** Dans un fichier d'interop, les constantes qui suivent une déclaration de struct sont rattachées à ce struct et non à la classe statique englobante : tout `TrayMenuNativeMethods.cs` sous `SIZE`, tout `TaskbarCoverNativeMethods.cs` sous `MSG`/`APPBARDATA`, tout `ScreenCaptureInterop.cs` sous `RECT`. Sans effet ici (ces entrées sont retirées), mais le champ n'est pas fiable.
- **Constantes locales de méthode annoncées `field`.** Récurrent et vérifiable au nom en minuscule : `SurfaceProfiler.minTimedSentences`, `LiveTagResolver.limit`, `McpHttpHost.scheme`, `HomePropertyWriter.limit`, `HdrState.SdrReferenceNits`, `LogWindow.MaxEntries`. Les exposer suppose d'abord de les hisser au niveau du type.
- **Doublons de déclaration.** Plusieurs valeurs existent à deux endroits sans constante partagée : les 59 champs de `ConicArcStrokeConfig` mirroités dans `TuningModel`, `AudioLevelMapper.MinDbfs/MaxDbfs/DbfsCurveExponent` mirroir de `LevelWindowSettings`, `SeparatorRunCap` déclaré deux fois, la cadence 50 ms / 800 échantillons déclarée dans deux fichiers. Tout total global doit être dédupliqué par nom.
