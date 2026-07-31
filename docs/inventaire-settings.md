# Inventaire des réglages Deckle

> Artefact de travail (workflow composer-inventory, 13 agents). Chiffres par défaut à recouper à la source — voir Lacunes en bas.

## Deckle.App + Deckle.Settings (shell + General)

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| Theme | setting | migrated | Choice<string> (System/Light/Dark) | new AppearanceSettings().Theme → "System" |  | D:\projects\ai\deckle\src\Deckle.Settings\Persistence\AppSettings.cs:46 |
| Overlay Enabled | setting | migrated | Group (master toggle with children) | new OverlaySettings().Enabled → true |  | D:\projects\ai\deckle\src\Deckle.Settings\Persistence\AppSettings.cs:53 |
| Overlay Fade on Proximity | setting | migrated | Toggle (child of Overlay group) | new OverlaySettings().FadeOnProximity → true |  | D:\projects\ai\deckle\src\Deckle.Settings\Persistence\AppSettings.cs:54 |
| Overlay Animations | setting | migrated | Toggle (child of Overlay group) | new OverlaySettings().Animations → true | • | D:\projects\ai\deckle\src\Deckle.Settings\Persistence\AppSettings.cs:63 |
| Overlay Position | setting | migrated | Choice<string> (TopCenter/BottomCenter; child of Overlay group) | new OverlaySettings().Position → "BottomCenter" |  | D:\projects\ai\deckle\src\Deckle.Settings\Persistence\AppSettings.cs:55 |
| Auto-Paste Enabled | setting | migrated | Toggle | new PasteSettings().AutoPasteEnabled → false |  | D:\projects\ai\deckle\src\Deckle.Settings\Persistence\AppSettings.cs:40 |
| Start with Windows | setting | migrated | Toggle | AutostartService.DefaultEnabled → false (registry-backed) |  | D:\projects\ai\deckle\src\Deckle.Settings\ViewModels\GeneralViewModel.cs:106 |
| Backup Directory | setting | hand-authored | Path (FolderPickerCard, hand-authored in XAML) | new PathsSettings().BackupDirectory → "" (empty = AppPaths) |  | D:\projects\ai\deckle\src\Deckle.Settings\Persistence\AppSettings.cs:79 |
| Audio Input Device | setting | hand-authored | Choice<int> (waveIn enumeration, ComboBox in code-behind) | new CaptureSettings().AudioInputDeviceId → -1 |  | D:\projects\ai\deckle\src\Deckle.Audio\CaptureSettings.cs:15 |
| Preprocessing Enabled | setting | migrated | Toggle | new PreprocessingSettings().Enabled → false |  | D:\projects\ai\deckle\src\Deckle.Audio\Preprocessing\PreprocessingSettings.cs:27 |
| Voice Level Min dBFS | setting | migrated | Slider (double, -90 to -10, step 1, unit dBFS; child of group) | new LevelWindowSettings().MinDbfs → -55f |  | D:\projects\ai\deckle\src\Deckle.Audio\CaptureSettings.cs:58 |
| Voice Level Max dBFS | setting | migrated | Slider (double, -60 to -10, step 1, unit dBFS; child of group) | new LevelWindowSettings().MaxDbfs → -32f |  | D:\projects\ai\deckle\src\Deckle.Audio\CaptureSettings.cs:59 |
| Voice Level Curve Exponent | setting | migrated | Slider (double, 0.3 to 3.0, step 0.05; child of group) | new LevelWindowSettings().DbfsCurveExponent → 1.0f | • | D:\projects\ai\deckle\src\Deckle.Audio\CaptureSettings.cs:60 |
| Voice Level Auto-Calibration | setting | migrated | Group (master, inverted semantics) | new LevelWindowSettings().AutoCalibrationEnabled → false |  | D:\projects\ai\deckle\src\Deckle.Audio\CaptureSettings.cs:61 |
| Log Ambient Capture Activity | setting | migrated | Toggle | LoggingSettings default → false |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Logging\LoggingSettings.cs |
| Log Streaming Transcription Activity | setting | migrated | Toggle | LoggingSettings default → false |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Logging\LoggingSettings.cs |
| Log Autocorrect Activity | setting | migrated | Toggle | LoggingSettings default → false |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Logging\LoggingSettings.cs |
| Log Windowing Activity | setting | migrated | Toggle | LoggingSettings default → false |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Logging\LoggingSettings.cs |
| Application Log to Disk | setting | migrated | Toggle (with confirmOnEnable gate) | new TelemetrySettings().ApplicationLogToDisk → false |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Telemetry\TelemetrySettings.cs:30 |
| Microphone Telemetry | setting | migrated | Toggle (with confirmOnEnable gate) | new TelemetrySettings().MicrophoneTelemetry → false |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Telemetry\TelemetrySettings.cs:14 |
| Latency Telemetry | setting | migrated | Toggle | new TelemetrySettings().LatencyEnabled → false | • | D:\projects\ai\deckle\src\Deckle.Diagnostics.Telemetry\TelemetrySettings.cs:13 |
| Corpus Enabled | setting | hand-authored | Toggle (with consent gate in code-behind) | new TelemetrySettings().CorpusEnabled → false |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Telemetry\TelemetrySettings.cs:15 |
| Record Audio Corpus | setting | hand-authored | Toggle (nested, with consent gate) | new TelemetrySettings().RecordAudioCorpus → false |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Telemetry\TelemetrySettings.cs:16 |
| Audio Corpus Content | setting | hand-authored | Radio (RadioButtons, 2 options) | new TelemetrySettings().AudioCorpusContent → AudioCorpusContent.MatchTranscription |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Telemetry\TelemetrySettings.cs:25 |
| Autocorrect Decisions | setting | hand-authored | Toggle (with consent gate in code-behind) | new TelemetrySettings().AutocorrectDecisions → false |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Telemetry\TelemetrySettings.cs:38 |
| Autocorrect Text | setting | hand-authored | Toggle (nested, with consent gate) | new TelemetrySettings().AutocorrectText → false |  | D:\projects\ai\deckle\src\Deckle.Diagnostics.Telemetry\TelemetrySettings.cs:46 |
| Telemetry Storage Directory | setting | hand-authored | Path (FolderPickerCard, hand-authored in XAML) | new TelemetrySettings().StorageDirectory → "" (empty = AppPaths.TelemetryDirectory) | • | D:\projects\ai\deckle\src\Deckle.Diagnostics.Telemetry\TelemetrySettings.cs:31 |

**Gestes destructifs :** Reset Appearance Settings _(adhoc-dialog)_ · Reset Behaviour Settings _(adhoc-dialog)_ · Reset Startup Settings _(adhoc-dialog)_ · Restore Backup _(service)_ · Reset Recording Settings _(adhoc-dialog)_

## Deckle.Settings (Recording + Capture Audio)

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| Preprocessing.Enabled | setting | migrated | Toggle | new PreprocessingSettings().Enabled (false) |  |  |
| LevelWindow.AutoCalibrationEnabled (inverted: SetWindowManually) | setting | migrated | Group | !new LevelWindowSettings().AutoCalibrationEnabled (true = manual by default, auto off) |  |  |
| LevelWindow.MinDbfs | setting | migrated | Slider (child of Group) | new LevelWindowSettings().MinDbfs (-55f) |  |  |
| LevelWindow.MaxDbfs | setting | migrated | Slider (child of Group) | new LevelWindowSettings().MaxDbfs (-32f) |  |  |
| LevelWindow.DbfsCurveExponent | setting | migrated | Slider (child of Group) | new LevelWindowSettings().DbfsCurveExponent (1.0f) |  |  |
| AudioInputDeviceId | setting | hand-authored | ComboBox | -1 (WAVE_MAPPER system default) |  |  |
| MaxRecordingDurationSeconds | setting | n/a | n/a | new CaptureSettings().MaxRecordingDurationSeconds (20 * 60 = 1200) |  |  |

**Gestes destructifs :** Reset Recording (section link) _(none)_ · Microphone device selection (ComboBox change) _(none)_ · Preprocessing toggle on/off _(none)_ · Level window manual/auto toggle _(none)_

## Deckle.Settings — Diagnostics Page

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| LogAmbientCaptureActivity | setting | migrated | LoggingSettings.LogAmbientCaptureActivity | LoggingSettings POCO initializer (false) |  | DiagnosticsViewModel.Settings.cs:25–28; DiagnosticsPage composing LoggingHost |
| LogStreamingTranscriptionActivity | setting | migrated | LoggingSettings.LogStreamingTranscriptionActivity | LoggingSettings POCO initializer (false) |  | DiagnosticsViewModel.Settings.cs:29–32; DiagnosticsPage composing LoggingHost |
| LogAutocorrectActivity | setting | migrated | LoggingSettings.LogAutocorrectActivity | LoggingSettings POCO initializer (false) |  | DiagnosticsViewModel.Settings.cs:33–36; DiagnosticsPage composing LoggingHost |
| LogWindowingActivity | setting | migrated | LoggingSettings.LogWindowingActivity | LoggingSettings POCO initializer (false) |  | DiagnosticsViewModel.Settings.cs:37–40; DiagnosticsPage composing LoggingHost |
| LogWindowVisibilityMode | setting | migrated | LoggingSettings.LogWindowVisibilityMode | LoggingSettings POCO initializer (LogWindowVisibilityMode.All) |  | LoggingSettings.cs:54; viewer-only lens, does not gate disk app.jsonl |
| ApplicationLogToDisk | setting | migrated | TelemetrySettings.ApplicationLogToDisk | TelemetrySettings POCO initializer (false) |  | DiagnosticsViewModel.Settings.cs:59–63; app.jsonl text source |
| MicrophoneTelemetry | setting | migrated | TelemetrySettings.MicrophoneTelemetry | TelemetrySettings POCO initializer (false) |  | DiagnosticsViewModel.Settings.cs:64–68; microphone.jsonl RMS rows per Recording Stop |
| TelemetryLatencyEnabled | setting | migrated | TelemetrySettings.LatencyEnabled | TelemetrySettings POCO initializer (false) |  | DiagnosticsViewModel.Settings.cs:69–72; latency.jsonl step timings only |
| TelemetryCorpusEnabled | setting | hand-authored | TelemetrySettings.CorpusEnabled | TelemetrySettings POCO initializer (false) |  | DiagnosticsPage.xaml:113–115; master toggle for corpus expansion |
| RecordAudioCorpus | setting | hand-authored | TelemetrySettings.RecordAudioCorpus | TelemetrySettings POCO initializer (false) |  | DiagnosticsPage.xaml:120–122; child of TelemetryCorpusEnabled |
| AudioCorpusContent | setting | hand-authored | TelemetrySettings.AudioCorpusContent (enum) | TelemetrySettings POCO initializer (AudioCorpusContent.MatchTranscription = index 0) |  | DiagnosticsPage.xaml:128–131; RadioButtons.SelectedIndex TwoWay to AudioCorpusContentIndex |
| AutocorrectDecisions | setting | hand-authored | TelemetrySettings.AutocorrectDecisions | TelemetrySettings POCO initializer (false) |  | DiagnosticsPage.xaml:143–145; master toggle for autocorrect expansion |
| AutocorrectText | setting | hand-authored | TelemetrySettings.AutocorrectText | TelemetrySettings POCO initializer (false) |  | DiagnosticsPage.xaml:149–151; child of AutocorrectDecisions |
| TelemetryStorageDirectory | setting | hand-authored | TelemetrySettings.StorageDirectory | TelemetrySettings POCO initializer ("", resolves to AppPaths.TelemetryDirectory when empty) |  | DiagnosticsPage.xaml:158–159; TelemetryFolderPicker Path binding TwoWay |

**Gestes destructifs :** Reset Logging _(none)_ · Reset Telemetry _(none)_

