# Brief: tier 3 symbols — resolution, maintenance, fail-safe

Date 2026-10-18. Scope: the symbol side of Deckle's taskbar customization (bar width, task button width, thumbnail size) on Louis's machine: Windows 11 IoT Enterprise LTSC 2024, build 26100.9711, x64, native vertical taskbar on the left, Small mode. Loading the DLL into explorer.exe and the IPC channel belong to another agent and are not discussed here.

Legend: **[V]** verified by reading the named source or file on this machine; **[W]** verified on the web by a research agent (summarizer-mediated, quotes may be slightly off); **[I]** inferred, a lead to check.

## Sources

- Mod sources Louis runs: `D:\bin\scoop\persist\windhawk\AppData\ModsSource\taskbar-icon-size.wh.cpp` (1.3.10, with Louis's patch applied) and `taskbar-thumbnail-size.wh.cpp` (1.2.1).
- Louis's patch and probes: branch `tskbr-exploration-3e602b`, `docs/research/taskbar-probes/` (patch, `README.md`, `tray-uia.ps1`, `windhawk-build-mod.ps1`, `windhawk-install-mod.ps1`, `pe-keys2.ps1`).
- Windhawk engine, sparse clone of `ramensoftware/windhawk` in the scratchpad (`src-windhawk/src/windhawk/engine`): `mod.cpp` (cache key ~1185-1271, cache read/write ~895-1116, resolution flow ~2520-2675, online cache ~2999-3090, queued hooks ~1527-1598 and ~2076-2121), `symbol_enum.cpp` (search path line 43, msdia loading ~646-685, undecoration flags ~480-491), `mods_manager.cpp` 114/298, `stdafx.h`.
- Windhawk resolved caches: `D:\bin\scoop\persist\windhawk\AppData\Engine\ModsWritable\taskbar-icon-size.ini`, `taskbar-thumbnail-size.ini`; settings in `...\Engine\Mods\*.ini`.
- Modules loaded in explorer.exe: `C:\Windows\System32\Taskbar.dll` (10.0.10011.16384), `C:\Windows\SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy\Taskbar.View.dll` (2608.26001.200.0) and `SystemTray.dll` (2608.26001.100.0) in the same folder, Explorer.EXE 10.0.26100.8875.
- Deckle: `src/Deckle.Core/Paths/AppPaths.cs`.
- Web (2026-10-18): `ramensoftware/windhawk-mod-symbol-cache` scripts, Winbindex data, Microsoft Learn DbgHelp pages and VS 2022 redistribution list, MinHook/SlimDetours/minhook-detours/Detours repos, `ramensoftware/windhawk-mods` README and PR template.

---

# (a) Findings

## Q1. Minimal symbol sets

### How the mod changes the bar width [V]

- `TaskbarConfiguration::GetFrameSize(int)` (Taskbar.View.dll), `SystemTrayController::GetFrameSize(int)` and `SystemTraySecondaryController::GetFrameSize(int)` (SystemTray.dll since Taskbar.View 2604) return the frame thickness for a `TaskbarSize` enum (0 small, 1 regular, 2 large). The mod returns `g_taskbarHeight` for 1 and 2 only.
- Because it answers only 1 and 2, the mod forces Small into Regular by hooking the `consume_ITaskbarSettings::Size` getter in all three binaries (taskbar.dll, SystemTray.dll, Taskbar.View.dll). That getter is linker-folded with unrelated getters, hence the `IsRuntimeClass` check in the hook.
- Upstream guards the three GetFrameSize hooks with `!IsVerticalTaskbar() &&` [V source, W upstream main]. Louis's patch removes those three guards; that is what made the width work on the native vertical bar (memory: 48 → 44 → 24 → 32 live). On a vertical bar, the "height" returned by GetFrameSize is the bar's width.
- Live re-apply: `ApplySettings` sends `WM_SETTINGCHANGE`/`SPI_SETLOGICALDPIOVERRIDE` to `Shell_TrayWnd`; the hooked `TrayUI::_HandleSettingChange` (taskbar.dll) calls `TrayUI::_StuckTrayChange` (taskbar.dll, called through its resolved address, not hooked), then `0x452` goes to `MSTaskSwWClass`.

### Set for "bar width 32", vertical left, x64, build 26100 [I, derived from the source; untested in isolation]

Variant W (faithful to the mod), 8 symbols over 3 binaries:

| Binary | Symbol | Role |
|---|---|---|
| Taskbar.View.dll | `TaskbarConfiguration::GetFrameSize` | the main frame size |
| SystemTray.dll | `SystemTrayController::GetFrameSize` | tray frame on the main monitor |
| SystemTray.dll | `SystemTraySecondaryController::GetFrameSize` | other monitors only (optional for Louis) |
| taskbar.dll, SystemTray.dll, Taskbar.View.dll | `consume_ITaskbarSettings::Size` getter (one per binary) | forces Small → Regular |
| taskbar.dll | `TrayUI::_HandleSettingChange` (hooked) | live re-apply entry |
| taskbar.dll | `TrayUI::_StuckTrayChange` (called) | live re-apply action |

Variant M (smaller, 3 to 5 symbols): the three GetFrameSize hooks also answer for enum 0, and nothing overrides `Size`. The bar stays natively Small, so icons stay at 16 without any icon hook. Risk: the mod's readme says it "isn't compatible with the small size" without saying why; there may be Small-specific code paths (resource keys, metrics) that break. Must be tested (T1).

Hooks that do nothing on a vertical bar (the mod skips them when `IsVerticalTaskbar()`, or they are horizontal-only/ARM64-only) [V]: `SystemTrayController::UpdateFrameSize`, `SystemTraySecondaryController::UpdateFrameSize`, `SystemTrayFrame::Height`, `SystemTrayFrame::MeasureOverride`, `FrameworkElementOverrides::MeasureOverride`, `TaskbarFrame::MeasureOverride` (frame part), `TaskbarFrame::Height`/`MaxHeight` (gone on 24H2 anyway), `TaskbarController::UpdateFrameHeight` and `OnGroupingModeChanged`, `TrayUI::GetMinSize`, the `SHAppBarMessage` `ABM_QUERYPOS` hook, `__real@4048000000000000` (21H2), the ARM64 `UpdateFrameSize` and event `operator()`, the "-1 refresh trick" and wait loop in `ApplySettings`.

### Set for "button width 24" [I, derived from the source]

- `IMap<ResourceDictionary,...>::Lookup` hook (Taskbar.View.dll, 2 name variants): overrides `MediumTaskbarButtonExtent` (variant W) or `SmallTaskbarButtonExtent` (variant M).
- `TaskbarFrame::GetMetrics` **plus** `GetIconHeightInViewPixels(void)`: required crash guard. The overflow flyout fail-fasts if the extent in its metrics differs from the one the buttons use; `GetMetrics` needs the posture recorded by the `GetIconHeightInViewPixels(void)` hook. Treat both as required whenever the extent is overridden.
- Live trio (optional): `TaskListButton::UpdateVisualStates` (hooked), `TaskListButton::UpdateIconColumnDefinition` (scanned to find the extent field offset), `TaskListButton::UpdateButtonPadding` (called). Without them, the new width applies only when buttons are recreated (explorer restart or taskbar re-template).

### Set for thumbnail size [V]

Two Taskbar.View.dll symbols: `ThumbnailHelpers::GetScaledThumbnailSize(Size,float)` (scales by the percentage) and `TaskItemThumbnailView::OnApplyTemplate()` (sets `MaxWidth` to infinity). Louis's settings `size=300, useAbsoluteSize=0` [V], so only the percentage path matters.

### Symbols that belong to other options [V]

Icon size and quality: `IconUtils::GetIconSize`, `IconContainer::IsStorageRecreationRequired`, `CIconLoadingFunctions::GetClassLongPtrW`/`SendMessageCallbackW`, the `ShellIconLoaderV2` coroutine, `TaskListItemViewModel`/`TaskListGroupViewModel::GetIconHeight`, the other two `GetIconHeightInViewPixels` overloads, `TaskListButton::IconHeight`, badge/overlay/clip hooks, `ProgressBar::Width`, `TaskbarComponentHost::IconHeight`/`UpdateDefaultWidth`, `ExperienceToggleButton::IconHeight`/`UpdateButtonPadding`, and the 4 SearchUx.UI.dll symbols. Deckle does not need these for Louis's choices (icon 16 = native Small).

### Current resolutions on this build [V]

Windhawk's cache already holds the RVAs for Louis's binaries: `pdb_DAB69A79FEE033F0D81E023FBF172B511` taskbar.dll (9 entries), `pdb_2007F3890B9D4D16AF78BAFE771227281` SystemTray.dll (8), `pdb_24534974E00E46A58F69F60D0CEB9BD91` Taskbar.View.dll (29; the thumbnail mod resolves the same key to RVAs 1388592 and 2581996). Useful as a cross-check fixture for T3 (these are public symbol RVAs, not personal data).

## Q2. Resolution component

- **Windhawk** [V]: DIA (`msdia140_windhawk.dll`, loaded with NoRegCoCreate, its `LoadLibraryExW` import patched so it loads `symsrv_windhawk.dll`), enumerating every symbol through `srv*<Symbols>*https://msdl.microsoft.com/download/symbols`, inside the target process. Before DIA it tries its local cache, then the online cache `https://ramensoftware.github.io/windhawk-mod-symbol-cache/<modId>/<key>.txt` (retried for 30 s after session start).
- **windhawk-mod-symbol-cache** [W]: hourly GitHub Action; mod sources → symbol names (regex on `SYMBOL_HOOK`), Winbindex → binary download from msdl at `{name}/{timestamp:08X}{virtualSize:x}/{name}`, then a closed prebuilt `windhawk-symbol-helper.exe` extracts RVAs. **No licence** on that repo (`license: null`): readable, not copyable.
- **Winbindex** [W, LOCAL]: Taskbar.View.dll is indexed despite living under SystemApps; 92 x64 entries, all with `virtualSize`. Newest 24H2: 2608.26001.200.0, ts 1789085818, vs 10055680 (KB5124010 and KB5127648 ship the same binary) — this matches Louis's cache key `1789085818-10055680` [V]. So a generator can fetch exact binaries without guessing.
- **Redistribution** [W]:
  - System32 `dbghelp.dll` lacks symbol server support and "is not redistributable" — but using the copy already in Windows is not redistributing it. This machine has no symsrv.dll in System32 [W LOCAL].
  - SymSrv only works next to a non-system DbgHelp; shipping the Debugging Tools copies needs REDIST.TXT wording that was **not** read.
  - `msdia140.dll` is redistributable unmodified under the VS licence (VS 2022 redistribution list, "DIA SDK").
- **Key insight** [I]: a PDB can be fetched from msdl by `<pdbname>/<GUID><age>/<pdbname>` with a plain HTTP GET, so no symsrv is needed at all, and the binary does not need to be downloaded. The system dbghelp can open a local `.pdb` passed directly to `SymLoadModuleEx` (known pattern; verify in T3).

## Q3. Cache

- Windhawk key [V]: `pdb_<GUID as %08X%04X%04X + 8 bytes hex><age hex>`; fallback `pe_<arch>_<timestamp>_<SizeOfImage>_<name>` for binaries without CodeView info. Value `1#module#ts-size#symbol#rva...`, empty RVA = missing optional symbol; RVAs ≥ SizeOfImage are rejected; failures cached as `error:3#...` with a throttle.
- The PDB identity is a content identity: a new binary always means a new key, so the cache never needs invalidation, only pruning [I, holds as long as the key includes GUID+age].
- `AppPaths.GetModuleDirectory(moduleId)` gives `<UserDataRoot>\modules\<moduleId>\` and creates it [V]; `UserDataRoot` defaults to `%LOCALAPPDATA%\Deckle`, overridable by `DECKLE_DATA_ROOT` [V].

## Q4. Hooking library

- Windhawk uses the MinHook API over **MinHook-Detours** (m417z, MIT, "uses SlimDetours under the hood") [V `stdafx.h` + `libraries/`; W repo]. It queues enable/disable (`MH_QueueEnableHookEx`, `MH_ApplyQueuedEx`) and, at unload, waits until no thread's stack lies in the mod (`ThreadsCallStackWaitForRegions(regions, n, 200, 400)`, thread-call-stack-scanner) [V].
- **MinHook** (BSD-2, v1.3.4 2025-03) [W]: suspends threads via Toolhelp, remaps IPs inside the prologue; does not track a thread already inside the detour.
- **SlimDetours** (MIT, beta per its README, used by ExplorerPatcher and Windhawk) [W]: Detours-based, updates threads automatically, ntdll-only.
- **Microsoft Detours** (MIT, actively pushed, last release tag 2018) [W]: transaction API, the caller enumerates threads; no safe-undetour protocol.
- None of them makes unloading safe by itself [W Raymond Chen, I]. The simplest safe answer is never to unload: disable = passthrough flag.

## Q5. Fail-safe

- Windhawk's `HookSymbols` is all-or-nothing over the non-optional symbols of a call [V], and the mod is one big call per binary: one missing symbol disables everything. Deckle should scope "required" per feature instead.

## Q6. Live vs restart

- Bar width: live (44 → 24 → 32 applied through `Wh_ModSettingsChanged` → `ApplySettings`, confirmed by UIA) [V memory + source].
- Button width: live only through the trio above; without it, at recreation [V source].
- Thumbnail: `LoadSettings` re-reads values; the next thumbnail shown uses them [I].
- Observation 1 ("had to restart explorer" for width 24 while UIA saw it live) and observation 2 (at 32, task buttons measured 32x30 vs 24 at widths 24 and 44): hypotheses in (b) D9 and task T1.

## Q7. Upstream

- The mod header says PRs go to `https://github.com/m417z/my-windhawk-mods`; issues to `ramensoftware/windhawk-mods/issues` [V].
- windhawk-mods rules [W]: an update PR touches only `mods/<id>.wh.cpp`, bumps `@version`, carries a changelog block, and the `github` field must match the PR author — so only m417z can PR his own mod there; others "send changes to the author".
- Upstream main is still 1.3.10 with the `!IsVerticalTaskbar()` guards [W]; Louis's patch is not upstream. Related closed issues: #2261, #2519 (compatibility with the "Vertical Taskbar for Windows 11" mod) [W] — the guard most likely exists so this mod does not fight taskbar-vertical's own `TaskbarWidth` [I].

---

# (b) Recommended decisions

**D1. Resolve symbols in Deckle.exe, never in explorer.** The DLL in explorer only reports each target module's identity (name, PDB name, GUID, age, TimeDateStamp, SizeOfImage, read from its in-memory PE) and receives RVAs. Reason: no network, no COM, no DIA, no file IO inside explorer; a failure costs a status line, not a crash. Rejected: in-process DIA like Windhawk (redistribution + 2.7 MB + COM inside the shell).

**D2. Resolver = PDB download by GUID + System32 `dbghelp.dll` via P/Invoke.** Deckle GETs `https://msdl.microsoft.com/download/symbols/<pdb>/<GUID><age>/<pdb>` with HttpClient, then `SymInitializeW` / `SymLoadModuleExW(pdbPath, base, size)` / `SymFromNameW` on decorated (mangled) names, serialized behind one lock (DbgHelp is single-threaded). Reason: nothing redistributed, small code, offline once cached, matches names exactly. Rejected: shipping DbgHelp+SymSrv (REDIST.TXT unread, adds files); DIA (COM, size); our own PDB publics-stream reader (more code, kept as fallback if T3 shows dbghelp refuses a bare PDB).

Snippet (shape only):
```csharp
// Publics-only lookup on a downloaded PDB; Deckle never needs symsrv.
SymSetOptions(SYMOPT_UNDNAME_OFF | SYMOPT_EXACT_SYMBOLS | SYMOPT_FAIL_CRITICAL_ERRORS);
ulong mod = SymLoadModuleExW(h, 0, pdbPath, null, FakeBase, sizeOfImage, 0, 0);
bool ok  = SymFromNameW(h, mangledName, ref info); // rva = info.Address - FakeBase
```

**D3. Ship a precomputed table for known builds, generated offline.** A repo script (Winbindex → msdl binary → PDB → same resolver) produces `symbols.json` keyed by PDB identity for the Windows 11 24H2/25H2 builds of the three binaries. At runtime: table → local cache → download. Reason: Louis's machine works offline on day one, and the script doubles as an early warning when a new update drops a symbol. Rejected: Windhawk's online cache (no licence on the repo, ties Deckle to a third-party service, contradicts the emancipation goal); download-only (needs network after every update).

**D4. Cache at `AppPaths.GetModuleDirectory("taskbar")\symbols\cache.json`, keyed `pdb_<GUID><age>`.** Value: module name, TimeDateStamp, SizeOfImage, map `mangled name → RVA | null` (null = known missing), resolver version, date. Never invalidated (content identity); pruned to the last N keys per module; a symbol-set change (new name in the manifest) triggers a resolve of only the names absent from the entry; the PDB is deleted after extraction. Failures cached with a retry-after (e.g. 1 h, then next update). Rejected: keying by file version (two builds can share a version string) or by path+mtime.

**D5. In-explorer guards.** Before hooking, the DLL re-checks GUID+age against what Deckle resolved for, and every RVA < SizeOfImage; any mismatch → feature `failed: identity-mismatch`, nothing patched.

**D6. Hooking: MinHook-Detours (MinHook API, SlimDetours inside), pinned DLL, never unloaded.** Reason: it is the stack Windhawk runs in explorer every day on Louis's machine, MIT, x64+ARM64, automatic thread update, MinHook-style API that maps one-to-one to the mod's code. Disabling a feature = flip a passthrough flag (the detour calls the original), optionally `MH_DisableHook` too; the DLL stays loaded (`GetModuleHandleEx(PIN)`) so no thread can return into freed code. Rejected: Detours (manual thread updates, last release 2018); MinHook classic (prologue-only IP remap, no ARM64); SlimDetours raw (beta, Detours-style API — fine as a fallback since MinHook-Detours wraps it).

**D7. Feature-scoped fail-safe.** Each feature declares, per module, required and optional symbols, its hooks and apply/revert. A feature applies only if all its required symbols resolved and all its hooks installed; otherwise it reports a reason and touches nothing. A hook shared by two features (e.g. `Lookup`, the `Size` getters) is installed once, reference-counted, and dispatches per active feature.

```cpp
struct FeatureSpec {
  const char* id;                       // "bar-width"
  std::span<const SymbolRef> required;  // module + mangled name
  std::span<const SymbolRef> optional;  // e.g. the live trio
  bool (*apply)(const Settings&);  void (*revert)();
};
```

**D8. Status payload (JSON over the other agent's channel).**
```json
{ "schema": 1, "explorerPid": 1234, "explorerStart": "2026-10-18T09:12:03Z",
  "os": { "build": 26100, "ubr": 9711 },
  "modules": [ { "name": "Taskbar.View.dll", "pdb": "pdb_2453...BD91", "loaded": true } ],
  "features": [ { "id": "bar-width", "state": "applied", "value": 32, "appliedValue": 32,
                  "reason": null, "missing": [], "live": true } ] }
```
`state` ∈ applied | pending | unavailable | failed | disabled | requires-restart; `reason` ∈ symbol-missing | download-failed | offline | identity-mismatch | hook-failed | module-not-loaded | null. The UI maps `unavailable/symbol-missing` to "not supported on this Windows build yet", `requires-restart` to a "Restart Explorer" action. No personal data in the payload.

**D9. Live vs restart per setting.** Bar width live (variant W re-apply pair, or M with the same pair); thumbnail live on next show; button width live only if Louis wants the trio carried (3 more symbols, one of them byte-scanned — the most fragile part of the set), else `requires-restart`. Hypotheses to verify in T1:
- H1a: UIA updated but the composition lagged → capture pixels (Windows.Graphics.Capture or PrintWindow) next to the UIA rectangles after a live change.
- H1b: work area not updated → log `SPI_GETWORKAREA` and `Shell_TrayWnd` rect before and after; check a maximized window's left edge.
- H1c: elements that cached extents in `OnApplyTemplate` (TaskbarFrame) kept the old ones → per-element UIA diff, live vs after an explorer restart.
- H2a: 32 collides with a stock extent (tablet posture 32 in `GetMetrics`; tray stock heights 24/32/72, fix skipped on vertical) → measure at 30, 31, 33, 34 with `tray-uia.ps1`; log what `GetMetrics` returns.
- H2c: the 32 measurement was mid-transition → re-measure after an explorer restart at 32.

**D10. Upstream: issue first, then a PR to `m417z/my-windhawk-mods` if he wants it.** The PR keeps the guard for the taskbar-vertical mod case and lifts it only for the native vertical bar (e.g. detect the native setting rather than "edge is left/right"), updates the setting description, bumps 1.3.11. Rejected: PR to ramensoftware/windhawk-mods (only the author can update his mod); silent fork (maintenance burden).

---

# (c) Implementation tasks

Proposed folders (the loading agent has the final word): `src/Deckle.Shell.Taskbar/` (C#: manifest, resolver, cache, status mapping), `src/Deckle.Shell.Taskbar.Native/` (C++ DLL: identity report, hooks, features), `scripts/taskbar-symbols/` (generator), `tests/` per `deckle-testing`.

### T1. Probe variants and the two observations — S/M
- Goal: decide W vs M, live trio or not, and explain observations 1 and 2, before writing Deckle code.
- Done: a short probe note on branch `tskbr-exploration-3e602b` with measurements for M, W, widths 30/31/32/33/34, and the H1/H2 verdicts.
- Files: `docs/research/taskbar-probes/` (mod variants as patches, measurement scripts).
- Steps: 1) variant M patch (GetFrameSize answers 0,1,2; Size hooks removed) built and installed through route B; 2) measure bar, task buttons, tray with `tray-uia.ps1`; 3) widths 30-34 for H2a; 4) work-area and pixel capture logging for H1a/H1b; 5) explorer restart and re-measure for H1c/H2c.
- Risks: variant M may crash explorer (overflow fail-fast); keep W installed as rollback.
- Louis verifies: what he sees on screen at each step (the agent cannot judge rendering); restarting explorer is his act.
- Depends on: nothing. Route B already proven.

