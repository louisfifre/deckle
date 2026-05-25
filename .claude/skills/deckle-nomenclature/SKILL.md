---
name: deckle-nomenclature
description: Naming doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries casing and prefix rules aligned with the Framework Design Guidelines, the project's assumed stance on accepted suffixes versus fuzzy suffixes to avoid, the convention of namespaces mirroring folders, WinUI x:Uid and theme resource patterns, and progressive renaming discipline. The detailed taxonomy (tabulated suffixes, x:Uid patterns, EventSource structure, commented examples) lives in the companion file taxonomie.md loaded on demand. Triggers on phrases like deckle naming, how do I name deckle, rename deckle, naming convention deckle, suffix deckle, namespace deckle, naming audit deckle, x:Uid deckle, EventSource provider deckle, Service Manager ambiguity deckle.
---

# Deckle — Naming doctrine

## Role

Project-specific skill that answers a recurring question: **what name does this symbol, file, folder, resource, provider deserve**. Invoked before introducing a new type or resource, before a non-trivial renaming, and when auditing an area of the repo whose naming has drifted.

The doctrine covers every named surface of the project — modules, namespaces, classes, methods, properties, fields, events, parameters, folders, files, `.resw` and `x:Uid` keys, WinUI theme resources, `EventSource` providers and events, `LogSource` vocabulary. It does not describe module structure — that belongs to `deckle-modularite` — nor the drafting of human-readable logging writes — that belongs to `deckle-logging`. Here we only deal with name choices. The goal is that an agent discovering a file can reconstruct responsibility from names alone, and that a naming decision is taken with reference to a doctrine, not by mimicking neighboring code that may itself be debt.

## The name describes responsibility, not implementation

A name says **what the symbol is responsible for doing or representing**, never how it does it. `ScreenCaptureService` is legitimate because the responsibility — providing screen frames on demand — is named; `WaveInPollingLoopRunner` would, on the contrary, be a name that expires at the next backend change. When a name carries an implementation detail (framework, call pattern, internal mechanism), it is a signal to step up one level. Consequence for renamings: an implementation invisible to consumers does not force a rename; a change in public responsibility requires one — that is what justified `Deckle.Capture → Deckle.Audio` and `Deckle.Localization → Deckle.Catalog`.

## Closed vocabulary per dimension

Several dimensions carry a **closed** vocabulary whose elements are decided once and reused identically: `LogSource.*` observation sources, accepted class suffixes, boolean prefixes, module and namespace names. In a closed dimension, you pick from the existing vocabulary or extend it by a traced decision — no ad hoc invention. A real case that does not fit the existing set is the opportunity to extend cleanly or to reformulate the responsibility so it falls into an already-named category.

## Casing and prefixes

