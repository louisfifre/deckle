---
name: claude-deckle-setup
description: "Doctrine for Deckle.Setup, the first-run wizard and provisioning primitives (native runtimes, models). Read before touching the wizard flow, the SetupWindow shell, or any provisioning primitive orchestrated from it."
type: agent-instructions
module: Deckle.Setup
---

# CLAUDE.md — Deckle.Setup

Deckle's first-run wizard. `SetupWindow` (three-row shell: header + Frame + footer Cancel/Back/Next) with three frame-navigated pages (`ChoicesPage`, `InstallingPage`, `SummaryPage`). The module owns its own `SetupContext` (wizard state shared across pages) and orchestrates provisioning primitives supplied by other modules: `Downloader` and `ModelEntry` (generic) on the `Deckle.Transcription` side, `NativeRuntime` and `SpeechModels` (whisper-specific) on the `Deckle.Transcription.Whisper` side. Detached from `Deckle.App` during the mapping cleanup so that the `GeneralPage` of Settings can reopen the wizard without the host app dragging the XAML along.

## Wizard role

Deckle cannot transcribe without three families of post-install artifacts: a native whisper.cpp runtime (8 DLLs, ~50 MB), a Whisper model (~150 MB for `base`, ~3 GB for `large-v3`), and a Silero VAD (~700 KB). The shipped binary is empty of these three pieces — the wizard provisions them under `<UserDataRoot>`. No degraded mode: without a model nothing useful can be done. The wizard is therefore **blocking** at first launch, and reachable on demand from Settings once it has been completed (for model swap, location change, or native re-import).

## Structural decisions

**Linear wizard inside a dedicated Mica `Window`.** Not `ContentDialog` (too lightweight for > 3 steps), not a persistent `InfoBar` (the app would be semi-functional), not the PowerToys catalog (our flow is not exploratory). Inspirations: Dev Home `SetupFlow` for the structure, PowerToys `OobeWindow` for visual conventions (Mica, Tall TitleBar, drag region).

**Frame stepper as the step container.** Dev Home's `Orchestrator` ViewModel + `ContentControl`/`DataTemplateSelector` is more testable but requires 3-4 VMs + a selector — disproportionate for 3 pages. A refactor remains possible later if the wizard grows.

**3 steps: global Choices → bulk Install → Summary/errors.** The user makes all their choices before triggering a single batch of downloads; errors bubble up at the end, no inline retry is offered (a global Retry is available from Summary).

**Auto-download of natives by default, fallback to local Browse.** The bundle is published as a GitHub Deckle release tagged `native-vX.Y.Z`; the wizard does an unauthenticated GET on the asset. The bundle reference lives in `NativeRuntime.cs` on the Transcription side: `CurrentBundle = NativeRuntimeBundle(Version, Url, Sha256, SizeBytes, DisplayName)`. Degraded mode: if the Browse button points at a valid folder, the user skips the download.

## UX structure

Window 720×520 centered, `MicaBackdrop`, Tall `TitleBar` without a back button. Grid 3 rows: header (step title h2 + subtitle body secondary), body (`Frame`), fixed footer (Cancel | Back | Install/Next AccentButton).

**Step 1 — Choices.** Combined pattern from Dev Home `RepoConfigView` + VS Installer "Locations". The user makes *all* their choices on a single page: where to install + native runtime status + which model. These choices condition the total size, displayed at the bottom of the page via `InfoBar Severity=Informational` as continuous feedback. The native runtime has three visual states: *Installed* (button `Replace...`), *Will be downloaded* with size in parentheses (button `Use local copy...`), *Missing N file(s)* (button `Browse...`). The third case is only a safety net for dev builds where the Deckle repo has not yet published a native release — `BundleUrlIsPlaceholder` returns `true`, the Next gate forces Browse. Path picked through `TextBox(IsReadOnly=True)` + `Button` opening a `FolderPicker`, model picked through `RadioButtons`, cards through `controls:SettingsCard`. Footer: Cancel | Back disabled | **Install** accent.

