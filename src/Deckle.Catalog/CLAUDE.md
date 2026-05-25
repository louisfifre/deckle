---
name: claude-deckle-catalog
description: "Doctrine for Deckle.Catalog, the UI resource catalog module (localized strings via ResourceLoader / x:Uid, Segoe Fluent Icons glyphs). Read before adding or modifying a localized string or a glyph key."
type: agent-instructions
module: Deckle.Catalog
---

# CLAUDE.md — Deckle.Catalog

Catalog of UI resources named by semantic key. Covers two families. **Localization** through the `Loc` facade on top of the Windows App SDK `ResourceLoader`, consumed in code and in XAML (`x:Uid`) by all WinUI modules. **Glyphs** from Segoe Fluent Icons centralized in `Themes/Icons.xaml` (consumed in XAML via `{StaticResource Icon.X}`) and `Glyphs.cs` (consumed in code-behind via `Glyphs.X`), ~51 semantic keys organized into thematic groups (generic, actions, Whisper, Diagnostics, Ambient, HUD badges, transport).

Today ~51 glyphs and ~200 strings across 15 user-facing surfaces. The app stays user-facing **English from day one** (see project CLAUDE.md). This iteration produces only the `en-US` file. No FR, no language selection dropdown in Settings — the runtime resolves on the Windows display language and falls back on `en-US` by default.

## Architecture localization

Three pieces tied together by file and naming convention.

**String source file**. Each module that ships XAML with `x:Uid` carries its own `Strings/en-US/Resources.resw` (XML, legacy ResX format). One entry per key. Multi-assembly PRI pattern: `<EnableMsixTooling>true</EnableMsixTooling>` in the module csproj generates a `.pri` next to the DLL at build time, and `MakePri` can be invoked to pre-compile the resources. At runtime, the module `ResourceLoader` resolves on its own instance. Modules concerned today: `Deckle.Settings`, `Deckle.Transcription`, `Deckle.Llm.Rewrite`, `Deckle.Lighting.Ambient`, `Deckle.Setup`, `Deckle.Playground`, plus the host app `Deckle.App`.

**Neutral language**. `<DefaultLanguage>en-US</DefaultLanguage>` in every csproj that carries `.resw`. Without this tag, the MRT resolver does find the file but no language is declared as fallback; `x:Uid` values can stay empty when the system language diverges. The tag locks `en-US` as unconditional fallback.

**Consumption**. Two modes side by side. `x:Uid="MyKey"` in XAML automatically resolves the `MyKey.Text`, `MyKey.Header`, `MyKey.Description`, `MyKey.Title`, `MyKey.Content`, `MyKey.PlaceholderText`, `MyKey.ToolTipService.ToolTip` properties. The XAML resolver reads `Strings/<lang>/Resources.resw` at runtime and applies the found values to the element, zero-code on the XAML side. `Loc.Get("Key")` and `Loc.Format("Key", args...)` in code, a static facade in this module. Used for everything built programmatically: `ConsentDialog`s, engine status, HUD, tray, dynamic statuses of the setup wizard.

The API used is `Microsoft.Windows.ApplicationModel.Resources.ResourceLoader` (Windows App SDK), **not** the legacy `Windows.ApplicationModel.Resources.ResourceLoader` (UWP). Both exist in `Microsoft.WindowsAppSDK 1.8` but only the former works unpackaged.

## Key convention

A single `Resources.resw` file per module. Prefixes structure the human reading.

**`x:Uid` in XAML** — pattern `<UidValue>.<Property>` where `UidValue` is free (pick something clear in `CamelCase`, no underscore, no separator) and `<Property>` is resolved automatically. The same `UidValue` can carry multiple properties. Convention: `<Surface><ElementRole>` in `CamelCase` (`LogWindowSearchBox`, `GeneralPageTranscribeCard`, `LlmEnableCard`, `SetupChoicesInstallLocation`).