## Deckle.Transcription / WhisperPage

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| ModelsDirectory | setting | hand-authored | Paths.ModelsDirectory | TranscriptionSettings.ModelsDirectory = "" |  | WhisperPage.xaml:195–205, WhisperViewModel.cs:27, TranscriptionSettings.cs:16 |
| Language | setting | hand-authored | Transcription.Language | EngineSettings.Language = "fr" |  | WhisperPage.xaml:104–130, WhisperViewModel.cs:45, TranscriptionSettings.cs:84 |
| InitialPrompt | setting | hand-authored | Transcription.InitialPrompt | EngineSettings.InitialPrompt = "Bon. Je suis..." (hardcoded French prompt) |  | WhisperPage.xaml:132–152, WhisperViewModel.cs:48, TranscriptionSettings.cs:85–91 |
| Model | setting | hand-authored | Transcription.Model | EngineSettings.Model = "ggml-base.bin" |  | WhisperPage.xaml:175–207, WhisperViewModel.cs:39, TranscriptionSettings.cs:82, WhisperPage.xaml.cs:414–418 |
| UseGpu | setting | hand-authored | Transcription.UseGpu | EngineSettings.UseGpu = true |  | WhisperPage.xaml:154–166, WhisperViewModel.cs:42, TranscriptionSettings.cs:83 |
| VadEnabled | setting | migrated | Streaming.SpeechTrim.Enabled | SpeechTrimSettings.Enabled = true |  | WhisperViewModel.Settings.cs:43, WhisperViewModel.cs:85, TranscriptionSettings.cs:58, EnergySegmenterSettings.cs:56 |
| VadThreshold | setting | migrated | Streaming.SpeechTrim.Threshold | SpeechTrimSettings.Threshold = 0.5f | • | WhisperViewModel.Settings.cs:47–51, WhisperViewModel.cs:88, TranscriptionSettings.cs:63 |
| VadMinSpeechDurationMs | setting | migrated | Streaming.SpeechTrim.MinSpeechDurationMs | SpeechTrimSettings.MinSpeechDurationMs = 250 | • | WhisperViewModel.Settings.cs:52–56, WhisperViewModel.cs:91, TranscriptionSettings.cs:66 |
| VadMinSilenceDurationMs | setting | migrated | Streaming.SpeechTrim.MinSilenceDurationMs | SpeechTrimSettings.MinSilenceDurationMs = 100 | • | WhisperViewModel.Settings.cs:57–61, WhisperViewModel.cs:94, TranscriptionSettings.cs:70 |
| VadSpeechPadMs | setting | migrated | Streaming.SpeechTrim.SpeechPadMs | SpeechTrimSettings.SpeechPadMs = 30 | • | WhisperViewModel.Settings.cs:62–66, WhisperViewModel.cs:97, TranscriptionSettings.cs:74 |
| Temperature | setting | hand-authored | Decoding.Temperature | DecodingSettings.Temperature = 0.0 |  | WhisperPage.xaml:337–357, WhisperViewModel.cs:137, TranscriptionSettings.cs:117 |
| TemperatureIncrement | setting | hand-authored | Decoding.TemperatureIncrement | DecodingSettings.TemperatureIncrement = 0.2 |  | WhisperPage.xaml:359–379, WhisperViewModel.cs:140, TranscriptionSettings.cs:118 |
| EntropyThreshold | setting | hand-authored | Confidence.EntropyThreshold | ConfidenceSettings.EntropyThreshold = 2.4 | • | WhisperPage.xaml:399–418, WhisperViewModel.cs:159, TranscriptionSettings.cs:103 |
| LogprobThreshold | setting | hand-authored | Confidence.LogprobThreshold | ConfidenceSettings.LogprobThreshold = -1.0 | • | WhisperPage.xaml:420–438, WhisperViewModel.cs:162, TranscriptionSettings.cs:104, WhisperPage.xaml.cs:71–72 |
| NoSpeechThreshold | setting | hand-authored | Confidence.NoSpeechThreshold | ConfidenceSettings.NoSpeechThreshold = 0.6 | • | WhisperPage.xaml:440–459, WhisperViewModel.cs:165, TranscriptionSettings.cs:105, WhisperPage.xaml.cs:73 |
| SuppressNonSpeechTokens | setting | hand-authored | OutputFilters.SuppressNonSpeechTokens | OutputFilterSettings.SuppressNonSpeechTokens = true |  | WhisperPage.xaml:235–247, WhisperViewModel.cs:191, TranscriptionSettings.cs:110 |
| SuppressBlank | setting | hand-authored | OutputFilters.SuppressBlank | OutputFilterSettings.SuppressBlank = true |  | WhisperPage.xaml:249–261, WhisperViewModel.cs:194, TranscriptionSettings.cs:111 |
| SuppressRegex | setting | hand-authored | OutputFilters.SuppressRegex | OutputFilterSettings.SuppressRegex = "" | • | WhisperPage.xaml:263–281, WhisperViewModel.cs:197, TranscriptionSettings.cs:112 |
| UseContext | setting | hand-authored | Context.UseContext | ContextSettings.UseContext = true |  | WhisperPage.xaml:287–299, WhisperViewModel.cs:223, TranscriptionSettings.cs:132 |
| MaxTokens | setting | hand-authored | Context.MaxTokens | ContextSettings.MaxTokens = -1 |  | WhisperPage.xaml:301–321, WhisperViewModel.cs:226, TranscriptionSettings.cs:133, WhisperPage.xaml.cs:307–314 |
| StreamingEnabled | setting | migrated | Streaming.Strategy (projected: bool from PipelineStrategyKind) | StreamingSettings.Strategy = PipelineStrategyKind.Monolithic (projects to false) |  | WhisperViewModel.Settings.cs:86, WhisperViewModel.cs:252, TranscriptionSettings.cs:39 |
| SegThresholdDbfs | setting | migrated | Streaming.Segmenter.ThresholdDbfs | EnergySegmenterSettings.ThresholdDbfs = -45.0 | • | WhisperViewModel.Settings.cs:90–94, WhisperViewModel.cs:255, EnergySegmenterSettings.cs:48 |
| SegHangoverMaxMs | setting | migrated | Streaming.Segmenter.HangoverMaxMs | EnergySegmenterSettings.HangoverMaxMs = 5000 | • | WhisperViewModel.Settings.cs:95–99, WhisperViewModel.cs:258, EnergySegmenterSettings.cs:49 |
| SegHangoverMinMs | setting | migrated | Streaming.Segmenter.HangoverMinMs | EnergySegmenterSettings.HangoverMinMs = 500 | • | WhisperViewModel.Settings.cs:100–104, WhisperViewModel.cs:261, EnergySegmenterSettings.cs:50 |
| SegHangoverRampStartMs | setting | migrated | Streaming.Segmenter.HangoverRampStartMs | EnergySegmenterSettings.HangoverRampStartMs = 15000 | • | WhisperViewModel.Settings.cs:105–109, WhisperViewModel.cs:264, EnergySegmenterSettings.cs:51 |
| SegHangoverRampEndMs | setting | migrated | Streaming.Segmenter.HangoverRampEndMs | EnergySegmenterSettings.HangoverRampEndMs = 120000 | • | WhisperViewModel.Settings.cs:110–114, WhisperViewModel.cs:267, EnergySegmenterSettings.cs:52 |
| SegMarginMs | setting | migrated | Streaming.Segmenter.MarginMs | EnergySegmenterSettings.MarginMs = 150 | • | WhisperViewModel.Settings.cs:115–119, WhisperViewModel.cs:270, EnergySegmenterSettings.cs:57 |
| SegMinUtteranceMs | setting | migrated | Streaming.Segmenter.MinUtteranceMs | EnergySegmenterSettings.MinUtteranceMs = 250 | • | WhisperViewModel.Settings.cs:120–124, WhisperViewModel.cs:273, EnergySegmenterSettings.cs:58 |
| CarryInitialPrompt | setting | hand-authored | Engine.CarryInitialPrompt | EngineSettings.CarryInitialPrompt = true | • | TranscriptionSettings.cs:95 |
| UseBeamSearch | setting | hand-authored | Decoding.UseBeamSearch | DecodingSettings.UseBeamSearch = true | • | TranscriptionSettings.cs:123 |
| BeamSize | setting | hand-authored | Decoding.BeamSize | DecodingSettings.BeamSize = 5 | • | TranscriptionSettings.cs:124 |

**Gestes destructifs :** Individual setting reset (per-card reset button, hover-reveal) _(none)_ · Reset all Whisper settings _(adhoc-dialog)_ · Model / UseGpu change (requires app restart) _(adhoc-dialog)_

## Deckle.Llm.Rewrite

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| Enabled | setting | hand-authored | LlmSettings.Enabled (bool) | LlmSettings ctor: true |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:57 |
| Ollama endpoint | setting | hand-authored | LlmSettings.OllamaEndpoint (string) | LlmSettings ctor: 'http://localhost:11434/api/generate' |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:58 |
| Primary rewrite profile | setting | hand-authored | LlmSettings.PrimaryRewriteProfileName (string?) + PrimaryRewriteProfileId (string?) | LlmSettings ctor: null (not set by default) |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:65,74 |
| Secondary rewrite profile | setting | hand-authored | LlmSettings.SecondaryRewriteProfileName (string?) + SecondaryRewriteProfileId (string?) | LlmSettings ctor: null (not set by default) |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:69,75 |
| Legacy auto-rewrite rule metric | legacy setting | retained for deserialization | LlmSettings.RuleMetric (string) | No runtime or UI consumer |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs |
| Legacy auto-rewrite rules by duration | legacy setting | retained for deserialization | LlmSettings.AutoRewriteRules (List<AutoRewriteRule>) | No runtime or UI consumer |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs |
| Legacy auto-rewrite rules by word count | legacy setting | retained for deserialization | LlmSettings.AutoRewriteRulesByWords (List<AutoRewriteRuleByWords>) | No runtime or UI consumer |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs |
| Rewrite profile: Name | setting | hand-authored | RewriteProfile.Name (string) | LlmSettings ctor: 'Lissage', 'Affinage', 'Arrangement' (three defaults) |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:19,88-242 |
| Rewrite profile: Model | setting | hand-authored | RewriteProfile.Model (string) | LlmSettings ctor: '' (empty, user must choose) |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:20,88-242 |
| Rewrite profile: System prompt | setting | hand-authored | RewriteProfile.SystemPrompt (string) | LlmSettings ctor: Three shipped prompts for Lissage/Affinage/Arrangement (tuned via autoresearch on Ministral 14B Q4) |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:21,100-240 |
| Rewrite profile: Temperature | setting | hand-authored | RewriteProfile.Temperature (double?) | LlmSettings ctor: 0.30 for all three defaults (Lissage/Affinage/Arrangement) |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:24,94,141,195 |
| Rewrite profile: Context size (NumCtxK) | setting | hand-authored | RewriteProfile.NumCtxK (int?), mapped to ProfileViewModel.CtxIndex (0..8 index into CtxKSteps array [1,2,4,8,16,32,64,128,256]) | LlmSettings ctor: Lissage 8K, Affinage 16K, Arrangement 16K |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:25,95,142,196; ProfileViewModel.cs:100,119 |
| Rewrite profile: Top P (advanced) | setting | hand-authored | RewriteProfile.TopP (double?) | LlmSettings ctor: null (not sent to Ollama, uses Modelfile default) | • | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:26 |
| Rewrite profile: Repeat penalty (advanced) | setting | hand-authored | RewriteProfile.RepeatPenalty (double?) | LlmSettings ctor: null (not sent to Ollama, uses Modelfile default) | • | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:27 |
| Rewrite profile: Stable ID | diagnostic | n/a | RewriteProfile.Id (string, 12-char Guid suffix N format) | Generated on new profile or legacy load via LlmSettingsMigrations.RepairProfileReferences |  | D:\projects\ai\deckle\src\Deckle.Llm.Rewrite\LlmSettings.cs:17; LlmSettingsMigrations.cs:48-52 |

**Gestes destructifs :** Reset General section _(adhoc-dialog)_ · Reset Shortcuts section _(adhoc-dialog)_ · Reset Profiles section _(adhoc-dialog)_ · Reset all LLM settings _(adhoc-dialog)_ · Delete profile _(adhoc-dialog)_ · Delete model from Ollama _(adhoc-dialog)_

## Deckle.Autocorrect + Deckle.Lighting.Ambient

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| Enabled | setting | hand-authored | Toggle | AutocorrectSettings.cs:11 (public bool Enabled = true) |  | AutocorrectPage.xaml:48 (ToggleSwitch IsOn binding) |
| Apps (per-app decision map) | setting | hand-authored | bespoke | AutocorrectSettings.cs:16-17 (Dictionary with ['notepad'] = true) |  | AutocorrectPage.xaml:71-101 (ItemsControl), AutocorrectViewModel.cs:28,45-50, AutocorrectAppRow.cs |
| Ambient.Enabled | setting | hand-authored | Toggle | AmbientSettings.cs:23 (public bool Enabled = false) |  | AmbientPage.xaml.cs:95-96 (EnabledToggle_Toggled), AmbientPage.xaml:93-97 |
| Ambient.HueBridgeIp | setting | hand-authored | Text | AmbientSettings.cs:39 (public string? HueBridgeIp = null) |  | AmbientPage.xaml.cs:159-161 (HueBridgeIpTextBox), AmbientPage.xaml:157-161 |
| Ambient.HueBridgeId | setting | hand-authored | bespoke | AmbientSettings.cs:42 (public string? HueBridgeId = null) |  | AmbientPage.xaml.cs: (set after pairing via HuePairingService.Instance.PairedBridge) |
| Ambient.HueUsername | setting | hand-authored | bespoke | AmbientSettings.cs:48 (public string? HueUsername = null) |  | AmbientPage.xaml.cs: (set after pairing via HuePairingService.Instance.PairedBridge.Credentials) |
| Ambient.HueLastGroupId | setting | hand-authored | bespoke | AmbientSettings.cs:53 (public string? HueLastGroupId = null) |  | AmbientPage.xaml.cs:413, Playground AmbientPage.Hue.cs:223 |
| Ambient.Mode | setting | hand-authored | Choice (enum) | AmbientSettings.cs:86 (public AmbientMode Mode = AmbientMode.Game) |  | AmbientPage.xaml:103-113 (ModeCombo), AmbientPage.xaml.cs:175-189 |
| Ambient.UseMultiLight | setting | hand-authored | Toggle | AmbientSettings.cs:109 (public bool UseMultiLight = false) |  | Playground AmbientPage.xaml:173-178 (PipelineModeRadios), Playground AmbientViewModel.cs:205-213 |
| Ambient.LightZones | setting | hand-authored | bespoke | AmbientSettings.cs:118 (public Dictionary<string, LightZone> LightZones = new()) |  | AmbientSettings.cs:118 (Dictionary<string, LightZone>), Playground per-light zone combo (runtime UI) |
| Ambient.LightBrightness | setting | hand-authored | bespoke | AmbientSettings.cs:129 (public Dictionary<string, double> LightBrightness = new()) |  | AmbientSettings.cs:129 (Dictionary<string, double>), Playground per-light brightness sliders (runtime UI) |
| Ambient.SelectedMonitorDeviceName | setting | hand-authored | Text | AmbientSettings.cs:69 (public string? SelectedMonitorDeviceName = null) |  | AmbientSettings.cs:69 (scaffolding for J9, not yet UI) |
| Ambient.BorderMode | setting | hand-authored | Choice (enum) | AmbientSettings.cs:140 (public BorderThicknessMode BorderMode = BorderThicknessMode.Share) |  | Playground AmbientPage.xaml: (BorderMode picker, scaffold only, commented) |
| Ambient.BorderDepth | setting | hand-authored | Slider | AmbientSettings.cs:152 (public double BorderDepth = 0.33) |  | Playground: code-behind mentions BorderDepth, tuning slider range [0.05, 0.5] |
| Ambient.BorderCells | setting | hand-authored | Slider | AmbientSettings.cs:168 (public int BorderCells = 8) |  | Playground: code-behind, tuning slider range [4, 24] stepping by 2 |
| Ambient.ExposureEv | setting | hand-authored | Slider | AmbientSettings.cs:205 (public double ExposureEv = 0.0) |  | Playground AmbientPage.xaml:411-430 (PlaygroundExposureSlider), Playground AmbientViewModel.cs:108-115 |
| Ambient.SaturationBoost | setting | hand-authored | Slider | AmbientSettings.cs:211 (public double SaturationBoost = 1.0) |  | Playground AmbientPage.xaml:440-459 (PlaygroundSaturationSlider), Playground AmbientViewModel.cs:117-124 |
| Ambient.MinBrightness | setting | hand-authored | Slider | AmbientSettings.cs:221 (public int MinBrightness = 180) |  | Playground AmbientPage.xaml:469-488 (PlaygroundMinBrightnessSlider), Playground AmbientViewModel.cs:126-133 |
| Ambient.BrightnessCurveType | setting | hand-authored | Choice (enum) | AmbientSettings.cs:229 (public BrightnessCurveType BrightnessCurveType = BrightnessCurveType.Gamma) |  | Playground AmbientPage.xaml:310-317 (PlaygroundBrightnessCurveCombo), Playground AmbientViewModel.cs:135-142 |
| Ambient.BrightnessCurveParam | setting | hand-authored | Slider | AmbientSettings.cs:242 (public double BrightnessCurveParam = 1.8) |  | Playground AmbientPage.xaml:324-337 (PlaygroundGammaSlider), Playground AmbientViewModel.cs:144-151 |
| Ambient.BrightnessCurveSCurveSteepness | setting | hand-authored | Slider | AmbientSettings.cs:260 (public double BrightnessCurveSCurveSteepness = 2.0) |  | Playground AmbientViewModel.cs:153-160 (setter reads from settings) |
| Ambient.ChangeThreshold | setting | hand-authored | Slider | AmbientSettings.cs:269 (public int ChangeThreshold = 6) |  | Playground AmbientPage.xaml:382-401 (PlaygroundChangeThresholdSlider), Playground AmbientViewModel.cs:162-169 |
| Ambient.SmoothingAlpha | setting | hand-authored | Slider | AmbientSettings.cs:281 (public double SmoothingAlpha = 0.30) |  | Playground AmbientPage.xaml:354-372 (PlaygroundSmoothingSlider), Playground AmbientViewModel.cs:171-178 |

