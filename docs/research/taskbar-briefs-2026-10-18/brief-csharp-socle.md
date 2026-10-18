# Brief: Deckle.Shell.Taskbar, C# socle, tier 1, relicensing, Windhawk exit

Written 2026-10-18 by a framing agent, read-only on worktree `D:\worktrees\deckle\context-compression-279c6a` (branch `deckle-shell-taskbar-socle-1d80e2`, v0.57.4). Nothing was built, nothing was written to the registry.

Markers: **[V]** verified, with file:line, URL or a read-only command run on Louis's machine on 2026-10-18; **[I]** inferred, not verified; **[C]** community source, not Microsoft, not verified on the machine.

---

## (a) Findings

### A1. The taskbar cover module, as it stands

- [V] Settings record `TaskbarCoverSettings` has two values, `Enabled` (default false) and `Approach` (`ApproachDistance` Near/Standard/Far, default Standard). `src/Deckle.Shell.TaskbarCover/TaskbarCoverSettings.cs:4-18`.
- [V] `TaskbarCoverSettingsService` is a lazy singleton over `JsonSettingsStore<T>`, camelCase JSON, path `AppPaths.UserDataRoot\modules\taskbar-cover\settings.json`, mutex `{AppFolderName}-Settings-TaskbarCover-Save`, exposes `Current`, `Changed`, `Save/Flush/Reload/Replace`. `TaskbarCoverSettingsService.cs:6-48`.
- [V] Descriptor `TaskbarCoverSettingsModule.Describe(order)`: `Id="taskbar-cover"`, `PageTag="Deckle.Shell.TaskbarCover.TaskbarCoverPage, Deckle.Shell.TaskbarCover"`, `OwningAssembly`, `Glyph=Glyphs.Taskbar`, no `ParentId`. `TaskbarCoverSettingsModule.cs:7-14`.
- [V] Search entries: two, label keys `TaskbarCoverPage_Group` and `TaskbarCoverPage_ApproachCard`. `SettingsSearch.cs:7-19`.
- [V] Page pattern: `catalog:SettingsPageHeader` (breadcrumb, description, Reset all), a `SwitchHost` StackPanel for the module switch alone, then a `catalog:SettingsSection` hidden while off; each host is filled by a `SettingsComposer` from a manifest; Reset all gated on `IsDirty()`; `NavigationCacheMode.Required` and `ViewModel.Load()` on every `OnNavigatedTo`. `Ui/TaskbarCoverPage.xaml:1-43`, `Ui/TaskbarCoverPage.xaml.cs:8-68`.
- [V] ViewModel: `[ObservableProperty]` per value, `_isSyncing` guard, every `On…Changed` writes `Current` then `Save()`; manifests built with `Setting.Toggle` / `Setting.Choice<T>`, defaults read from `new TaskbarCoverSettings().X`. `ViewModels/TaskbarCoverViewModel.cs:9-52`, `ViewModels/TaskbarCoverViewModel.Settings.cs:12-45`.
- [V] Strings: `Strings/en-US/Resources.resw`, keys `<LabelKey>.Header/.Description`, `<OptionKey>.Content`, `TaskbarCoverPageHeader.Title/.Description`, `SettingsModuleNavLabel`. Deckle ships en-US strings only (14 `Strings/en-US/Resources.resw`, no other culture; glob of `src/**/Strings/*/Resources.resw`).
- [V] Logging provider `[EventSource(Name = "Deckle-TaskbarCover")]` over `DeckleEventSource`, IDs sequential, never reused. `DeckleShellTaskbarCoverSource.cs:13-45`.
- [V] csproj: `UseWinUI`, `DefaultLanguage en-US`, `EnableMsixTooling`, packages WindowsAppSDK, SDK.BuildTools, CommunityToolkit.Mvvm, SettingsControls; references Core, Catalog, Diagnostics, Shell.Foreground; `NoWarn IDE1006`. `Deckle.Shell.TaskbarCover.csproj:1-31`.
- [V] App wiring: `App.TaskbarCover.cs` builds `TaskbarCoverHost`, subscribes `Changed → ReconcileTaskbarCover`, `ShutdownTaskbarCover` unhooks, flushes, disposes (`src/Deckle.App/App.TaskbarCover.cs:9-44`); `InitializeTaskbarCover()` at `App.Startup.Modules.cs:78`; registration at `App.Startup.Settings.cs:168` order 580; shutdown at `App.Lifetime.cs:70`; tray switch at `App.TrayToggles.cs:50-56`; hotkey at `App.Hotkeys.Actions.cs:25`; project reference at `src/Deckle.App/Deckle.App.csproj:90`; listed in `Deckle.Tests.sln:94,96`.
- [V] Tests: `tests/Deckle.Shell.TaskbarCover.Tests/` (CoverGeometry, CoverStacking, CoverSuppression, TaskbarPlacement, bug notes); csproj is a bare `ProjectReference` (the shared `tests/Directory.Build.props` brings the xUnit v3 stack); every test carries `[Trait("Category", …)]`. `CoverSuppressionTests.cs:19-60`, `tests/GLOSSARY.md`.

### A2. The sub-page mechanism

