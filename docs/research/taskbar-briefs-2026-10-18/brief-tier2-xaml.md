# Brief: Deckle tier 2 engine (XAML Diagnostics inside explorer.exe)

Scope: the in-process XAML engine of Deckle's native DLL. Injection, IPC and the
settings transport belong to another brief; here the DLL is assumed loaded in
explorer.exe and handed a settings document. Target: Windows 11 IoT Enterprise
LTSC 2024, build 26100, native vertical taskbar on the left, Small mode.

Sources were fetched on 2026-10-18 (line numbers refer to those copies):

- Windhawk mods: `https://github.com/ramensoftware/windhawk-mods/blob/main/mods/<mod>.wh.cpp`
  - `windows-11-taskbar-styler` (abbreviated **TS** below)
  - `windows-11-start-menu-styler` (**SMS**)
  - `start-menu-size` (**SMSZ**)
  - `taskbar-icon-size` (**TIS**)
  - `taskbar-notification-icon-spacing` (**NIS**)
  - `taskbar-tray-system-icon-tweaks` (**TSIT**)
  - `taskbar-clock-customization` (**CLK**)
- TranslucentTB ExplorerTAP (**TTB**): `https://github.com/TranslucentTB/TranslucentTB/tree/develop/ExplorerTAP`

Both projects are GPLv3. Deckle is relicensed GPLv3, so their code can be copied
as long as attribution is kept.

Tags: **[V]** = verified in source (file and line or function). **[I]** = inferred, not verified; a probe must confirm it.

---

## (a) Findings

### A1. TAP connection

1. **[V] Connection name loop.** TS `InjectWindhawkTAP` (lines 11395-11444) loops over
   `VisualDiagConnection1..10000` and calls
   `InitializeXamlDiagnosticsEx(name, GetCurrentProcessId(), L"", ownDllPath, CLSID_TAP, nullptr)`
   until the result is not `HRESULT_FROM_WIN32(ERROR_NOT_FOUND)`. Its comment cites
   microsoft-ui-xaml `DXamlCore.cpp#L2782`.
   - TTB `tapsite.cpp Install` does the same over `1..60`, 500 ms apart.
2. **[V] One initialization per thread.** TTB comment: "XAML Diagnostics can only be
   initialized once per thread; future calls simply return S_OK without doing anything".
   - TTB therefore calls ixde on a fresh `std::thread`, then joins it.
   - TS calls it from the thread that triggers init. A `g_initialized` flag makes it
     inject only once (lines 20008-20029).
3. **[V] One TAP connection per process.** The TS settings text (line 611) says there
   can "only be one consumer at a time".
   - TS hooks `InitializeXamlDiagnosticsEx` (lines 20225-20341) and reads the calling
     module through `__builtin_return_address`. It then alerts, blocks (returns S_OK
     without doing anything) or allows.
   - Known competitors: TranslucentTB, the Windhawk taskbar styler, and Visual Studio
     Live Visual Tree when a debugger is attached.
4. **[V] SetSite.**
   - TS (lines 11260-11302): `SetSite` unadvises any previous watcher, creates a new one,
     then calls `FreeLibrary(ownModule)` to undo the extra refcount that ixde added. As a
     result the DLL stays unloadable.
   - TTB keeps that pin on purpose ("pins the DLL forever"), and throws if a watcher
     already exists.
5. **[V] Advise from another thread.** AdviseVisualTreeChange must not run on the
   calling UI thread.
   - TS (lines 11063-11098) runs it from a `CreateThread`. Its comment: calling it from
     the current thread hangs in `Advising::RunOnUIThread`.
   - TTB uses a detached thread: "AdviseVisualTreeChange is special-cased to bring us to
     the UI thread for the callback".
   - So callbacks arrive on the owning UI thread.
6. **[V] DisableCompositionDiag trick.** TS (lines 10949-10958 and 20343-20401):
   - The problem: composition diagnostics corrupt the heap when several UI threads add
     DComp visuals.
   - `XamlDiagnostics::CreateCompVisualDiag` skips them when
     `HKLM\Software\Microsoft\XAML\Debug\DisableCompositionDiag=1`. The value is read
     once, inside AdviseVisualTreeChange.
   - TS hooks `RegOpenKeyExW` (it substitutes `HKLM\Software\Microsoft` when the Debug
     key is missing) and `RegQueryValueExW` (it returns DWORD 1). Both hooks act only
     while the thread_local flag `g_reportCompositionDiagAsDisabled` is set around the
     Advise call.
