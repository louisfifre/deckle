---
name: claude-deckle-playground
description: "Doctrine for Deckle.Playground, the dev-only tuning and diagnostics sandbox surface. Read before touching the shell, the page routing, or any tuning knob wired to a live pipeline."
type: agent-instructions
module: Deckle.Playground
---

# CLAUDE.md — Deckle.Playground

Developer tuning surface for the app. Hosts `PlaygroundWindow` (NavigationView Auto + Frame of pages), three owned pages (`HomePage` landing, `HudPage` for stroke composition, `AmbientPage` for screen capture + Hue + HDR tuning), a static `PlaygroundShell` that serves as a navigation routing point on the page side, and two ViewModels (`HudViewModel`, `AmbientViewModel`). The module is dev-only: it exists so Louis can adjust live the parameters of the pipelines running in the app without a rebuild, and to serve as an antechamber for UX overhauls before they migrate into Settings or another final user-facing surface.

The detailed architecture (shell + 3 pages, Tag → Type.GetType pattern, partial MVVM targeted at what pays off, real destruction on close) borrows the routing shape of `Deckle.Settings`. The Playground holds heavy runtime resources and is rebuilt fresh on the next open; read `src/Deckle.Settings/CLAUDE.md` before touching shared shell patterns. The Settings doctrine (Auto-save everywhere, NavigationView Auto, SettingsCard / SettingsExpander, H1 header per page) does not apply verbatim because the Playground is not a final user settings page — it is a tuning workshop with dense sliders, live previews, and programmatically generated panels.

## Non-negotiable patterns

**No HUD persistence.** Every tuning value manipulated in `HudPage` lives in memory for the duration of the process and dies at app exit. The "Reset all" button and per-section Reset buttons snap to the compiled defaults. The purpose of the Playground HUD is to find the right defaults, not to memorize them — a setting that proves correct migrates into the code as a new default, the Playground stores nothing. If per-session persistence becomes necessary, that is a new dedicated service (`PlaygroundSettingsService`), never a drift toward `AmbientSettingsService` or an ad-hoc file.

**Ambient persistence via the shared service.** `AmbientPage` reads and writes `AmbientSettingsService.Instance.Current` (file `<UserDataRoot>/modules/ambient/settings.json`). The knobs tuned in the Playground are the same source of truth as those of the Ambient Settings page — any modification applies live to the `AmbientEngine.Current` engine running in the host app. The propagation pattern goes both ways: the Playground observes `AmbientSettingsService.Changed` to reflect modifications made from Settings, and the Settings page does the same for those made from the Playground.

**Targeted MVVM, not mechanical.** `HudViewModel` is limited to transport state and the current target (`CurrentTarget`, `IsPlaying`, computed properties `IsPlayEnabled` / `IsPauseEnabled` / `IsStopEnabled`, `CurrentTargetLabel`). `TuningModel` remains a mutable POCO with public fields directly manipulated by the slider lambdas in the code-behind of `HudPage` — `[ObservableProperty]` wrapping rejected because the tuning panel is built programmatically without `x:Bind`, so the duplication buys no binding value. `AmbientViewModel` on the other hand follows the full pattern of `GeneralViewModel` (partial properties, `_isSyncing` flag, `Load()` / `PushToSettings()` via the `partial void OnXxxChanged`) because the sliders touch a persisted store and the centralization of writes + side-effects (flip to `AmbientMode.Custom` on every tuning) makes the pattern worthwhile.

**HUD toolbar: flat target flyout + enable-bound transport.** The target picker is a `DropDownButton` with a flat `MenuFlyout`: four HUD states (Charging/Recording/Transcribing/Rewriting), separator, then three primitives (Conic/ArcMask/Combined). The flat shape is intentional because switching target is a high-frequency tuning action. The transport is two explicit Start / Stop buttons whose `IsEnabled` is bound to computed properties of the VM — no toggle that swaps its icon under the cursor. The tuning panel only shows the `SettingsExpander` relevant to the active target (mapping defined by `HudViewModel.ActiveTuningSections`), with the Parked expander always appended at the bottom (collapsed by default) for transition values rarely touched.

**Page contract inside one window session.** Each page inherits from `Page`, declares `NavigationCacheMode = NavigationCacheMode.Required` in the constructor, and survives navigations in the `Frame` for the lifetime of that Playground window. The runtime resources (naked composition preview, screen capture service, frame sampler, Hue REST output, preview cells) live as instance fields of the page and are disposed via `DisposeResources()` when `PlaygroundWindow.Closed` fires. `OnNavigatedTo` reloads the ViewModel from the settings service; `OnNavigatingFrom` stops the off-screen UI timers without touching the canonical pipeline that keeps pushing to Hue.

## PlaygroundShell — delegate registry

Mirror of `SettingsHost`. A static class with a single delegate today (`NavigateTo` invoked by `HomePage` to switch the NavView selection to HUD or Ambient). The pattern keeps `HomePage` from holding a direct reference to `PlaygroundWindow`. If other callbacks emerge, they are added as named typed static fields — no string dictionary, no disguised Service Locator. The shell sets the delegate in the constructor and removes it on `Closed` so that a surviving page does not route to a destroyed window.

## Window lifetime

Lazy instance created on the first `ShowPlaygroundLazy()` call. Close is real destruction, not hide: `PlaygroundWindow.Closed` disposes page resources and `App.xaml.cs` clears `_playgroundWindow`, so the next open constructs a fresh window. The host also restores/saves native Win32 `WINDOWPLACEMENT` so the fresh window returns to the user's previous size/position without adding a Playground settings file. This is intentional because Playground resources are heavy and dev-only; durable Ambient values already live in `AmbientSettingsService`, while HUD tuning remains process-memory only and resets with the new page instance.

## Accepted limitations

No full localization: the dense strings in the SettingsExpander, the slider labels, the card captions, the RadioButton texts are hard-coded in English. Only the high-level titles (TitleBar, NavView items, page H1, headers and descriptions of the Home cards) go through `x:Uid` + `Strings/en-US/Resources.resw`. The project localization backlog has explicitly left Playground out of V0.1 — the full migration will come in a dedicated pass aligned with the other surfaces. The FontIcon glyph icons go through `Deckle.Catalog` (XAML: `{StaticResource Icon.X}`, code-behind: `Glyphs.X`), no hard-coded code-point outside `Icons.xaml` / `Glyphs.cs`.

No automated tests on this module: the Playground being a dev surface and its rendering being essentially visual + interactive, verification remains manual for now. The upcoming EventSource logging + tests refactor first targets the critical production modules (`Deckle.Audio`, `Deckle.Transcription`, `Deckle.Lighting.Ambient` for the Hue pushes); Playground will come if a visible regression motivates it.
