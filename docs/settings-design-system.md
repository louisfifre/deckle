# Settings design system

First version, 2026-10-13, read against commit 243602f5. The grid and the
rules are the core; the annexes photograph the code on that date and are meant
to be regenerated. A line marked *to decide* is Louis's call and nothing below
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
| Linear magnitude | One control: slider and number field, grain derived | Composer `Magnitude` with `MagnitudeGrain` | Covered by one block, but three others still do the job: composer `Slider` (14 cards) and `Number` (9 cards), `TunableRow` (Playground), HUD row factories. |
| Geometric magnitude (stops that multiply: 1, 2, 4 … 256) | Slider over a stop list, field shows the value | None. Rewriting context size is a hand-drawn index slider. | **Empty row.** The composer comment says the log variant was deliberately not built. |
| Curve or envelope | A dedicated editor | Composer `Editor` hosts a module element. Two editors: `HangoverCurveCanvas` (Win2D) and `BrightnessCurveCanvas` (XAML shapes), same two-handle interaction, one `CurveHandleStyle` copied in both. | Split. One interaction, two renderers, three hostings (composer card, hand card, raw in the Playground). |
| Free text, one line | One shared input | Composer `Text` | Covered. |
| Free text, multi-line | Same input, wraps | `Text` has `Multiline`, no manifest uses it. Two hand-drawn TextBoxes (initial prompt, system prompt). | Dormant. |
| Path or folder | One picker, affordances by ownership | Composer `Path` over `FolderPickerCard` (read-only, Change, Open) and `FolderPickerEditableCard` (typeable). `OpenOnly` mode exists, unused. | Covered for two of three affordances. The app-owned data folder and the diagnostic files folder are hand-drawn cards. |
| Hotkey chord | (Deckle-specific) | Composer `Hotkey` | Covered. |
| Fine configuration of something activatable | A fold under a master switch, hidden when off | Composer `Group` (SettingsExpander) | Covered. Folds never nest. |
| Related settings, no master | A fold without master | Composer `Section` (SettingsExpander) | Covered when collapsible. A flat section is a header style plus hand markup, copied on nine pages. |
| Preset (a named set of values for a group, plus Custom) | Not named in the doctrine | `Choice` fed by `PresetFamily.SelectorOptions()`; the Playground rebuilds the same combo by hand | *To decide*: is a preset a nature of its own, and where does its card sit relative to the group it governs. |
| Command (an action now) | A button, confirmation by reversibility | None. Hand-placed SettingsCard and Button on every page; `ConfirmationService` is the only shared piece. | **Empty row.** The composer drives values, not buttons. |
| Diagnostic readout (version, status, live reading) | Consulted, not configured | None. Hand-drawn: version card, status dots, frame rate, mic-check InfoBars. | **Empty row.** |
| Preview or instrument (a scene, a meter) | Opt-in for a live microphone meter | `AmbientPreviewCard` (Ambient only). No meter control exists anywhere in `src`. | Module-specific. A shared preview component and a meter are open tasks in Anytype. |
| Editable collection (profiles, excluded words, per-app decisions, per-light rows) | Not named in the doctrine | Hand templates per page; `LightZonesCard` rows rebuilt in the Playground | **Empty row.** |
| Advisory next to a value | One inline channel, tone in the wording | Composer `WrapWithAdvisory` (InfoBar, one severity) | Covered. |
| Reset of a value, a group, the surface | Hover reveal, one default source | Composer reset chrome on every composed card and fold | Covered by the composer; five hand forms of the per-value reset and five of the section reset remain, with two glyphs and some English tooltips. |
| Page header (title, subtitle, Reset all) | Not named | Hand markup on all thirteen pages | **Empty row** as a block; two resources (`SettingsCardSpacing`, section header style) are redefined locally on nine to twelve pages. |

## Gesture rules: how a value changes, what is held back

From the doctrine, with the code facts that bear on each rule.

- **Live by default.** A composed card writes on each change and re-syncs on
  `PropertyChanged`. Louis's rule of 2026-10-12: any edit auto-saves and, under
  a preset, switches it to Custom; picking a preset restores its values.
- **On release when expensive.** A change that relaunches a measure or reloads
  a model waits for the release of the gesture. *To decide*: which settings are
  in this class; the code has no marker for it.
- **Held when hard to undo.** Section and surface resets, clearing a pairing,
  deleting on disk, overwriting live state are confirmed first; the safe button
  is the default. Eleven call sites use `ConfirmationService`; several resets
  still fire with no gate, listed in the June inventory.
- **Restart-required.** Whisper model and GPU use need a restart; the page
  shows a footer. The doctrine does not name this case. *To decide*: held until
  confirmed, or applied and announced.
- **Hidden, not greyed.** A setting that does not apply is masked. Greying is
  only a transient busy signal.
- **Reset at three levels** from one default source. The composer reads the
  default from the POCO initializer. Hand-drawn cards keep their own.
- **One advisory channel.** An InfoBar inline, one severity.
- **Playground.** Preview only; it never writes the live thing. The Ambient tab
  still writes the live Ambient store, posed in Anytype, not decided.

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

## Open decisions

For Louis, in the order they block the rest.

1. Does the doctrine "a magnitude is always both slider and field" hold for
   every numeric setting? If yes, `Slider` and `Number` retire into `Magnitude`
   (23 cards), and the grain rule decides their readouts.
2. Preset as a row of the grid: a card above the group it governs, or the
   group's header? And the postponed Revert/Save band.
3. One curve editor: which renderer survives (Win2D or XAML shapes), hosted
   only through the composer `Editor` card.
4. Runtime-list choice as a block, so the six hand-drawn dropdowns compose.
5. Command and diagnostic readout as blocks, or left to hand markup with a
   shared page header and section header.
6. Preview contract: which pages get a preview slot first (Ambient, segmentation,
   HUD), and the meter's explicit-gesture rule.
7. Geometric magnitude and editable collections: build the rows or decide
   they stay bespoke.
8. Restart-required and on-release classes: which settings, what marker.

## Annex A: blocks in the code, 2026-10-13

Where a value lives and who uses it. Kinds are in `src/Deckle.Catalog/Composer/SettingKind.cs`
(eleven: Toggle, Slider, Path, Choice, Number, Magnitude, Text, Editor, Hotkey,
Group, Section; Radio is a flag of Choice). Rows marked (sweep) come from a
read-only agent pass and were not re-read line by line.

| Block | File | Covers | Used by |
|---|---|---|---|
| Toggle | Composer | on/off, consent gate | ten pages, about thirty cards (sweep) |
| Choice, Radio | Composer `SettingsComposer.Inputs.cs` | choice, fixed labels | six Choice cards on four pages, three Radio cards on two (sweep) |
| Slider, Number, Magnitude | Composer `SettingsComposer.Numeric.cs`, `MagnitudeGrain.cs` | linear magnitude | Slider 14 cards, Number 9, Magnitude 9 (sweep) |
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
layers (`docs/inventaire-exposables.md` and `.csv`, July 2026; `docs/inventaire-settings.md`,
June 2026) were produced once by agent workflows and patched by hand since; the
CSV's verdict and comment columns are empty on all 693 rows.

What the code already declares, and a sweep could read without any new marker:
the composer manifests (kind, default, visibility, advisory, consent) give the
exposed settings with their block; the search entries give every findable card,
hand-drawn ones included. Hand-drawn cards without a search entry are the blind
spot; a marker there is the one addition the idea would need.