7. **[V] Running code on a window's thread.** `RunFromWindowThread` (TS lines 20033-20081):
   - install `SetWindowsHookEx(WH_CALLWNDPROC, proc, nullptr, GetWindowThreadProcessId(hwnd))`;
   - `SendMessage(hwnd, RegisterWindowMessage(L"..."), 0, &param)`;
   - `UnhookWindowsHookEx`.

   The windows to target:
   - the taskbar island: `FindWindowEx(Shell_TrayWnd, 0, L"Windows.UI.Composition.DesktopWindowContentBridge", 0)`;
   - each `XamlExplorerHostIslandWindow` (lines 20403-20468).
8. **[V] Late creation.** XAML windows can appear after the DLL loads. TS hooks
   `CreateWindowExW`, `CreateWindowInBand` and `CreateWindowInBandEx` (lines 20083-20117)
   and starts init when either of these windows is created:
   - a DesktopWindowContentBridge whose parent is `Shell_TrayWnd`;
   - an `XamlExplorerHostIslandWindow`.
9. **[V] Callback contract.** `OnVisualTreeChange` (TS lines 11151-11244):
   - On Add: `GetIInspectableFromHandle` gives a `FrameworkElement`; the style is then
     applied.
   - On Remove: only `element.Handle` is valid (TTB says the same).
   - Always return S_OK: "Returning an error prevents (some?) further messages".
   - The callee frees the BSTRs.
10. **[V] Releasing element references.** TS (lines 11005-11025) uses the undocumented
    `IXamlDiagnosticsTestHooks` {735941a2-3ee3-495a-8da9-972627003075},
    `UnregisterInstance`, to release diagnostics references. It avoids
    `GetHandleFromIInspectable`, which re-pins the element.
11. **[V] Settings change.** TS `Wh_ModSettingsChanged` (lines 20612-20769):
    - Unadvise (uninit);
    - per thread: restore the originals, then reload the rules;
    - re-inject.

    **[I]** Re-advising replays the whole live tree as Add events. TS relies on this.
    I did not find it written anywhere in Microsoft documentation.
12. **[V] Explorer restart.** The DLL dies with explorer.exe. Re-injection belongs to the
    injection brief.

### A2. Rule model and value conversion

1. **[V] Selector syntax.** TS `ElementMatcherFromString` (lines 18976-19069):
   - a single matcher: `Type#Name@VisualStateGroup[Prop=Value][N]`;
   - a path: matchers joined with ` > `; `*` matches any chain of ancestors (it
     backtracks).
   - Matching (lines 15935-16110):
     - the type is compared with `winrt::get_class_name(element)`;
     - the name with `element.Name()`;
     - the index with `VisualTreeHelper::GetChild(parent, N-1) == element`;
     - `[Prop=Value]` with `ReadLocalValue`;
     - ancestors are walked with `VisualTreeHelper::GetParent`.
2. **[V] Short type names.** `AdjustTypeName` (lines 19143-19169):
   - a bare name expands to `Windows.UI.Xaml.Controls.X` (`Rectangle` gives `...Shapes.Rectangle`);
   - prefixes: `taskbar:`, `systemtray:`.
3. **[V] Value conversion.** `GetStyleFromXamlSetters` and `GetResolvedPropertyOverrides`
   (lines 15466-15650) build this markup:

   ```xml
   <ResourceDictionary ... xmlns:t="using:Taskbar">
     <Style x:Key="s" TargetType="t:TaskbarFrame">
       <Setter Property="Fill" Value="Transparent"/>   <!-- string form -->
       <Setter Property="RenderTransform"><Setter.Value>...</Setter.Value></Setter>
     </Style></ResourceDictionary>
   ```

   - It loads the markup with `XamlReader::Load`, then reads `setter.Property()` (a
     `DependencyProperty`) and `setter.Value()` (the converted value).
   - The XAML parser resolves the property and converts the string. So the engine needs
     no type table.
   - If 0x802B000A comes back (the type cannot be resolved), it retries with a fallback
     type.
   - Results are cached lazily per type.
4. **[V] Applying values.** `ApplyCustomizationsForVisualStateGroup` (lines 17552-17762):
   - save `ReadLocalValue` as the original, then `SetValue`;
   - `RegisterPropertyChangedCallback` re-asserts the value when Explorer's code
     overwrites it. A re-entrancy guard (`g_elementPropertyModifying`) prevents loops;
   - per-state values attach to `VisualStateGroup.CurrentStateChanged`.