### T2. Symbol manifest — S
- Goal: one machine-readable list of features → modules → required/optional mangled names, shared by generator, resolver and DLL.
- Done: `symbols.manifest.json` with bar-width, button-width, thumbnail; a test checks it parses and every name is unique per module.
- Files: `src/Deckle.Shell.Taskbar/Symbols/`.
- Steps: 1) take the decorated names from the mod (the cache uses undecorated names; record both, match on decorated); 2) mark required/optional per D7; 3) embed as resource.
- Risks: mod names are undecorated strings with WinRT templates; deriving the mangled form needs one resolve pass (T3) to read them from the PDB.
- Louis verifies: nothing.
- Depends on: T1 (W vs M).

### T3. Identity report, PDB download and dbghelp resolver — M
- Goal: given a module identity, return RVAs or per-name misses.
- Done: resolving the three current binaries reproduces the RVAs in Windhawk's cache [fixture: the three `pdb_...` entries]; offline run with a cached entry makes no HTTP call; an unknown name returns null, not an exception.
- Files: `src/Deckle.Shell.Taskbar/Symbols/` (`PdbDownloader`, `DbgHelpResolver`), native side `src/Deckle.Shell.Taskbar.Native/identity.*`.
- Steps: 1) native: read CodeView RSDS (GUID, age, pdb name), TimeDateStamp, SizeOfImage from the loaded module; 2) C#: GET from msdl to a temp file (size cap, timeout, cancellation); 3) dbghelp P/Invoke behind one lock, publics only; 4) fallback decision if dbghelp refuses a bare PDB (own publics reader).
- Risks: dbghelp behaviour on a bare PDB unverified; msdl may serve compressed `.pd_` (handle CAB); large PDBs (Taskbar.View ~ tens of MB) — download once, delete after.
- Louis verifies: nothing on screen.
- Depends on: T2.