**Gestes destructifs :** Autocorrect: RemoveDecision (per-app Forget button) _(none)_ · Ambient: Forget Hue bridge pairing _(adhoc-dialog)_ · Playground: Reset to defaults (all HDR + zone sampling + mode) _(none)_ · Playground: Reset HDR section _(none)_ · Playground: Reset zone-sampling section _(none)_

## Deckle Settings Inventory

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| Theme | setting | migrated | AppearanceSettings.Theme | AppSettings.Appearance.Theme initializer ("System") |  |  |
| Overlay Enabled | setting | migrated | OverlaySettings.Enabled | OverlaySettings.Enabled initializer (true) |  |  |
| Overlay Fade on Proximity | setting | migrated | OverlaySettings.FadeOnProximity | OverlaySettings.FadeOnProximity initializer (true) |  |  |
| Overlay Animations | setting | migrated | OverlaySettings.Animations | OverlaySettings.Animations initializer (true) |  |  |
| Overlay Position | setting | migrated | OverlaySettings.Position | OverlaySettings.Position initializer ("BottomCenter"), normalized in Load() |  |  |
| Auto-Paste Enabled | setting | migrated | PasteSettings.AutoPasteEnabled | PasteSettings.AutoPasteEnabled initializer (false) |  |  |
| Autostart Enabled | setting | hand-authored | AutostartService registry state (HKCU) | AutostartService.DefaultEnabled (false) |  |  |
| Backup Directory | setting | hand-authored | PathsSettings.BackupDirectory | PathsSettings.BackupDirectory initializer ("") |  |  |
| Preprocessing Enabled | setting | migrated | PreprocessingSettings.Enabled | PreprocessingSettings.Enabled initializer (false) |  |  |
| Level Window Manual (inverted auto-calibration) | setting | migrated | LevelWindowSettings.AutoCalibrationEnabled (inverted in VM: !AutoCalibration) | LevelWindowSettings.AutoCalibrationEnabled initializer (false) |  |  |
| Voice Level Floor (Min dBFS) | setting | migrated | LevelWindowSettings.MinDbfs | LevelWindowSettings.MinDbfs initializer (-55.0) |  |  |
| Voice Level Ceiling (Max dBFS) | setting | migrated | LevelWindowSettings.MaxDbfs | LevelWindowSettings.MaxDbfs initializer (-32.0) |  |  |
| Voice Level Curve Exponent | setting | migrated | LevelWindowSettings.DbfsCurveExponent | LevelWindowSettings.DbfsCurveExponent initializer (1.0) |  |  |
| Microphone Input Device | setting | hand-authored | CaptureSettings.AudioInputDeviceId | CaptureSettings.AudioInputDeviceId initializer (-1, WAVE_MAPPER) |  |  |
| Max Recording Duration | setting | hand-authored | CaptureSettings.MaxRecordingDurationSeconds | CaptureSettings.MaxRecordingDurationSeconds initializer (1200, 20 min) | • |  |
| High-Pass Filter Enabled | setting | hand-authored | PreprocessingSettings.HighPassEnabled | PreprocessingSettings.HighPassEnabled initializer (true) | • |  |
| High-Pass Frequency | setting | hand-authored | PreprocessingSettings.HighPassHz | PreprocessingSettings.HighPassHz initializer (90f Hz) | • |  |
| Noise Gate Enabled | setting | hand-authored | PreprocessingSettings.GateEnabled | PreprocessingSettings.GateEnabled initializer (false) | • |  |
| Compressor & Makeup Gain Settings | setting | hand-authored | PreprocessingSettings.CompressorEnabled, CompThresholdDbfs, CompRatio, CompKneeDb, CompAttackMs, CompReleaseMs, TargetRmsDbfs, MaxMakeupGainDb | PreprocessingSettings initializers (see .cs) | • |  |
| Limiter Enabled | setting | hand-authored | PreprocessingSettings.LimiterEnabled | PreprocessingSettings.LimiterEnabled initializer (true) | • |  |
| Whisper Model | setting | hand-authored | EngineSettings.Model | EngineSettings.Model initializer ("ggml-base.bin") |  |  |
| Use GPU | setting | hand-authored | EngineSettings.UseGpu | EngineSettings.UseGpu initializer (true) |  |  |
| Language | setting | hand-authored | EngineSettings.Language | EngineSettings.Language initializer ("fr") |  |  |
| Initial Prompt | setting | hand-authored | EngineSettings.InitialPrompt | EngineSettings.InitialPrompt initializer (long French text) | • |  |
| Carry Initial Prompt | setting | hand-authored | EngineSettings.CarryInitialPrompt | EngineSettings.CarryInitialPrompt initializer (true) | • |  |
| Entropy Threshold | setting | hand-authored | ConfidenceSettings.EntropyThreshold | ConfidenceSettings.EntropyThreshold initializer (2.4) | • |  |
| Logprob Threshold | setting | hand-authored | ConfidenceSettings.LogprobThreshold | ConfidenceSettings.LogprobThreshold initializer (-1.0) | • |  |
| No-Speech Threshold | setting | hand-authored | ConfidenceSettings.NoSpeechThreshold | ConfidenceSettings.NoSpeechThreshold initializer (0.6) | • |  |
| Suppress Non-Speech Tokens | setting | hand-authored | OutputFilterSettings.SuppressNonSpeechTokens | OutputFilterSettings.SuppressNonSpeechTokens initializer (true) | • |  |
| Suppress Blank | setting | hand-authored | OutputFilterSettings.SuppressBlank | OutputFilterSettings.SuppressBlank initializer (true) | • |  |
| Suppress Regex | setting | hand-authored | OutputFilterSettings.SuppressRegex | OutputFilterSettings.SuppressRegex initializer ("") | • |  |
| Temperature | setting | hand-authored | DecodingSettings.Temperature | DecodingSettings.Temperature initializer (0.0) | • |  |
| Temperature Increment | setting | hand-authored | DecodingSettings.TemperatureIncrement | DecodingSettings.TemperatureIncrement initializer (0.2) | • |  |
| Use Beam Search | setting | hand-authored | DecodingSettings.UseBeamSearch | DecodingSettings.UseBeamSearch initializer (true) | • |  |
| Beam Size | setting | hand-authored | DecodingSettings.BeamSize | DecodingSettings.BeamSize initializer (5) | • |  |
| Use Context | setting | hand-authored | ContextSettings.UseContext | ContextSettings.UseContext initializer (true) | • |  |
| Max Context Tokens | setting | hand-authored | ContextSettings.MaxTokens | ContextSettings.MaxTokens initializer (-1) | • |  |
| Streaming Pipeline Strategy | setting | migrated | StreamingSettings.Strategy (PipelineStrategyKind) | StreamingSettings.Strategy initializer (Monolithic = false) |  |  |
| Energy Segmenter Threshold | setting | migrated | EnergySegmenterSettings.ThresholdDbfs | EnergySegmenterSettings.ThresholdDbfs initializer (-38) | • |  |
| Energy Segmenter Hangover Max | setting | migrated | EnergySegmenterSettings.HangoverMaxMs | EnergySegmenterSettings.HangoverMaxMs initializer (1500) | • |  |
| Energy Segmenter Hangover Min | setting | migrated | EnergySegmenterSettings.HangoverMinMs | EnergySegmenterSettings.HangoverMinMs initializer (500) | • |  |
| Energy Segmenter Hangover Ramp Start | setting | migrated | EnergySegmenterSettings.HangoverRampStartMs | EnergySegmenterSettings.HangoverRampStartMs initializer (5000) | • |  |
| Energy Segmenter Hangover Ramp End | setting | migrated | EnergySegmenterSettings.HangoverRampEndMs | EnergySegmenterSettings.HangoverRampEndMs initializer (60000) | • |  |
| Energy Segmenter Margin | setting | migrated | EnergySegmenterSettings.MarginMs | EnergySegmenterSettings.MarginMs initializer (300) | • |  |
| Energy Segmenter Min Utterance | setting | migrated | EnergySegmenterSettings.MinUtteranceMs | EnergySegmenterSettings.MinUtteranceMs initializer (300) | • |  |
| VAD Enabled | setting | migrated | SpeechTrimSettings.Enabled | SpeechTrimSettings.Enabled initializer (true) |  |  |
| VAD Threshold | setting | migrated | SpeechTrimSettings.Threshold | SpeechTrimSettings.Threshold initializer (0.5) | • |  |
| VAD Min Speech Duration | setting | migrated | SpeechTrimSettings.MinSpeechDurationMs | SpeechTrimSettings.MinSpeechDurationMs initializer (250) | • |  |
| VAD Min Silence Duration | setting | migrated | SpeechTrimSettings.MinSilenceDurationMs | SpeechTrimSettings.MinSilenceDurationMs initializer (100) | • |  |
| VAD Speech Pad | setting | migrated | SpeechTrimSettings.SpeechPadMs | SpeechTrimSettings.SpeechPadMs initializer (30) | • |  |
| Models Directory | setting | hand-authored | TranscriptionSettings.ModelsDirectory | TranscriptionSettings.ModelsDirectory initializer ("") | • |  |
| LLM Enabled | setting | hand-authored | LlmSettings.Enabled | LlmSettings.Enabled initializer (true) |  |  |
| Ollama Endpoint | setting | hand-authored | LlmSettings.OllamaEndpoint | LlmSettings.OllamaEndpoint initializer ("http://localhost:11434/api/generate") |  |  |
| Primary Rewrite Profile | setting | hand-authored | LlmSettings.PrimaryRewriteProfileName / PrimaryRewriteProfileId | LlmSettings.PrimaryRewriteProfileName initializer (null) |  |  |
| Secondary Rewrite Profile | setting | hand-authored | LlmSettings.SecondaryRewriteProfileName / SecondaryRewriteProfileId | LlmSettings.SecondaryRewriteProfileName initializer (null) |  |  |
| Rewrite Profiles (List) | setting | hand-authored | LlmSettings.Profiles (RewriteProfile[]) | LlmSettings.Profiles initializer (Lissage, Affinage, Arrangement presets) |  |  |
| Legacy Auto-Rewrite Rules (Duration) | legacy setting | retained for deserialization | LlmSettings.AutoRewriteRules (AutoRewriteRule[]) | No runtime or UI consumer |  |  |
| Legacy Auto-Rewrite Rules (Words) | legacy setting | retained for deserialization | LlmSettings.AutoRewriteRulesByWords (AutoRewriteRuleByWords[]) | No runtime or UI consumer |  |  |
| Rule Metric | setting | hand-authored | LlmSettings.RuleMetric | LlmSettings.RuleMetric initializer ("Duration") | • |  |
| Trackpad Enabled (Three-finger drag) | setting | hand-authored | TrackpadSettings.Enabled | TrackpadSettings.Enabled initializer (false) |  |  |
| Trackpad Drag Speed | setting | hand-authored | TrackpadSettings.DragSpeed | TrackpadSettings.DragSpeed initializer (1.0) |  |  |
| Trackpad Record Frames (diagnostic) | diagnostic | hand-authored | TrackpadSettings.RecordFrames | TrackpadSettings.RecordFrames initializer (false) | • |  |
| Taskbar Cover Enabled | setting | hand-authored | TaskbarCoverSettings.Enabled | TaskbarCoverSettings.Enabled initializer (false) |  |  |
| Speech Enabled (Skeleton) | setting | hand-authored | SpeechSettings.Enabled | SpeechSettings.Enabled initializer (false) |  |  |
| Speech Voice | setting | hand-authored | SpeechSettings.Voice | SpeechSettings.Voice initializer (Pierre) |  |  |
| Speech Temperature | setting | hand-authored | SpeechSettings.Temperature | SpeechSettings.Temperature initializer (0.6) | • |  |
| Ambient Enabled | setting | hand-authored | AmbientSettings.Enabled | AmbientSettings.Enabled initializer (false) |  |  |
| Ambient Mode (Game/Movie/Ambient/Custom) | setting | hand-authored | AmbientSettings.Mode | AmbientSettings.Mode initializer (Game) |  |  |
| Ambient HDR Tuning (Exposure, Saturation, MinBrightness, CurveType, Curve Params) | setting | hand-authored | AmbientSettings.ExposureEv, SaturationBoost, MinBrightness, BrightnessCurveType, BrightnessCurveParam, BrightnessCurveSCurveSteepness, ChangeThreshold, SmoothingAlpha | AmbientSettings initializers (see .cs) | • |  |
| Ambient Border Thickness Mode | setting | hand-authored | AmbientSettings.BorderMode (Share / Cells) | AmbientSettings.BorderMode initializer (Share) | • |  |
| Ambient Multi-Light & Zones | setting | hand-authored | AmbientSettings.UseMultiLight, LightZones, LightBrightness | AmbientSettings initializers | • |  |
| Autocorrect Enabled (Master) | setting | hand-authored | AutocorrectSettings.Enabled | AutocorrectSettings.Enabled initializer (true) |  |  |
| Autocorrect Per-App Decisions | setting | hand-authored | AutocorrectSettings.Apps (Dictionary<string, bool>) | AutocorrectSettings.Apps initializer (OrdinalIgnoreCase, Notepad=true by default) |  |  |
| Logging Ambient Capture Activity | diagnostic | migrated | LoggingSettings.LogAmbientCaptureActivity | LoggingSettings.LogAmbientCaptureActivity initializer (false) | • |  |
| Logging Streaming Transcription Activity | diagnostic | migrated | LoggingSettings.LogStreamingTranscriptionActivity | LoggingSettings.LogStreamingTranscriptionActivity initializer (false) | • |  |
| Logging Autocorrect Activity | diagnostic | migrated | LoggingSettings.LogAutocorrectActivity | LoggingSettings.LogAutocorrectActivity initializer (false) | • |  |
| Logging Windowing Activity | diagnostic | migrated | LoggingSettings.LogWindowingActivity | LoggingSettings.LogWindowingActivity initializer (false) | • |  |
| Logging Window Visibility Mode | diagnostic | hand-authored | LoggingSettings.LogWindowVisibilityMode | LoggingSettings.LogWindowVisibilityMode initializer (All) | • |  |
| Telemetry Latency Enabled | setting | migrated | TelemetrySettings.LatencyEnabled | TelemetrySettings.LatencyEnabled initializer (false) | • |  |
| Telemetry Microphone | setting | migrated | TelemetrySettings.MicrophoneTelemetry | TelemetrySettings.MicrophoneTelemetry initializer (false) | • |  |
| Telemetry Corpus Enabled | setting | hand-authored | TelemetrySettings.CorpusEnabled | TelemetrySettings.CorpusEnabled initializer (false) | • |  |
| Telemetry Record Audio Corpus | setting | hand-authored | TelemetrySettings.RecordAudioCorpus | TelemetrySettings.RecordAudioCorpus initializer (false) | • |  |
| Telemetry Audio Corpus Content | setting | hand-authored | TelemetrySettings.AudioCorpusContent (MatchTranscription / AlwaysRaw) | TelemetrySettings.AudioCorpusContent initializer (MatchTranscription) | • |  |
| Telemetry Application Log to Disk | setting | migrated | TelemetrySettings.ApplicationLogToDisk | TelemetrySettings.ApplicationLogToDisk initializer (false) | • |  |
| Telemetry Log Storage Directory | setting | hand-authored | TelemetrySettings.StorageDirectory | TelemetrySettings.StorageDirectory initializer ("") | • |  |
| Telemetry Autocorrect Decisions | setting | migrated | TelemetrySettings.AutocorrectDecisions | TelemetrySettings.AutocorrectDecisions initializer (false) | • |  |
| Telemetry Autocorrect Text | setting | migrated | TelemetrySettings.AutocorrectText | TelemetrySettings.AutocorrectText initializer (false) | • |  |
| Mouse Wheel Record Events (diagnostic) | diagnostic | hand-authored | MouseWheelSettings.RecordEvents | MouseWheelSettings.RecordEvents initializer (false) | • |  |