5. **[V] Resources.** `ProcessResourceVariable` (lines 19419-19522):
   - converts the value with `XamlBindingHelper::ConvertValue(TypeName of the existing resource)`;
   - then `Application::Current().Resources().Insert(key, value)`;
   - keeps the originals so they can be restored.

### A3. Louis's wants, one by one

#### Background: black or transparent. Tier 2.

- **[V]** TS theme SimplyTransparent (lines 721-726):

  ```
  Taskbar.TaskbarFrame > Grid#RootGrid > Taskbar.TaskbarBackground > Grid > Rectangle#BackgroundFill   Fill=Transparent
  ... > Rectangle#BackgroundStroke                                                                     Fill=Transparent
  ```

- Black is `Fill=Black`.
- **[V]** TTB targets the same two rectangles (`visualtreewatcher.cpp`).

#### Tray icons: width 24, one row/column. Tier 2, with property-changed callbacks.

- **[V]** Path, from NIS (lines 648-701 and 829-881):

  ```
  SystemTray.SystemTrayFrame > Grid#SystemTrayFrameGrid > ...#NotificationAreaIcons > ItemsPresenter > <WrapGrid> > ContentPresenter > SystemTray.NotifyIconView#NotifyItemIcon
  ```

- **[V]** Vertical tray detection: visual state group `OrientationStates`, state
  `VerticalOrientation`, on `SystemTrayFrameGrid` (NIS line 296).
  - In vertical mode, "width" means the icon height and rows become columns (NIS readme).
- **[V]** Properties to set in vertical mode (NIS lines 342-393 and 506-578):
  - `NotifyItemIcon` MinHeight=24 and MinWidth=0;
  - `ContainerGrid` Padding=0 for TextIconContent and ImageIconContent;
  - WrapGrid `ItemHeight=24`;
  - WrapGrid `MaximumRowsOrColumns=1`, ItemWidth = frame width / columns;
  - an edge inset of 4 (lines 582-640).
- **[V]** Explorer rewrites these values from code. NIS hooks
  `NotifyIconView::ApplyLayoutMode` (it resets MinHeight, lines 976-997),
  `UpdateNotificationAreaIconsLayout` and `ApplyPositionAwareHorizontalPadding`
  (lines 1006-1067).
  - **[I]** A property-changed callback on MinHeight, ItemHeight and
    MaximumRowsOrColumns replaces those hooks. The probe for that is task T6.
- **[V]** NIS says: "The heights of the other icons in a vertical tray are set by the
  taskbar for each layout." So it only touches NotificationAreaIcons in vertical mode.

#### Quick-settings group (network, volume, battery). Tier 2 to tighten or hide; moving is out of reach.

- **[V]** Path (NIS lines 703-773, TSIT):

  ```
  SystemTray.OmniButton#ControlCenterButton > Grid > ContentPresenter#ContentPresenter > ItemsPresenter > StackPanel > ContentPresenter > SystemTray.IconView#SystemTrayIcon > Grid#ContainerGrid > Grid#ContentGrid > SystemTray.TextIconContent > Grid#ContainerGrid
  ```

  - Tightening = inner `ContainerGrid` Padding (NIS formula: 4, clamped at 0 under
    width 24) plus the outer `StackPanel` Spacing.
- **[V]** TSIT hides individual icons; it does not move them.
  - An icon is identified by its glyph in `... > Base > InnerTextBlock` (Text):
    - volume: E74F, E992-E995, ...;
    - network: E709, E839, ...;
    - battery: class `SystemTray.BatteryIconContent`.
  - Hiding sets `Visibility=Collapsed` and `IsEnabled=false`. Spacing is folded into
    negative margins, and an empty `ControlCenterButton` collapses.
  - It re-runs on `StackPanel.SizeChanged` and on a Text property-changed callback,
    because icons swap glyphs.
- **[V]** Siblings under SystemTrayFrameGrid (TSIT): NotificationAreaIcons,
  NotifyIconStack, MainStack, NonActivatableStack, ControlCenterButton,
  NotificationCenterButton, ShowDesktopStack.
- **[I]** "Move elsewhere":
  - XAML can only change the order inside the frame: Grid.Row/Column if
    SystemTrayFrameGrid is a Grid (TS themes target both `StackPanel#` and
    `Grid#SystemTrayFrameGrid`, so the type varies by build), or margins/transforms.
  - Moving the group out of the tray is not possible: the flyout anchors to the button.