Casing rules follow the [Framework Design Guidelines](https://learn.microsoft.com/dotnet/standard/design-guidelines/capitalization-conventions) and the [C# identifier naming rules](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names) guide. **PascalCase** for everything visible (namespaces, types, methods, properties, events, public fields, constants, enum values, positional parameters of records). **camelCase** for parameters and locals, and for positional parameters of classes and structs. No Hungarian notation, no dash or underscore in public identifiers.

For **private fields**, the adopted convention is that of the [.NET Runtime coding style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md) — `_camelCase` for instance, `s_camelCase` for private static, `t_camelCase` for `[ThreadStatic]`. This is not in the historical Framework Design Guidelines but it is the living practice of Microsoft on its own runtime, and Deckle adopts it to align and visually signal scope.

Two-letter acronyms in uppercase (`IOStream`, `DbContext`), three letters and more in PascalCase (`Xml`, `Json`, `Html`). Consequence for Deckle: `Llm`, `Hud`, `Vad` in code — the familiar uppercase form remains valid in comments and human-readable logs. Interfaces prefixed `I`, generics prefixed `T` (rule [CA1715](https://learn.microsoft.com/visualstudio/code-quality/ca1715)), async methods suffixed `Async` without exception.

## Suffixes — accepted and to avoid

The tabulated detail lives in `taxonomie.md` with precise semantics and examples. Three families to retain at the doctrine level.

**Canonical accepted suffixes** — `Attribute`, `EventArgs`, `Exception`, `Stream`, `Reader`, `Writer`, `Collection`, `Builder`, `Factory`, `Service`, `Provider`, `Repository`, `Store`, `Strategy`, `Visitor`. All carry semantics recognized from the BCL or GoF patterns.

**Stabilized Deckle-specific suffixes** — `Engine` for a complex business pipeline with a lifecycle, `Host` for an adapter that bridges a boundary (interop, isolation), `Mapper` for a pure `(In) → Out` transformation, `Calculator` for a stateless aggregative computation, `Detector` for a binary classifier of a condition. Adding a new suffix to the closed vocabulary assumes a responsibility nameable in one sentence and a traced decision.

**Suffixes to avoid in new applicative code** — `Manager`, `Helper`, `Utility`/`Util`/`Utils`, generic `Wrapper`, `Handler` without pipeline context. The stance is documented on the .NET community side (see [Name Smells](https://daedtech.com/name-smells/)). `Helper` indicates that the main type is not self-sufficient; `Manager` typically signals an unrefactored overflow; `Utils` is the receptacle for functions without a home. For Deckle, `TrayIconManager` and `HotkeyManager` are legacy Windows interop cases admitted by explicit derogation — any new code prefers the precise role (`Registry`, `Store + Reader`, `Coordinator`).

**Service / Provider / Engine / Host disambiguation**. A `Service` orchestrates; a `Provider` answers passively; an `Engine` orchestrates a heavy pipeline with its own lifecycle; a `Host` adapts or bridges a boundary. When two suffixes seem applicable to the same type, it is usually because it carries two responsibilities — decompose.

## Booleans, collections, events

Booleans prefixed by a verb of state or capability — `Is*`, `Has*`, `Can*`, `Should*`, `Are*`, `Supports*`, `Allows*`. The prefix is required in Deckle to remove ambiguity with a type or method of the same name (the "optional" stance of the Framework Design Guidelines is hardened here). Negations in the name are forbidden — `CanSeek`, not `CantSeek`; no double negation. Booleans without a verb (`Flag`, `Status`, `Mode`) indicate nothing — name what is true.

Collections plural (`Items`, `Subscribers`, `Sinks`), single element singular. Non-flag enumerations singular, flags plural. Namespaces plural when semantically correct (`Strings`, `Controls`, `Converters`, `ViewModels`), singular for functional aggregates (`Engine`, `Setup`, `Telemetry`).

Events in the past tense for the accomplished fact (`Changed`, `Stopped`, `FrameArrived`, `TranscriptionFinished`), in the present participle for the cancelable preview (`Changing`, `Closing`). The [CA1713](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1713) rule forbids `Before*` and `After*`. The associated raise method carries the `On` prefix (canonical protected virtual pattern); this prefix is **reserved for the raise method on the emitter** — a handler on the subscriber side is named by its intent, not by `On*`.

## Namespaces and module boundaries

The namespace **mirrors the folder hierarchy** — a file under `Engine/` declares `<Module>.Engine`. The [Program organization](https://learn.microsoft.com/dotnet/csharp/fundamentals/program-structure/program-organization) doc calls violating this convention "actively confusing". Organization inside a module is done by feature, not by technical stereotype, except when the module is small and the stereotype stays readable (`Controls`, `Converters`, `Strings`, `Themes`).

Fuzzy generic namespaces are to avoid — `Common`, `Shared`, `Utilities`, `Helpers`, `Misc`. The project prefers naming the real capability. Special case of `Deckle.Core`: admissible **as long as** its responsibility stays "cross-module foundations without applicative dependency" and its public surface stays narrow — otherwise split or rename.

The sub-namespace versus sub-project choice follows `deckle-modularite`. The synthetic rule: a sub-namespace as long as the deployment cycle and the dependency graph stay simple; a sub-project when an acyclic boundary, an isolated test cycle, or a problematic volume justifies it. The sub-project name reflects the business capability (`Deckle.Lighting.Ambient`), not the stereotype.

## WinUI resources and localization

Three XAML directives must never be confused. `x:Name` identifies an element for code-behind (PascalCase, unique per namescope). `x:Key` is the key of a `ResourceDictionary`. `x:Uid` is the **localization** key on the PRI side — distinct from the XAML namescope.

`.resw` keys follow the pattern `<Scope>_<Element>.<Property>` or `<Scope>.<Property>`, with scope per page or per dialog (`WhisperPage_HeaderText.Text`, `CorpusConsent_Title`, `Common_Cancel`). A single `Resources.resw` per module under `Strings/en-US/`. A key sent for translation no longer changes — a rename triggers a retranslation cycle and is treated as a contract change. See `taxonomie.md` for detailed examples.

WinUI **theme resources** are named by their functional semantics, never by value — `LayerFillColorDefaultBrush`, `CardStrokeColorDefaultBrush`, `OverlayCornerRadius`, `ControlCornerRadius`. Any literal value in XAML that should follow the theme is a signal of the wrong primitive (see root doctrine). For local Deckle theme resources, convention `<Domain>.<Descriptor>.<Variant>` with type suffix (`Hud.Glow.BrushDefault`), recognizable domain, living under `Themes/<Domain>.xaml` of the relevant module.

## Typed observability and providers

When Deckle switches to EventSource (workstream tracked by `deckle-logging`), the **provider name** follows `Deckle-<Component>` with `-` as separator (never dot — ETW collision). The name is defined via `[EventSource(Name = "...")]`, not inherited from the C# name. The singleton is `public static readonly Log = new()`, type `sealed` inheriting directly from `EventSource`.

Events in the past tense for accomplished facts (`ModelLoaded`, `AppStarted`), adjacent `XStart`/`XStop` pairs with consecutive IDs for measured units of work. Keywords named by functional domain (`Lifecycle`, `Transcription`, `Capture`), not by module or technique. Complete canonical structure with reference code in `taxonomie.md`.

The **closed `LogSource` vocabulary** remains relevant even when the underlying engine switches to EventSource — it is the "event category" dimension exposed on the UI side. The `LogSource ↔ Keywords` mapping must be explicit and traced. Hierarchical sources (`SET.WHISPER`, `SET.GENERAL`) use the dot as level separator, distinct from the provider name format.

## Renaming and progressive hygiene

A non-trivial renaming is a contract change — it is done when responsibility has actually moved or when a past drift is consciously corrected. **Module by module at the moment the module is touched**, never in a giant centralized pass. This discipline joins that of comments (`deckle-docs`).

Three signals invite reconsidering an existing name. The name **describes implementation** rather than responsibility. The name carries a **fuzzy suffix** when the responsibility is precise and nameable otherwise. Two names **resemble each other to the point of being confused** (case of `HudWindow` and `HudOverlayWindow` which share the essentials — either factor out, or rename to make the role difference explicit).

A traced renaming leaves an **entry in the journal of the relevant module** with the old name, the new one, what triggered the change. The historical renamings (`Deckle.Capture → Deckle.Audio`, `Deckle.Localization → Deckle.Catalog`) are the canonical examples.

## Pointers

- **`taxonomie.md`** in this skill — tabulated detail of suffixes, x:Uid patterns, EventSource structure with keywords and tasks, commented good and bad examples. Loaded on demand.
- **`deckle-logging`** — `LogSource` vocabulary, write levels, procedure to decide what to observe.
- **`deckle-modularite`** — where a module ends, when to break out into a sub-project.
- **`deckle-docs`** — documentation convention and comment hygiene; a non-trivial renaming leaves a trace in the module journal.
- **`personal-conventions`** — cross-project conventions (language, wording, git, worktrees). `deckle-nomenclature` applies these conventions for the .NET / WinUI 3 context.