**Gestes destructifs :** Reset All Whisper Settings _(adhoc-dialog)_ · Reset All LLM Settings _(adhoc-dialog)_ · Reset LLM Profiles Section _(adhoc-dialog)_ · Delete LLM Profile _(adhoc-dialog)_ · Reset LLM Shortcuts Section _(adhoc-dialog)_ · Neutralize Windows Three-Finger Gestures _(none)_ · Restore Windows Three-Finger Gestures _(none)_ · Repair Trackpad Bluetooth Connection _(none)_ · Forget Autocorrect App _(none)_

## Deckle

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| Theme | setting | migrated | Choice (System/Light/Dark) | AppSettings initializer: System |  | Settings > General > Appearance |
| OverlayEnabled | setting | migrated | Group (master toggle) | OverlaySettings initializer: true |  | Settings > General > Behaviour > Overlay |
| OverlayFadeOnProximity | setting | migrated | Toggle | OverlaySettings initializer: true |  | Settings > General > Behaviour > Overlay (child) |
| OverlayAnimations | setting | migrated | Toggle | OverlaySettings initializer: true |  | Settings > General > Behaviour > Overlay (child) |
| OverlayPosition | setting | migrated | Choice (TopCenter/BottomCenter) | OverlaySettings initializer: TopCenter |  | Settings > General > Behaviour > Overlay (child) |
| AutoPasteEnabled | setting | migrated | Toggle | PasteSettings initializer: false |  | Settings > General > Behaviour |
| AutostartEnabled | setting | migrated | Toggle | AutostartService.DefaultEnabled: false |  | Settings > General > Startup |
| AudioInputDeviceId | setting | hand-authored | Choice (runtime waveIn enumeration) | CaptureSettings initializer: -1 (system default) |  | Settings > Recording > Microphone selection |
| MaxRecordingDurationSeconds | setting | hand-authored | Number | CaptureSettings initializer: 1200 (20 min) |  | Playground / Not exposed in Settings |
| LevelWindowMinDbfs | setting | migrated | Slider (-90 to -10 dBFS) | LevelWindowSettings initializer: -55.0f |  | Settings > Recording > Voice level window |
| LevelWindowMaxDbfs | setting | migrated | Slider (-60 to -10 dBFS) | LevelWindowSettings initializer: -32.0f |  | Settings > Recording > Voice level window |
| LevelWindowExponent | setting | migrated | Slider (0.3 to 3.0) | LevelWindowSettings initializer: 1.0f |  | Settings > Recording > Voice level window |
| LevelWindowAutoCalibrationEnabled | setting | hand-authored | Toggle (inverted: master = manual window) | LevelWindowSettings initializer: false | • | Settings > Recording > Voice level window |
| PreprocessingEnabled | setting | migrated | Toggle | PreprocessingSettings initializer: false |  | Settings > Recording > DSP preprocessing |
| TranscriptionPipelineStrategy | setting | hand-authored | Choice (Monolithic/Streaming) | StreamingSettings initializer: Monolithic |  | Playground or WhisperPage |
| EnergySegmenterThresholdDbfs | setting | hand-authored | Slider | EnergySegmenterSettings initializer: -45.0 | • | WhisperPage (Segmentation tuning) |
| SpeechTrimEnabled | setting | hand-authored | Toggle | SpeechTrimSettings initializer: true |  | Settings > Whisper (Voice activity detection) |
| WhisperModel | setting | hand-authored | Choice (model file selection) | EngineSettings initializer: ggml-base.bin |  | Playground > Segmentation or WhisperPage |
| TranscriptionModelsDirectory | setting | hand-authored | Path | TranscriptionSettings initializer: empty (uses AppPaths.ModelsDirectory) |  | Not directly exposed |
| LogAmbientCaptureActivity | setting | migrated | Toggle | LoggingSettings initializer: false |  | Settings > Diagnostics > Logging |
| LogStreamingTranscriptionActivity | setting | migrated | Toggle | LoggingSettings initializer: false |  | Settings > Diagnostics > Logging |
| LogAutocorrectActivity | setting | migrated | Toggle | LoggingSettings initializer: false |  | Settings > Diagnostics > Logging |
| LogWindowingActivity | setting | migrated | Toggle | LoggingSettings initializer: false |  | Settings > Diagnostics > Logging |
| ApplicationLogToDisk | setting | migrated | Toggle (with confirmOnEnable gate) | TelemetrySettings initializer: false |  | Settings > Diagnostics > Telemetry |
| MicrophoneTelemetry | setting | migrated | Toggle (with confirmOnEnable gate) | TelemetrySettings initializer: false |  | Settings > Diagnostics > Telemetry |
| LatencyEnabled | setting | migrated | Toggle | TelemetrySettings initializer: false |  | Settings > Diagnostics > Telemetry |
| CorpusEnabled | setting | hand-authored | Toggle (with nested consent dialog) | TelemetrySettings initializer: false |  | Settings > Diagnostics > Telemetry > Corpus |
| RecordAudioCorpus | setting | hand-authored | Toggle | TelemetrySettings initializer: false |  | Settings > Diagnostics > Telemetry > Corpus (child) |
| AudioCorpusContent | setting | hand-authored | Choice / Radio (MatchTranscription/AlwaysRaw) | TelemetrySettings initializer: MatchTranscription |  | Settings > Diagnostics > Telemetry > Corpus (child) |
| AutocorrectDecisions | setting | hand-authored | Toggle (with nested consent dialog) | TelemetrySettings initializer: false |  | Settings > Diagnostics > Telemetry > Autocorrect decisions |
| AutocorrectText | setting | hand-authored | Toggle (with nested consent dialog) | TelemetrySettings initializer: false |  | Settings > Diagnostics > Telemetry > Autocorrect text |
| TelemetryStorageDirectory | setting | hand-authored | Path | TelemetrySettings initializer: empty (uses AppPaths.TelemetryRoot) |  | Settings > Diagnostics > Telemetry > Storage folder |
| AutocorrectEnabled | setting | hand-authored | Toggle | AutocorrectSettings initializer: true |  | Autocorrect module (future page) |
| AutocorrectAppsDict | setting | n/a | n/a | AutocorrectSettings initializer: {notepad: true} |  | Not exposed |
| TrackpadEnabled | setting | hand-authored | Toggle | TrackpadSettings initializer: false |  | Trackpad module page (future or Playground) |
| TrackpadDragSpeed | setting | hand-authored | Slider (multiplier) | TrackpadSettings initializer: 1.0 |  | Trackpad module page (future or Playground) |
| MouseWheelRecordEvents | setting | hand-authored | Toggle (diagnostic) | MouseWheelSettings initializer: false |  | Playground > Home (diagnostic surface) |
| AmbientEnabled | setting | hand-authored | Toggle | AmbientSettings initializer: false |  | Ambient page (future) or Playground |
| AmbientHueBridgeIp | setting | n/a | Text (IP address) | AmbientSettings initializer: null |  | Playground > Ambient pairing |
| AmbientHueUsername | setting | n/a | Text (API key) | AmbientSettings initializer: null |  | Playground > Ambient pairing |
| AmbientMode | setting | hand-authored | Choice / Radio (Game/Movie/Ambient/Custom) | AmbientSettings initializer: Game |  | Playground > Ambient or future Settings page |
| AmbientExposureEv | setting | hand-authored | Slider (-2 to +2 EV) | AmbientSettings initializer: 0.0 | • | Playground > Ambient tuning |
| AmbientSaturationBoost | setting | hand-authored | Slider (0 to 2) | AmbientSettings initializer: 1.0 | • | Playground > Ambient tuning |
| AmbientMinBrightness | setting | hand-authored | Slider (0 to 254) | AmbientSettings initializer: 180 | • | Playground > Ambient tuning |
| AmbientBrightnessCurveType | setting | hand-authored | Choice (Linear/Gamma/SCurve/Logarithmic) | AmbientSettings initializer: Gamma | • | Playground > Ambient tuning |
| AmbientBrightnessCurveParam | setting | hand-authored | Slider (varies per curve type) | AmbientSettings initializer: 1.8 | • | Playground > Ambient tuning |
| AmbientBrightnessCurveSCurveSteepness | setting | hand-authored | Slider (-5 to 5) | AmbientSettings initializer: 2.0 | • | Playground > Ambient tuning |
| AmbientChangeThreshold | setting | hand-authored | Slider (0 to 765) | AmbientSettings initializer: 6 | • | Playground > Ambient tuning |
| AmbientSmoothingAlpha | setting | hand-authored | Slider (0 to 1) | AmbientSettings initializer: 0.30 | • | Playground > Ambient tuning |
| AmbientBorderMode | setting | hand-authored | Choice (Share/Cells) | AmbientSettings initializer: Share | • | Playground > Ambient tuning |
| LlmEnabled | setting | hand-authored | Toggle | LlmSettings initializer: true |  | Settings > LLM (custom page) |
| LlmOllamaEndpoint | setting | hand-authored | Text (URL) | LlmSettings initializer: http://localhost:11434/api/generate |  | Settings > LLM > General |
| LlmProfiles | setting | n/a | n/a (complex list) | LlmSettings initializer: three default profiles (Lissage/Affinage/Arrangement) |  | Settings > LLM > Profiles |
| LlmAutoRewriteRules | legacy setting | retained for deserialization | n/a (complex list) | No runtime or UI consumer |  | No UI |
| SpeechEnabled | setting | hand-authored | Toggle | SpeechSettings initializer: false |  | Not yet exposed |
| SpeechVoice | setting | hand-authored | Choice / Radio (Pierre/Jessica) | SpeechSettings initializer: Pierre |  | Not yet exposed (reserved for ONNX backend) |
| SpeechTemperature | setting | hand-authored | Slider (0.5 to 0.7) | SpeechSettings initializer: 0.6 |  | Not yet exposed (reserved for ONNX backend) |
| TaskbarCoverEnabled | setting | hand-authored | Toggle | TaskbarCoverSettings initializer: false |  | Tray menu or future Settings |