#### Clock. Styling is tier 2. The text is tier 2 by an unproven method, tier 3 otherwise.

- **[V]** Path (CLK lines 4844-4936):

  ```
  SystemTray.DateTimeIconContent > Grid#ContainerGrid > StackPanel > TextBlock#TimeInnerTextBlock / TextBlock#DateInnerTextBlock
  ```

  - XAML-settable properties (lines 4686-4842):
    - StackPanel `Spacing` (TextSpacing=4) and `MaxWidth`;
    - TextBlock FontSize (12 / 18), FontFamily, FontWeight, Foreground, TextAlignment,
      LineHeight with `LineStackingStrategy=BlockLineHeight`, TextWrapping,
      Visibility, CharacterSpacing.
- **[V]** How CLK sets the text: it hooks `GetTimeFormatEx` and `GetDateFormatEx`
  (lines 5098 and 5125) and returns the custom lines only inside the symbol-hooked
  `ClockSystemTrayIconDataModel::RefreshIcon` (lines 5695-5702).
  - A `GetLocalTime` hook forces a refresh every second.
  - So it relies on PDB symbols. That is tier 3.
- **[I]** Tier 2 option: call `RegisterPropertyChangedCallback(TextBlock::TextProperty)`
  on both TextBlocks and write `Text` from a string the DLL formats itself
  (`GetDateFormatEx` with "dd" / "ddd", `GetTimeFormatEx` with "HH:mm"), guarded
  against re-entrancy.
  - Precedent: TSIT puts a Text callback on `InnerTextBlock`, but only to read the
    value, never to write it.
  - Risks (all **[I]**):
    - the view model may write Text through a binding. Setting a local value then
      breaks the binding, which stops the refreshes that trigger the callback;
    - refreshes arrive at most once a minute (enough for HH:mm);
    - the tooltip and the accessibility name still show the original format.
  - Safety net: a 60 s DispatcherQueue timer that rewrites the text.
- **Vertical layout [I].** No vertical-specific code in CLK (grep for "Vertical" found
  nothing).
  - On a 32 px bar, a TextBlock wider than the bar gets clipped or ellipsized. So the
    lines must stay narrow: "HH" / "mm" or "17" / "sam".
  - Louis's current settings already produce several stacked lines; memory says the
    clock was fixed through settings alone.
  - Tier 2 can also add lines, by putting `\n` in Text with TextWrapping. Probe needed.

#### Start menu: 580x1080, no Recommended section. Tier 2, but in another process.

- **[V]** The host is `StartMenuExperienceHost.exe` (SMSZ and SMS, `@include`).
  - SMS runs its own TAP in that process (`InjectWindhawkTAP`, line 7888, same
    VisualDiagConnection loop).
  - It triggers on creation of a `Windows.UI.Core.CoreWindow` window (lines 15558-15569).
  - For SearchHost, init must be posted through `RunFromWindowThreadViaPostMessage`
    ("too early").
- **[V]** Size, done by direct API calls in SMSZ:
  - Classic (Window content = Canvas):
    - `StartDocked.StartSizingFrame` Width / MinWidth / MaxWidth = 580-24 and
      Height / MinHeight / MaxHeight = 1080-24 (12 px padding per edge);
    - newer builds also need `StartDocked.LauncherFrame > RootPanel > RootGrid`
      Width / MinWidth / MaxWidth;
    - clear `RootContent.MinWidth`.

    Source: SMSZ lines 324-438.
  - Redesigned (content = `StartMenu.StartBlendedFlexFrame`):
    - `FrameRoot` Height, Min, Max and Margin=0;
    - `FrameRoot > AnimationRoot > MainMenu` Width (minimum 270).

    Source: lines 440-529.
  - Re-apply triggers (lines 549-587): on `Window.VisibilityChanged(visible)`, set a
    pending flag, then apply on the next `LayoutUpdated`.
  - Init is triggered by a `RoGetActivationFactory("Windows.UI.Xaml.Hosting.XamlIsland")`
    hook (lines 620-650).
- **[V]** NoRecommendedSection fallback (SMS lines 732-759):
  - Redesigned:
    - `Grid#TopLevelSuggestionsListHeader` Height=0;
    - `Grid#TopLevelSuggestionsRoot` Grid.Row=0, MaxHeight=54 and a TranslateTransform;
    - `Grid#ShowMoreSuggestions` Margin=0.
  - Classic:
    - `TopLevelSuggestionsListHeader`, `NoTopLevelSuggestionsText`, `RecommendedList`
      and `ShowMoreSuggestions` get Visibility=Collapsed;
    - `TopLevelSuggestionsContainer` gets Height=0.
