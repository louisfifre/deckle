---
name: claude-deckle-settings
description: "Doctrine for Deckle.Settings, the settings UI shell and per-module persistence (SettingsHost). Read before touching the Settings window, navigation, any settings page, the FolderPickerCard, or the per-module persistence layout."
type: agent-instructions
module: Deckle.Settings
---

# CLAUDE.md — Deckle.Settings

The app's Settings UI shell. Hosts the `SettingsWindow` (adaptive Auto NavigationView + page Frame), the owned pages (`GeneralPage`, `RecordingPage`, `DiagnosticsPage`), the consent dialogs (corpus logging, paste opt-in, autorewrite rules), the persistence root (`SettingsService` for non-modular settings), and the `SettingsHost` delegate registry that business modules consume to invoke shell-side actions (theme broadcast, level window propagation, restart, parent-window access for cross-module dialogs, opening the first-run wizard).

Modular pages (`WhisperPage` in `Deckle.Transcription`, `LlmPage` in `Deckle.Llm.Rewrite`, and `AmbientPage` in `Deckle.Lighting.Ambient`) do not live here — they are owned by their respective module and resolved via `Type.GetType(tag)` from the `NavigationViewItem`'s `Tag` (e.g. `Tag="Deckle.Transcription.WhisperPage, Deckle.Transcription"`).

**Settings modularity doctrine.** The Settings page that configures a domain lives in the module that owns that domain, and so does its persistence service. This is the rule for any new Settings page — it is born in the domain's module, never in this shell. The shell aggregates dynamically, it does not host. `RecordingPage` and `DiagnosticsPage` are today historical residue still carried here; their migration toward `Deckle.Audio` and `Deckle.Diagnostics.Logging` is planned under the codename Move H (see [docs/reference/reference--cartographie-modules--1.1.md](../../docs/reference/reference--cartographie-modules--1.1.md)).

## TitleBar and backdrop

Native `Microsoft.UI.Xaml.Controls.TitleBar` (WindowsAppSDK 1.8), **Standard** caption buttons (`AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard`). App icon via named `ImageIconSource`. `ExtendsContentIntoTitleBar=true` + `SetTitleBar(AppTitleBar)`. Caption button colors set manually by `UpdateCaptionButtonColors` with re-binding on `ActualThemeChanged` to follow the live theme (transparent backgrounds to let Mica show through, foreground adapted light/dark). `MicaBackdrop`. Classic `OverlappedPresenter` (min/max/resize). Initial resize 960×1440. Close is real destruction: the host restores and saves native Win32 `WINDOWPLACEMENT` on `Closing`, then recreates the window lazily from the tray or `--settings`.

## Adaptive NavigationView — PaneDisplayMode Auto