### T4. Cache store — S
- Goal: D4.
- Done: tests for hit, partial hit (new name), known-missing, prune, corrupt file → rebuilt, atomic write.
- Files: `src/Deckle.Shell.Taskbar/Symbols/SymbolCache.cs`.
- Steps: 1) schema with version; 2) atomic write (temp + replace); 3) prune; 4) failure retry-after.
- Risks: concurrent Deckle instances — reuse the settings mutex pattern from `AppPaths`.
- Depends on: T3.

### T5. Generator for the shipped table — M
- Goal: D3; plus an early warning when an update removes a symbol.
- Done: the script outputs `symbols.json` for every 24H2/25H2 entry of the three binaries; a diff report lists names newly missing.
- Files: `scripts/taskbar-symbols/`, output embedded in `src/Deckle.Shell.Taskbar/`.
- Steps: 1) read Winbindex `by_filename_compressed/<name>.json.gz`; 2) download binary from msdl (`{ts:08X}{vs:x}`), read RSDS; 3) download PDB, resolve with the T3 resolver (run as a console mode of the same code); 4) write table + report.
- Risks: taskbar.dll and SystemTray.dll Winbindex coverage not checked (only Taskbar.View.dll was); Winbindex is a third-party service, used at build time only.
- Louis verifies: whether he wants it run by hand or on a schedule.
- Depends on: T3.