- **[I]** Which start menu (classic or redesigned) build 26100 LTSC shows: to check
  through the probe.
- **[I]** The DLL needs an ACL that grants ALL APPLICATION PACKAGES read and execute,
  because StartMenuExperienceHost is an AppContainer-like packaged process. This belongs
  to the injection brief.

#### Taskbar button width 24. Tier 3 for app buttons.

- **[V]** TIS:
  - swaps `SmallTaskbarButtonExtent` / `MediumTaskbarButtonExtent` inside a hook of the
    inlined `IMap::Lookup` (lines 278-335, found by symbol in Taskbar.View.dll);
  - and patches `TaskListButton` instance fields at an offset found by disassembling
    `UpdateIconColumnDefinition` (lines 1533-1729).
  - So the extent is cached in code, not read from XAML.
- **[V]** Start, Task View and Search: `ExperienceToggleButtonRootPanel` /
  `SearchBoxButtonRootPanel` Width = extent + padding - 4 (-3 for Start) are plain
  XAML properties (lines 2044-2197). That part is tier 2.
- **[I]** A tier-2 attempt for app buttons would insert `SmallTaskbarButtonExtent=24`
  into `Application.Current().Resources` before buttons are built. Existing buttons
  would keep their cached value, and the code path probably bypasses the global
  dictionary. Not worth building before the tier 3 brief.
- **[I]** On a vertical bar the extent probably drives the button height. Memory says the
  bar width already comes from the TIS GetFrameSize patch.

---

## (b) Design decisions

| # | Decision | Reason | Rejected alternative (one line) |
|---|---|---|---|
| D1 | Port TS's TAP plumbing (connection loop, SetSite with FreeLibrary, Advise from a worker thread, test-hooks release) almost verbatim, with attribution | It has survived years of Explorer updates; GPLv3 allows it | TTB's version: keeps the DLL pinned, so it cannot be unloaded for an update |
| D2 | Each connection attempt runs on a fresh thread; the per-thread rule setup runs on each XAML window thread via RunFromWindowThread | The once-per-thread rule [V]; callbacks land on the UI thread anyway | A single long-lived worker thread: a second init on it silently does nothing |
| D3 | Fake DisableCompositionDiag with thread-scoped RegOpenKeyExW / RegQueryValueExW hooks (needs the hook library from the injection brief) | Avoids heap corruption [V] without changing the machine | Writing the real HKLM key: needs admin, affects every XAML app, survives uninstall |
| D4 | Hook InitializeXamlDiagnosticsEx to detect a competing consumer; report it, never block it | Louis must see "TranslucentTB / Windhawk styler is active" rather than a silent failure | Block like TS: breaks the other tool without telling anyone |
| D5 | Rules = a subset of the TS syntax: `Type#Name[N]` joined by ` > `, `*`, `@VisualStateGroup` for per-state values, `[Prop=Val]` filter; values as XAML strings | Reuses a proven parser and lets TS themes be pasted in | A typed JSON schema per property: a type table to maintain for no gain |
| D6 | Convert values through a synthetic `<Style><Setter>` loaded by XamlReader, cached per type | The XAML parser resolves the property and converts the value [V] | XamlBindingHelper.ConvertValue alone: needs the DependencyProperty and target type known ahead |
| D7 | Persist values with SetValue + RegisterPropertyChangedCallback + a re-entrancy guard; save originals with ReadLocalValue | Explorer's code resets tray values [V]; restoring needs the originals | Hooking the resetting methods (NIS style): tier 3, symbols |
| D8 | Settings change = Unadvise, restore everything, reload rules, re-Advise (Add replay) | Same flow as TS; one code path for "first time" and "changed" | Diffing rules live: more code, more bugs, no visible benefit |
| D9 | Per-rule status: matched / applied / reverted-by-code / never-matched after a 10 s settle following Advise ("not applied on this build"), plus errors (conversion, property not found); sent back over the injection IPC | Louis needs to know which want worked on this build | Logging only: invisible from Settings |
| D10 | Wants that need logic (clock text, quick-settings hiding by glyph, tray single column) become named "behaviors" in C++, not rules | Their logic (glyph tables, formatting, frame width / columns) does not fit `Prop=Value` | A generic expression language like TS `{{}}`: too large for four behaviors |
| D11 | Start menu = the same engine in a second process (StartMenuExperienceHost), triggered on CoreWindow creation | SMS proves this works [V] | Size by registry alone: no registry setting gives an exact 580x1080 |
| D12 | Button width 24 stays tier 3; tier 2 only adjusts the Start / Task View / Search root panels | The extents are cached in code [V] | Inserting the resource into Application.Resources: unlikely to work [I], fragile |

