# Settings design system

First version, 2026-10-13, read against commit 243602f5; Louis's answers of
the same day are in the Decisions section. The grid and the rules are the
core; the annexes photograph the code on that date and are meant to be
regenerated. A line marked *to decide* is Louis's call and nothing below
assumes an answer.

## Intent

A settings surface is composed from a small set of blocks, one per nature of
value, so that a new setting costs a declaration rather than a drawing. The
design system names the natures, binds each to one block, and says how a value
changes and what is held back. When a surface needs something the grid does not
name, the grid grows by one row, decided, before the surface is built.

Doctrine already written lives in `deckle-settings-ux` (what to expose, three
levels, live apply, reset, confirmation) and `deckle-interface` (native
primitives, theme resources, materials). This file does not restate them; it
maps them onto the blocks and records where the code still disagrees.

## Sorting a setting

Three questions, in this order, place any value on the grid.

1. **What is it?** A persistent setting, a command (an immediate action), or a
   diagnostic (consulted, not configured). Only settings carry a value and a
   default.
2. **What is the nature of its value?** The grid below; one row per nature,
   one block per row.
3. **How does a change apply?** Live by default. On release when applying is
   expensive. Held until confirmed when it is hard to undo. The gesture rules
   section says which is which.

## The grid: nature of value to block

"Block today" names what the code has. "State" says whether the row is covered
by one block, split across several, or empty.

| Nature of value | Doctrine says | Block today | State |
|---|---|---|---|
| On or off | A switch, label fixed | Composer `Toggle` | Covered. Hand-drawn ToggleSwitch copies remain in Trackpad, the tray panel, row templates on four pages, Playground HUD rows. |
| On or off behind a consent | A switch, gated on enable | `Toggle` with `confirmOnEnable`, six dialog builders in Deckle.Settings | Covered. |
| One of a few options | Radio buttons, all visible | Composer `Choice` with `Radio` flag | Covered. Theme (three options) and overlay position (two) still render as dropdowns, *to check on screen*. |
| One of many options, labels fixed | A dropdown | Composer `Choice` | Covered. |
| One of a runtime list (microphone, monitor, Hue group, model file, profile, language) | A dropdown | None in the composer. Hand-drawn ComboBox or AutoSuggestBox on six pages. | **Empty row.** Options are not `.resw` keys, so `Choice` cannot carry them. |
| Linear magnitude | One control: slider and number field, grain derived | Composer `Magnitude` with `MagnitudeGrain` | Done 2026-10-13 (feat/magnitude-block): one numeric block. `Magnitude` hides its slider by option and declares a count; `Slider` (14 cards) and `Number` (9 cards) retired into it, arrows on hover (`Compact`), written at the release. `TunableRow` and the HUD row factories still stand in the Playground, for the Playground pass. |
| Geometric magnitude (stops that multiply: 1, 2, 4 … 256) | Slider over a stop list, field shows the value | None. Rewriting context size is a hand-drawn index slider. | **Empty row.** The composer comment says the log variant was deliberately not built. |
| Curve or envelope | A dedicated editor | Composer `Editor` hosts a module element. Two editors: `HangoverCurveCanvas` (Win2D) and `BrightnessCurveCanvas` (XAML shapes), same two-handle interaction, one `CurveHandleStyle` copied in both. | Decided 2026-10-13: one editor in XAML shapes, a composer block. It carries the control-point values, a computed sentence that says what the curve does with them (model: `SegmentationPage.xaml.cs:171`), and one form that adapts to a compact width, two forms only if one cannot. |
| Free text, one line | One shared input | Composer `Text` | Covered. |
| Free text, multi-line | Same input, wraps | `Text` has `Multiline`, no manifest uses it. Two hand-drawn TextBoxes (initial prompt, system prompt). | Dormant. |
| Path or folder | One picker, affordances by ownership | Composer `Path` over `FolderPickerCard` (read-only, Change, Open) and `FolderPickerEditableCard` (typeable). `OpenOnly` mode exists, unused. | Covered for two of three affordances. The app-owned data folder and the diagnostic files folder are hand-drawn cards. |
| Hotkey chord | (Deckle-specific) | Composer `Hotkey` | Covered. |
| Fine configuration of something activatable | A fold under a master switch, hidden when off | Composer `Group` (SettingsExpander) | Covered. Folds never nest. |
| Related settings, no master | A fold without master | Composer `Section` (SettingsExpander) | Covered when collapsible. A flat section is a header style plus hand markup, copied on nine pages. |
| Preset (a named set of values for a group, plus Custom) | Not named in the doctrine | `Choice` fed by `PresetFamily.SelectorOptions()`; the Playground rebuilds the same combo by hand | Decided 2026-10-13: a row of its own. The selector sits above the group it governs. The save and revert gesture is an application-wide question, not settled here. |
| Command (an action now) | A button, confirmation by reversibility | None. Hand-placed SettingsCard and Button on every page; `ConfirmationService` is the only shared piece. | **Empty row.** The composer drives values, not buttons. |
| Diagnostic readout (version, status, live reading) | Consulted, not configured | None. Hand-drawn: version card, status dots, frame rate, mic-check InfoBars. | **Empty row.** |
| Preview or instrument (a scene, a meter) | Opt-in for a live microphone meter | `AmbientPreviewCard` (Ambient only). No meter control exists anywhere in `src`. | Module-specific. A shared preview component and a meter are open tasks in Anytype. |
| Editable collection (profiles, excluded words, per-app decisions, per-light rows) | Not named in the doctrine | Hand templates per page; `LightZonesCard` rows rebuilt in the Playground | **Empty row.** |
| Advisory next to a value | One inline channel, tone in the wording | Composer `WrapWithAdvisory` (InfoBar, one severity) | Covered. |
| Reset of a value, a group, the surface | Hover reveal, one default source | Composer reset chrome on every composed card and fold | Covered by the composer; five hand forms of the per-value reset and five of the section reset remain, with two glyphs and some English tooltips. |
| Page header (title, subtitle, Reset all) | Not named | Hand markup on all thirteen pages | **Empty row** as a block; two resources (`SettingsCardSpacing`, section header style) are redefined locally on nine to twelve pages. |