### T6. Native hook engine — M
- Goal: D6.
- Done: the DLL installs, disables (passthrough) and re-enables a hook on a test process function; pinned; builds Debug x64 with `/m:4`.
- Files: `src/Deckle.Shell.Taskbar.Native/` (vendored `minhook-detours` + SlimDetours, licence files).
- Steps: 1) vendor at a pinned commit; 2) wrapper with queued apply; 3) passthrough flags; 4) test harness out of explorer.
- Risks: first native project in the repo (build integration belongs to the loading agent); beta status of SlimDetours.
- Depends on: loading agent's project skeleton.

### T7. The three features — M/L
- Goal: bar width, button width, thumbnail, per D7, ported from the mod (GPLv3 source: port the logic, not the text; licence question in (d)).
- Done: each feature applies, reverts and reports status; a missing required symbol leaves the taskbar stock and reports `unavailable`.
- Files: `src/Deckle.Shell.Taskbar.Native/features/`.
- Steps: 1) bar width with the live re-apply pair; 2) thumbnail; 3) button width with the GetMetrics guard, then the trio if chosen.
- Risks: GetMetrics guard forgotten → explorer fail-fast on the overflow flyout; `Size` getter folding → the IsRuntimeClass check is mandatory in variant W.
- Louis verifies: each setting on screen, including the overflow flyout and a second monitor if he has one.
- Depends on: T1, T3, T6.