---

## (c) Implementation tasks

The other briefs own these (they are dependencies here): the C++ DLL project, its
build, the injection, the hook library (MinHook or similar) and the IPC. Every
on-machine test: **disable the Windhawk "Windows 11 Taskbar Styler" mod and quit
TranslucentTB first** (D4). Restarting Explorer is Louis's act.

### T1. TAP core: connect and observe (M)
- **Goal:** inside explorer.exe, the DLL connects to XAML Diagnostics and receives
  Add/Remove events for the taskbar tree.
- **Done when:** a log lists every Add with type and name (`Taskbar.TaskbarFrame`,
  `Rectangle#BackgroundFill`, `SystemTray.SystemTrayFrame`...) and a clean Unadvise
  and unload leaves Explorer running.
- **Files:** `<native dll>/xamldiag/tap.{h,cpp}` (CLSID, class factory, SetSite),
  `watcher.{h,cpp}` (IVisualTreeServiceCallback2), `connect.cpp`.
  The exact folder follows the native DLL brief.
- **Steps:**
  1. Generate a Deckle CLSID and export `DllGetClassObject`.
  2. Port `InjectWindhawkTAP`: own module path, VisualDiagConnection1..10000 loop, each
     attempt on a new thread that is joined.
  3. SetSite: unadvise the old watcher, create the new one, `FreeLibrary(self)`.
  4. Watcher: Advise from `CreateThread`, with the composition flag set (T2).
  5. OnVisualTreeChange: always S_OK; on Add, `GetIInspectableFromHandle` and dispatch;
     release through `IXamlDiagnosticsTestHooks::UnregisterInstance`; free the BSTRs.
  6. RunFromWindowThread on the taskbar island and on each XamlExplorerHostIslandWindow.
  7. CreateWindowExW / CreateWindowInBand(Ex) hooks for windows created later.
- **Risks:**
  - a hang if Advise runs on the UI thread;
  - a competing consumer swallows the connection (T3);
  - an Explorer crash in Louis's session (every test is a live Explorer).
- **Louis verifies:** with the styler disabled, enable the module, read the event log,
  check that the taskbar still responds, disable the module, check that Explorer
  survives.
- **Depends on:** the native DLL brief (injection, hooks, logging).

### T2. DisableCompositionDiag fake (S)
- **Goal:** while Advise runs, XAML reads DisableCompositionDiag=1.
- **Done when:** a log inside the hooks shows the faked key read during Advise and
  nothing outside it; no heap corruption after many open/close cycles of the tray
  overflow.
- **Files:** `xamldiag/compdiag.cpp`.
- **Steps:**
  1. thread_local flag.
  2. `RegOpenKeyExW` hook: when the flag is set, the key is `Software\Microsoft\XAML\Debug`
     and opening fails, return HKLM\Software\Microsoft.
  3. `RegQueryValueExW` hook: for `DisableCompositionDiag`, return DWORD 1.
  4. Hook the kernelbase exports (TS lines 20343-20401).
- **Risks:** a hook left active outside the flag; a different value name on 26100 [I].
- **Louis verifies:** open the tray overflow, notification center and calendar 20 times
  without an Explorer crash.
- **Depends on:** T1 and the hook library.

### T3. Competing consumer detection (S)
- **Goal:** know when another tool has taken, or tries to take, XAML Diagnostics in
  explorer.exe.
- **Done when:** the status reports "competitor: <module file name>" when TranslucentTB
  or the Windhawk styler is active, and "connection failed" when the loop runs out.
- **Files:** `xamldiag/competitor.cpp`.
- **Steps:**
  1. Hook `InitializeXamlDiagnosticsEx` once Windows.UI.Xaml.dll is loaded (hook
     LoadLibraryExW, or install it right away if the DLL is already loaded).
  2. Identify the caller with `_ReturnAddress()` and `GetModuleHandleEx(FROM_ADDRESS)`;
     let the call through.
  3. Also report when Deckle's own connection attempt finds a connection already
     active [I: how this shows up is not verified; expect S_OK with no SetSite].
     Detect it with a 5 s timeout on SetSite.