Louis's rule of 2026-10-13: every surface takes a form the application knows.
An empty row is a block to build, not a licence to hand-draw. A case that
cannot be brought to a block is discussed when it arises.

## Gesture rules: how a value changes, what is held back

From the doctrine, with the code facts that bear on each rule.

- **Live, written once at the end of the gesture.** Decided 2026-10-13: every
  block writes when its gesture ends, the release of a slider or a curve
  handle, the commit of a field, the click of a switch. Never per tick, and
  the same on every page. Louis's rule of 2026-10-12 still holds: the write
  auto-saves and, under a preset, switches it to Custom; picking a preset
  restores its values. The composer magnitude writes at the release since
  feat/magnitude-block (`GestureHold`, pure state, tested apart from the
  control); a brightness-curve drag still saves the Ambient store on every
  handle move, once per coordinate (`AmbientViewModel.cs:169-176`).
- **Confirmation and restart are parameters of the setting.** Decided
  2026-10-13: they are carried by the descriptor, never behaviours of a block,
  so every block applies them the same way. The marker names are still open.
- **Held when hard to undo.** Section and surface resets, clearing a pairing,
  deleting on disk, overwriting live state are confirmed first; the safe button
  is the default. Eleven call sites use `ConfirmationService`; several resets
  still fire with no gate, listed in the June inventory (now in git history).
- **Restart-required.** Whisper model and GPU use need a restart; the page
  shows a footer. The doctrine does not name this case. Decided 2026-10-13:
  confirmed first, then the restart is offered as the next step.
- **Hidden, not greyed.** A setting that does not apply is masked. Greying is
  only a transient busy signal.
- **Reset at three levels** from one default source. The composer reads the
  default from the POCO initializer. Hand-drawn cards keep their own.
- **One advisory channel.** An InfoBar inline, one severity.
- **Playground.** Preview only; it never writes the live thing. The Ambient tab
  still writes the live Ambient store, posed in Anytype, not decided. Decided
  2026-10-13: the Playground and Settings share exactly the same blocks; the
  Playground alone may host custom experiments and new forms on trial, and what
  matures leaves it for Settings.

## What is dynamic today

- A page declares its composed settings as manifests of `SettingDescriptor`
  (`Setting.*` factories in `src/Deckle.Catalog/Composer/`), places empty host
  panels in its XAML, and calls `SettingsComposer.Compose`. Two pages are fully
  composed (Precision scrolling, Taskbar cover), eight mix composed and
  hand-drawn parts, three are entirely hand-written (Lexical domains, Apps
  enrolled, Apps and games).
- Pages are registered from a static list in `src/Deckle.App/App.Startup.Settings.cs`;
  each module describes itself with a `SettingsModuleDescriptor`, the window
  resolves the page type from the nav item tag. No attribute, no reflection.
- Every findable card registers a `SettingSearchEntry`; about one hundred exist.
- Presets are a support module (`Deckle.Presets`): families, selection and
  Custom values stored by the consumer. Two consumers today, Ambient and the
  dictation cut.
- Ambient has two surfaces over one view model, Settings and the Playground,
  with the preview, the light rows, the Hue pairing, the preset combo and the
  tuning values each implemented twice.

## Decisions, 2026-10-13

Louis's answers to the first version's open points.

