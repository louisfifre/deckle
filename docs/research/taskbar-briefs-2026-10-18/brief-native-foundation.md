# Brief: native foundation of Deckle.Shell.Taskbar (2026-10-18)

Scope: how the native C++ DLL is built inside Deckle, how it reaches explorer.exe, how Deckle and the DLL talk, how the DLL logs. Tier 2 (XAML Diagnostics content) and tier 3 (symbol resolution, inline hooks) are only touched where the foundation must leave room for them.

Section (a)A was written by a framing agent. Sections (a)B onward were written by the orchestrator from the 2026-10-17 research note, the agent's hand-back and the tier 2 brief, after the agent's run stopped.

Markers: **[V]** verified, with the source. **[I]** inferred, not verified. **[N]** taken from the 2026-10-17 research note and not re-verified here.

---

## (a) Findings

### A. Building a C++ DLL in Deckle's build flow

1. **[V] `dotnet build` cannot build a `.vcxproj`.** Error MSB4278: "This error can also occur if you try to build a C++ project (.vcxproj) with dotnet build. Resolution: Use MSBuild.exe or Visual Studio." https://learn.microsoft.com/visualstudio/msbuild/errors/msb4278 . The dotnet CLI's MSBuild has no `$(VCTargetsPath)` and no C++ targets. A C# `ProjectReference` to a vcxproj, or a vcxproj inside a solution that `dotnet build` builds, hits the same wall [I for the sln case, same mechanism].
2. **[V] Deckle has no main solution file.** The repo root holds only `Deckle.Tests.sln` (80 projects). Builds target the csproj directly: `scripts/commands/build-run.ps1:127` runs `dotnet build <Deckle.App.csproj> -c:<cfg> -p:Platform=x64`. `scripts/commands/release/publish-app.ps1:296-305` runs `dotnet publish src\Deckle.App\Deckle.App.csproj -c:Release -p:RuntimeIdentifierOverride=win-x64 -p:SelfContained=true -p:Platform=x64 -o <PublishDir> /m:4`. So whatever the DLL needs has to flow through `Deckle.App.csproj` and its ProjectReferences.
3. **[V] Deckle.App is not NativeAOT.** No `PublishAot` in `src/Deckle.App/Deckle.App.csproj`. Only `src/Deckle.Installer/Deckle.Installer.csproj:31` has `<PublishAot>true</PublishAot>`. The app publishes as a self-contained JIT folder (`Deckle.App.csproj:44-46`).
4. **[V] Toolchain on this machine.** VS 18.7.2 at `D:\bin\visual-studio\visual-studio-2026`. The only C++ platform toolset is **v145**. MSVC 14.51.36231. Windows SDK 10.0.26100.0, with `um\xamlom.h` and `cppwinrt\winrt` present. `MSBuild\Current\Bin\amd64\MSBuild.exe` is present. `vswhere.exe` is at `C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe`. Installed components include `VC.Tools.x86.x64`, `Workload.NativeDesktop`, `Windows11SDK.26100`, `VC.CMake.Project`. Debugging Tools `dbghelp.dll` and `symsrv.dll` are under `C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\`. DIA `msdia140.dll` is under `DIA SDK\bin\amd64`. clang-cl is **not** installed.
5. **[V] The dev-env bootstrap already requires the MSVC tools.** `scripts/commands/setup/bootstrap-dev-env.ps1:108-112` requires `Microsoft.VisualStudio.Component.VC.Tools.x86.x64`, because the WinUI XAML compiler enumerates `VC\Tools\MSVC`. A native project adds no new prerequisite on a machine bootstrapped this way.
6. **[V] `Directory.Build.props` and `.targets` are imported by C++ projects too.** https://learn.microsoft.com/cpp/build/reference/vcxproj-file-structure . Deckle's root `Directory.Build.props` sets `TargetFramework`, `Nullable`, `ImplicitUsings`, `AllowUnsafeBlocks` and `ArtifactsPath`. None of these belongs in a vcxproj. Precedent: `benchmark/` opts out with its own `Directory.Build.props` that does not import the root one (root `Directory.Build.props:21-22, 48-49`).
7. **[V] `Directory.Build.rsp` is read by MSBuild.exe as well.** https://learn.microsoft.com/visualstudio/msbuild/msbuild-response-files . A nested `MSBuild.exe <vcxproj>` call inherits `-nodeReuse:false -lowPriority:true` from the root `Directory.Build.rsp`.
8. **[V] TranslucentTB builds ExplorerTAP as a vcxproj.** `ConfigurationType=DynamicLibrary`, static CRT (/MT), x64 + ARM64, a `.def` file, IDL files compiled by MIDL with proxy/stub. NuGet `Microsoft.Windows.SDK.CPP`, `Microsoft.Windows.CppWinRT`; vcpkg (static) for `detours` and `wil`. Source: https://raw.githubusercontent.com/TranslucentTB/TranslucentTB/develop/ExplorerTAP/ExplorerTAP.vcxproj .
9. **[N] Windhawk builds its engine with VS 2022 MSBuild, v143, C++23, SDK 10.0.**
10. **[V] Install and uninstall wipe folders.** `src/Deckle.Setup/DeployPage.xaml.cs:147-176` (`CopyPayload`) clears the install folder on update; `Uninstaller.cs:61-92` fails when a folder cannot be deleted. The published root's required entries are listed in `scripts/lib/release-validation.psm1:280`.
11. **[V] `%LOCALAPPDATA%\Programs` carries no ALL APPLICATION PACKAGES access entry** (icacls). Only relevant if a packaged host (StartMenuExperienceHost) is ever a target.

### B. Getting the DLL into explorer, the TranslucentTB way

12. **[N] Mechanism.** TranslucentTB's host loads its own copy of ExplorerTAP.dll, then installs a `WH_CALLWNDPROC` hook with `SetWindowsHookEx` on the thread id of the taskbar window (`Shell_TrayWnd`), sends a message to that window so the hook fires, and unhooks. Windows loads the hook DLL into explorer for that thread; the DLL's `DllMain` starts its own work (the XAML Diagnostics connection) and the DLL stays loaded. This is the documented purpose of global or thread hooks (`SetWindowsHookExW`, Microsoft Learn: a hook procedure in a DLL is loaded into the target thread's process). No process enumeration, no remote threads, no polling. Seelen UI uses the same call for its tray hook [N, verified in the landscape report].
13. **[N] The DLL stays pinned until explorer restarts** in TranslucentTB's design; the Windhawk styler instead releases the extra reference at `SetSite` so it can be unloaded (tier 2 brief, A1.4). Either way a running explorer holds the file open.
14. **[V] Deckle already hears `TaskbarCreated`.** `TaskbarCoverHost.LifecycleAndWindow.cs:138` registers the message; `CallbacksAndMessagePump.cs:80-84` rebuilds on it; `src/Deckle.Shell/TrayIconManager.cs:112, 179-184` re-adds the tray icon on it. A message-only window never receives the broadcast (`MessageOnlyHost.cs:15`).
15. **[N] Two target processes.** Everything taskbar-side lives in explorer.exe. The start menu size and its Recommended fallback live in StartMenuExperienceHost.exe (tier 2 brief, T9), a packaged process that needs an ACL on the DLL and a different trigger. The tier 1 policy already removes Recommended; the size is the only thing left there.

### C. The channel Deckle ↔ DLL

16. **[N] TranslucentTB** publishes a COM object in the Running Object Table from inside explorer; the host calls it. **Windhawk** watches INI files from inside explorer and relies on `SettingsChangeTime`. Neither reports per-setting status back to a settings page; that need is Deckle's own (tier 2 brief D9, C# brief B7).
17. **[I] Named pipe, Deckle as server.** Deckle creates a duplex message-mode pipe `\\.\pipe\Deckle.Taskbar.<session id>` before loading the DLL; the DLL connects on load and reconnects on disconnect with a backoff; Deckle pushes the settings document (JSON) and receives status and log records (JSON lines). C# side: `NamedPipeServerStream`, System.Text.Json; C++ side: `CreateFileW` + `ReadFile`/`WriteFile`, a tiny JSON reader (or a flat `key=value` line format if JSON in C++ weighs too much). Pipe disconnect = "Deckle is gone".
18. **[I] What a Deckle exit means.** With Windhawk, mods unload when the host dies. For Deckle: on disconnect, the DLL restores every value it changed and goes idle (it keeps a reconnect loop); it does not try to unload itself. Restore-on-disconnect keeps "quit Deckle, get Windows back" true, which matches the C# brief's B8 for tier 1 (tier 1 values persist, but those are registry choices, not an injected DLL). Unloading from inside a hook thread is possible but fragile; it buys nothing while explorer keeps running.

### D. Upgrade and uninstall with a locked file

19. **[V+I] Conflict.** The install folder is wiped on update (finding 10) and explorer holds the DLL open (finding 13). A copy in the install folder cannot be replaced while explorer runs.
20. **[I] Versioned copy outside the install folder.** At module start, Deckle copies the DLL from the install folder to `%LOCALAPPDATA%\Deckle\modules\taskbar\native\<file version>\Deckle.Shell.Taskbar.Native.dll` if that path does not exist, and loads that copy. A new Deckle version brings a new file version, hence a new folder and a new path; explorer loads the new one on the next `TaskbarCreated` or restart, while the old file stays locked until then. Old folders are deleted when no longer locked (try at start, ignore sharing violations). This is what Windhawk does with `<mod>_<version>_<hash>.dll` [N]. Update of the install folder never hits a lock.
21. **[I] Uninstall.** The uninstaller must not fail on the native folder under the data root: skip locked files and schedule them with `MoveFileExW(..., MOVEFILE_DELAY_UNTIL_REBOOT)` (documented), or tell the user to restart explorer. Which one is a product choice (open question).
22. **[I] Security of a DLL in a user-writable folder.** Anything that can write to the user's profile can already run code as the user; loading a DLL from there into explorer adds no privilege. Reasonable hygiene: Deckle checks that the file it is about to load has the expected size and hash recorded at publish time, logs and refuses otherwise. Code signing is not available to Deckle today (no certificate) [I].

### E. Logging from inside explorer

23. **[V] Deckle's logging doctrine** is `.agents/skills/deckle-logging/SKILL.md`: one `EventSource` per provider, events read by a person. The C# brief plans `[EventSource(Name = "Deckle-Taskbar")]` as `DeckleShellTaskbarSource`.
24. **[I] Route.** The DLL writes log records on the pipe (level, event name, a few fields); Deckle re-emits them through `DeckleShellTaskbarSource` with their own event ids. No file written by the DLL, no `OutputDebugString` as the primary route. Before the pipe is up, the DLL buffers a bounded number of records.

---

## (b) Decisions recommended

| # | Decision | Reason | Rejected |
|---|---|---|---|
| N1 | A `native/` subtree at the repo root with its own `Directory.Build.props` (opt-out of the root one, like `benchmark/`), one vcxproj `native/Deckle.Shell.Taskbar.Native/`, toolset v145, x64 only, static CRT (/MT), C++/WinRT from NuGet, SDK 26100 headers | `dotnet build` cannot build it (finding 1); the root props would poison it (finding 6) | CMake: a second build system for one project; clang: not installed |
| N2 | `Deckle.Shell.Taskbar.csproj` invokes `MSBuild.exe` on the vcxproj from a `BeforeBuild` target (path through `vswhere`, same Configuration), then includes the produced DLL as `Content` copied to output; the target is skipped with a warning when MSBuild.exe is absent so a machine without VC tools still builds the C# | Keeps `dotnet build /m:4` and `dotnet publish` as the single entry (finding 2); `Directory.Build.rsp` carries low priority into the nested call (finding 7) | A separate publish step in `publish-app.ps1`: a second thing to forget; a prebuilt binary checked in: unbuildable by an agent |
| N3 | Loading: Deckle loads its own copy of the DLL, installs a `WH_CALLWNDPROC` hook on the `Shell_TrayWnd` thread, sends one registered message, unhooks. Re-done on `TaskbarCreated` and when the pipe reports no client after a timeout | The proven, documented route (finding 12); no polling, Louis's explicit requirement | Windhawk's process scanning and remote threads: the thing Louis refused |
| N4 | Channel: duplex named pipe, Deckle server, JSON lines both ways; DLL restores and idles on disconnect | Per-setting status must come back (finding 16-18) | COM in the ROT: a registered class, proxy/stub IDL, more surface for the same bytes; INI watching: no status channel |
| N5 | Versioned copy under the data root, loaded from there, old versions swept when unlocked | Install folder wipe vs. locked file (findings 19-20) | Restarting explorer on update: user-hostile; locking the install folder: breaks Setup |
| N6 | Logs travel on the pipe and are re-emitted by `DeckleShellTaskbarSource` | One doctrine, one reader (finding 23-24) | A log file written by the DLL: a second place to look |
| N7 | StartMenuExperienceHost is out of the foundation's v1. Only explorer.exe is a target | The tier 1 policy covers Recommended; the start size alone does not justify a packaged-process ACL and a second trigger in v1 | Doing both hosts at once: doubles the risk of the first on-machine runs |
| N8 | The DLL exposes one `Apply(document)` and one `Restore()` entry to the pipe layer; tiers 2 and 3 register "parts" behind that, each with its own status | Lets the tier 2 and tier 3 briefs plug in without touching the foundation | Separate DLL per tier: two files to load, two pins |

---

## (c) Implementation tasks

Order: N-T1 → N-T2 → N-T3 → N-T4 → N-T5 → N-T6. N-T7 after everything. Tier 2 T1 (TAP core) plugs in after N-T3.

### N-T1. Native project skeleton and build integration (M)
- **Goal:** `dotnet build src/Deckle.App/Deckle.App.csproj -p:Platform=x64 /m:4` also builds `Deckle.Shell.Taskbar.Native.dll` and copies it next to Deckle.App.
- **Files:** `native/Directory.Build.props` (empty opt-out, comment why), `native/Deckle.Shell.Taskbar.Native/Deckle.Shell.Taskbar.Native.vcxproj` (+ `.filters`), `packages.config` or PackageReference for `Microsoft.Windows.CppWinRT`, `pch.h/.cpp`, `dllmain.cpp` (empty `DllMain` + a `DeckleTaskbarNativeVersion` export), `Deckle.Shell.Taskbar.Native.def`, `version.rc`; `src/Deckle.Shell.Taskbar/Deckle.Shell.Taskbar.csproj` (BeforeBuild target: locate MSBuild.exe through vswhere, run it with `-p:Configuration=$(Configuration);Platform=x64`, `Content` include of the output DLL with `CopyToOutputDirectory`), `scripts/lib/release-validation.psm1:280` (add the DLL), `scripts/commands/setup/bootstrap-dev-env.ps1` (note that the SDK 26100 is required).
- **Steps:** (1) vcxproj by hand, v145, x64, /MT, warning level 4, `/permissive-