**Gestes destructifs :** Reset section (Appearance/Behaviour/Startup/Logging/etc.) _(none)_ · Reset per-card (individual slider/toggle) _(none)_ · Purge audio corpus JSONL _(none)_ · Purge autocorrect decisions JSONL _(none)_ · Clear application log (app.jsonl) _(none)_ · Reset LLM profiles to defaults _(none)_ · Re-pair Hue bridge (clears pairing state) _(none)_

## Deckle Settings Inventory

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| Theme | setting | migrated | GeneralViewModel.Theme (Choice) | new AppearanceSettings().Theme ("System") |  | Settings > General > Appearance |
| OverlayEnabled | setting | migrated | GeneralViewModel.OverlayEnabled (Group master toggle) | new OverlaySettings().Enabled (true) |  | Settings > General > Behaviour > Overlay |
| OverlayFadeOnProximity | setting | migrated | GeneralViewModel.OverlayFadeOnProximity (child of Group) | new OverlaySettings().FadeOnProximity |  | Settings > General > Behaviour > Overlay |
| OverlayAnimations | setting | migrated | GeneralViewModel.OverlayAnimations (child of Group) | new OverlaySettings().Animations |  | Settings > General > Behaviour > Overlay |
| OverlayPosition | setting | migrated | GeneralViewModel.OverlayPosition (child of Group, Choice) | new OverlaySettings().Position ("TopCenter"/"BottomCenter" normalized) |  | Settings > General > Behaviour > Overlay |
| AutoPasteEnabled | setting | migrated | GeneralViewModel.AutoPasteEnabled (Toggle) | new PasteSettings().AutoPasteEnabled |  | Settings > General > Behaviour |
| AutostartEnabled | setting | migrated | GeneralViewModel.AutostartEnabled (Toggle, registry-backed) | AutostartService.DefaultEnabled (false) |  | Settings > General > Startup |
| PreprocessingEnabled | setting | migrated | RecordingViewModel.PreprocessingEnabled (Toggle) | new PreprocessingSettings().Enabled (false) |  | Settings > Recording |
| LevelWindowMinDbfs | setting | migrated | RecordingViewModel.LevelWindowMinDbfs (Slider, child of Group with inverted master) | new LevelWindowSettings().MinDbfs (-55f) |  | Settings > Recording > Voice Level Calibration |
| LevelWindowMaxDbfs | setting | migrated | RecordingViewModel.LevelWindowMaxDbfs (Slider, child of Group) | new LevelWindowSettings().MaxDbfs (-32f) |  | Settings > Recording > Voice Level Calibration |
| LevelWindowExponent | setting | migrated | RecordingViewModel.LevelWindowExponent (Slider, child of Group) | new LevelWindowSettings().DbfsCurveExponent (1.0f) |  | Settings > Recording > Voice Level Calibration |
| LevelWindowAutoCalibration | setting | migrated | RecordingViewModel.LevelWindowAutoCalibration (inverted as Group master) | !new LevelWindowSettings().AutoCalibrationEnabled (true = manual mode) |  | Settings > Recording > Voice Level Calibration |
| LogAmbientCaptureActivity | setting | migrated | DiagnosticsViewModel.LogAmbientCaptureActivity (Toggle) | new LoggingSettings().LogAmbientCaptureActivity (false) |  | Settings > Diagnostics > Logging |
| LogStreamingTranscriptionActivity | setting | migrated | DiagnosticsViewModel.LogStreamingTranscriptionActivity (Toggle) | new LoggingSettings().LogStreamingTranscriptionActivity (false) |  | Settings > Diagnostics > Logging |
| LogAutocorrectActivity | setting | migrated | DiagnosticsViewModel.LogAutocorrectActivity (Toggle) | new LoggingSettings().LogAutocorrectActivity (false) |  | Settings > Diagnostics > Logging |
| LogWindowingActivity | setting | migrated | DiagnosticsViewModel.LogWindowingActivity (Toggle) | new LoggingSettings().LogWindowingActivity (false) |  | Settings > Diagnostics > Logging |
| ApplicationLogToDisk | setting | migrated | DiagnosticsViewModel.ApplicationLogToDisk (Toggle, confirmOnEnable) | new TelemetrySettings().ApplicationLogToDisk (false) |  | Settings > Diagnostics > Telemetry |
| MicrophoneTelemetry | setting | migrated | DiagnosticsViewModel.MicrophoneTelemetry (Toggle, confirmOnEnable) | new TelemetrySettings().MicrophoneTelemetry (false) |  | Settings > Diagnostics > Telemetry |
| TelemetryLatencyEnabled | setting | migrated | DiagnosticsViewModel.TelemetryLatencyEnabled (Toggle) | new TelemetrySettings().LatencyEnabled (false) |  | Settings > Diagnostics > Telemetry |
| VadEnabled | setting | migrated | WhisperViewModel.VadEnabled (Group master toggle) | new SpeechTrimSettings().Enabled (true) |  | Settings > Transcription (Whisper) > Voice Activity Detection |
| VadThreshold | setting | migrated | WhisperViewModel.VadThreshold (Slider, child of VAD Group) | new SpeechTrimSettings().Threshold (0.5f) |  | Settings > Transcription > Voice Activity Detection |
| VadMinSpeechDurationMs | setting | migrated | WhisperViewModel.VadMinSpeechDurationMs (Slider, child of VAD Group) | new SpeechTrimSettings().MinSpeechDurationMs (250) | • | Settings > Transcription > Voice Activity Detection |
| VadMinSilenceDurationMs | setting | migrated | WhisperViewModel.VadMinSilenceDurationMs (Slider, child of VAD Group) | new SpeechTrimSettings().MinSilenceDurationMs (100) | • | Settings > Transcription > Voice Activity Detection |
| VadSpeechPadMs | setting | migrated | WhisperViewModel.VadSpeechPadMs (Slider, child of VAD Group) | new SpeechTrimSettings().SpeechPadMs (30) | • | Settings > Transcription > Voice Activity Detection |
| StreamingEnabled | setting | migrated | WhisperViewModel.StreamingEnabled (Group master toggle, projected from PipelineStrategyKind) | new StreamingSettings().Strategy == Streaming (false by default = Monolithic) |  | Settings > Transcription > Streaming Pipeline |
| SegThresholdDbfs | setting | migrated | WhisperViewModel.SegThresholdDbfs (Number, child of Streaming Group) | new EnergySegmenterSettings().ThresholdDbfs (-45.0) | • | Settings > Transcription > Streaming Pipeline > Segmenter |
| SegHangoverMaxMs | setting | migrated | WhisperViewModel.SegHangoverMaxMs (Number, child of Streaming Group) | new EnergySegmenterSettings().HangoverMaxMs (5000) | • | Settings > Transcription > Streaming Pipeline > Segmenter |
| SegHangoverMinMs | setting | migrated | WhisperViewModel.SegHangoverMinMs (Number, child of Streaming Group) | new EnergySegmenterSettings().HangoverMinMs (500) | • | Settings > Transcription > Streaming Pipeline > Segmenter |
| SegHangoverRampStartMs | setting | migrated | WhisperViewModel.SegHangoverRampStartMs (Number, child of Streaming Group) | new EnergySegmenterSettings().HangoverRampStartMs (15000) | • | Settings > Transcription > Streaming Pipeline > Segmenter |
| SegHangoverRampEndMs | setting | migrated | WhisperViewModel.SegHangoverRampEndMs (Number, child of Streaming Group) | new EnergySegmenterSettings().HangoverRampEndMs (120000) | • | Settings > Transcription > Streaming Pipeline > Segmenter |
| SegMarginMs | setting | migrated | WhisperViewModel.SegMarginMs (Number, child of Streaming Group) | new EnergySegmenterSettings().MarginMs (150) | • | Settings > Transcription > Streaming Pipeline > Segmenter |
| SegMinUtteranceMs | setting | migrated | WhisperViewModel.SegMinUtteranceMs (Number, child of Streaming Group) | new EnergySegmenterSettings().MinUtteranceMs (250) | • | Settings > Transcription > Streaming Pipeline > Segmenter |
| TaskbarCoverEnabled | setting | hand-authored | TaskbarCoverSettings.Enabled (Toggle, NOT in Settings UI) | new TaskbarCoverSettings().Enabled (false) |  | System Tray Context Menu > Taskbar Cover |

**Gestes destructifs :** Reset all Settings in a section (General, Recording, Diagnostics, Transcription) _(adhoc-dialog)_ · Clear persisted telemetry data (application logs, corpus, autocorrect telemetry) _(adhoc-dialog)_ · Revoke autocorrect on an enrolled app (clear app from Apps dict) _(none)_ · Reset autocorrect personal dictionary _(none)_