**Direct lookup in code** — pattern `<Surface>_<Purpose>` for strings consumed via `Loc.Get`. `CamelCase` for `Surface`, underscore as separator, `CamelCase` or lowercase for `Purpose`. Examples: `CorpusConsent_Title`, `CorpusConsent_Body_Intro`, `CorpusConsent_PrimaryButton`, `Setup_StepTitle_Choices`.

**Parameterized strings** — mandatory `_Format` suffix, visible at the call site. Composite-format placeholders `{0}`, `{1}`, … consumed by `Loc.Format`. Examples: `Status_Rewriting_Format = "Rewriting ({0})…"`, `Tray_Tooltip_Format = "Deckle — {0}"`, `Llm_StartOllama_Format = "Start Ollama or check the endpoint setting ({0})."`.

**Reusable strings** — `Common_` prefix for generic buttons and statuses that appear across multiple surfaces. Before creating a surface-specific key, check that a `Common_*` does not already exist. Examples: `Common_Cancel`, `Common_Back`, `Common_Next`, `Common_Enable`, `Common_Reset`, `Common_Remove`, `Common_Keep`, `Common_Browse`. A `Common_*` key never contains a parameter. Contextual variants (`Cancel install`, `Reset all`) keep their surface-specific key — `Common_*` stays the canonical short form.

## Technical strings not translated

Closed list of strings that stay **hardcoded** in code and never go through `.resw` nor `Loc`. Any addition to this list requires a justification documented here.

- **File names and extensions** — `app.jsonl`, `latency.jsonl`, `microphone.jsonl`, `corpus.jsonl`, `settings.json`, `Deckle.pri`, `Deckle.exe`. The names are contracts with the filesystem and with diagnostic tooling; translating them breaks scripts and telemetry.
- **URLs and endpoints** — `http://localhost:11434/api/chat` (Ollama default), GitHub redist URLs, `ms-resource://` schemes. Technical identifiers.
- **Product and brand names** — `Deckle`, `Ollama`, `Silero VAD`, `whisper.cpp`. Product identity; translation is neither possible nor desirable.
- **Whisper model names** — `base`, `small`, `medium`, `large-v3`, `tiny`. Model identification tag, surfaced as-is in the UI.
- **EventSource provider names and log tags** (`Deckle.Audio`, `Deckle.Whisp`, etc., and their short labels `AUDIO`, `WHISP`, …). Internal vocabulary, read by developers in the LogWindow and in the JSONL, not by users in the UX sense of the term.

Any other text visible to the user goes through `.resw`.

## Adding a new string

1. Pick the pattern that fits. If the string appears in a static XAML attribute, aim for `x:Uid`. If it is built in code, aim for `Loc.Get` or `Loc.Format`.
2. Before inventing a specific key, check that a `Common_*` does not already cover the need.
3. Add the entry in the `Strings/en-US/Resources.resw` of the module that owns the surface. The order in the file follows the sections — Common, then per surface. Keep the file grouped to make human review easy.
4. On the consumer side: in XAML, add `x:Uid="<UidValue>"` on the element and remove the literal value from the relevant attribute; in code, replace the literal with `Loc.Get("<key>")` or `Loc.Format("<key>_Format", args...)` (import `Deckle.Catalog`).
5. Build via `dotnet build`. Check at runtime that the string displays correctly; in DEBUG a missing key appears as `[!key]` on screen (loud enough to be caught in seconds).

## Adding a future language

When the time comes (FR, ES, …), create `Strings/<lang>/Resources.resw` next to the `en-US` of the relevant module, by copying the file and translating each `<value>`. Keep the keys strictly identical. Do not touch the technical strings in the list above. For `_Format` parameterized strings, keep the same number of placeholders `{0}`, `{1}` — the grammar of the target language may require a different order, `string.Format` accepts placeholders in any order in the string, which is exactly what they are for.

At runtime, MRT resolves on the Windows display language. To expose a manual selection in Settings (override), introduce a `ResourceContext` with `QualifierValues["Language"] = "<lang>"` or `Languages = new[] { "<lang>" }` and wire it to a persisted setting. Out of scope for V1.

