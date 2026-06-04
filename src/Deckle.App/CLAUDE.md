---
name: claude-deckle-app
description: "Doctrine for Deckle.App, the WinUI 3 host module that composes all Deckle.* modules. Read before authoring or modifying the app lifecycle, long-lived windows (HUD, LogWindow, SettingsWindow, PlaygroundWindow), tray, global hotkeys, or any cross-cutting WinUI 3 code."
type: agent-instructions
module: Deckle.App
---

# CLAUDE.md — Deckle.App (host app)

The unpackaged WinUI 3 host app that composes the `Deckle.*` modules. Single UI entry point for the project. This module's responsibility is limited to composition: app lifecycle, long-lived windows (HUD, LogWindow, SettingsWindow, PlaygroundWindow), system tray, global hotkeys, wiring of business modules through their host interfaces. No business logic is supposed to live here outside event handlers and bridge adapters — when one is added, it is almost always a signal that it should have landed in a specific module.

Before any runtime test, kill any already-running instance (Deckle or earlier prototype). Two processes calling `RegisterHotKey` on the same combination collide with `err 1409`.

## Build

The build runs via `dotnet build`. From `src/Deckle.App/`, PowerShell without admin:

```
dotnet build -c Release -p:Platform=x64
```

Output: `bin\x64\Release\net10.0-windows10.0.26100.0\Deckle.exe` (self-contained). Restore is implicit (a separate phase before Build), no explicit Restore target needed.