## Deckle Settings Inventory

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| Theme | setting | migrated | GeneralViewModel.Theme (AppearanceSettings.Theme) | new AppearanceSettings().Theme = "System" |  | GeneralPage, Appearance section |
| Auto-paste enabled | setting | migrated | GeneralViewModel.AutoPasteEnabled (PasteSettings.AutoPasteEnabled) | new PasteSettings().AutoPasteEnabled = false |  | GeneralPage, Behaviour section |
| Overlay enabled (master toggle) | setting | migrated | GeneralViewModel.OverlayEnabled (OverlaySettings.Enabled) | new OverlaySettings().Enabled = true |  | GeneralPage, Behaviour section |
| Overlay fade-on-proximity | setting | migrated | GeneralViewModel.OverlayFadeOnProximity (OverlaySettings.FadeOnProximity) | new OverlaySettings().FadeOnProximity = true |  | GeneralPage, Behaviour section (Overlay group child) |
| Overlay animations | setting | migrated | GeneralViewModel.OverlayAnimations (OverlaySettings.Animations) | new OverlaySettings().Animations = true |  | GeneralPage, Behaviour section (Overlay group child) |
| Overlay position | setting | migrated | GeneralViewModel.OverlayPosition (OverlaySettings.Position) | new OverlaySettings().Position = "BottomCenter" |  | GeneralPage, Behaviour section (Overlay group child) |
| Autostart (with Windows) | setting | migrated | GeneralViewModel.AutostartEnabled (registry-backed) | AutostartService.DefaultEnabled = false |  | GeneralPage, Startup section |
| Preprocessing enabled | setting | migrated | RecordingViewModel.PreprocessingEnabled (PreprocessingSettings.Enabled) | new PreprocessingSettings().Enabled = true |  | RecordingPage, main section |
| Voice level window (auto-calibration master) | setting | migrated | RecordingViewModel.LevelWindowAutoCalibration (LevelWindowSettings.AutoCalibrationEnabled) | new LevelWindowSettings().AutoCalibrationEnabled = true |  | RecordingPage, main section |
| Voice level window minimum (dBFS) | setting | migrated | RecordingViewModel.LevelWindowMinDbfs (LevelWindowSettings.MinDbfs) | new LevelWindowSettings().MinDbfs = -90.0 |  | RecordingPage, Voice level group |
| Voice level window maximum (dBFS) | setting | migrated | RecordingViewModel.LevelWindowMaxDbfs (LevelWindowSettings.MaxDbfs) | new LevelWindowSettings().MaxDbfs = -36.0 |  | RecordingPage, Voice level group |
| Voice level window curve exponent | setting | migrated | RecordingViewModel.LevelWindowExponent (LevelWindowSettings.DbfsCurveExponent) | new LevelWindowSettings().DbfsCurveExponent = 1.0 |  | RecordingPage, Voice level group |
| VAD enabled | setting | migrated | WhisperViewModel.VadEnabled (SpeechTrimSettings.Enabled) | new SpeechTrimSettings().Enabled = true |  | WhisperPage, streaming fold |
| VAD threshold | setting | migrated | WhisperViewModel.VadThreshold (SpeechTrimSettings.Threshold) | new SpeechTrimSettings().Threshold = 0.5 |  | WhisperPage, VAD group |
| VAD minimum speech duration (ms) | setting | migrated | WhisperViewModel.VadMinSpeechDurationMs (SpeechTrimSettings.MinSpeechDurationMs) | new SpeechTrimSettings().MinSpeechDurationMs = 100 |  | WhisperPage, VAD group |
| VAD minimum silence duration (ms) | setting | migrated | WhisperViewModel.VadMinSilenceDurationMs (SpeechTrimSettings.MinSilenceDurationMs) | new SpeechTrimSettings().MinSilenceDurationMs = 300 |  | WhisperPage, VAD group |
| VAD speech padding (ms) | setting | migrated | WhisperViewModel.VadSpeechPadMs (SpeechTrimSettings.SpeechPadMs) | new SpeechTrimSettings().SpeechPadMs = 0 |  | WhisperPage, VAD group |
| Streaming enabled | setting | migrated | WhisperViewModel.StreamingEnabled (StreamingSettings.Strategy) | new StreamingSettings().Strategy == PipelineStrategyKind.Streaming ? true : false |  | WhisperPage, streaming fold |
| Segmenter threshold (dBFS) | setting | migrated | WhisperViewModel.SegThresholdDbfs (EnergySegmenterSettings.ThresholdDbfs) | new EnergySegmenterSettings().ThresholdDbfs = -38 | • | WhisperPage, Streaming group |
| Segmenter hangover maximum (ms) | setting | migrated | WhisperViewModel.SegHangoverMaxMs (EnergySegmenterSettings.HangoverMaxMs) | new EnergySegmenterSettings().HangoverMaxMs = 4000 | • | WhisperPage, Streaming group |
| Segmenter hangover minimum (ms) | setting | migrated | WhisperViewModel.SegHangoverMinMs (EnergySegmenterSettings.HangoverMinMs) | new EnergySegmenterSettings().HangoverMinMs = 500 | • | WhisperPage, Streaming group |
| Segmenter hangover ramp start (ms) | setting | migrated | WhisperViewModel.SegHangoverRampStartMs (EnergySegmenterSettings.HangoverRampStartMs) | new EnergySegmenterSettings().HangoverRampStartMs = 60000 | • | WhisperPage, Streaming group |
| Segmenter hangover ramp end (ms) | setting | migrated | WhisperViewModel.SegHangoverRampEndMs (EnergySegmenterSettings.HangoverRampEndMs) | new EnergySegmenterSettings().HangoverRampEndMs = 180000 | • | WhisperPage, Streaming group |
| Segmenter margin (ms) | setting | migrated | WhisperViewModel.SegMarginMs (EnergySegmenterSettings.MarginMs) | new EnergySegmenterSettings().MarginMs = 40 | • | WhisperPage, Streaming group |
| Segmenter minimum utterance (ms) | setting | migrated | WhisperViewModel.SegMinUtteranceMs (EnergySegmenterSettings.MinUtteranceMs) | new EnergySegmenterSettings().MinUtteranceMs = 100 | • | WhisperPage, Streaming group |
| Logging: Ambient capture activity | setting | migrated | DiagnosticsViewModel.LogAmbientCaptureActivity (LoggingSettings.LogAmbientCaptureActivity) | new LoggingSettings().LogAmbientCaptureActivity = false |  | DiagnosticsPage, Logging section |
| Logging: Streaming transcription activity | setting | migrated | DiagnosticsViewModel.LogStreamingTranscriptionActivity (LoggingSettings.LogStreamingTranscriptionActivity) | new LoggingSettings().LogStreamingTranscriptionActivity = false |  | DiagnosticsPage, Logging section |
| Logging: Autocorrect activity | setting | migrated | DiagnosticsViewModel.LogAutocorrectActivity (LoggingSettings.LogAutocorrectActivity) | new LoggingSettings().LogAutocorrectActivity = false |  | DiagnosticsPage, Logging section |
| Logging: Windowing activity | setting | migrated | DiagnosticsViewModel.LogWindowingActivity (LoggingSettings.LogWindowingActivity) | new LoggingSettings().LogWindowingActivity = false |  | DiagnosticsPage, Logging section |
| Telemetry: Application log to disk | setting | migrated | DiagnosticsViewModel.ApplicationLogToDisk (TelemetrySettings.ApplicationLogToDisk) | new TelemetrySettings().ApplicationLogToDisk = false |  | DiagnosticsPage, Telemetry section |
| Telemetry: Microphone telemetry | setting | migrated | DiagnosticsViewModel.MicrophoneTelemetry (TelemetrySettings.MicrophoneTelemetry) | new TelemetrySettings().MicrophoneTelemetry = false |  | DiagnosticsPage, Telemetry section |
| Telemetry: Latency enabled | setting | migrated | DiagnosticsViewModel.TelemetryLatencyEnabled (TelemetrySettings.LatencyEnabled) | new TelemetrySettings().LatencyEnabled = false |  | DiagnosticsPage, Telemetry section |
| Telemetry: Corpus enabled | setting | migrated | DiagnosticsViewModel.TelemetryCorpusEnabled (TelemetrySettings.CorpusEnabled) | new TelemetrySettings().CorpusEnabled = false |  | DiagnosticsPage, Telemetry section (hand-authored expander) |
| Telemetry: Audio corpus enabled | setting | hand-authored | DiagnosticsViewModel.RecordAudioCorpus (TelemetrySettings.RecordAudioCorpus) | new TelemetrySettings().RecordAudioCorpus = false |  | DiagnosticsPage, Telemetry section (hand-authored expander child) |
| Telemetry: Audio corpus content selection | setting | hand-authored | DiagnosticsViewModel.AudioCorpusContentIndex → TelemetrySettings.AudioCorpusContent | new TelemetrySettings().AudioCorpusContent = AudioCorpusContent.Transcribed |  | DiagnosticsPage, Telemetry section (hand-authored expander child) |
| Telemetry: Autocorrect decisions | setting | hand-authored | DiagnosticsViewModel.AutocorrectDecisions (TelemetrySettings.AutocorrectDecisions) | new TelemetrySettings().AutocorrectDecisions = false |  | DiagnosticsPage, Telemetry section (hand-authored expander) |
| Telemetry: Autocorrect text | setting | hand-authored | DiagnosticsViewModel.AutocorrectText (TelemetrySettings.AutocorrectText) | new TelemetrySettings().AutocorrectText = false |  | DiagnosticsPage, Telemetry section (hand-authored expander) |
| Telemetry: Storage directory | setting | hand-authored | DiagnosticsViewModel.TelemetryStorageDirectory (TelemetrySettings.StorageDirectory) | new TelemetrySettings().StorageDirectory = "" (resolves to AppPaths.TelemetryDirectory) |  | DiagnosticsPage, Telemetry section (hand-authored) |
| Trackpad: Three-finger drag enabled | setting | hand-authored | TrackpadViewModel.Enabled (TrackpadSettings.Enabled) | new TrackpadSettings().Enabled = false |  | TrackpadPage, main section |
| Trackpad: Drag speed multiplier | setting | hand-authored | TrackpadViewModel.DragSpeed (TrackpadSettings.DragSpeed) | new TrackpadSettings().DragSpeed = 0.5 |  | TrackpadPage, main section |
| Trackpad: Record frames (diagnostic) | diagnostic | hand-authored | TrackpadViewModel.RecordFrames (TrackpadSettings.RecordFrames) | new TrackpadSettings().RecordFrames = false |  | TrackpadPage, Diagnostics section |
| Autocorrect: Master enabled | setting | hand-authored | AutocorrectViewModel.Enabled (AutocorrectSettings.Enabled) | new AutocorrectSettings().Enabled = false |  | AutocorrectPage, main section |
| Autocorrect: Per-app decision map | setting | hand-authored | AutocorrectViewModel.Apps (AutocorrectSettings.Apps) | new AutocorrectSettings().Apps = new() |  | AutocorrectPage, main section |
| Ambient: Master enabled | setting | hand-authored | AmbientSettings.Enabled | new AmbientSettings().Enabled = false |  | AmbientPage (Deckle.Lighting.Ambient module) |
| Ambient: Hue bridge IP | setting | hand-authored | AmbientSettings.HueBridgeIp | null (not paired by default) |  | AmbientPage/Playground, Pairing |
| Ambient: Hue bridge ID | setting | hand-authored | AmbientSettings.HueBridgeId | null (not paired by default) |  | AmbientPage/Playground, Pairing |
| Ambient: Hue username | setting | hand-authored | AmbientSettings.HueUsername | null (not paired by default) |  | AmbientPage/Playground, Pairing |
| Ambient: Last selected Hue group | setting | hand-authored | AmbientSettings.HueLastGroupId | null (no group selected by default) |  | AmbientPage/Playground, Group selector |
| Ambient: Selected monitor device | setting | hand-authored | AmbientSettings.SelectedMonitorDeviceName | null (default to primary monitor) |  | AmbientPage (planned) |
| Ambient: Mode preset | setting | hand-authored | AmbientSettings.Mode (enum AmbientMode) | AmbientMode.Game |  | AmbientPage/Playground, Mode selector |
| Ambient: Use multi-light zones | setting | hand-authored | AmbientSettings.UseMultiLight | false (single-colour group mode by default) |  | AmbientPage/Playground, Multi-light section |
| Ambient: Light zone assignments | setting | hand-authored | AmbientSettings.LightZones (Dict<string, LightZone>) | new() (empty map) |  | AmbientPage/Playground, Multi-light grid |
| Ambient: Light brightness multipliers | setting | hand-authored | AmbientSettings.LightBrightness (Dict<string, double>) | new() (empty map) |  | AmbientPage/Playground, Multi-light grid |
| Ambient: Border mode (Share/Cells) | setting | hand-authored | AmbientSettings.BorderMode (enum BorderThicknessMode) | BorderThicknessMode.Share |  | AmbientPage/Playground, Border section |
| Ambient: Border depth (Share mode, 0.05–0.5) | setting | hand-authored | AmbientSettings.BorderDepth | 0.33 |  | AmbientPage/Playground, Border section |
| Ambient: Border cells (Cells mode, 4–24) | setting | hand-authored | AmbientSettings.BorderCells | 8 |  | AmbientPage/Playground, Border section |
| Ambient: Exposure (EV, -2 to +2) | setting | hand-authored | AmbientSettings.ExposureEv | 0.0 |  | AmbientPage/Playground, HDR tuning section |
| Ambient: Saturation boost (0–2) | setting | hand-authored | AmbientSettings.SaturationBoost | 1.0 |  | AmbientPage/Playground, HDR tuning section |
| Ambient: Minimum brightness (0–254) | setting | hand-authored | AmbientSettings.MinBrightness | 180 |  | AmbientPage/Playground, HDR tuning section |
| Ambient: Brightness curve type | setting | hand-authored | AmbientSettings.BrightnessCurveType (enum BrightnessCurveType) | BrightnessCurveType.Gamma |  | AmbientPage/Playground, HDR tuning section |
| Ambient: Brightness curve parameter (Gamma exponent, 0.3–3.0) | setting | hand-authored | AmbientSettings.BrightnessCurveParam | 1.8 |  | AmbientPage/Playground, HDR tuning section |
| Ambient: Brightness S-curve steepness (-5.0 to +5.0) | setting | hand-authored | AmbientSettings.BrightnessCurveSCurveSteepness | 2.0 |  | AmbientPage/Playground, HDR tuning section |
| Ambient: Change threshold (0–765) | setting | hand-authored | AmbientSettings.ChangeThreshold | 6 |  | AmbientPage/Playground, HDR tuning section |
| Ambient: Smoothing alpha (EMA, 0.05–1.0) | setting | hand-authored | AmbientSettings.SmoothingAlpha | 0.30 |  | AmbientPage/Playground, HDR tuning section |
| LLM Rewrite: Enabled | setting | hand-authored | LlmSettings.Enabled (LlmSettings via ViewModels, check LlmPage) | false |  | LlmPage (Deckle.Llm.Rewrite module) |
| Speech: Master enabled | setting | n/a | SpeechSettings.Enabled | false (skeleton default) |  | NOT YET — speech module is dormant skeleton |
| Speech: Voice selection | setting | n/a | SpeechSettings.Voice (enum SpeechVoice: Pierre/Jessica) | SpeechVoice.Pierre |  | NOT YET — speech module is dormant skeleton |
| Speech: Temperature (0.5–0.7) | setting | n/a | SpeechSettings.Temperature | 0.6 (middle ground) |  | NOT YET — speech module is dormant skeleton |

**Gestes destructifs :** Reset Section (Logging) _(adhoc-dialog)_ · Reset Section (Telemetry) _(adhoc-dialog)_ · Forget app decision (Autocorrect) _(none)_ · Re-pair Hue bridge _(none)_

## Deckle.Playground Tuning Parameters Inventory

