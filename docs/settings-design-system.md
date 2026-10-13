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
3. **How does a change apply?** Written when its gesture ends. Held until
   confirmed when it is hard to undo. Confirmed, then offered a restart, when
   it needs one. The gesture rules section says which is which.

## Placing a setting

Decided 2026-10-13. A setting climbs this ladder from the top and stops at the
first rung that holds it; the rung names where it goes.

1. **Which module owns it?** Its rail node. The rail is Home, General, a
   separator, then the modules. Home is an empty page until it earns content.
2. **Which subject in that module?** A subject becomes a page of its own when
   it has a vocabulary of its own and a user can want it without the rest (the
   engine's decoding, the live cut). A module split into pages is a rail node
   that expands and does not navigate; its pages sit at one level, and the
   first is General, the page for what every user of the module touches. A
   module with one page keeps its name on that page.
3. **Which question on that page?** A section per question the user asks: a
   title, at most one line of caption, cards under it. No body paragraphs.
4. **Does it configure a switch, or depend on a choice?** The fine
   configuration of a switch goes in the fold under that switch. Settings that
   exist only under one value of a choice go in a mode fold whose header
   carries the choice. Folds never nest.
5. **Otherwise it is a card in its section.** A module that can be off has its
   switch as the first card of its first page, alone, icon and toggle, never a
   fold.

Every page shows a breadcrumb in place of its title, the module then the page
(`Transcription › Decoding`); the module segment goes to General. The
breadcrumb is the way up when the rail is closed. Whether a General page also
carries tiles to its sibling pages is *to decide*.

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
| Curve or envelope | A dedicated editor | Composer `Editor` hosts a module element. Two editors: `HangoverCurveCanvas` (Win2D) and `BrightnessCurveCanvas` (XAML shapes), same two-handle interaction, one `CurveHandleStyle` copied in both. | Decided 2026-10-13, third round (provisional, see decision 21): one drawing control in XAML shapes, a two-point Bézier with fixed ends, the Playground Segmentation form. The handles stay and write at their release; the four values are fields in a two-by-two grid beside the drawing, which takes the block's width; the computed sentence is its caption (model: `SegmentationPage.xaml.cs:171`); an option lets a handle leave the bounds. The second round's handle-less form is superseded. |
| Free text, one line | One shared input | Composer `Text` | Covered. |
| Free text, multi-line | Same input, wraps | `Text` has `Multiline`, no manifest uses it. Two hand-drawn TextBoxes (initial prompt, system prompt). | Dormant. |
| Path or folder | One picker, affordances by ownership | Composer `Path` over `FolderPickerCard` (read-only, Change, Open) and `FolderPickerEditableCard` (typeable). `OpenOnly` mode exists, unused. | Covered for two of three affordances. The app-owned data folder and the diagnostic files folder are hand-drawn cards. |
| Hotkey chord | (Deckle-specific) | Composer `Hotkey` | Covered. |
| Fine configuration of something activatable | A fold under a master switch, hidden when off | Composer `Group` (SettingsExpander) | Covered. Folds never nest. |
| Related settings, no master | A fold without master | Composer `Section` (SettingsExpander) | Covered when collapsible. Decided 2026-10-13: a flat section is a block, title, optional one-line caption, cards (the PowerToys `SettingsGroup` form); the header style copied on nine pages goes into it. |
| Preset (a named set of values for a group, plus Custom) | Not named in the doctrine | `Choice` fed by `PresetFamily.SelectorOptions()`; the Playground rebuilds the same combo by hand | Decided 2026-10-13: a row of its own. Second round: the selector sits in the header of the fold it governs, a mode fold. The save and revert gesture is an application-wide question, not settled here. |
| Command (an action now) | A button, confirmation by reversibility | None. Hand-placed SettingsCard and Button on every page; `ConfirmationService` is the only shared piece. | **Empty row.** The composer drives values, not buttons. |
| Diagnostic readout (version, status, live reading) | Consulted, not configured | None. Hand-drawn: version card, status dots, frame rate, mic-check InfoBars. | **Empty row.** |
| Preview or instrument (a scene, a meter) | Opt-in for a live microphone meter | `AmbientPreviewCard` (Ambient only). No meter control exists anywhere in `src`. | Decided 2026-10-13: an instrument shows and never writes; the values it draws are cards beside it; it is the last card of its fold, full width. Third round (provisional, decisions 17 and 18): one preview component on every surface, a stage with commands above and readouts below, in its card and in a floating copy opened on hover; never a side pane. A meter is an open task in Anytype. |
| Editable collection (profiles, excluded words, per-app decisions, per-light rows) | Not named in the doctrine | Hand templates per page; `LightZonesCard` rows rebuilt in the Playground | **Empty row.** |
| Advisory next to a value | One inline channel, tone in the wording | Composer `WrapWithAdvisory` (InfoBar, one severity) | Covered. |
| Reset of a value, a group, the surface | Hover reveal, one default source | Composer reset chrome on every composed card and fold | Covered by the composer; five hand forms of the per-value reset and five of the section reset remain, with two glyphs and some English tooltips. |
| Page header (title, subtitle, Reset all) | Not named | `SettingsPageHeader` (Catalog, built in code) on all thirteen pages and Home | Done 2026-10-13 (feat/page-frame): one block on every page: breadcrumb or title, one line of description, an optional link, Reset all at the right, active once something changed and confirmed (the block asks with a generic wording, or the page asks with its own when it says what the reset keeps). No image. Pages that hold only lists the user authors carry no Reset all. Two resources (`SettingsCardSpacing`, section header style) are still redefined locally on nine to twelve pages. |
| Module switch (a module that can be off) | A switch | Masters in manifests (Autocorrect, Ambient state, the Streaming group, the Rewriting enable), placed differently on each page | Decided 2026-10-13: one plain card, first on the module's first page, icon and toggle, never a fold that nests the rest. |
| Mode (a choice that decides which settings exist) | Not named in the doctrine | `Choice` plus `visibleWhen` on its siblings (the cut preset and its Custom-only cards) | Decided 2026-10-13: a fold whose header carries the choice; each child names the mode that shows it; an information card when the mode has nothing to set; the instrument last. In the composer, a Group whose master may be a Choice. |
| Navigation (rail, breadcrumb) | Three levels, a child page a destination | Rail from `SettingsModuleRegistry`: Home, General, a separator, the modules; a module with `ParentId` children is a node that expands without navigating, its own page its first child named General; the breadcrumb is resolved by the shell (`SettingsNavigation.BreadcrumbFor`) | Done 2026-10-13 (feat/page-frame): Home (empty), General, separator, modules; a split module is a node that expands, its pages at one level starting with General; a breadcrumb replaces the title and its module segment leads to General. |

Louis's rule of 2026-10-13: every surface takes a form the application knows.
An empty row is a block to build, not a licence to hand-draw. A case that
cannot be brought to a block is discussed when it arises.

## Gesture rules: how a value changes, what is held back

From the doctrine, with the code facts that bear on each rule.

- **Live, written once at the end of the gesture.** Decided 2026-10-13: every
  block writes when its gesture ends, the release of a slider, the commit of a
  field, the click of a switch. Never per tick, and the same on every page.
  Louis's rule of 2026-10-12 still holds: the write auto-saves and, under a
  preset, switches it to Custom; picking a preset restores its values. The
  composer magnitude writes at the release since feat/magnitude-block
  (`GestureHold`, pure state, tested apart from the control). The
  brightness-curve drag that saved the Ambient store on every handle move
  (`AmbientViewModel.cs:169-176`) becomes one write at the handle's release
  (decision 21, which brought the handles back).
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
  only a transient busy signal. Confirmed 2026-10-13 against Microsoft's
  guideline and PowerToys, which grey a module's sections when the module is
  off: Louis keeps hiding, one rule for a module off and for the cards of a
  mode not chosen.
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

## Decisions, 2026-10-13, second round

Louis's answers after a reading of PowerToys, the model to copy, Windows
Settings next. Read in the PowerToys source: the Light Switch page, its page
control, its section control, its timeline and its shell. The facts that bear
on the blocks: the page control fixes the title above a scroll area and puts
the content 12 px under a description row; `SettingsGroup` is a BodyStrong
title with 32 px above, an optional caption, the cards 8 px below; the Light
Switch schedule is an expander with the mode ComboBox in its header, cards
shown or hidden per mode by visual states, an information card when the mode
is off, and the timeline as the last card, full width; the timeline binds one
way and handles no pointer; the shell is Dashboard, General, a separator, then
folder nodes that expand without navigating. Microsoft's app-settings
guideline says the same about description (optional) and expanders (one
level), and says to disable rather than hide, which Louis declined.

8. **Hidden, not greyed, stays.** One rule for a module off and for the cards
   of a mode not chosen.
9. **One page header block**, on every page: breadcrumb or title, one line of
   description, an optional link, Reset all at the right, active once
   something changed and confirmed. No image.
10. **A module's switch is one plain card**, first on its first page, icon and
    toggle. Never a fold that nests the rest.
11. **A section block**: title, optional one-line caption, cards. The
    PowerToys `SettingsGroup` form. Body paragraphs go.
12. **A mode fold**: the choice in the fold's header, each child names the
    mode that shows it, an information card when the mode has nothing to set,
    the instrument last and full width. In the composer, a Group whose master
    may be a Choice. The preset selector moves into the header of the fold it
    governs, which refines decision 2.
13. **A description says the effect in one sentence, or is absent. Never the
    default.** (The one-sentence limit is lifted by decision 20.) The reset tooltip carries the value (`Reset to 0.50`), so the
    default keeps one source and gains one place to be seen. About thirty
    descriptions end with `Default: …` today, twenty-six in Transcription.
14. **Instruments show, fields write.** (Superseded for the curve by decision
    21, which keeps the handles; it still holds for previews and meters.) The
    curve editor loses its handles:
    the control-point values and the bounds are magnitude cards, the drawing
    is the last card of the fold, full width, with the computed sentence as
    its caption. One drawing control, a two-point Bézier with fixed ends, two
    layouts: wide with axes and bounds, square without.
15. **Navigation**: Home (empty for now), General, a separator, the modules.
    A module split into pages is a rail node that expands; its pages share
    one level and start with General. A breadcrumb replaces the page title.
    The placing ladder above says which rung a setting takes.
16. **Transcription splits into three pages.** General: overlay and pasting,
    model and engine, diagnostics, the restart footer. Decoding: output
    filters, context, decoding, confidence, the Whisper parameters that apply
    in both pipelines. Streaming: the pipeline switch alone at the top, then
    voice activity detection, then segmentation with the cut mode fold, its
    bounds and curve under Custom, then margin and minimum utterance.
    Autocorrect takes the same shape: General, Lexical domains, Apps enrolled.

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

## Decisions, 2026-10-13, third round — PROVISIONAL

**Louis decided these at the end of a long day and asked that they be marked
as the first thing to revisit if a problem shows.** Each one is binding until
revisited, but a difficulty met while building against 17 to 22 is a reason to
reopen the decision, not to work around it. The brief they answer is
`docs/research/research--settings-ux--2026-10-13.md`.

17. **No right pane.** A preview is a block of its page, only on the pages
    that use one, and scrolls with its section. A button shown on hover opens
    a floating copy of it, a picture-in-picture window. The right pane
    survives as one idea only, a logs pane detachable into a window; it is
    not asked for.
18. **One preview component**, everywhere: a stage, commands above (Play,
    Pause), readouts below. It hosts the Ambient capture, the HUD and later
    scenes. The same component in its card and in its floating copy. This
    closes the "module-specific" state of the preview row.
19. **A module's tuning is a page of its own** once it overflows General
    (rung 2 of the ladder), with room to explain each value.
20. **A description is as long as understanding needs, never the default.**
    Decision 13 loses its one-sentence limit. Reference values (1.0 instant,
    0.3 moderate, 0.1 cinematic) are a legend beside the instrument, never a
    description. The legend's block is to design; decision 11 (no body
    paragraphs) holds.
21. **The curve keeps its handles.** The Playground Segmentation Bézier is the
    model: the drawing takes the block's width, the four values are fields in
    a two-by-two grid beside it, the computed sentence stays as caption, an
    option lets a handle leave the bounds. One component on every surface.
    A handle writes at its release, like every gesture, and moves by keyboard
    too. This reverses decision 14 for the curve alone; previews and meters
    still show and never write.
22. **Presets follow the Windows model, taken as the default, not Louis's
    call.** Built-in presets are read-only; Save as creates a named copy;
    Revert returns to the selected preset's values; a changed value switches
    the selector to Custom, unchanged.

## Still open

- The descriptor markers for confirmation and restart: their names, and which
  settings carry them.
- The legend block (decision 20): its form and where it sits.
- The floating preview copy (decision 17): its window, its size, its lifetime.
- Decision 22, once Louis has an opinion on it.
- Whether a General page also carries tiles to its sibling pages.
- What clicking a module node does besides expanding (PowerToys only expands).
- The Home page content, later.
- The composer markers for a mode fold (which value shows which child); the
  breadcrumb can read `ParentId` from the registration as it stands.

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