- **Risks:** a competitor injected before Deckle goes unnoticed (the timeout covers it).
- **Louis verifies:** enable the Windhawk styler and Deckle together; the status shows
  the conflict.
- **Depends on:** T1.

### T4. Rule parser, matcher and value conversion (M)
- **Goal:** turn settings rules into applied properties, with originals restored on
  removal.
- **Done when:** unit-testable parts (the parser) pass the tests; on the machine, the
  SimplyTransparent rules make the bar transparent and removing them brings back the
  original background.
- **Files:** `xamldiag/rules/{parse,match,convert,apply}.cpp`.
- **Steps:**
  1. Port `ElementMatcherFromString`, `AdjustTypeName` and the ` > ` split (the subset
     from D5).
  2. Matcher: class name, Name, index through VisualTreeHelper, `[Prop=Val]` through
     ReadLocalValue, ancestor walk with `*` backtracking.
  3. Port `GetStyleFromXamlSetters` (lazy cache per type, fallback on 0x802B000A).
  4. Apply:
     - save the original with ReadLocalValue, then SetValue;
     - RegisterPropertyChangedCallback re-asserts the value, under a guard;
     - `@Group` values attach to CurrentStateChanged.
  5. On Remove or uninit, restore and unregister.
- **Risks:**
  - a callback loop when Explorer fights back (cap at N re-asserts per second, then mark
    the rule "contested");
  - the XAML type namespace differs from the class-name prefix [I].
- **Louis verifies:** black background, then transparent, then reset, each without
  restarting Explorer.
- **Depends on:** T1.

### T5. Re-application and per-rule status (S)
- **Goal:** apply settings changes live and report every rule's state.
- **Done when:**
  - a settings change applies within 1 s;
  - each rule reports matched, applied, contested, not-applied-on-this-build or error;
  - the report goes through the IPC.
- **Files:** `xamldiag/engine.cpp`, `status.cpp`.
- **Steps:**
  1. Settings change: Unadvise, restore all, swap the rule set, re-Advise (relies on the
     Add replay [I]: if it is missing, walk the tree from each XamlRoot with
     VisualTreeHelper).
  2. Per-rule counters.
  3. A 10 s settle timer after Advise; a rule with no match becomes
     "not applied on this build".
  4. Send the status.
- **Risks:** the Add replay is unproven [I]; elements created lazily (tray overflow,
  calendar) count as "not applied" until they are opened. The status says
  "not seen yet" rather than "failed".
- **Louis verifies:** change a value in Settings and watch it apply live; a rule with a
  wrong name shows "not applied".
- **Depends on:** T4 and the IPC.

### T6. Behavior: condensed tray, width 24, one column (M)
- **Goal:** notification icons 24 px, a single column, homogeneous, on the vertical bar.
- **Done when:** with the bar on the left, all visible tray icons sit in one column with
  a 24 px pitch, and they stay that way after adding or removing an app icon and after
  Explorer resets the layout.
- **Files:** `xamldiag/behaviors/tray_spacing.cpp`.
- **Steps:**
  1. Detect `OrientationStates` / `VerticalOrientation` on SystemTrayFrameGrid and
     re-run when the state changes.
  2. Vertical mode:
     - NotifyItemIcon MinHeight=24, MinWidth=0;
     - ContainerGrid Padding=0;
     - WrapGrid ItemHeight=24 and MaximumRowsOrColumns=1, ItemWidth = frame width;
     - edge inset 4.
  3. Property-changed callbacks on each of those values.
  4. Compare with the NIS formulas (lines 342-640).
- **Risks:** Explorer resets the values through code paths that bypass the callback
  (NIS hooks 3 methods) [I]. If the callbacks lose, this want moves to tier 3.
- **Louis verifies:** visual check, plus adding and removing tray icons (start and quit
  an app that has a tray icon).
- **Depends on:** T4.

### T7. Behavior: quick-settings tighten / hide (S, after Louis's choice in Q1)
- **Goal:** the network / volume / battery group blends into the tray.
- **Done when:** the group's icons have the same 24 px pitch as the tray (tighten), or
  the chosen icons are hidden and leave no gap (hide).