**Step 2 — Installing.** Dev Home `LoadingView` pattern. Everything is launched in one block on page arrival, no user interaction during the operation except Cancel. Determinate global `ProgressBar` (`Maximum=3, Value=tasksDone`) + a sub-progress per item with current/total size and percentage. Sequential: native runtime → Whisper model → Silero VAD. The runtime is short (DL ~18 MB + extract), models can take minutes. Cancel = `CancellationTokenSource.Cancel()`, `.partial` files deleted. `SHA-256` verified for the native bundle (hash hardcoded in `CurrentBundle.Sha256`) — not for HuggingFace models (no canonical hash on the upstream side). Footer: Cancel only, Back disabled, Install hidden.

**Step 3 — Summary.** Success: `✓ All set` + recap Location / Runtime / Model / VAD + `Get started` button. Partial failure: `! Some items could not be installed` + line-by-line recap with successes and errors + buttons `[Retry] [Quit]`. No partial-success boot — either everything is OK and the app boots, or the user Retries step 2 or Quits.

## WinUI 3 components by role

| Role | Control |
|---|---|
| Root window | `Window` + `MicaBackdrop` |
| TitleBar | `Microsoft.UI.Xaml.Controls.TitleBar` (Tall, no back button) |
| Stepper | `Frame` |
| Footer | `Grid` 2-col + `Button` Back / `AccentButton` Next |
| Path picker | `TextBox(IsReadOnly=True)` + `Button` → `FolderPicker` |
| Model picker | `RadioButtons` |
| Choice card | `controls:SettingsCard` (CommunityToolkit) |
| Estimated total | `InfoBar Severity=Informational` |
| Global progress | determinate `ProgressBar` (Min=0, Max=3) |
| Download progress | determinate `ProgressBar` (Min=0, Max=ContentLength) |
| Per-item status | `TextBlock` Body / Caption + `TextFillColorSecondaryBrush` |
| Error recap | `InfoBar Severity=Error` + detail `TextBlock` |
| Theme resources | `MicaBackdrop`, `OverlayCornerRadius`, `CardBackgroundFillColorDefaultBrush`, `TextFillColor*Brush` |

## Provisioning primitives (orchestrated from Setup, hosted by Transcription)

Deliberate decoupling: the wizard UI lives in this module, but the primitives that *execute* whisper-specific provisioning (`NativeRuntime`, `SpeechModels`) live in `Deckle.Transcription.Whisper/Setup/` alongside the `IAsrBackend` implementation. The wizard references them directly. When a second ASR backend ships (Voxtral), it will carry its own provisioning primitives in its child module, and the wizard will select the appropriate set based on the chosen backend.

**`NativeRuntime.cs`** — encapsulates ALL knowledge of the whisper DLLs. Exposes `const string EntryDll`, `IReadOnlyList<string> RequiredDllNames` (8 entries), `record NativeRuntimeBundle(Version, Url, Sha256, SizeBytes, DisplayName)`, `static NativeRuntimeBundle CurrentBundle` (single source of truth), `static bool BundleUrlIsPlaceholder`, `static bool IsInstalled()`, `static int CopyFromFolder(string source)`, `static Task<int> InstallFromZipAsync(string zipPath, CancellationToken)`, `static IReadOnlyList<string> GetMissing()`. **Native encapsulation**: this module is the **only** one that names `libwhisper.dll`, `ggml-*.dll`, `libgcc_s_seh-1.dll`, etc. All other consumers (wizard, settings, debug) go through its public methods. If we switch to another stack tomorrow (Vulkan → DirectML, MinGW → MSVC), only this file changes. `NativeMethods.cs` (Core/Interop) stays isolated on the `[DllImport]` and `SetDllImportResolver` side — it knows `"libwhisper"` as a P/Invoke identifier but it is `NativeRuntime` that orchestrates the install. The versioned bundle is produced by `scripts/lib/publish-native-runtime.ps1` (maintainer-only); recompilation recipe in [docs/reference/reference--native-runtime--1.0.md](../../docs/reference/reference--native-runtime--1.0.md).