1. **Magnitude is the one numeric block.** A number field with its arrows on
   hover (`Compact`), paired with a slider that an option hides when the value
   has no gesture worth it. `Slider` and `Number` retire into `Magnitude`; the
   23 cards, `TunableRow` and the HUD row factories follow. Done the same day
   for the composer and the 23 cards (feat/magnitude-block): arrows on hover,
   slider optional (`MagnitudeArgs.Slider`), count-aware grain
   (`MagnitudeArgs.Integer`, floored at one), one write at the release. The
   former Number cards keep the field alone, the former Slider cards gain the
   field; the grain now derives from each range instead of the hand-set steps.
   `TunableRow` and the HUD rows wait for the Playground pass.
2. **Preset is a row of its own.** The selector sits above the group it
   governs. `Deckle.Presets` stays a separate module: `Deckle.Lighting.Ambient`
   and `Deckle.Transcription` reference it and own the families;
   `Deckle.Settings` does not. The save and revert gesture is decided once
   for the whole application, not here.
3. **One curve editor, in XAML shapes, as a composer block.** It shows the
   control-point values, a sentence computed from them that says what the
   curve does (the Segmentation page of the Playground is the model), and one
   form that adapts to a compact width, two forms only if one cannot. Win2D
   stays confined to `Deckle.Composition` for as long as the HUD's graded
   conic ring exists; that is a HUD decision, not a settings one. The
   inventory behind this is at the end of the section.
4. **Every surface takes a form the application knows.** Runtime-list choice,
   command, diagnostic readout, editable collection, page header, preview and
   reset each become a block. Geometric magnitude follows the doctrine, a
   slider over a stop list. A case that cannot be brought to a block is
   discussed when it arises.
5. **docs/ is cleaned.** Nine files removed on 2026-10-13 (commit 5607b4d7).
   The ADRs, the sweep script, the exposables inventory, the audio-chain note
   and the taskbar research stay.
6. **The Playground and Settings share the same blocks.** The Playground alone
   may host custom experiments and new forms on trial; what matures leaves it.
7. **One write rule everywhere.** Every block writes once, when its gesture
   ends. Confirmation and restart are parameters of the setting, carried by
   the descriptor, never behaviours of a block.

Inventory behind decision 3, 2026-10-13: `HangoverCurveCanvas` is the only
`CanvasControl` in `src`. The HUD uses Win2D for static surfaces painted once
(the conic wheel, the arc mask) and for the effect graphs (alpha mask,
saturation, hue, exposure) that Composition animates on the compositor thread;
Composition cannot describe those effects without Win2D's classes. No blur
exists anywhere in the code. The window and proximity fades are layered-window
alpha driven by timers, with no Win2D. The Transcription project took Win2D to
avoid rebuilding a shapes tree on resize (`Deckle.Transcription.csproj:28-32`);
the new editor updates its shapes in place instead. Leads from the same pass,
not re-read: the two editors duplicate the Bézier solver (`UnitBezier`,
`AmbientBrightnessCurve.Solve`), flip the y axis, and neither has keyboard
nudge, tooltip or automation name.

## Still open

- The descriptor markers for confirmation and restart: their names, and which
  settings carry them.
- The preset save interaction beyond the switch to Custom: save as a new
  preset, overwrite, revert. Louis's application-wide question.
- The preview contract and the meter's explicit-gesture rule.

## Annex A: blocks in the code, 2026-10-13

Where a value lives and who uses it. Kinds are in `src/Deckle.Catalog/Composer/SettingKind.cs`
(nine since feat/magnitude-block: Toggle, Path, Choice, Magnitude, Text, Editor,
Hotkey, Group, Section; Radio is a flag of Choice). Rows marked (sweep) come
from a read-only agent pass and were not re-read line by line.