- [V] `SettingsModuleDescriptor.ParentId` nests a page under another module's `Id`; `PageTag` is `required`; a child renders without icon; a missing parent is skipped and traced. `src/Deckle.Catalog/SettingsModuleDescriptor.cs:29-62`, `src/Deckle.Settings/SettingsWindow.xaml.cs:216-223`.
- [V] **The first child turns the parent into a node and moves the parent's own page down as a child labelled "General"** (`SettingsNavGeneral/Content`), the parent stops navigating. `SettingsWindow.xaml.cs:225-237`. The doctrine says the same: "its pages at one level and the first named General" (`.agents/skills/deckle-settings-ux/SKILL.md:14`).
- [V] Breadcrumb: a child page shows `Parent › Child`; the parent's own page shows the module name alone. `SettingsWindow.xaml.cs:396-427`.
- [V] Registry sorts by `Tier` then `Order`; children are appended to their node in that order. `src/Deckle.Settings/SettingsModuleRegistry.cs:24-27`.
- [V] Whisper and Autocorrect: one module, one assembly, three descriptors (`Describe`, `Describe<Child>`), each child with `ParentId = ModuleId`, its own `LabelKey`, the parent's glyph; each registered separately so search resolves page coordinates per page. `src/Deckle.Transcription/WhisperSettingsModule.cs:26-61`, `src/Deckle.Autocorrect/AutocorrectSettingsModule.cs:29-63`, `App.Startup.Settings.cs:121-151`.
- [V] Nothing forces parent and child into the same assembly: `OwningAssembly` and `LabelKey` are per descriptor, and the rail resolves each label from its own assembly (`SettingsWindow.xaml.cs:239-243`). A cross-module child is therefore possible with no shell change. [I] It has never been done in the repo.
- **Consequence [V]:** with today's shell, "a Taskbar node with exactly two sub-pages, cover and customization" cannot be drawn. A node always carries its own page as "General". Either the customization page *is* the node's own page and shows as "General", or the shell changes. See decision B2 and question D1.

### A3. Per-setting status, the existing channel

- [V] Every `Setting.*` factory takes `advisory: Func<string?>`; the composer hangs a non-closable `InfoBar` (Severity Warning) under the card, open only when the function returns non-null, re-evaluated on every refresh, collapsed with the card. `src/Deckle.Catalog/Composer/Setting.cs:24-226`, `SettingsComposer.cs:154-239`; fold children get it too (`SettingsComposer.Folds.cs:344-373`). Used today by hotkey duplicates (`src/Deckle.Settings/ViewModels/GeneralViewModel.Settings.cs:95-103`) and Whisper (`WhisperViewModel.Settings.cs:264`).
- [V] `Setting.Toggle` and `Setting.Group` take `confirmOnEnable: Func<XamlRoot, Task<bool>>`, which holds the OFF→ON write until it resolves true. `Setting.cs:20-47, 257-282`.
- [V] `Setting.Mode<T>` exists for "a choice that decides which settings exist". `Setting.cs:305-341`.

### A4. Tier 1 facts