## Pitfalls and operational notes

- **API to use**: `Microsoft.Windows.ApplicationModel.Resources.ResourceLoader` (Windows App SDK). The legacy UWP `Windows.ApplicationModel.Resources.ResourceLoader` is still referenced in some stale search results — it does not work unpackaged.
- **Creating the `ResourceLoader`**: must happen after the Windows App SDK runtime init (auto-bootstrap via `<WindowsPackageType>None</WindowsPackageType>` which calls the bootstrapper API). The `_loader` in `Loc` is lazy to guarantee this temporal constraint; only use `Loc.Get` once `App.OnLaunched` has started.
- **Missing key**: `ResourceLoader.GetString` returns an **empty string** per WindowsAppSDK contract, with no exception. In DEBUG, `Loc.Get` substitutes `[!key]` to make the regression visible. In RELEASE the default behavior is kept.
- **Inspecting the PRI**: `MakePri.exe dump <path>.pri` (Windows 10 SDK) lists the embedded resources and their keys. Useful to verify that the build pipeline did embed a `.resw` after a change.
- **Invalid `x:Uid` in XAML**: emits a `WMC*` build warning but does not block compilation. Watch the MSBuild output to catch broken Uids early.
- **Sharing an `x:Uid` across heterogeneous elements** — not a build warning, **crashes at runtime** in `InitializeComponent` with `XamlParseException: Unable to resolve property '<Prop>' while processing properties for Uid '<Uid>'`. Cause: MRT applies every property declared in the `.resw` to **each** element that carries this `x:Uid`. If one of the elements does not expose the property (a `Button` has no `.Text`, it has `.Content`), the entire XAML load of the page collapses. Correct pattern: a distinct `x:Uid` per element type, with an explicit role suffix (`*Button` for the interactive container, `*Label` for the inner `TextBlock`). Tolerated case: several elements of the **same type** sharing one Uid (for example eight `HyperlinkButton x:Uid="Settings_SectionResetLink"` in the Settings sections, all resolved on `.Content` and `.ToolTipService.ToolTip` — none has a missing property).
- **`<data name name>` value OR scope, not both** — the same `name` cannot serve both as a value (`<data name="X"><value>...</value></data>`) and as a scope for sub-keys (`X.SubKey`). At build time, error `PRI175` or `PRI278` shows up. Always use two distinct keys (`X_Label` + `X.SubKey`).
- **XML comment**: `--` is forbidden inside a `.resw` comment (`MSB4025`). Escape as `- -` spaced or rewrite without the double dash.

## Segoe Fluent Icons glyphs

`Themes/Icons.xaml` is a `ResourceDictionary` that maps ~51 semantic keys (`Icon.Transcribe`, `Icon.Rewrite`, `Icon.Save`, `Icon.Pin`, etc.) to Segoe Fluent hex codes. Consumed in XAML via `{StaticResource Icon.X}` on a `FontIcon.Glyph` or a `PathIcon`. The dictionary is referenced once in each module that uses icons via `<ResourceDictionary Source="ms-appx:///Deckle.Catalog/Themes/Icons.xaml" />` in the module resources.

`Glyphs.cs` is the code-side version: a static class with the same semantic keys exposed as `const string` (for example `Glyphs.Transcribe = ""`). Consumed by every call site that builds a `FontIcon` programmatically (typically the tray, the HUD badges, some items generated on the fly).

The two files are kept in sync by naming convention — each semantic key exists in both. The pattern is documented in a header comment in both files. Changing the glyph of a key updates both entries at the same time. Adding a key follows the same principle and picks its thematic group (generic, actions, Whisper, Diagnostics, Ambient, HUD badges, transport).

## Pointers

- [Microsoft Learn — Resource files (.resw)](https://learn.microsoft.com/en-us/windows/uwp/app-resources/localize-strings-ui-manifest) — ResourceLoader/MRT basics.
- [Microsoft Learn — Segoe Fluent Icons](https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font) — Segoe Fluent catalog.