| Réglage | Cat. | État | Mappé sur | Défaut | Av. | Emplacement |
|---|---|---|---|---|---|---|
| Palette — Oklch Lightness | setting | hand-authored | TuningModel.OklchLightness | TuningModel field initializer (0.75f) |  | HudPage — Palette expander |
| Palette — Oklch Chroma | setting | hand-authored | TuningModel.OklchChroma | TuningModel field initializer (0.3f) |  | HudPage — Palette expander |
| Palette — Hue Start | setting | hand-authored | TuningModel.HueStart | TuningModel field initializer (0f) | • | HudPage — Palette expander |
| Palette — Hue Range | setting | hand-authored | TuningModel.HueRange | TuningModel field initializer (1f) | • | HudPage — Palette expander |
| Palette — Wedge Count | setting | hand-authored | TuningModel.WedgeCount | TuningModel field initializer (360) | • | HudPage — Palette expander |
| Hue Rotation — Period (seconds) | setting | hand-authored | TuningModel.HuePeriodSeconds | TuningModel field initializer (14.0) |  | HudPage — Hue Rotation expander |
| Hue Rotation — Direction | setting | hand-authored | TuningModel.HueDirection | TuningModel field initializer (1f, forward) | • | HudPage — Hue Rotation expander |
| Hue Rotation — Phase (turns) | setting | hand-authored | TuningModel.HuePhaseTurns | TuningModel field initializer (0f) | • | HudPage — Hue Rotation expander |
| Hue Rotation — Ease P1X | setting | hand-authored | TuningModel.HueEaseP1X | TuningModel field initializer (0.125f) | • | HudPage — Hue Rotation expander |
| Hue Rotation — Ease P1Y | setting | hand-authored | TuningModel.HueEaseP1Y | TuningModel field initializer (0.375f) | • | HudPage — Hue Rotation expander |
| Hue Rotation — Ease P2X | setting | hand-authored | TuningModel.HueEaseP2X | TuningModel field initializer (0.875f) | • | HudPage — Hue Rotation expander |
| Hue Rotation — Ease P2Y | setting | hand-authored | TuningModel.HueEaseP2Y | TuningModel field initializer (0.625f) | • | HudPage — Hue Rotation expander |
| Hue Rotation — Min Speed Fraction | setting | hand-authored | TuningModel.HueMinSpeedFraction | TuningModel field initializer (0f) | • | HudPage — Hue Rotation expander |
| Conic Fade — Span (turns) | setting | hand-authored | TuningModel.ConicSpanTurns | TuningModel field initializer (0.5f) |  | HudPage — Conic Fade expander |
| Conic Fade — Lead Fade (turns) | setting | hand-authored | TuningModel.ConicLeadFadeTurns | TuningModel field initializer (1f) |  | HudPage — Conic Fade expander |
| Conic Fade — Tail Fade (turns) | setting | hand-authored | TuningModel.ConicTailFadeTurns | TuningModel field initializer (1f) |  | HudPage — Conic Fade expander |
| Conic Fade — Curve | setting | hand-authored | TuningModel.ConicFadeCurve | TuningModel field initializer (4f) | • | HudPage — Conic Fade expander |
| Arc Shape — Mirror | setting | hand-authored | TuningModel.ArcMirror | TuningModel field initializer (true) |  | HudPage — Geometry expander |
| Arc Rotation — Period (seconds) | setting | hand-authored | TuningModel.ArcPeriodSeconds | TuningModel field initializer (8.0) |  | HudPage — Arc Rotation expander |
| Arc Rotation — Direction | setting | hand-authored | TuningModel.ArcDirection | TuningModel field initializer (1f, forward) | • | HudPage — Arc Rotation expander |
| Arc Rotation — Phase (turns) | setting | hand-authored | TuningModel.ArcPhaseTurns | TuningModel field initializer (0f) | • | HudPage — Arc Rotation expander |
| Arc Rotation — Ease P1X | setting | hand-authored | TuningModel.ArcEaseP1X | TuningModel field initializer (0.125f) | • | HudPage — Arc Rotation expander |
| Arc Rotation — Ease P1Y | setting | hand-authored | TuningModel.ArcEaseP1Y | TuningModel field initializer (0.375f) | • | HudPage — Arc Rotation expander |
| Arc Rotation — Ease P2X | setting | hand-authored | TuningModel.ArcEaseP2X | TuningModel field initializer (0.875f) | • | HudPage — Arc Rotation expander |
| Arc Rotation — Ease P2Y | setting | hand-authored | TuningModel.ArcEaseP2Y | TuningModel field initializer (0.625f) | • | HudPage — Arc Rotation expander |
| Arc Rotation — Min Speed Fraction | setting | hand-authored | TuningModel.ArcMinSpeedFraction | TuningModel field initializer (0f) | • | HudPage — Arc Rotation expander |
| Clone Placement — Center X (fraction) | setting | hand-authored | TuningModel.CloneCentreXFraction | TuningModel field initializer (196f / 272f, ~0.721) |  | HudPage — Clone Placement expander |
| Clone Placement — Center Y (fraction) | setting | hand-authored | TuningModel.CloneCentreYFraction | TuningModel field initializer (0f) |  | HudPage — Clone Placement expander |
| Clone Palette — Oklch Lightness | setting | hand-authored | TuningModel.CloneOklchLightness | TuningModel field initializer (0.9f) |  | HudPage — Clone Placement expander |
| Clone Palette — Oklch Chroma | setting | hand-authored | TuningModel.CloneOklchChroma | TuningModel field initializer (0.3f) |  | HudPage — Clone Placement expander |
| Clone Hue Rotation — Period (seconds) | setting | hand-authored | TuningModel.CloneHuePeriodSeconds | TuningModel field initializer (7.0) |  | HudPage — Clone Placement expander |
| Clone Hue Rotation — Direction | setting | hand-authored | TuningModel.CloneHueDirection | TuningModel field initializer (-1f, reverse) | • | HudPage — Clone Placement expander |
| Clone Arc Rotation — Period (seconds) | setting | hand-authored | TuningModel.CloneArcPeriodSeconds | TuningModel field initializer (4.0) |  | HudPage — Clone Placement expander |
| Clone Arc Rotation — Direction | setting | hand-authored | TuningModel.CloneArcDirection | TuningModel field initializer (-1f, reverse) | • | HudPage — Clone Placement expander |
| Rewriting — Saturation | setting | hand-authored | TuningModel.RewritingSaturation | TuningModel field initializer (1f) |  | HudPage — Rewriting expander |
| Rewriting — Hue Shift (turns) | setting | hand-authored | TuningModel.RewritingHueShiftTurns | TuningModel field initializer (0f) | • | HudPage — Rewriting expander |
| Rewriting — Exposure (EV) | setting | hand-authored | TuningModel.RewritingExposure | TuningModel field initializer (0f) |  | HudPage — Rewriting expander |
| Rewriting — Opacity | setting | hand-authored | TuningModel.RewritingOpacity | TuningModel field initializer (1f) |  | HudPage — Rewriting expander |
| Rewriting — Blend Duration (seconds) | setting | hand-authored | TuningModel.RewritingBlendSeconds | TuningModel field initializer (2) | • | HudPage — Rewriting expander |
| Transcribing — Saturation (dark) | setting | hand-authored | TuningModel.TranscribingSaturationDark | TuningModel field initializer (0f) |  | HudPage — Transcribing expander |
| Transcribing — Saturation (light) | setting | hand-authored | TuningModel.TranscribingSaturationLight | TuningModel field initializer (0f) |  | HudPage — Transcribing expander |
| Transcribing — Hue Shift (turns) | setting | hand-authored | TuningModel.TranscribingHueShiftTurns | TuningModel field initializer (0f) | • | HudPage — Transcribing expander |
| Transcribing — Exposure (dark, EV) | setting | hand-authored | TuningModel.TranscribingExposureDark | TuningModel field initializer (1.0f) |  | HudPage — Transcribing expander |
| Transcribing — Exposure (light, EV) | setting | hand-authored | TuningModel.TranscribingExposureLight | TuningModel field initializer (-1.0f) |  | HudPage — Transcribing expander |
| Transcribing — Opacity | setting | hand-authored | TuningModel.TranscribingOpacity | TuningModel field initializer (1f) |  | HudPage — Transcribing expander |
| Transcribing — Blend Duration (seconds) | setting | hand-authored | TuningModel.TranscribingBlendSeconds | TuningModel field initializer (2) | • | HudPage — Transcribing expander |
| Recording — Conic Span (turns) | setting | hand-authored | TuningModel.RecordingConicSpanTurns | TuningModel field initializer (0.5f) |  | HudPage — Recording expander |
| Recording — Conic Lead Fade (turns) | setting | hand-authored | TuningModel.RecordingConicLeadFadeTurns | TuningModel field initializer (1f) |  | HudPage — Recording expander |
| Recording — Conic Tail Fade (turns) | setting | hand-authored | TuningModel.RecordingConicTailFadeTurns | TuningModel field initializer (1f) |  | HudPage — Recording expander |
| Recording — Conic Fade Curve | setting | hand-authored | TuningModel.RecordingConicFadeCurve | TuningModel field initializer (2f) | • | HudPage — Recording expander |
| Recording — Arc Mirror | setting | hand-authored | TuningModel.RecordingArcMirror | TuningModel field initializer (true) |  | HudPage — Recording expander |
| Recording — Arc Phase (turns) | setting | hand-authored | TuningModel.RecordingArcPhaseTurns | TuningModel field initializer (0f) | • | HudPage — Recording expander |
| Recording — Saturation (dark) | setting | hand-authored | TuningModel.RecordingSaturationDark | TuningModel field initializer (0f) |  | HudPage — Recording expander |
| Recording — Saturation (light) | setting | hand-authored | TuningModel.RecordingSaturationLight | TuningModel field initializer (0f) |  | HudPage — Recording expander |
| Recording — Hue Shift (turns) | setting | hand-authored | TuningModel.RecordingHueShiftTurns | TuningModel field initializer (0f) | • | HudPage — Recording expander |
| Recording — Exposure (dark, EV) | setting | hand-authored | TuningModel.RecordingExposureDark | TuningModel field initializer (1.0f) |  | HudPage — Recording expander |
| Recording — Exposure (light, EV) | setting | hand-authored | TuningModel.RecordingExposureLight | TuningModel field initializer (-1.0f) |  | HudPage — Recording expander |
| Recording — Blend Duration (seconds) | setting | hand-authored | TuningModel.RecordingBlendSeconds | TuningModel field initializer (2) | • | HudPage — Recording expander |
| Recording — Hue Period (seconds) | setting | hand-authored | TuningModel.RecordingHuePeriodSeconds | TuningModel field initializer (0) |  | HudPage — Recording expander |
| Simulated RMS — Min (linear, dBFS equivalent) | setting | hand-authored | HudPage._simRmsMin | HudPage field initializer (0.013f, ~-38 dBFS) | • | HudPage — Simulated RMS expander |
| Simulated RMS — Max (linear, dBFS equivalent) | setting | hand-authored | HudPage._simRmsMax | HudPage field initializer (0.100f, ~-20 dBFS) | • | HudPage — Simulated RMS expander |
| Simulated RMS — Period (seconds) | setting | hand-authored | HudPage._simRmsPeriodSeconds | HudPage field initializer (2.0f) | • | HudPage — Simulated RMS expander |
| Simulated RMS — Manual Override | setting | hand-authored | HudPage._simManualOverride | HudPage field initializer (false) | • | HudPage — Simulated RMS expander |
| Simulated RMS — Manual Value (linear) | setting | hand-authored | HudPage._simManualValue | HudPage field initializer (0.012f) | • | HudPage — Simulated RMS expander |
| Simulate Changed Digits | setting | hand-authored | HudPage._simulateChangedDigits | HudPage field initializer (true) |  | HudPage — Simulated RMS expander |
| Swipe Wave — Cycle Duration (seconds) | setting | hand-authored | SwipeWaveAnimator.SwipeCycleSeconds (Deckle.Composition) | SwipeWaveAnimator field (2.4f) | • | HudPage — Swipe expander |
| Swipe Wave — Stagger Duration (seconds) | setting | hand-authored | SwipeWaveAnimator.SwipeStaggerSeconds | SwipeWaveAnimator field (0.1f) | • | HudPage — Swipe expander |
| Swipe Wave — Envelope Duration (seconds) | setting | hand-authored | SwipeWaveAnimator.SwipeEnvelopeSeconds | SwipeWaveAnimator field (1.4f) | • | HudPage — Swipe expander |
| Swipe Wave — Ramp Fraction | setting | hand-authored | SwipeWaveAnimator.SwipeRampFraction | SwipeWaveAnimator field (0.4f) | • | HudPage — Swipe expander |
| Swipe Wave — Ease P1 (X, Y) | setting | hand-authored | SwipeWaveAnimator.SwipeEaseP1 | SwipeWaveAnimator field (0.4f, 0f) | • | HudPage — Swipe expander |
| Swipe Wave — Ease P2 (X, Y) | setting | hand-authored | SwipeWaveAnimator.SwipeEaseP2 | SwipeWaveAnimator field (0.6f, 1f) | • | HudPage — Swipe expander |
| Audio Mapping — EMA Alpha | setting | hand-authored | AudioLevelMapper.EmaAlpha (Deckle.Audio) | AudioLevelMapper field (0.25f) | • | HudPage — Audio Mapping expander |
| Audio Mapping — Min dBFS | setting | hand-authored | AudioLevelMapper.MinDbfs | AudioLevelMapper field (-55f) | • | HudPage — Audio Mapping expander |
| Audio Mapping — Max dBFS | setting | hand-authored | AudioLevelMapper.MaxDbfs | AudioLevelMapper field (-32f) | • | HudPage — Audio Mapping expander |
| Audio Mapping — Curve Exponent | setting | hand-authored | AudioLevelMapper.DbfsCurveExponent | AudioLevelMapper field (1.0f) | • | HudPage — Audio Mapping expander |
| HUD Geometry — Inset (DIP) | setting | hand-authored | HudComposition.InsetDip | HudComposition field (-2f) | • | HudPage — Geometry expander |
| Ambient — Exposure (EV) | setting | migrated | AmbientSettings.ExposureEv (AmbientSettingsService persistence) | AmbientSettings initializer (0.0) |  | AmbientPage — HDR Tuning card |
| Ambient — Saturation Boost | setting | migrated | AmbientSettings.SaturationBoost | AmbientSettings initializer (1.0) |  | AmbientPage — HDR Tuning card |
| Ambient — Min Brightness | setting | migrated | AmbientSettings.MinBrightness | AmbientSettings initializer (180) |  | AmbientPage — HDR Tuning card |
| Ambient — Brightness Curve Type | setting | migrated | AmbientSettings.BrightnessCurveType | AmbientSettings initializer (BrightnessCurveType.Gamma) |  | AmbientPage — HDR Tuning card |
| Ambient — Brightness Curve Param (Gamma) | setting | migrated | AmbientSettings.BrightnessCurveParam | AmbientSettings initializer (1.8) |  | AmbientPage — HDR Tuning card |
| Ambient — Brightness Curve Param (S-Curve Steepness) | setting | migrated | AmbientSettings.BrightnessCurveSCurveSteepness | AmbientSettings initializer (2.0) |  | AmbientPage — HDR Tuning card |
| Ambient — Change Threshold | setting | migrated | AmbientSettings.ChangeThreshold | AmbientSettings initializer (6) |  | AmbientPage — HDR Tuning card |
| Ambient — Smoothing Alpha (EMA) | setting | migrated | AmbientSettings.SmoothingAlpha | AmbientSettings initializer (0.30) |  | AmbientPage — HDR Tuning card |
| Ambient — Border Mode (Zone Sampling Scale) | setting | migrated | AmbientSettings.BorderMode | AmbientSettings initializer (BorderThicknessMode.Share) |  | AmbientPage — Zone Sampling card |
| Ambient — Border Depth (as fraction/percentage) | setting | migrated | AmbientSettings.BorderDepth | AmbientSettings initializer (0.33) |  | AmbientPage — Zone Sampling card |
| Ambient — Border Cells (sampler grid count) | setting | migrated | AmbientSettings.BorderCells | AmbientSettings initializer (8) |  | AmbientPage — Zone Sampling card |
| Ambient — Mode (Preset) | setting | migrated | AmbientSettings.Mode | AmbientSettings initializer (AmbientMode.Game) |  | AmbientPage — HDR Tuning card |
| Ambient — Use Multi-Light (pipeline mode) | setting | migrated | AmbientSettings.UseMultiLight | AmbientSettings initializer (false) |  | AmbientPage — Pipeline card |
| Ambient — Enabled (master toggle) | setting | migrated | AmbientSettings.Enabled | AmbientSettings initializer (false) |  | AmbientPage — Pipeline card |
| Ambient — Bridge IP | setting | migrated | AmbientSettings.HueBridgeIp | AmbientSettings initializer (null) |  | AmbientPage — Hue card |
| Ambient — Bridge ID | setting | migrated | AmbientSettings.HueBridgeId | AmbientSettings initializer (null) |  | AmbientPage — Hue card (internal, no dedicated UI control) |
| Ambient — Hue API Username | setting | migrated | AmbientSettings.HueUsername | AmbientSettings initializer (null) |  | AmbientPage — Hue card (internal, no dedicated UI control) |
| Ambient — Last Hue Group ID | setting | migrated | AmbientSettings.HueLastGroupId | AmbientSettings initializer (null) |  | AmbientPage — Hue card (internal state, no direct UI) |
| Ambient — Selected Monitor Device Name | setting | migrated | AmbientSettings.SelectedMonitorDeviceName | AmbientSettings initializer (null, primary monitor default) | • | AmbientPage (J9 scaffolding, no UI yet) |
| Ambient — Light Zones (per-light assignments) | setting | migrated | AmbientSettings.LightZones (Dictionary<string, LightZone>) | AmbientSettings initializer (empty Dictionary) |  | AmbientPage — Light Zones card (J4, zone assignment UI) |
| Ambient — Light Brightness (per-light dimmer) | setting | migrated | AmbientSettings.LightBrightness (Dictionary<string, double>) | AmbientSettings initializer (empty Dictionary, default 1.0 per light) |  | AmbientPage — Light Zones card (J4, per-light slider) |