### T8. Status payload and UI mapping — S
- Goal: D8 in C# and C++, Settings shows per-setting state.
- Done: round-trip tests for the JSON; Settings strings for each state/reason.
- Files: `src/Deckle.Shell.Taskbar/Status/`, the Settings page of the module.
- Depends on: the channel (other agent), T7.

### T9. Upstream issue/PR — S
- Goal: D10.
- Done: issue text ready for Louis; PR branch ready if m417z agrees.
- Steps: 1) read the full upstream file and diff against Louis's patch; 2) draft issue (native vertical bar, KB5127648, the guard, proposed detection); 3) after reply, PR to `m417z/my-windhawk-mods` with version 1.3.11.
- Louis verifies: posting is his act, under his account.
- Depends on: T1 (the 32 anomaly should be understood first).

### T10. Maintenance runbook — S
- Goal: when an update drops a symbol, find the candidate replacement fast.
- Done: a script that lists PDB publics matching a pattern (e.g. `*GetFrameSize*`) for old and new PDB, side by side; a short comment block in the manifest saying how to use it.
- Files: `scripts/taskbar-symbols/`.
- Depends on: T3, T5.

---

# (d) Open questions for Louis

1. Module id (the `AppPaths` folder name): `taskbar`? Must be decided once, it names the data folder.
2. Variant M (stay natively Small, fewer symbols) or W (the mod's way), once T1 has measured both?
3. Button width: carry the live trio (3 more symbols, one byte-scanned) or accept "applies after restarting Explorer"?
4. Ship a precomputed symbol table in Deckle (works offline after updates already indexed) or download on demand only?
5. Hooking library: MinHook-Detours (recommended) vs Microsoft Detours?
6. The mod is GPLv3: porting its logic into Deckle raises a licence question for Deckle (what licence does Deckle carry?). Port the ideas only, re-derive from the PDBs, or accept GPLv3 for the native part?
7. Upstream: open the issue with m417z before or after Deckle's own module works?
8. Is a Deckle-hosted online cache ever acceptable, or strictly local (download from Microsoft only)?