**`SpeechModels.cs`** — catalog + model resolution. Exposes `record ModelEntry(Id, FileName, Url, SizeBytes, Sha256?)`, `IReadOnlyList<ModelEntry> WhisperModels`, `ModelEntry VadModel`, `bool IsInstalled(ModelEntry)`. No SHA-256 on HuggingFace models in V1 — no canonical hash published on the upstream side, to be added when hashes are pinned upstream or in the catalog.

**`Downloader.cs`** — HTTP primitive `HttpClient` + `IProgress<DownloadProgress>` + `SHA-256` + `.partial` write before atomic rename. Cancel via `CancellationToken` deletes the `.partial`.

**`SetupContext.cs`** — state shared across wizard pages (`Location`, `SelectedModel`, `List<InstallResult> Results`). Passed via `Frame.Navigate(typeof(X), context)` + `OnNavigatedTo` that retrieves it from `e.Parameter`. Pages mutate the context; `SetupWindow` observes it to enable/disable Next or to conclude.

**`CopyFromFolder` vs `InstallFromZipAsync`** — not to be confused. The first is sync, reads a folder whose content is guaranteed by the user (Browse). The second is async, reads a zip whose integrity is guaranteed upstream by `Downloader` (SHA-256 verified). Both converge on `NativeDirectory` but are not interchangeable.

## Wire-up on the App side

```csharp
// App.OnLaunched (first-run gate)
if (!NativeRuntime.IsInstalled() || !SpeechModels.IsDefaultInstalled())
{
    var setup = new SetupWindow();
    setup.Body.Navigate(typeof(ChoicesPage), setup);
    setup.Activate();
    bool success = await setup.Completion;
    if (!success) { Environment.Exit(0); return; }
}

// Settings — "Run setup again..." button
SettingsHost.OpenSetupWizard = () => new SetupWindow().Activate();
```

## Anti-patterns to avoid

- **Inline `FolderPicker`** inside the page. Dev Home/PowerToys pattern: `TextBox(IsReadOnly)` + `Button`.
- **Indeterminate `ProgressBar`** when the total is known. HuggingFace returns `Content-Length` — determinate bar + bytes/total ratio.
- **`DesktopAcrylicBackdrop`** on the setup window. Reserved for transient surfaces (HUD, popups). Setup is persistent → `MicaBackdrop`.
- **Left-pane `NavigationView`** for a linear wizard. The pane suggests free navigation, a false signal for our case.
- **`ContentDialog` without `XamlRoot`** — systematic WinUI 3 crash.
- **`Frame.GoBack()` with visible history.** Back must cancel the previous step's commit, not navigate inside a stack.
- **UI element created off the UI thread.** `HttpClient` callbacks → `DispatcherQueue.TryEnqueue` for any Progress update.
- **Hardcoding DLL names anywhere other than `NativeRuntime`** — violates native encapsulation. The catalog is duplicated on the PowerShell side (`scripts/lib/setup-assets.ps1`, `scripts/lib/publish-native-runtime.ps1`) with a traceability comment; any other duplication is a bug.
- **Hardcoding `#xxxxxx` or numeric `CornerRadius`** in XAML. Theme resources only.

## Observability

All emissions go through `DeckleSetupSource.Log` — provider `Deckle.Setup`, SETUP tag in the LogWindow.

## Out of scope for V1

Parallel native + model download (sequential is enough — the bottleneck is the model). Resuming an interrupted download (the `.partial` is deleted, the user restarts from scratch). SHA-256 verification of HuggingFace models (no canonical hash published). Auto-migration of settings/telemetry from the old `<exe>/config/` layout (clean break, the user copies over if they want to). Runtime language selection in Settings (`ResourceContext` override) — V1 resolves on the Windows display language only.