No custom code-behind for the breakpoints. `PaneDisplayMode="Auto"` (WinUI default) handles the switch between the three modes on its own: **Left** ≥ 1008 dip, **LeftCompact** 641–1007, **LeftMinimal** ≤ 640. `PreferredMinimumWidth=320` on the presenter exposes the LeftMinimal mode. The `DisplayModeChanged` handler manages Frame padding (`+48 px top` in Minimal mode so the hamburger isn't overlapped, Windows Terminal Settings pattern) and normalizes `IsPaneOpen`: open only in Expanded mode, closed in Compact/Minimal, so restored window placement cannot leave a visually collapsed pane reserving expanded width.

Content: `NavigationView.MenuItems` = General → Recording → Transcription → Rewriting → Diagnostics. `FooterMenuItems` = Logs (`SelectsOnInvoked=False`, click via `ItemInvoked` which delegates to `SettingsHost.OpenLogWindow` to open the shared `LogWindow` — Logs is not a nav page, it's an action). Before the 2026-05-04 split there were only 3 pages (General concentrated Recording and Diagnostics); the separation pulled General down from 28 settings / 7 sections to 6 coherent sections and created two dedicated pages for the distinct functional surfaces.

## Navigation Frame + Page

`<Frame x:Name="PageFrame" />` in the NavigationView's content slot. Navigation via `Type.GetType(tag)` (pattern from the official Microsoft Learn sample). `CurrentSourcePageType != pageType` guard against redundant re-Navigate calls. All pages set to `NavigationCacheMode.Required` to preserve state between visits. `_initializing` guard around code-behind sync (combos, folder pickers) — prevents stray writes during the initial `Load()`; the flag is released at `DispatcherQueuePriority.Low` post-layout to let TwoWay bindings that apply their initial value after the ctor pass through.

## Non-negotiable patterns

**Auto-save everywhere.** No Settings page has a Save or Cancel button. Each control propagates its value to the ViewModel on every change, the ViewModel pushes to the corresponding service which serializes immediately (light debounce via `JsonSettingsStore`). Consequence: no "dirty" model, no "unsaved changes" prompt on close, no Cancel button. Consistent with the Windows 11 Settings pattern.

**SettingsCard and SettingsExpander.** All setting controls are wrapped in `SettingsCard` (simple toggle, slider, ComboBox) or `SettingsExpander` (group of related settings or editable list). NuGet package `CommunityToolkit.WinUI.Controls.SettingsControls`. `SettingsCardSpacing=4` resource set globally, `SettingsSectionHeaderTextBlockStyle` style (`BodyStrongTextBlockStyle` + `Margin 1,30,0,6`), `StackPanel MaxWidth=1000` inside a `Grid` wrapper (workaround for bug [microsoft-ui-xaml#3842](https://github.com/microsoft/microsoft-ui-xaml/issues/3842)). No custom `StackPanel` or `Grid` wrapping a setting control at the root of a page.

**H1 header per page.** Each page begins with a `TextBlock` styled `TitleLargeTextBlockStyle` that announces the section name. No sticky scroll header, no sub-tab inside a page, no breadcrumb. The hierarchy stays flat across two levels: `NavigationViewItem` → page.

## FolderPickerCard — single pattern for paths

`UserControl` reused wherever a folder is exposed (Backup location in General, Telemetry storage in Diagnostics, Models directory in Whisper, editable variant). Before the May 2026 overhaul, three divergent implementations coexisted — text labels "Set / Change folder / Pick a folder" depending on the location, icons or no icons, editable TextBox or not.

Canonical layout: read-only `TextBlock` styled `CaptionTextBlockStyle` that displays the path full-width under the description (not squeezed against the buttons), **Set** + **Open** buttons on the right **text only** (no icons — decision made 2026-05-04), `IsTextSelectionEnabled=True` on the path to allow manual copy-paste.

Picker API: `Microsoft.Windows.Storage.Pickers.FolderPicker(WindowId)` — the new API that takes a `WindowId` in the constructor, not the old UWP `Windows.Storage.Pickers.FolderPicker` which requires `WinRT.Interop.InitializeWithWindow` and breaks under elevation. The `Window` resolution goes through `SettingsHost.GetSettingsWindow?.Invoke()` — the module does not reference the window directly.

Path resolution: the card reads `Path` (TwoWay DependencyProperty). If empty, it displays `DefaultPath` as a transparent placeholder — does not store the default in the setting, preserving the "empty = system picks the default" semantics.

Editable variant: `FolderPickerEditableCard` adds an editable `TextBox` and a `RightContent` slot to host a Reset button. Used only for Models directory. Realistic case: cloning a models folder from another machine and typing the resulting path.

Important: the card itself is a minimal `UserControl`, **not** a `SettingsCard` wrapper. It's the consumer that places it inside a `<controls:SettingsCard ContentAlignment="Vertical">`. This allows reusing the card inside `SettingsExpander.Items`, which rejects UserControls that themselves wrap a SettingsCard.

## Parent SettingsExpander pattern for slider groups

When several related sliders each have 3–5 lines of description (Decoding with Temperature + Fallback step, Confidence with Entropy + Logprob + No-speech), a horizontal layout squeezes the slider to `MinWidth=180` and truncates the description. Retained pattern: parent `SettingsExpander` (header + icon) + child sliders as `SettingsCard ContentAlignment="Vertical"`. The description takes the full width, the slider full-width below. Staged disclosure (NN/G): sliders hidden behind the expander, visible only when the user looks for them. Glyphs by convention: `` (Tuner) for Decoding, `` (gauge) for Confidence. Children without `HeaderIcon` — the visual identity is carried by the parent. An `InfoBar` that depends on a slider in the group (e.g. `TemperatureIncrementWarning`) stays **outside** the expander to remain visible when the group is collapsed.

## GeneralPage

Shell level and global configuration. Auto-save via `SettingsService`. Six sections in order: **Hotkeys** (3 read-only display, primary `` Win + ` ``, primary rewrite `` Shift + Win + ` ``, secondary rewrite `` Ctrl + Win + ` ``), **Appearance** (ComboBox System / Light / Dark, applied live to all windows via `SettingsHost.ApplyTheme`), **Behaviour** (auto-paste after transcription + overlay HUD master toggle / fade on proximity / animations / screen position — migrated from Recording on 2026-05-04 because these settings describe what Deckle does for the user, not the capture pipeline), **Startup** (HKCU autostart managed by `AutostartService`, outside `AppSettings` + warmup on launch), **Backup & restore** (`SettingsExpander` PowerToys-style with `SettingsBackupService` point-in-time snapshot `settings-YYYYMMDD-HHmmss.json` under `<ConfigDirectory>/backups/`, restore via atomic swap, `BackupDirectory` configurable via `FolderPickerCard` to point at OneDrive/Drive), **Application data** (data folder display + Open in Explorer + Re-run setup). Reset `HyperlinkButton` per section (Appearance, Behaviour, Startup) — Win11 Settings pattern, restores the defaults of the section alone, not the whole page.

## RecordingPage

Page extracted from General on 2026-05-04. Concentrates everything that strictly belongs to the audio capture pipeline. **Microphone**: `ComboBox` Audio input device, Win32 `waveIn` enumeration, `AudioInputDeviceId` (`-1 = WAVE_MAPPER`) with "System default" at index 0. **Voice level window**: master `SettingsExpander` (Auto-calibration toggle in header) + 3 child sliders (Floor `MinDbfs`, Ceiling `MaxDbfs`, Curve exponent). Drags push live into `AudioLevelMapper` via `SettingsHost.ApplyLevelWindow` — the HUD reflects the new curve at the next sub-window without restart. Persistence: `CaptureSettingsService` under `modules/audio/settings.json`.

## DiagnosticsPage

Page extracted from General on 2026-05-04. Internal vocabulary: *log* refers to real time (LogWindow), *telemetry* refers to what is persisted on disk (JSONL). "Diagnostics" is the umbrella. Structured to host future sections (real-time log settings: levels, filtering, LogWindow buffer capacity); today a single Telemetry section.

Telemetry, 5 opt-ins all off by default, in order: **Application log to disk** (toggle, persists eventing into `app.jsonl`; at the top of the section by design decision), **Microphone telemetry** (toggle + privacy consent dialog for the per-recording RMS summary, microphone glyph), **Latency telemetry** (toggle, per-run pipeline measurements), **Corpus** (master `SettingsExpander` Corpus toggle in header + consent, Audio corpus child with separate toggle + consent), **Storage folder** (`FolderPickerCard` pointing at the folder where `.jsonl` files are serialized).

Consent dialog pattern: re-entry guards (`_suppressMicrophoneToggled`, etc.) — a programmatic revert after Cancel does not re-open the dialog. Persistence: `TelemetrySettingsService` (`telemetry.json`).

## SettingsHost — shell-side delegate registry

`SettingsHost` is a static class of delegates that the app wires at boot and that Settings pages invoke for shell-side actions. The pattern spares `Deckle.Settings` from holding a reference on the host project while letting any Settings page call `ApplyTheme(string theme)`, `ApplyLevelWindow(LevelWindow lw)`, `RestartApp()`, `GetSettingsWindow()` (to pass a `WindowId` to the FolderPicker or a parent hwnd to dialogs), `OpenSetupWizard()`, `OpenLogWindow()`. The app sets these hooks in `App.OnLaunched` before the first Settings window instantiation.

This is an intentional pattern — the registry is not a disguised Service Locator. The delegates are nominal (one per capability), not a string dictionary. Adding a hook means adding a typed static field on `SettingsHost`, wiring it at boot, and calling it explicitly from the page that needs it via `SettingsHost.X?.Invoke(...)` — null-safe when the shell hasn't wired it (isolated module test, partial integration).

## Per-module persistence

Each module owns its settings file under `<UserDataRoot>/modules/<moduleId>/settings.json`. The services involved are `SettingsService` (shell, non-modular), `CaptureSettingsService` in `Deckle.Audio`, `TelemetrySettingsService` (Diagnostics), `TranscriptionSettingsService` in `Deckle.Transcription`, `LlmSettingsService` in `Deckle.Llm.Rewrite`, and `AmbientSettingsService` in `Deckle.Lighting.Ambient`. Each service exposes `Current` (POCO singleton), `Save()` (debounced ~300 ms), and a `Changed` event. Atomic write-then-swap.

| Service | File | Content |
|---|---|---|
| `SettingsService` | `settings.json` | Shell: Hotkeys, Theme, Behaviour (auto-paste + overlay), Startup, `Paths.BackupDirectory` |
| `CaptureSettingsService` | `modules/audio/settings.json` | Microphone, Voice level window |
| `TelemetrySettingsService` | `modules/telemetry/settings.json` | Diagnostics opt-ins + storage path |
| `TranscriptionSettingsService` | `modules/transcription/settings.json` | Transcription orchestrator + active backend settings |
| `LlmSettingsService` | `modules/llm/settings.json` | Ollama + profiles + auto-rewrite rules + shortcuts |

The migration from the old combined file toward the per-module layout lives in `SettingsBootstrap.MigrateLegacyToPerModule()`. This method runs first thing in `App.OnLaunched`, before any service touches its file — otherwise the service would write defaults and the migration would see an already-existing target. It also handles the JSON section rename `recording → capture` (2026-05-02 legacy), the dispatch of JSON key `capture` to module id `audio` (2026-05-15 rename), and the folder migration `modules/capture/ → modules/audio/` for users already on per-module. Any future module migration follows this pattern via `MigrateModuleFolder` and the dispatch adjustment.

## Restart target

`SettingsHost.RestartApp?.Invoke(pageTag?)` relaunches the exe with `--settings [pageTag]`; `OnLaunched` detects the flag and reopens Settings on the named page. Used by pages that require an engine restart to apply a change (typically Whisper Model and UseGpu — `MarkRestartPending()` on the ViewModel pushes a "Restart required" `InfoBar` + footer with Restart now / Discard buttons).