**Show all tray icons**
- [V] On the machine: `HKCU\Control Panel\NotifyIconSettings` has 39 subkeys; `IsPromoted=1` on 14, `=0` on 12, absent on 13. Value names seen on a subkey: `IsPromoted, IconGuid, ExecutablePath, Publisher, IconSnapshot` (names only, no data read).
- [V] The Windhawk mod `taskbar-notification-icons-show-all` (https://raw.githubusercontent.com/ramensoftware/windhawk-mods/main/mods/taskbar-notification-icons-show-all.wh.cpp, read 2026-10-18) **writes nothing in `showAll` mode**: it hooks `RegGetValueW` / `RegSetValueExW` in explorer to report `IsPromoted=1` and to swallow explorer's own writes. It never calls `RegNotifyChangeKeyValue`. It triggers explorer by writing then deleting a temporary REG_SZ value on every subkey, because "Explorer has a registry watcher for each notify icon item". At unload it does not restore anything. Only its `showNew` mode writes `IsPromoted=1` for entries with no value.
- Consequence [V+I]: once Windhawk is gone, the 25 icons at 0 or absent will drop into the overflow [I], since nothing in the registry says otherwise. A hook-free Deckle must **write** `IsPromoted=1` and keep re-writing it, which is different from what the mod does. Explorer picks the write up live through its per-item watcher [V per the mod comment, I on 26100.9711 without Windhawk].
- [I] Side effect of a write-based approach: when the user hides an icon from Windows Settings, explorer writes `IsPromoted=0` and Deckle's watcher writes it back to 1. That is the setting doing its job, but the native per-icon toggles in Windows Settings become ineffective while the Deckle setting is on.
- [V] `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotify\SystemTrayChevronVisibility` exists on the machine, value 1. [I] It backs the "Hidden icon menu" toggle; not in Louis's list, left out.

**Small taskbar**
- [V] On the machine, `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`: `TaskbarSize=0`, `TaskbarLocation=0`, `IconSizePreference=1`, `TaskbarSmallIcons=0`, `TaskbarSi` absent. OS: Windows 10 IoT Enterprise LTSC 2024 (product name string), EditionID `IoTEnterpriseS`, 24H2, build 26100.9711.
- [C] `TaskbarSize` is the value behind the new "Taskbar size: Default / Small" control: **0 = Default, 1 = Small** (elevenforum, kapilarya, search 2026-10-18). If that holds, **Louis's registry says Default, not Small**, and the small bar he sees comes from Windhawk's `taskbar-icon-size`, which forces `TaskbarSettings::Size` 0 → 1 inside explorer (research notes §3, "the mod forces TaskbarSettings::Size 0 → 1"). This contradicts the session memory "native left + Small". **To verify before writing any code** (C-task T0).
- [C] `IconSizePreference` backs "Show smaller taskbar buttons": 0 = Always, 1 = Never, 2 = When taskbar is full (community; one source differs). [V] Microsoft documents the same three choices only as a policy, `ConfigureTaskbarSmallButtonBehavior` (0 Always, 1 When full, 2 Never, 65535 not enforced), applicable to **Windows Insider Preview** only, registry key `Software\Policies\Microsoft\Windows\Explorer`. https://learn.microsoft.com/windows/client-management/mdm/policy-csp-start#configuretaskbarsmallbuttonbehavior . Note the policy numbering differs from the community one for the user value; do not borrow one for the other.
- [V] Microsoft Insider notes: "Taskbar size > Small" makes icons and the bar height smaller, and in build 26220.9022 "**The smaller taskbar is currently supported only for top and bottom positions.**" https://learn.microsoft.com/windows-insider/release-notes/beta/preview-build-26220-9022 . [I] On Louis's left-docked bar, `TaskbarSize=1` may be ignored, or honoured only because of the extra ViVeTool flags. The condensed vertical bar may rest on tier 3 alone.
- [V] `TaskbarSi` ignored since 22H2 Moment 2 (research notes §4). Not used.
- [I] Live pickup: Windows Settings applies the size live, so explorer listens; a Deckle write probably needs a `WM_SETTINGCHANGE` broadcast with `lParam = "TraySettings"` to be seen without restarting explorer. Unverified.

**Hide the Recommended section**
- [V] Policy `HideRecommendedSection`, scope **Device and User**, editions include **IoT Enterprise / IoT Enterprise LTSC**, Windows 11 22H2 (22621) and later, values 0 shown / 1 hidden, GP key `Software\Policies\Microsoft\Windows\Explorer`, value `HideRecommendedSection`, ADMX `StartMenu.admx`, location "Computer and User Configuration". https://learn.microsoft.com/windows/client-management/mdm/policy-csp-start#hiderecommendedsection . So HKCU is honoured.
- [V] **HKCU does not save us from elevation.** ACL of `HKCU\Software\Policies` and of `HKCU\Software\Policies\Microsoft\Windows\Explorer`: Louis = ReadKey only; Administrators and SYSTEM = FullControl (`Get-Acl`, 2026-10-18). Both policy keys exist and are empty on the machine. The current shell is not elevated (`IsInRole(Administrator)` = false) while Louis is in the Administrators group (split token).
- [V] Deckle's elevation precedent: `ElevatedStartupService` runs `schtasks.exe` with `UseShellExecute=true, Verb="runas"`, treats Win32 error 1223 (UAC declined) as a plain false, logs, never throws to the UI. `src/Deckle.Shell/ElevatedStartupService.cs:76-132`. Deckle can also already run elevated through that "Start elevated" task (`ElevatedStartupService.cs:10-14`).
- [V] Installer and setup do not elevate: no `runas`, `requireAdministrator` or `IsInRole` in `src/Deckle.Install`, `src/Deckle.Installer`, `src/Deckle.Setup`, `src/Deckle.App` (grep); the installer manifest has no `requestedExecutionLevel` (`src/Deckle.Installer/app.manifest`); the uninstall entry is written to HKCU (`src/Deckle.Install/UninstallEntry.cs:24-47`).
- [I] An elevated `reg.exe` started with `runas` by Louis runs under Louis's own admin token, so its HKCU is Louis's hive and the HKCU policy lands where it should. If a standard user ever types an admin's credentials, HKCU would be the admin's hive and the write would miss; HKLM would then be the only correct target.
- [I] Live pickup: Start probably reads the policy when `StartMenuExperienceHost` starts; expect a sign-out, or a restart of that process, before it shows. Unverified.
- [I] Survival: policy and user values under HKCU persist across cumulative updates. A feature update could drop `TaskbarSize` if its feature flags go away (they were set by KB5127648 and ViVeTool per memory), in which case the value stays but stops working.

### A5. Third-party licences (relicensing)

- [V] Licence mentions in the repo (Grep `\bMIT\b|[Ll]icen[cs]e|LICENSE|GPL`, excluding bin/obj and vendored skills):
  - `LICENSE:1-21` MIT, "Copyright (c) 2025-2026 Louis Fifre".
  - `README.md:105-107` "## License / MIT — see [LICENSE](LICENSE)."
  - `CONTRIBUTING.md:27` "distributed under Deckle's [MIT license](LICENSE)."
  - `NOTICE.md` (third-party list, lines 1-141).
  - `TREE.md:2265` lists `LICENSE` (auto-generated).
  - No licence in any csproj (`PackageLicenseExpression` absent; `Directory.Build.props` has none), none in `scripts/commands/release/publish-app.ps1` (notes come from `changelog.ps1`, lines 266-275, 403), none in the installer, the uninstall entry (only `Publisher`, `UninstallEntry.cs:35`), the setup wizard or any About page (no match under `src/` UI strings).
  - Not Deckle's: `.agents/skills/ux-designer/SKILL.md:4` (`license: MIT`) and `.agents/skills/winui-app/LICENSE.txt` (Apache-2.0) belong to vendored skills; leave them.
- [V] Package licences, read from the local NuGet cache nuspecs:
  - MIT: CommunityToolkit.Mvvm, CommunityToolkit.WinUI.Controls.SettingsControls, CommunityToolkit.WinUI.Controls.Sizers, Microsoft.ML.Tokenizers, System.Management, BouncyCastle.Cryptography (expression MIT), **Anthropic 12.57.0**, Microsoft.ML.OnnxRuntime.DirectML (LICENSE file = MIT), Microsoft.ML.OnnxRuntimeGenAI.DirectML (MIT).
  - Apache-2.0: ModelContextProtocol, ModelContextProtocol.AspNetCore, xunit.v3 (test only, not shipped).
  - JsonSchema.Net 9.4.0: `OSMFEULA.txt`; source is MIT, the fee applies only to the binary for revenue-generating users over US$10,000, and section 4 says the MIT licence governs and "imposes no additional restrictions". Compatible.
  - **Not MIT, contrary to NOTICE.md:43-45**: `Microsoft.WindowsAppSDK` ships under "MICROSOFT SOFTWARE LICENSE TERMS – MICROSOFT WINDOWS APP SDK" (`license.txt`); `Microsoft.Windows.SDK.BuildTools` points to `https://aka.ms/WinSDKLicenseURL`; `Microsoft.Graphics.Win2D` points to a Microsoft EULA URL. [I] DirectML.dll, pulled in transitively by the ONNX Runtime DirectML package (`microsoft.ai.directml` 1.15.x in the cache), is a Microsoft redistributable under its own terms.
  - NOTICE.md omits Anthropic, ModelContextProtocol (base), CommunityToolkit Sizers. It calls the MinGW runtime "public domain / various permissive", while `scripts/commands/release/publish-native-runtime.ps1:326-327` says "GPL-3 with runtime exception (libgcc, libstdc++) / MIT (libwinpthread)".
- [I] GPLv3 compatibility: MIT, BSD, Apache-2.0 are compatible with GPLv3. OFL 1.1 (the bundled font) is a separate asset, fine. CC BY-SA 4.0 data has a one-way compatibility declaration towards GPLv3 (Creative Commons, 2015) and is data, not code. **The Microsoft proprietary runtimes (Windows App SDK, Win2D package, DirectML) are not "System Libraries" in the GPLv3 sense with certainty**, so a GPLv3 Deckle distributed with them is cleanest with a section 7 additional permission from Louis (he is the sole author, he can grant it). Legal reading, not verified with a lawyer.

### A6. Windhawk on the machine

- [V] `D:\bin\scoop\apps\windhawk\current` is a junction to `1.7.3`; `windhawk.exe` running from that path (PID 4636 on 2026-10-18); data in `D:\bin\scoop\persist\windhawk\AppData`; eight mod INIs under `...\1.7.3\AppData\Engine\Mods`: start-menu-size, taskbar-clock-customization, taskbar-icon-size, taskbar-notification-icon-spacing, taskbar-notification-icons-show-all, taskbar-thumbnail-size, windows-11-start-menu-styler, windows-11-taskbar-styler.
- [V] `%LOCALAPPDATA%\Deckle\windhawk-backup` holds four files (names and sizes only): `taskbar-clock-customization.ini` (4,208 B, 2026-10-14), `taskbar-clock-customization.wh.cpp` (231,924 B, 2026-10-03), `taskbar-icon-size.ini` (574 B, 2026-10-17), `taskbar-icon-size.wh.cpp` (128,705 B, 2026-10-17).
- [V] That folder sits inside Deckle's default data root, `%LOCALAPPDATA%\Deckle` (`src/Deckle.Core/Paths/DefaultDataRoot.cs:22-31`). [I] A data reset or an uninstall that clears the data root would take the backups with it.
- [V] The styler and any XAML Diagnostics consumer exclude each other, "only one consumer at a time" (research notes §3, §4).

---

## (b) Design decisions recommended

**B1. Two sibling projects, the cover stays where it is.** New project `src/Deckle.Shell.Taskbar` holds the customization (settings, page, tier 1, the channel to the native DLL). `Deckle.Shell.TaskbarCover` stays a separate project, untouched except that its descriptor becomes a child of the node.
- Why: two responsibilities, two sentences. The cover draws Deckle's own window over the bar; the customization changes Windows' own bar. Different runtimes, different failure modes, different tests (deckle-modularite).
- Rejected: merging the cover into `Deckle.Shell.Taskbar`, which would give one module two responsibilities and move 25 files for a rail change.
- Rejected: `Deckle.Shell.TaskbarCover` referencing `Deckle.Shell.Taskbar` to read the parent id, which creates a dependency for a string.
- Naming note: `Deckle.Shell.Taskbar` was given in the brief. Its sentence is "applies the user's choices to the native Windows taskbar". Its namespace is broad next to `Deckle.Shell.TaskbarCover`; nomenclature asks that two capability names never overlap. Kept as decided; `Deckle.Shell.TaskbarCustomization` is the fallback if Louis finds the overlap confusing (question D5).

**B2. The composition root makes the cover a child.** In `App.Startup.Settings.cs`: `TaskbarCoverSettingsModule.Describe(order: 590) with { ParentId = Shell.Taskbar.TaskbarSettingsModule.ModuleId }`. The node is `TaskbarSettingsModule.Describe(order: 580)`.
- Why: the root already supplies `Order`; it alone sees both modules; the record supports `with`.
- Rejected: hard-coding `"taskbar"` inside the cover module, which couples it silently.

**B3. The node's own page is the customization page; how it is labelled is Louis's call (D1).** Two options, recommended order:
- **B3-a (recommended, small shell change):** add `string? NodePageLabelKey` to `SettingsModuleDescriptor`, "the label the module's own page takes under its node; General when null". `SettingsWindow.xaml.cs:230-234` reads it; the breadcrumb on that page shows `Taskbar › <label>` when it is set. The rail reads `Taskbar ▸ Customization, Taskbar cover`. This needs an amendment of `deckle-settings-ux` ("the first named General"), which Louis must approve before it is written.
- **B3-b (no code):** keep "General". The rail reads `Taskbar ▸ General, Taskbar cover`, and the customization name lives only in the master switch card label.
- Rejected: a page-less node (nullable `PageTag`). It touches the descriptor contract, search, breadcrumb and deep links for a gain B3-a already gives.

**B4. Settings record shape.** One record `TaskbarSettings`, master `Enabled` plus one property per user choice, each with Windows' behaviour as default. A choice that leaves Windows alone is the default value (`WindowsDefault` in enums, `false` in toggles). Tier is not stored in the record; it is code (B6).

| Property | Type, default | Control | Tier | Louis's value |
|---|---|---|---|---|
| `Enabled` | bool, false | Switch, first card alone | — | true |
| `CompactBar` | bool, false | Group (master) | 1 (`TaskbarSize`) + 3 (frame size) | true |
| `CompactBarWidth` | double px, 32 | Magnitude 24–48 px, child of `CompactBar` | 3 | 32 |
| `Background` | `TaskbarBackground` {WindowsDefault, Black, Transparent} | Choice (ComboBox) | 2 | Transparent |
| `ShowAllTrayIcons` | bool, false | Switch | 1 | true |
| `CompactTrayIcons` | bool, false | Switch | 2 (24 px, one row) | true |
| `QuickSettingsButtons` | `QuickSettingsStyle` {WindowsDefault, Blended} (+ Moved, D3) | Choice | 2 | to clarify |
| `Clock` | `ClockLayout` {WindowsDefault, SingleLine} | Mode fold | 2 | SingleLine |
| `ClockShowsMonth` | bool, false | Switch, child shown under SingleLine | 2 | false |
| `CustomThumbnails` | bool, false | Group | 3 | true |
| `ThumbnailWidth` | double px, 300 | Magnitude 160–480 px, child | 3 | 300 |
| `HideRecommended` | bool, false | Switch, `confirmOnEnable` = elevation | 1 (policy, elevated) | true |
| `CustomStartSize` | bool, false | Group | 2 | true |
| `StartWidth` / `StartHeight` | double px, 580 / 1080 | Magnitudes 400–1200 / 600–1600 px, children | 2 | 580 / 1080 |

- Why a width magnitude on the bar: Louis moved it four times (48 → 44 → 24 → 32, memory), so it earns a control. Why one thumbnail width: the mod's min 180×120 is an aspect bound the DLL can derive; one field covers the need (D4). Ranges are proposals; the grain follows the 1-2-5 rule from the range (controls-and-behaviour.md).
- Rejected: one bool per Windhawk mod (mirrors a mod catalogue Louis rejected); exposing icon size 16 (decided by `CompactBar`); search box size (not in Louis's list).

**B5. Persistence.** `AppPaths.UserDataRoot\modules\taskbar\settings.json`, mutex `{AppPaths.AppFolderName}-Settings-Taskbar-Save`, module `Id = "taskbar"`. A second file `modules\taskbar\restore.json` records the registry values Deckle replaced (B8).

**B6. Each setting knows its parts and their tiers, in code.** A static table `CustomizationCatalog` maps each `CustomizationPart` (e.g. `BarSize`, `BarFrameWidth`, `BarBackground`, `TrayPromotion`, `TrayIconWidth`, `QuickSettings`, `ClockFormat`, `ThumbnailSize`, `RecommendedSection`, `StartSize`) to a `CustomizationTier` (Registry, Xaml, Symbols) and to the settings property it serves. A setting with two parts (`CompactBar`) aggregates their status, worst first.

**B7. Status model and how the page shows it.**
- `CustomizationStatus` {Pending, Applied, NotAppliedOnThisBuild, Unsupported, PermissionDenied, Failed}. The three the DLL reports, plus three tier 1 needs (no permission, write error, not yet answered).
- A `CustomizationStatusBoard` (one per process) holds the latest status per part, raises `Changed`; the ViewModel raises `PropertyChanged` on it so the composer re-evaluates advisories.
- **Page rendering: the existing advisory channel only.** `advisory: () => StatusMessage(parts)` returns null for Applied and Pending, so a working page shows nothing extra. A part that failed shows one sentence under its card, for example "Windows changed this part of the taskbar in this build. Deckle can't apply it yet." A partly applied `CompactBar` says which half applied.
- **A setting whose engine is absent is hidden, not warned** (settings-ux: what does not apply is hidden). Until the native DLL ships, `IExplorerCustomization.IsAvailable` is false and every tier 2/3 card is hidden; v1 shows the switch, `CompactBar` (tier 1 part), `ShowAllTrayIcons`, `HideRecommended`.
- Rejected: a status column or icon per card (noise on a page that works), a page-level InfoBar listing failures (a second channel, the doctrine wants one).

**B8. What "off" means.** Turning a tier 1 setting off, or the master off, restores what Deckle replaced, from `restore.json` (prior value, or "absent", per value). For tray icons that is one entry per subkey Deckle changed. Shutting Deckle down restores nothing: registry choices persist while Deckle is closed, only the new-icon watcher stops.
- Why: the user expects "off" to give back their Windows, and the snapshot is a few values.
- Rejected: leaving values in place on off (looks like Deckle broke Windows Settings), restoring on exit (the bar would jump at every quit).

**B9. Elevation for `HideRecommended`.** If `Environment.IsPrivilegedProcess`, write HKCU directly. Otherwise run `reg.exe add "HKCU\Software\Policies\Microsoft\Windows\Explorer" /v HideRecommendedSection /t REG_DWORD /d 1 /f` with `Verb = "runas"`, the `ElevatedStartupService` pattern, from the toggle's `confirmOnEnable`, so the switch only turns on when the write succeeded. Turning it off runs `reg.exe delete … /v HideRecommendedSection /f` the same way; if declined, the value stays on and the advisory says Windows refused. HKCU rather than HKLM, because it is per-user and the policy supports user scope.
- Rejected: HKLM (affects every account, same UAC cost), writing through Task Scheduler (heavier), a Deckle elevated helper exe (new binary for two reg commands).

**B10. Tray watcher.** `RegNotifyChangeKeyValue` on `HKCU\Control Panel\NotifyIconSettings`, `bWatchSubtree = true`, filter `REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET`, asynchronous with an event, re-armed after each signal (the call fires once). On each signal, debounce 200 ms, then set `IsPromoted=1` only where it is not already 1, so Deckle's own write never loops. One dedicated background thread waiting on the event plus a stop event.
- Rejected: polling; reproducing the mod's hook (that is tier 2/3 territory and needs injection).

**B11. The DLL is a seam, not a dependency.** `IExplorerCustomization { bool IsAvailable; void Apply(ExplorerCustomizationDocument doc); event Action<IReadOnlyList<PartStatus>> StatusReported; }` with `UnavailableExplorerCustomization` as the v1 implementation. `ExplorerCustomizationDocument.From(TaskbarSettings)` is a pure function producing the settings document the DLL receives (tier 2/3 parts only, JSON-serializable). The transport (named pipe, ROT, file) belongs to the other agents' brief.

**B12. Windhawk.** The module never detects, stops, edits or uninstalls Windhawk, and never reads its folders or the backups. Louis uninstalls it before testing anything (B-risk: the mods hook the same registry reads and force the bar size, so a test with Windhawk running proves nothing). Whether the page should warn when another XAML Diagnostics consumer holds explorer belongs to the DLL's status report (`Unsupported`/`NotApplied` with a reason), not to C# heuristics.

**B13. Licence: GPL-3.0, plus a section 7 linking permission for Microsoft runtimes** (Windows App SDK, Win2D, DirectML, ONNX Runtime GenAI natives), and a corrected NOTICE.md. "Only" or "or later" is Louis's call (D6).

---

## (c) Implementation tasks

Order: T0 → T1 → (T2, T3, T4 in sequence, one writer per file set) → T5 → T6 → T7. T8 and T9 are independent of the code and can go first. One build at a time, `dotnet build /m:4`, Debug x64, no Deckle restart (AGENTS.md).

### T0. Verify the tier 1 registry facts on the machine (Louis, no code) — S
- Goal: settle which value makes the bar small, and whether it works on a left bar, before T3 is written.
- Steps for Louis: (1) quit Windhawk's tray (or uninstall it, T9) and restart explorer; (2) note the bar; (3) in Windows Settings > Personalization > Taskbar > Taskbar behaviors, switch the size control between its options and, after each, read `TaskbarSize` and `IconSizePreference` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (an agent can read them for him); (4) note whether Small changes the left bar at all.
- Done when: the brief's table is updated with the value that means Small on 26100.9711 and whether it affects a vertical bar.
- Risk: if Small is ignored on a left bar, `CompactBar` is tier 3 only and its tier 1 part is dropped.

### T1. Scaffold `Deckle.Shell.Taskbar` and the node — M
- Goal: an empty but registered module: node in the rail, its page with the master switch, cover nested under it.
- Files to create: `src/Deckle.Shell.Taskbar/Deckle.Shell.Taskbar.csproj` (copy of the cover's csproj minus `Deckle.Shell.Foreground`), `TaskbarSettings.cs`, `TaskbarBackground.cs`, `QuickSettingsStyle.cs`, `ClockLayout.cs`, `TaskbarSettingsService.cs`, `TaskbarSettingsModule.cs` (`public const string ModuleId = "taskbar"`, `PageTag`, `ResourceLibrary`), `SettingsSearch.cs`, `DeckleShellTaskbarSource.cs` (`[EventSource(Name = "Deckle-Taskbar")]`), `Ui/TaskbarPage.xaml(.cs)`, `ViewModels/TaskbarViewModel.cs`, `ViewModels/TaskbarViewModel.Settings.cs`, `Strings/en-US/Resources.resw`; `tests/Deckle.Shell.Taskbar.Tests/Deckle.Shell.Taskbar.Tests.csproj`.
- Files to touch: `src/Deckle.App/Deckle.App.csproj` (ProjectReference), `src/Deckle.App/App.Startup.Settings.cs` (register node at 580, cover at 590 `with { ParentId = … }`, comment), `Deckle.Tests.sln` (both projects), `src/Deckle.Catalog/Glyphs.cs` only if a new glyph is needed (memory: PUA edit trap, insert by script), `TREE.md` is regenerated by its own script, not by hand.
- Steps: (1) record with all B4 properties and defaults, comment per property; (2) service, copy of `TaskbarCoverSettingsService` with path `modules/taskbar/settings.json`; (3) descriptor with `Glyph = Glyphs.Taskbar`; (4) page = header + `SwitchHost` + one section per group (Taskbar, System tray, Clock, Start), all hidden while `Enabled` is false, same code-behind shape as `TaskbarCoverPage.xaml.cs`; (5) manifests: switch only in T1, the other cards land with their tasks; (6) Strings: `TaskbarPageHeader.Title/.Description`, `TaskbarPage_Group.Header`, section titles, `SettingsModuleNavLabel` = "Taskbar"; (7) register in the App.
- Done when: build passes; the rail shows the Taskbar node with General (or the B3-a label) and Taskbar cover; the cover page still works; the breadcrumb on the cover reads `Taskbar › Taskbar cover`; a descriptor unit test passes (T7).
- Risks: the cover's nav label "Taskbar cover" under a "Taskbar" node reads twice (D2); `x:Uid` one element type per Uid (deckle-interface).
- Louis verifies: the rail and both pages on screen.
- Depends on: D1 for the General label (T1 can land with B3-b and switch to B3-a later).

### T1b. (If Louis picks B3-a) Node page label override — S
- Files: `src/Deckle.Catalog/SettingsModuleDescriptor.cs` (new `NodePageLabelKey`, comment), `src/Deckle.Settings/SettingsWindow.xaml.cs:228-237` (use it), `ResolveBreadcrumb` lines 412-427 (a parent's own page with an override shows `Module › Label`), `src/Deckle.Settings/SettingsWindow.Search.cs` around 366 (search shows the override; check how it labels the General child), `TaskbarSettingsModule.Describe` sets it, `.resw` key `SettingsModuleNavLabel_Customization`.
- Prerequisite: Louis approves the text amending `.agents/skills/deckle-settings-ux/SKILL.md:14` before it is written (AGENTS.md hard rule).
- Done when: Whisper and Autocorrect still show "General"; Taskbar shows the chosen label; breadcrumb and search agree.

### T2. Status model, catalogue and DLL seam — M
- Files to create in `src/Deckle.Shell.Taskbar/Customization/` (same namespace `Deckle.Shell.Taskbar`): `CustomizationPart.cs`, `CustomizationTier.cs`, `CustomizationStatus.cs`, `PartStatus.cs` (record part + status + optional reason), `CustomizationCatalog.cs` (part → tier → setting), `CustomizationStatusBoard.cs`, `IExplorerCustomization.cs`, `UnavailableExplorerCustomization.cs`, `ExplorerCustomizationDocument.cs`, `TaskbarCustomizer.cs` (reconciles settings → tier 1 appliers + `IExplorerCustomization.Apply`, feeds the board).
- Files to touch: `ViewModels/TaskbarViewModel.Status.cs` (new partial: subscribes to the board on the UI thread, raises `PropertyChanged`, `StatusMessage(parts)` returning null or a `.resw` string), `Strings/en-US/Resources.resw` (one string per non-silent status).
- Steps: (1) enums and catalogue; (2) board with `Changed`; (3) document `From(settings)` returning only tier 2/3 parts that differ from Windows default; (4) customizer: `Reconcile(TaskbarSettings)` idempotent, `ReapplyAfterExplorerRestart()`, `Dispose()`; (5) VM advisories and `visibleWhen: () => explorer.IsAvailable` on tier 2/3 cards.
- Done when: unit tests for catalogue completeness (every part has a tier and a setting), document mapping, status aggregation, advisory null on Applied/Pending (T7).
- Risk: UI-thread marshalling of status events (the DLL will report from another thread); use the `DispatcherQueue` the page already has.
- Depends on: T1.

### T3. Tier 1: compact bar (registry part) — S
- Files: `src/Deckle.Shell.Taskbar/Registry/TaskbarSizePreference.cs`, `Registry/RestorePoint.cs` (reads and writes `restore.json`: per value, prior DWORD or absent).
- Steps: (1) write the T0-verified value to `HKCU\…\Explorer\Advanced`, snapshot first; (2) broadcast `WM_SETTINGCHANGE` with `"TraySettings"` through `SendMessageTimeout(HWND_BROADCAST, …, SMTO_ABORTIFHUNG, 1000)` [I]; (3) status Applied on a successful write, Failed with the Win32 error otherwise; (4) restore on off.
- Done when: integration test against a temp key (T7); Louis sees the bar change without restarting explorer, or the broadcast is replaced by the step that works.
- Risks: T0 may remove this task; the value might need explorer restart (then say so in an advisory, do not restart explorer automatically).
- Louis verifies: switch on and off, bar size follows, Windows Settings shows the matching choice.
- Depends on: T0, T2.

### T4. Tier 1: show all tray icons — M
- Files: `Registry/TrayIconPromotion.cs` (pure plan: from `{subkey → IsPromoted or null}` to the subkeys to set to 1; apply; restore), `Registry/TrayIconPromotionWatcher.cs` (B10 thread, debounce, stop event, `IDisposable`), P/Invoke via `[LibraryImport("advapi32.dll")]` `RegNotifyChangeKeyValue` in `Interop/TaskbarNativeMethods.cs`.
- Steps: (1) the root key path `Control Panel\NotifyIconSettings` is a constructor argument of both types (the key they govern); (2) on enable: snapshot every subkey's prior state into `restore.json`, set 1 everywhere, start the watcher; (3) on signal: re-plan, write only where not 1, log a count (never the executable paths: they are user data); (4) on disable: stop watcher, restore snapshot (subkeys created after enable have no snapshot entry; proposed default, pending D7: delete their `IsPromoted` value so explorer applies its own default [I]); (5) on `ReapplyAfterExplorerRestart`, run the plan once.
- Done when: unit tests on the plan, integration test on a temp HKCU tree including a subkey added while watching (T7); no write loop in the log over one minute idle.
- Risks: explorer may rewrite `IsPromoted=0` when an icon re-registers, causing a write-back on each launch of that app (acceptable, log at verbose); the native per-icon toggles in Windows Settings do nothing while on (state it in the card description).
- Louis verifies, after T9: all icons visible; a newly launched tray app appears visible within a second; turning off returns the previous layout.
- Depends on: T2.

### T5. Tier 1: hide the Recommended section — S
- Files: `Registry/RecommendedSectionPolicy.cs` (`IsHidden()` reads HKCU and HKLM; `Hide()` / `Show()` return `Task<CustomizationStatus>`), `Registry/PolicyElevation.cs` (`reg.exe` with `runas`, `UseShellExecute = true`, maps exit code and `Win32Exception.NativeErrorCode == 1223` to `PermissionDenied`, the `ElevatedStartupService` shape); `ViewModels/TaskbarViewModel.Settings.cs` (`confirmOnEnable` calls `Hide()`).
- Steps: (1) direct write when `Environment.IsPrivilegedProcess`; (2) otherwise `reg.exe` elevated; (3) if HKLM already holds 1 (set by someone else), report Applied and do not touch it; (4) the card description says Windows asks for permission once and Start shows the change at next sign-in [I until verified].
- Done when: unit test for the exit code mapping; Louis sees the UAC prompt once, Start without Recommended after the step T5 verification establishes (sign-out or `StartMenuExperienceHost` restart).
- Risks: UAC console flash from `reg.exe` (ElevatedStartupService accepts the same for schtasks); a standard-user OTS elevation writes the wrong hive (A4, document in a code comment).
- Louis verifies: when Start picks it up, and whether IoT LTSC honours the HKCU value (docs say yes).
- Depends on: T2.

### T6. App wiring — S
- Files: create `src/Deckle.App/App.Taskbar.cs` (`InitializeTaskbar`, `ReconcileTaskbar`, `ShutdownTaskbar`, the `App.TaskbarCover.cs` shape: unhook `Changed`, `Flush()`, dispose the customizer, which stops the watcher and restores nothing); touch `App.Startup.Modules.cs` (call after `InitializeTaskbarCover()`, milestone `"taskbar"`), `App.Lifetime.cs` (`TryShutdown("taskbar shutdown", ShutdownTaskbar)` next to line 70); relay explorer restarts: [I] `src/Deckle.Shell/TrayIconManager.cs:112, 179-184` already handles `TaskbarCreated`; expose or reuse an event there and call `ReapplyAfterExplorerRestart()`.
- Done when: build passes; start, toggle, quit leave no thread behind (watcher disposed); explorer restart re-applies tier 1.
- Depends on: T2–T5.

### T7. Tests `tests/Deckle.Shell.Taskbar.Tests` — M (written along T1–T5)
- unit: defaults single source (manifest defaults equal `new TaskbarSettings()`), catalogue completeness, `ExplorerCustomizationDocument.From`, status aggregation and advisory silence, `TrayIconPromotion` plan, `PolicyElevation` exit mapping, descriptor (`Id == ModuleId`, `PageTag` resolves to `TaskbarPage`).
- integration: `TrayIconPromotion` and the watcher on a temporary tree `HKCU\Software\Deckle.Tests\<guid>\NotifyIconSettings`, deleted in `Dispose`; `RestorePoint` round trip in a temp folder; `TaskbarSizePreference` against a temp key.
- observability: one `TestEventListener` test on `Deckle-Taskbar` for a promotion count event (deckle-logging decides the events; invoke it before adding them).
- Every class `[Trait("Category", …)]` from `tests/GLOSSARY.md`; reach code through the constructor key path, nothing added for tests only (deckle-testing).
- Known noise: two tests already fail on main (memory: install-hooks, script-layout); not regressions.

### T8. Relicensing commit, MIT → GPL-3.0 — S
- Files: `LICENSE` (replace with the verbatim GPLv3 text from https://www.gnu.org/licenses/gpl-3.0.txt, then the section 7 additional permission appended under a heading, wording approved by Louis), `README.md:105-107`, `CONTRIBUTING.md:27`, `NOTICE.md` (fix lines 43-45 for Windows App SDK, SDK BuildTools, Win2D; add Anthropic MIT, ModelContextProtocol Apache-2.0, CommunityToolkit.WinUI.Controls.Sizers MIT, DirectML redistributable; align MinGW with `publish-native-runtime.ps1:326-327`).
- Steps: (1) Louis decides D6 and approves the section 7 text; (2) edit the four files; (3) regenerate `TREE.md` only if its generator lists licence changes (it lists `LICENSE` already, so likely no change); (4) one commit, type and bump chosen through `deckle-commits` and `deckle-versioning` (a licence change is a user-facing change; let the skills decide, do not guess); (5) note in the commit body that releases up to v0.57.4 remain available under MIT, which cannot be withdrawn.
- Illustrative README text: "GPL-3.0-or-later, with an additional permission to link the Microsoft runtime components listed in LICENSE. See [LICENSE](LICENSE)."
- Done when: `Grep "\bMIT\b"` hits only third-party lines and vendored skills.
- Risk: legal reading not reviewed by a lawyer (A5).
- Depends on: nothing. Should land before any GPL mod code enters the native DLL.

### T9. Windhawk exit (Louis, no code) — S
- Louis: export or note anything still needed from the eight INIs (the backups already hold two mods), uninstall Windhawk through scoop, restart explorer, confirm no `windhawk.dll` in explorer.
- Optional, his call: copy `%LOCALAPPDATA%\Deckle\windhawk-backup` out of Deckle's data root, so a data reset cannot erase it.
- Expect after uninstall: the tray icons at 0 or absent go into the overflow until T4 lands; the clock, background and sizes return to Windows defaults; the bar width returns to whatever `TaskbarSize` really means (T0).

---

## (d) Open questions for Louis

**D1. Name and place of the customization page.** Keep "General" under the Taskbar node (B3-b, no code), or give the node's own page its real name (B3-a, small shell change plus an amendment to deckle-settings-ux that you approve)?

**D1-bis. French name for the customization sub-page** (the UI ships in English; the French name is the one you use when we talk about it, the English one goes in the `.resw`):
1. **Personnalisation** / *Customization*: Windows names its own taskbar page "Personnalisation > Barre des tâches", so the word sits where people already look for it.
2. **Apparence** / *Appearance*: says what most settings change (size, background, clock, Start), but "show all tray icons" is behaviour, not appearance.
3. **Réglages Windows** / *Windows taskbar*: says it acts on Windows' own bar, unlike the cover, which draws Deckle's window over it, at the cost of a less natural label.

**D2.** Under a "Taskbar" node, should the cover keep the label "Taskbar cover" or shorten to "Cover"?

**D3.** "Quick-settings buttons blended or moved": what does "blended" mean on screen (the Wi-Fi/volume/battery group without its button background?) and where would "moved" put them?

**D4.** Thumbnails: one width value (300 px, aspect derived) enough, or do you want the min and max (180×120) back?

**D5.** Keep the project name `Deckle.Shell.Taskbar` next to `Deckle.Shell.TaskbarCover`, or rename it `Deckle.Shell.TaskbarCustomization` so the two capability names stop overlapping?

**D6.** GPL-3.0-only or GPL-3.0-or-later? And do you agree to the section 7 linking permission for Microsoft runtimes (Windows App SDK, Win2D, DirectML)?

**D7.** Turning a setting off restores what Deckle replaced (B8). For tray icons, icons that appeared after you turned it on: back to Windows' default (hidden), or left visible?

**D8.** Is "Start elevated" on for your Deckle? If yes, `HideRecommended` writes without a UAC prompt.

Sources: [Policy CSP Start](https://learn.microsoft.com/windows/client-management/mdm/policy-csp-start), [Insider build 26220.9022](https://learn.microsoft.com/windows-insider/release-notes/beta/preview-build-26220-9022), [show-all mod source](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-notification-icons-show-all.wh.cpp), [elevenforum, taskbar size](https://www.elevenforum.com/t/change-taskbar-size-in-windows-11.47782/), [kapilarya, taskbar size](https://www.kapilarya.com/?p=35252), [elevenforum, icon size](https://elevenforum.com/t/change-size-of-taskbar-icons-in-windows-11.45/page-2).