Points of attention on the csproj side. `Microsoft.WindowsAppSDK` is pinned to `1.8.260317003` (official stable). `global.json` pins SDK `10.0.104` — keep as is. `<EnableMsixTooling>true</EnableMsixTooling>` forces the Publish pipeline to generate `Deckle.pri` in `PublishDir`; without it, on WindowsAppSDK 1.8 unpackaged, the `.xbf` files embedded in the `.pri` are unreachable and the app starts without a window (see [microsoft/WindowsAppSDK#3451](https://github.com/microsoft/WindowsAppSDK/issues/3451)).

The orchestration scripts live under `scripts/`. The interactive menu `scripts/deckle.ps1` is the daily entry point; the leaf scripts live under `scripts/lib/` and remain directly invocable on the CLI. `scripts/lib/build-run.ps1` kills Deckle if it is running, builds via `dotnet build`, launches the exe — switches `-NoRun`, `-Wait`, `-Configuration`, `-Target`, `-Pick`, `-NoAutoRestart`.

## Cross-cutting WinUI 3 pitfalls

These pitfalls concern all WinUI 3 code in the app, not only the host module. They are recorded here because this is where the initial instrumentation pass captured them all, but they apply as soon as another module touches XAML or WinUI 3 windows.

`AllowUnsafeBlocks` is mandatory in any csproj that uses `LibraryImport`. Without this property, the compiler emits `SYSLIB1062` or `CS0227`.

`UseWindowsForms` is forbidden in any WinUI 3 csproj. Mixing WinUI 3 with Windows Forms breaks XAML resolution.

`Window` does not expose `Resources` directly in WinUI 3. XAML resources are declared on the root `Grid` via `<Grid.Resources>`, not on `<Window.Resources>` (compilation error `WMC0011`).

Any WinUI 3 UI object lives only on the UI thread, including `SolidColorBrush`. Any UI object instantiated from a background thread raises `COMException` (`RPC_E_WRONG_THREAD`). The pattern to apply: create brushes and UI objects in the `Window` constructor and reuse them in handlers coming from Record or Transcribe threads.

Tall caption buttons do not activate with `ExtendsContentIntoTitleBar=true` alone. You must explicitly add `AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall`. The rule also holds for the native `Microsoft.UI.Xaml.Controls.TitleBar` control.

The Win32 `SubclassProc` delegate must be an instance field, never a local lambda (otherwise the GC collects it and the subclass crashes). The pattern is in place in `MessageOnlyHost`.

`Microsoft.UI.Xaml.Controls.ItemsRepeater` is not an `ItemsControl` in the classic UWP/WPF sense — it does not propagate `DataContext` to descendants of its `DataTemplate`. Consequence: a `ComboBox` (or any control) inside the template has `DataContext == null` on `Loaded` and on every event, even if `x:Bind` inside the template resolves correctly against the implicit item. Deliberate design decision on the ItemsRepeater side for perf and virtualisation (see [microsoft-ui-xaml#7726](https://github.com/microsoft/microsoft-ui-xaml/issues/7726)). Correct pattern: `Tag="{x:Bind}"` on the control to capture the VM reference at template inflation, and `combo.Tag is MyViewModel vm` on the handler side. Walking up the visual tree via `VisualTreeHelper.GetParent` looking for a parent with `DataContext` is fragile and breaks at the slightest template refactor.

Secondary window lifetime is lazy and terminal. SettingsWindow, LogWindow, and PlaygroundWindow are created on first open, restore/save their native Win32 `WINDOWPLACEMENT` through `SecondaryWindowPlacement`, then allow close to destroy the HWND and DComp visual tree. The placement file is private app state under `<UserDataRoot>\window-placement.json`, not a user-facing setting and not part of `settings.json`. `App.Windows.cs` clears the matching lazy reference on `Closed`; LogWindow also detaches its `ILogWindowSink` so the global listener keeps buffering without routing to a dead UI object. The HUD is the exception: it is created eagerly at boot and hides via `SW_HIDE` after real visible sessions because it sits on the transcription hot path.

The tray and global hotkeys cannot be hosted by a `Microsoft.UI.Xaml.Window`: the required Win32 subclassing (`SetWindowSubclass`) is incompatible. The canonical solution is a Win32 message-only window (`MessageOnlyHost`, parent `HWND_MESSAGE`) created in `App.OnLaunched`. Invisible by construction — no possible flash, no off-screen trick. `TrayIconManager.Register(hwnd)` and `HotkeyManager` attach onto it.

## LogWindow

`LogWindow.xaml(.cs)` is the live window for visualising EventSource events. Resizable `OverlappedPresenter`, min 400×300, `MicaBackdrop`, system theme (light/dark auto, no forced `RequestedTheme`). Close is real destruction; the listener's ring buffer lives outside the window and is replayed when a fresh LogWindow attaches on the next open.

Native TitleBar `Microsoft.UI.Xaml.Controls.TitleBar` (WindowsAppSDK 1.8). The app icon lives in `TitleBar.IconSource`: `ImageIconSource` fully rebuilt on each idle/recording toggle (mutating `ImageSource` in place does not propagate visually). `AppWindow.SetIcon` follows the same state. Live search is below the TitleBar in the LogWindow content.

Below the TitleBar, two zones. On the **left**, a `SelectorBar` All / Activity / Alerts (initial selection All — everything passes through). On the **right**, a `CommandBar` with `IsDynamicOverflowEnabled` and `DynamicOverflowOrder`: the Copy/Save/Clear group migrates to overflow before the AutoScroll/Wrap group. Segoe Fluent glyphs: Copy `E8C8`, Save `E74E`, Clear `E74D`, AutoScroll `EC8F` (toggle, on by default), Wrap `E751`.

Two in-memory collections. `_entries` (`List<LogEntry>`) is the full buffer, capped at 5000 entries — on overflow the oldest is removed from both collections by ref equality (`LogEntry` is a class). `_visible` (`ObservableCollection<LogEntry>`) is the projection bound to `ListView.ItemsSource`. The filter is `Matches()`, combining SelectorBar + live search (`IndexOf` case-insensitive, 200 ms debounce to avoid blocking the UI thread on fast typing). Copy/Save operate on `_visible` — the user copies what they see. Copy writes through `Deckle.Core.Interop.Win32Clipboard`, the verified Win32 writer shared with the transcription engine (a concrete `CF_UNICODETEXT` global handle plus an immediate length read-back), not the WinRT `Clipboard.SetContent`/`DataPackage` API. The WinRT path wrote unverified and relied on delayed rendering, which truncated or failed silently on a full selection — the bug this replaced. A write that does not reach the clipboard surfaces on the floating badge (`Copy failed`) and a `LogWindowWarning`.

The data model wraps `EventEntry` produced by the `Deckle.Diagnostics` listener. The level is native `EventLevel` (Critical / Error / Warning / Informational / Verbose); there are no longer any application levels Success / Narrative — the LogService era is over. The mapping `Provider` → short source label (`"Deckle.Whisp"` → `"WHISP"`, `"Deckle.App"` → `"APP"`) lives in `LogLineFormatter`; it is applied once at construction and precomputed in `Text` (format `HH:mm:ss.fff [SOURCE] message`) to avoid reformatting on every row realisation during virtualisation. Colours are bound via `ThemeResource` in the `DataTemplates` (`Grid.Resources > ThemeDictionaries`), with automatic runtime theme switch.

`LogLevelTemplateSelector` (a C# class inheriting from `DataTemplateSelector`) routes templates by `EventName` for specialised telemetry rows (Latency / Corpus / Microphone) and by `EventLevel` for the rest. The Wrap toggle swaps `ItemTemplate` between `NoWrapRoot` and `WrapRoot`. WinUI 3 pitfall: `ItemsControl.ItemTemplateSelector` is not honoured at runtime (only `ListViewBase` respects it). The workaround is in place: `ItemTemplate` points to a `ContentControl` wrapper whose `ContentTemplateSelector` is the actual selector.

The Wrap toggle also switches `HorizontalScrollBarVisibility` between `Auto` and `Disabled`. Without that, `TextWrapping="Wrap"` does not apply — the `ScrollViewer` measures its content at infinite width as long as horizontal scrolling is allowed. **Shift+wheel = deliberate native WinUI 3 behaviour**: the `ListView`'s internal `ScrollViewer` scrolls vertically, not horizontally, because WinUI 3 does not expose Tunnel/Preview routing to intercept `PointerWheelChanged` before the inner SV consumes it. Any custom attempt (horizontal re-injection via `ChangeView`, baseline sync via `ViewChanged`) produces a jerky/inverted visual effect on each wheel notch — worse than plain native behaviour. To browse a long line without wrapping, use the horizontal scrollbar, or enable the Wrap toggle.

Bottom padding `12,4,12,24` on the ListView. The 24 px bottom margin prevents the floating horizontal scrollbar (~12 px) from overlapping the last entry, which is precisely where new lines appear when AutoScroll is on.

A `LogWindow` that was never shown has no initialised layout — `LogScrollViewer.UpdateLayout()` can only be called after the window has been shown at least once (flag `_isVisible` in place). The lazy-windows pattern is ratified by [ADR-0001](../../docs/adr/0001-lazy-secondary-windows.md).

## HudWindow — host-side usage

The `HudWindow` class now lives in `Deckle.Hud` (extracted from the host during the mapping cleanup). The host instantiates the singleton once in `OnLaunched` and never destroys it. UI handlers are marshalled via `DispatcherQueue.TryEnqueue` because `TranscriptionEngine` events come from background threads. Internal window detail: WinUI 3 `Window` of about 320×64, positioned bottom-centre via `DisplayArea.Primary.WorkArea`, as a non-resizable `OverlappedPresenter`, with `ExtendsContentIntoTitleBar=true`.

To show the HUD, the sequence is `MoveAndResize` then `ShowWindow(SW_SHOWNOACTIVATE)` followed by `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE)`. `HWND_TOPMOST` reasserts the native topmost band on every visible transition; `SWP_NOACTIVATE` keeps the no-focus-steal invariant. Never `SetForegroundWindow` for the HUD itself. To hide it, `ShowWindow(SW_HIDE)`. The details (progressive colouring of the timer, mouse-proximity fade via Raw Input and layered alpha with smoothstep, layered shadow constraint, notification regressions) live in [src/Deckle.Hud/CLAUDE.md](../Deckle.Hud/CLAUDE.md).

## Lifetime — `App.xaml.cs`

The order in `OnLaunched` is sensitive because it crosses several invariants: settings migration must run before any service touches its file, the telemetry listeners need their gates wired before writes are allowed, the `MessageOnlyHost` must exist before hotkeys are registered, and the tray must have its callbacks wired before `Register`. The canonical sequence is: migration `SettingsBootstrap.MigrateLegacyToPerModule()` first, telemetry/storage wiring, first-run gate (wizard if natives or models are missing), instantiation of `TranscriptionEngine`, creation of the eager HUD only, leaving LogWindow and SettingsWindow and PlaygroundWindow lazy, creation of the `TrayIconManager` (callbacks only, not yet `Register`), wiring engine events → tray + windows, creation of the `MessageOnlyHost`, then `tray.Register(messageHost.Hwnd)` and `hotkeyManager.Register()`, then application of the persisted theme and of the calibration level window, then conditional opening of Settings if `--settings` is passed on the CLI.

Three global diagnostic safety nets are set up in the `App` constructor: `Application.UnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`. All three route through `DeckleAppSource`. Without these safety nets, an exception that surfaces in a `TranscriptionEngine` handler can disappear silently.