| Block | File | Covers | Used by |
|---|---|---|---|
| Toggle | Composer | on/off, consent gate | ten pages, about thirty cards (sweep) |
| Choice, Radio | Composer `SettingsComposer.Inputs.cs` | choice, fixed labels | six Choice cards on four pages, three Radio cards on two (sweep) |
| Magnitude | Composer `SettingsComposer.Numeric.cs`, `MagnitudeGrain.cs`, `GestureHold.cs` | linear magnitude, field alone or with slider | the 23 former Slider and Number cards plus the existing magnitudes (Trackpad, Precision scrolling, Ambient) |
| Text | Composer | one-line text | two cards (sweep) |
| Path | Composer, `IPathControl`, `PathControlFactory` | folder | three cards (sweep) |
| Editor | Composer | hosts a module editor | one card, the cut curve |
| Hotkey | Composer `SettingsComposer.Hotkey.cs` | chord | seven cards on General (sweep) |
| Group, Section | Composer `SettingsComposer.Folds.cs` | folds | seven groups, four sections (sweep) |
| Reset chrome, advisory | Composer `SettingsComposer.Chrome.cs`, `SettingsComposer.cs` | reset, InfoBar | every composed card |
| FolderPickerCard, FolderPickerEditableCard | `src/Deckle.Settings/Controls/` | path | through Path |
| TunableRow, TuningCard | `src/Deckle.Settings/Controls/` | magnitude row, titled card with section reset | Playground only (sweep) |
| HangoverCurveCanvas, CutCurveEditor | `src/Deckle.Transcription/Ui/Controls/` | curve | Settings Dictation, Playground Segmentation |
| BrightnessCurveCanvas | `src/Deckle.Lighting.Ambient/Ui/Controls/` | curve | Settings Ambient, Playground Ambient |
| AmbientPreviewCard, LightZonesCard | `src/Deckle.Lighting.Ambient/Ui/Controls/` | preview, per-light rows | Settings Ambient |
| HUD row factories | `src/Deckle.Playground/Views/Hud/HudPage.RowFactories.cs` | section, magnitude, toggle, sign | Playground HUD |
| ConfirmationService | `src/Deckle.Catalog/ConfirmationService.cs` | confirm gate | eleven call sites (sweep) |
| Consent dialogs | `src/Deckle.Settings/Dialogs/` | consent on enable | four pages (sweep) |
| SettingsNavigation.GoToPage | `src/Deckle.Catalog/SettingsNavigation.cs` | drill-in card | Autocorrect (sweep) |
| Icons.xaml, Glyphs.cs | `src/Deckle.Catalog/Themes/`, `Glyphs.cs` | icons | app-wide |
| LogFilterBar | `src/Deckle.Diagnostics.Logging/Ui/Controls/` | multi-select filter | LogWindow, not a Settings page |

Dormant: `isAdvanced` on descriptors, `Text.Multiline` and `MaxLength`,
`FolderPickerMode.OpenOnly`, two slider styles in `WhisperPage.xaml` never
referenced (sweep).

## Annex B: pages by composition, 2026-10-13

| Page | File | Composed | Hand-drawn (sweep) |
|---|---|---|---|
| Precision scrolling | `src/Deckle.Input.PrecisionScroll/Ui/PrecisionScrollPage.xaml` | all | title, subtitle |
| Taskbar cover | `src/Deckle.Shell.TaskbarCover/Ui/TaskbarCoverPage.xaml` | all | title, subtitle |
| General | `src/Deckle.Settings/Pages/GeneralPage.xaml` | hotkeys, theme, two toggles, backup path | header, four section reset rows, version card, rerun setup, data folder card, backup expander |
| Recording | `src/Deckle.Audio/RecordingPage.xaml` | limit, toggle, level group, consent toggle | microphone list, mic check, listening test, section reset |
| Dictation | `src/Deckle.Transcription/WhisperPage.xaml` | ten manifests, cut curve editor | setup InfoBar, header, language, initial prompt, model, restart footer |
| Ambient | `src/Deckle.Lighting.Ambient/Ui/AmbientPage.xaml` | four manifests | curve card and reset, monitor list, Hue expander, not-paired InfoBar, Reset all |
| Autocorrect | `src/Deckle.Autocorrect/Ui/AutocorrectPage.xaml` | two manifests | drill-in cards, exclusion entry and list, empty state |
| Trackpad | `src/Deckle.Input.Trackpad/Ui/TrackpadPage.xaml` | two manifests | two command cards, elevated toggle |
| Diagnostics | `src/Deckle.Diagnostics.Logging/DiagnosticsPage.xaml` | three manifests | header, three reset rows, files card |
| Rewriting | `src/Deckle.Llm.Rewrite/Ui/LlmPage.xaml` | toggle and endpoint | profiles, models, active profile, InfoBar, Reset all |
| Lexical domains | `src/Deckle.Autocorrect/Ui/LexicalDomainsPage.xaml` | none | all |
| Apps enrolled | `src/Deckle.Autocorrect/Ui/AppsEnrolledPage.xaml` | none | all |
| Apps and games | `src/Deckle.Orchestration/Ui/OrchestrationPage.xaml` | none | all |

## Annex C: toward a regenerated inventory

What exists: `scripts/commands/maintenance/sweep-exposables.ps1`, a text sweep
of tracked `.cs` and `.json` files that lists every value someone could act on,
classified by persistence, one JSON line each with file and line. The arbitrated
layer (`docs/inventaire-exposables.md` and `.csv`, July 2026) was produced once
by an agent workflow and patched by hand since; the CSV's verdict and comment
columns are empty on all 693 rows. The June 2026 page inventory
(`inventaire-settings.md`) was removed on 2026-10-13 and lives in git history.

What the code already declares, and a sweep could read without any new marker:
the composer manifests (kind, default, visibility, advisory, consent) give the
exposed settings with their block; the search entries give every findable card,
hand-drawn ones included. Hand-drawn cards without a search entry are the blind
spot; a marker there is the one addition the idea would need.