- **Files:** `xamldiag/behaviors/quick_settings.cpp`.
- **Steps:**
  - Tighten: inner ContainerGrid Padding by the NIS formula (0 at 24), StackPanel
    Spacing 0.
  - Hide: port the TSIT glyph tables and `UpdateControlCenterButtonStackPanel`;
    Text callback on InnerTextBlock; re-run on SizeChanged.
- **Risks:** the glyphs change between builds; the battery is identified by class.
- **Louis verifies:** unplug or plug the network, change the volume (the glyphs
  change), the layout holds.
- **Depends on:** T4.

### T8. Behavior: clock text and style (M, includes a probe)
- **Goal:** clock on one or two compact lines, e.g. "17" / "sam." (month optional),
  12 / 18 px, spacing 4.
- **Done when:** the text matches the settings at boot and after a minute change,
  without symbols.
- **Files:** `xamldiag/behaviors/clock.cpp`.
- **Steps:**
  1. **Probe first:** set `TimeInnerTextBlock.Text` once and log whether it survives the
     next minute, and whether the callback fires.
  2. If it does, register callbacks on both TextBlocks; on change, under a guard, write
     the DLL-formatted string (GetDateFormatEx / GetTimeFormatEx with the user locale);
     add a 60 s timer as a safety net.
  3. Style rules (FontSize, Spacing, TextAlignment, LineHeight, MaxWidth) as plain rules
     from T4.
  4. Vertical bar: keep each line within the bar width; allow `\n` with TextWrapping.
- **Risks:**
  - a binding is overwritten and refreshes stop (the timer covers it);
  - the tooltip keeps the stock format;
  - if the probe fails, the text goes to the tier 3 brief (CLK's method).
- **Louis verifies:** watch the clock across a minute change, a sleep/resume and a
  date change at midnight.
- **Depends on:** T4.

### T9. Start menu engine in StartMenuExperienceHost (L)
- **Goal:** start menu 580x1080 and no Recommended section, when the tier 1 policy
  does not cover it.
- **Done when:** the menu opens at 580x1080, with no Recommended section, every time.
- **Files:** a second entry point of the same DLL (the process is detected in DllMain).
- **Steps:**
  1. The injection brief gets the DLL into StartMenuExperienceHost, with the ACL.
  2. Init on `Windows.UI.Core.CoreWindow` creation (SMS lines 15558-15569).
  3. Same TAP core.
  4. Size behavior from SMSZ (classic and redesigned branches, original values kept,
     re-apply on VisibilityChanged then the next LayoutUpdated).
  5. Recommended rules from SMS lines 732-759 as plain rules.
- **Risks:**
  - classic or redesigned on 26100 LTSC is unknown [I];
  - the packaged process may refuse the DLL;
  - the menu process restarts often (re-injection).
- **Louis verifies:** open the menu 10 times, after sleep, after an Explorer restart.
- **Depends on:** T1, T4, the injection brief.

### T10. Start / Task View / Search panel widths (S, optional)
- **Goal:** the experience buttons match a narrower bar.
- **Done when:** `ExperienceToggleButtonRootPanel` Width follows the setting.
- **Steps:** rules from TIS lines 2044-2197 (extent + padding - 4, -3 for Start).
- **Risks:** pointless if Louis's bar does not show them.
- **Depends on:** T4.

Order: T1 → T2 → T3 → T4 → T5 → (T6, T7, T8 in parallel) → T9. T10 can be done at any
time after T4.

---

## (d) Open questions for Louis

1. **Quick-settings: tighten or hide?**
   - Tighten (T7a, S): stays one tap target, flyout unchanged; the glyphs keep their
     native look.
   - Hide some icons (T7b, S/M): glyph tables to maintain per build; hidden state is
     still reachable through the flyout.
   - "Move elsewhere" is not available in XAML (anchoring) [I]. It would need the
     tier 3 brief or a Deckle-drawn replacement (L, and it would lose native
     behavior).
2. **Clock text if the probe fails:** accept the tier 3 symbol hooks (CLK's method,
   which breaks when the PDBs change), or keep the stock text with style only?
3. **Competitor policy:** when the Windhawk styler or TranslucentTB is detected, should
   Deckle only report it (recommended), or refuse to start its engine until the other
   tool is turned off?
4. **Start menu:** is the XAML fallback (T9, L, a second process to inject) worth it if
   the tier 1 policy already removes Recommended and only the exact size is missing?
5. **Button width 24:** accept that it stays with the tier 3 brief (the current TIS
   patch keeps doing the job until then)?