**Gestes destructifs :** Reset all (HUD Playground) _(none)_ · Reset all (Ambient Playground) _(none)_ · Reset HDR section (Ambient Playground) _(none)_ · Reset Zone Sampling section (Ambient Playground) _(none)_

---

# Synthèse

**Total réglages persistés : 132**

## Nouvelles familles requises

### Text
The SettingKind enum comment itself names Text as the next needed kind, and several flat string settings are hand-authored purely because no Text factory + composer case exists yet. These are plain get/set over a string with no runtime dependency — exactly the descriptor shape, blocked only by the missing kind. Add the enum value + Setting.Text factory + SettingsComposer case together, never speculatively.

- Forme : TextArgs(string? Placeholder = null, bool Multiline = false, int? MaxLength = null) — a single- or multi-line string editor; SetValue/GetValue over Func<string>/Action<string>, mirroring Path's string currency but with no picker. Multiline=true gives the InitialPrompt/SuppressRegex box (AcceptsReturn, MinHeight/MaxHeight), Multiline=false the one-line endpoint/IP field.
- Réclamé par :
  - LlmSettings.OllamaEndpoint (TextBox, http://localhost:11434/api/generate)
  - EngineSettings.InitialPrompt (multiline TextBox, hand-authored in WhisperPage)
  - OutputFilterSettings.SuppressRegex (single-line regex TextBox)
  - AmbientSettings.HueBridgeIp (IP TextBox) — only the manual-entry field, NOT the pairing orchestration

### Choice radio variant (ChoiceArgs.AsRadioButtons flag, not a separate kind)
Choice already exists and renders as ComboBox; the reports show several small fixed-set choices deliberately use RadioButtons for prominence over a dropdown — that is a presentation variant of the SAME kind, not a new value type. It is a real requirement (concrete settings render as radios today) but should reuse the Choice machinery, not introduce a parallel kind, to keep one case per value family. Note AudioCorpusContent/RuleMetric also drive cross-visibility of sibling panels, so they only become composable once that gating is expressible (see gaps).

- Forme : Extend the existing generic Choice<T>: add an optional bool flag to ChoiceArgs (e.g. ChoiceArgs(IReadOnlyList<ChoiceOption> Options, bool Radio = false)) or a sibling Setting.Radio<T> factory with the identical signature as Setting.Choice<T>. SettingsComposer's existing Choice case branches on the flag to emit RadioButtons instead of ComboBox. No new value-type currency — same boxed T as Choice.
- Réclamé par :
  - TelemetrySettings.AudioCorpusContent (RadioButtons, MatchTranscription/AlwaysRaw — 2 options, prominence)
  - LlmSettings.RuleMetric (RadioButtons, Word count / Duration — drives panel visibility)
  - AmbientSettings.Mode (Game/Movie/Ambient/Custom — small fixed set, currently hand-authored)
  - AmbientSettings.BorderMode (Share/Cells — RadioButtons in Playground)
  - AmbientSettings.BrightnessCurveType (Linear/Gamma/SCurve/Logarithmic)

## Confirmations (gestes destructifs)

| Geste | État actuel | Action | Emplacement |
|---|---|---|---|
| Restore Backup (restore settings.json from snapshot, overwrites live config) | service | Already routed through ConfirmationService.RequestAsync with IsDestructive=true — the reference implementation. No change; use it as the template for the others. | GeneralPage.xaml.cs:217 |
| Reset Appearance section (General) | adhoc-dialog | Replace the inline/ad-hoc reset with ConfirmationService.RequestAsync(root, new ConfirmationRequest(Title, Body, ResetVerb, IsDestructive:true)); gate the section ResetAll on confirmation, caller supplies its own Loc keys. | GeneralPage.xaml.cs:130 |
| Reset Behaviour section (General — Overlay group + Auto-paste) | adhoc-dialog | Route through ConfirmationService with IsDestructive:true; one ConfirmationRequest per section reset, Cancel owned by the service. | GeneralPage.xaml.cs:137 |
| Reset Startup section (General) | adhoc-dialog | Route through ConfirmationService with IsDestructive:true (the reset also writes HKCU via the autostart setter). | GeneralPage.xaml.cs:144 |
| Reset Recording section (Preprocessing + Voice Level + mic device) | none | Add ConfirmationService.RequestAsync gate before ResetAll(); currently fires with no dialog. | RecordingPage.xaml.cs:168 |
| Reset Logging section (Diagnostics) | none | Add ConfirmationService gate; currently resets+saves immediately with no dialog. | DiagnosticsPage.xaml.cs:176 (ResetLogging_Click) |
| Reset Telemetry section (Diagnostics — clears toggles + storage dir) | none | Add ConfirmationService gate, IsDestructive:true (clears consent state). | DiagnosticsPage.xaml.cs:171 (ResetTelemetry_Click) |
| Reset all Whisper settings | adhoc-dialog | Replace the local ContentDialog with ConfirmationService.RequestAsync, IsDestructive:true, passing the existing Settings_ResetWhisperDialog_* Loc strings as the request copy. | WhisperPage.xaml.cs:511 (ResetAll_Click) |
| Per-card / per-setting reset (Whisper hover-reveal, composed per-row reset) | none | Leave unconfirmed by design — a single-value revert is low-stakes and reversible by re-editing; ConfirmationService is for section/global/irreversible gestures. Confirm this exclusion with the maintainer. | WhisperPage.xaml.cs:411-462 |
| Reset all LLM settings | adhoc-dialog | Route the existing ContentDialog through ConfirmationService, IsDestructive:true. | LlmPage.xaml.cs:185 (ResetAll_Click) |
| Reset LLM Profiles section (replaces custom profiles with 3 defaults) | adhoc-dialog | Route through ConfirmationService, IsDestructive:true (custom profiles lost). | LlmProfilesSection.xaml.cs:225 |
| Reset LLM Shortcuts section | adhoc-dialog | Route through ConfirmationService (clears slot assignments). | LlmShortcutSlotsSection.xaml.cs:108 |
| Reset LLM General section | adhoc-dialog | Route through ConfirmationService. | LlmGeneralSection.xaml.cs:72 |
| Delete LLM Profile | adhoc-dialog | Route through ConfirmationService, IsDestructive:true. | LlmProfilesSection.xaml.cs:74 (DeleteProfile_Click) |
| Delete model from Ollama (DeleteModelAsync — real external delete) | adhoc-dialog | Route through ConfirmationService, IsDestructive:true — this deletes a model on disk via Ollama, the most irreversible of the lot. | LlmModelsSection.xaml.cs:112 |
| Forget Hue bridge pairing (clears IP/Id/Username) | adhoc-dialog | Route the existing ContentDialog through ConfirmationService, IsDestructive:true. | AmbientPage.xaml.cs:417 (OnHueForgetClick) |
| Re-pair Hue bridge (silently overwrites pairing state) | none | Add a ConfirmationService gate only when an existing pairing would be overwritten; fresh pairing needs none. | AmbientPage/Playground Pair flow |
| Forget Autocorrect app (per-app Forget button) | none | Low-stakes (re-enrollment re-prompts); leave unconfirmed or add a light gate — maintainer call. Flag explicitly. | AutocorrectAppRow.cs:54 / AutocorrectViewModel.OnRowForgotten |
| Playground Reset-all / Reset-section (HUD + Ambient HDR/Zone) | none | Playground is a tuning surface, not user Settings; resets are memory-only (HUD) or live-save tuning. Leave unconfirmed — out of the ConfirmationService remit. Flag so the maintainer confirms Playground is excluded. | HudPage OnResetAllClick; AmbientPage AmbientResetDefaultsButton + section TuningCards |

## À migrer tout de suite

| Réglage | Famille | Module |
|---|---|---|
| PathsSettings.BackupDirectory | Path | Deckle.Settings (General) |
| TelemetrySettings.StorageDirectory | Path | Deckle.Diagnostics.Telemetry (Diagnostics page) |
| TranscriptionSettings.ModelsDirectory | Path | Deckle.Transcription (WhisperPage) |
| TrackpadSettings.Enabled | Toggle | Deckle.Input.Trackpad (TrackpadPage) |
| TrackpadSettings.DragSpeed | Slider | Deckle.Input.Trackpad (TrackpadPage) |
| AutocorrectSettings.Enabled (master toggle only — NOT the per-app Apps map) | Toggle | Deckle.Autocorrect (AutocorrectPage) |
| LlmSettings.Enabled | Toggle | Deckle.Llm.Rewrite (LlmGeneralSection) |
| EngineSettings.UseGpu | Toggle | Deckle.Transcription (WhisperPage) — composes as Toggle; the restart-footer side effect rides the VM setter like the theme/autostart effects |
| OutputFilterSettings.SuppressNonSpeechTokens | Toggle | Deckle.Transcription (WhisperPage) |
| OutputFilterSettings.SuppressBlank | Toggle | Deckle.Transcription (WhisperPage) |
| ContextSettings.UseContext | Toggle | Deckle.Transcription (WhisperPage) |
| DecodingSettings.UseBeamSearch | Toggle | Deckle.Transcription (WhisperPage; surface it while migrating — currently runtime-only) |
| DecodingSettings.Temperature | Slider | Deckle.Transcription (WhisperPage) |
| DecodingSettings.TemperatureIncrement | Slider | Deckle.Transcription (WhisperPage; InfoBar-warning-when-0 chrome would be lost — see gaps) |
| ConfidenceSettings.EntropyThreshold | Slider | Deckle.Transcription (WhisperPage) |
| ConfidenceSettings.LogprobThreshold | Slider | Deckle.Transcription (WhisperPage) |
| ConfidenceSettings.NoSpeechThreshold | Slider | Deckle.Transcription (WhisperPage) |
| ContextSettings.MaxTokens | Number | Deckle.Transcription (WhisperPage) |
| DecodingSettings.BeamSize | Number | Deckle.Transcription (WhisperPage; gate visibility on UseBeamSearch via VisibleWhen) |
| CaptureSettings.MaxRecordingDurationSeconds | Number | Deckle.Audio (RecordingPage; currently persisted but unexposed — expose as Number while migrating) |

## À marquer bespoke (reste à la main)

- **CaptureSettings.AudioInputDeviceId — microphone ComboBox** — Runtime waveIn hardware enumeration (waveInGetNumDevs/waveInGetDevCapsW); options are not a static set, populated in code-behind. Not a flat get/set choice.
- **EngineSettings.Model — Whisper model AutoSuggestBox + restart footer** — Dynamic model discovery from disk .bin files, substring filtering, revert-on-invalid, plus a restart-required footer with discard flow. Bespoke chrome the descriptor model cannot carry.
- **EngineSettings.Language — editable ComboBox** — Editable combo accepting free-text custom language codes beyond the predefined list, with LostFocus revert validation. Not a closed Choice set.
- **LlmSettings.Profiles — rewrite profile list editor** — Dynamic multi-field list (Name/Model/SystemPrompt/Temperature/NumCtxK/TopP/RepeatPenalty per profile) in an ItemsRepeater with add/edit/delete and stable-Id reconciliation. Irreducible by decision.
- **LlmSettings.AutoRewriteRules / AutoRewriteRulesByWords — legacy storage only** — Kept deserializable for lossless compatibility with existing settings files; no runtime or UI consumer remains.
- **LlmSettings.PrimaryRewriteProfile / SecondaryRewriteProfile — slot ComboBoxes** — Choice list is the runtime Profiles collection (not static), resolved by stable Id with legacy-Name fallback. Runtime-dependent options.
- **AutocorrectSettings.Apps — per-app decision map** — Runtime-enumerated process list, on-the-fly enrollment, per-row toggle + Forget action over an ObservableCollection. Dynamic list, not a flat value.
- **AmbientSettings Hue pairing (HueBridgeId, HueUsername, HueLastGroupId)** — Populated by the link-button pairing dance / device enumeration, not user-edited; HueUsername is a sensitive issued credential never shown. Multi-step orchestration + secret, not a knob. (HueBridgeIp manual-entry field is the only Text-composable part.)
- **AmbientSettings.LightZones / LightBrightness — per-light grids** — Per-light zone assignment and brightness multiplier keyed by runtime-enumerated Hue light ids, with cross-light consistency; a dynamic grid of dropdowns/sliders, not flat values.
- **TelemetrySettings.CorpusEnabled + RecordAudioCorpus + AudioCorpusContent — nested corpus expander** — Multi-level SettingsExpander nesting with child IsEnabled bound to the master, code-behind consent dialogs, and a RadioButtons choice nested under a toggle — cross-gated reactive visibility the flat descriptor model (no folds-within-folds) cannot express today.
- **TelemetrySettings.AutocorrectDecisions + AutocorrectText — nested autocorrect-consent expander** — Same nested-expander + consent-dialog + cross-gated child IsEnabled shape as the corpus block.
- **All Deckle.Playground tuning surfaces (HudPage TuningModel ~60 params; AmbientPage HDR/Zone/Pipeline)** — Playground is a live-tuning audition surface, not user Settings (per AGENTS.md); HUD params are memory-only with no settings store, sliders built programmatically. Out of the composer's Settings remit by decision.

## Lacunes / à recouper

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
