---
name: deckle-testing
description: Testing doctrine for the Deckle project (Windows .NET 10 / WinUI 3). Carries the way testing is designed and executed — layers in automatic scope (unit, integration, observability, regression) versus outside automatic scope (system, interactive), technical stack (xUnit v3 + Microsoft Testing Platform, native Assert, no mock framework), placement (tests/Deckle.Tests sibling of src/, mirror per folder), naming conventions and Category trait, TestEventListener pattern for observability tests, leaf-first strategy facing the XamlCompiler MSB3073 bug. Invoked before writing a test, before adding a module to coverage, before deciding on a fake or isolation strategy, and before modifying the test project structure. Triggers on phrases like deckle testing, deckle tests, deckle xunit, deckle unit test, deckle observability test, deckle integration test, add deckle test, deckle coverage, TestEventListener, deckle-testing.
---

# Deckle — Testing doctrine

## Role

Project-specific skill that answers the recurring question "how does Deckle test its code". Invoked before writing a test, before adding a module to coverage, before deciding on a fake or isolation strategy, and every time a decision touches the structure of the test project.

Does not duplicate the cross-project skill `tdd` (general philosophy and techniques) or `deckle-nomenclature` (cross-cutting naming rules). Captures the Deckle-specific residue — frozen technical stack, layer boundaries, physical placement, observability pattern, posture facing the historic XamlCompiler bug (see ADR-0012).

## Philosophy

Code is designed to be testable, but the test must not deform the public interface. The drift to avoid is "testable but unusable code" — over-abstraction of interfaces, dependency injection for its own sake, fakes everywhere when a pure function would suffice. A seam is created only when the test needs it and the need is real — not by anticipation.

We start simple — unit tests on pure leaf modules — and extend layer by layer. Each added layer is a tracked decision, not a wholesale migration. Coverage progresses at the pace of workstreams; we don't write tests for the past, we write them for what we touch.

## Layers in automatic scope

Four layers run without human intervention — `dotnet test` invokes them, an LLM agent drives them, eventual CI validates them.

**Unit** — isolated functions and types, with no external dependency. Deterministic, fast (milliseconds), no I/O, no clock, no visible threading. Category: `[Trait("Category", "unit")]`. Canonical example: `ChronoFormatter` (`TimeSpan` decomposition, `MM:SS.cc` format).

**Observability** — exercises the EventSource chain from emission to collection. The provider is a process-wide singleton; the listener subscribes by ETW name (`Deckle.<Module>`), naturally isolated by `using`. Category: `[Trait("Category", "observability")]`. Canonical pattern documented in `TestEventListener` (see dedicated section).

**Integration** — exercises several units together behind a public boundary. Stays in-process, no network, no UI. Category: `[Trait("Category", "integration")]`. To be introduced case by case, when an orchestrated responsibility deserves an internal end-to-end check.

**Regression** — test written in reaction to a fixed bug, to prevent it from coming back. Category: `[Trait("Category", "regression")]`. The test name mentions the reproduced symptom. Any non-trivial bug fix is ideally accompanied by a test of this layer.

## Layers outside automatic scope

Two layers remain manual — the agent does not trigger them, Louis drives them.

**System** — verification of the integrated app under real conditions (published binary, native dependencies in place, hotkey registered, tray active). Driven by Louis, occasionally scriptable but not automated.

**Interactive** — visual and sensory verification (HUD, animations, contrast, readability, perceived response time). Stays Louis's prerogative; no credible automation at this stage.

## Technical stack

**xUnit v3 (3.2.2)** — official recommendation from the xUnit team for any new project in 2026 (Brad Wilson). The test project is a standalone executable (`OutputType=Exe`) under Microsoft Testing Platform, also compatible with VSTest via `xunit.runner.visualstudio` for Visual Studio's Test Explorer.

**Microsoft.NET.Test.Sdk + xunit.runner.visualstudio** — VSTest orchestration for discovery by `dotnet test` and Test Explorer. Frozen versions: `Microsoft.NET.Test.Sdk 17.13.0`, `xunit.v3 3.2.2`, `xunit.runner.visualstudio 3.1.5`.

**Native xUnit Assert** — `Assert.Equal`, `Assert.Single`, `Assert.IsType`, etc. No FluentAssertions (v8 commercial, out of scope for the project). No Shouldly or equivalent — the native assertion is readable enough and adds no dependency.

**No mock framework** — Moq, NSubstitute, FakeItEasy are not introduced. When a seam is necessary, the fake is written by hand (`Fake<Interface>` class under `tests/.../Shared/`). This discipline keeps the real interface simple and forces the question of whether the seam is legitimate.

**Direct `dotnet test`** — the agent invokes the command without an intermediate script (`dotnet test tests/Deckle.Tests/Deckle.Tests.csproj`). Category filters work natively (`--filter "Category=unit"`). Integration into the `scripts/deckle.ps1` menu is optional human comfort, not a dependency.

## Placement and structure

**A single test project** — `tests/Deckle.Tests/` sibling of `src/`. Not one project per module — fragmentation will come if and only if justified by a build cycle or platform boundary.

**Permanent `.Tests` suffix** — `Deckle.Tests` is not a transitional name awaiting "promotion" to `src/`. The project lives alongside the tested modules, indefinitely.

**Mirror per folder** — the internal structure mirrors that of `src/`. Tests for the `Deckle.Chrono` module under `tests/Deckle.Tests/Chrono/`, tests for the `Deckle.Diagnostics` module under `tests/Deckle.Tests/Diagnostics/`. The namespace follows (`Deckle.Tests.Chrono`).

**Shared helpers under `Shared/`** — `tests/Deckle.Tests/Shared/` hosts utilities reusable across tested modules (`TestEventListener`, common fakes, fixture builders). `internal sealed` by default — minimal visibility, controlled surface.

**`ProjectReference` on demand** — the csproj references only the modules actually tested. Each module added to coverage adds a `ProjectReference`.

## Naming conventions

**Test class**: `<TestedType>Tests`. Example: `ChronoFormatterTests`, `DeckleChronoSourceTests`. One class per tested type or responsibility.

**Test method**: PascalCase, complete sentence without underscore, describes the expected behavior. Examples: `DecomposeReturnsZeroForTimeSpanZero`, `PilotEmittedCarriesTheNoteAsFirstPayload`. The `Method_State_Result` form with underscores (historical Microsoft style) is not adopted — it conflicts with `deckle-nomenclature` (strict PascalCase, no underscore in public identifiers).

**Category trait**: applied at the class level when all methods in the class belong to the same layer. At the method level if a class mixes unit and observability (rare case, signal for splitting).

**Arrange / Act / Assert**: visible sequence, separated by blank lines, without redundant `// Arrange` comments. One test = one fact. If the assert demands several correlated checks (for example: an event has the right ID and the right level), they hold together in a single method; otherwise, split.

## TestEventListener pattern

Deckle's observability testing relies on an instrumented `EventListener` — `tests/Deckle.Tests/Shared/TestEventListener.cs`. The pattern is canonical for any future `Deckle.<Module>` provider.

Typical usage in a test:

```csharp
using var listener = new TestEventListener("Deckle.Chrono");
DeckleChronoSource.Log.PilotEmitted("payload-content");

var ev = Assert.Single(listener.Events);
Assert.Equal(DeckleChronoSource.EvtPilotEmitted, ev.EventId);
```

Two native pitfalls to know. `OnEventSourceCreated` is invoked for preexisting sources during the base class `EventListener` constructor, before the derived class's fields are assigned — hence the explicit re-scan via `EventSource.GetSources()` after assigning the name in the listener's constructor. And `OnEventWritten` can receive non-Deckle system events (`RuntimeEventSource`) depending on passive `EnableEvents` — hence the defensive filter by provider name at the entry of `OnEventWritten`.

The `using` is important: `Dispose` unsubscribes the listener, otherwise it keeps capturing emissions from subsequent tests.

## Coverage progression

`dotnet test` is usable on any Deckle module, including those that transitively pull `Microsoft.WindowsAppSDK`. The `MSB3073 XamlCompiler.exe exited with code 1` bug that had motivated a historic "leaf-first" strategy (see root `CLAUDE.md` and [microsoft-ui-xaml#8871](https://github.com/microsoft/microsoft-ui-xaml/issues/8871)) no longer reproduces in the current combination. Decision recorded by [ADR-0012](../../../docs/adr/0012-adoption-de-dotnet-build-et-dotnet-test.md).

The "pure modules before WinAppSDK modules" progression order remains a reasonable **pedagogical preference** — starting with `Deckle.Chrono` then `Deckle.Core` then the pure parts of `Deckle.Composition` isolates the testing mechanics before crossing platform dependencies. But it is no longer a **technical constraint**. When a workstream touches a WinAppSDK module (`Deckle.Catalog`, `Deckle.Hud`, `Deckle.Settings`, `Deckle.Transcription`, etc.), coverage can extend there directly.

If the bug reappears (signal: `MSB3073` on `dotnet build` or `dotnet test`, log enriched by the WindowsAppSDK 1.8.8 fix that will make the error readable, failure on an eventual CI/CD environment), reintroduce the MSBuild VS workaround — the technical recipe is tracked in [ADR-0012](../../../docs/adr/0012-adoption-de-dotnet-build-et-dotnet-test.md), reapplicable.

## Evolution

The test project starts minimalist. Adding a dimension (new category, test NuGet dependency, folder structure that deviates from the mirror, shared helper that changes shape) is a tracked decision, not an automatism — when the need arises, it gets discussed before being written. The frozen choices above (xUnit v3, native Assert, no mock framework, `.Tests` sibling, mirror per folder) are only revisited on observed and explicitly discussed drift.

## Pointers

- **`tdd`** — general TDD philosophy, design techniques for testability (deep modules, interface design, mocking, refactoring). Read as a complement when a design question emerges.
- **`deckle-nomenclature`** — cross-cutting naming rules (PascalCase, allowed suffixes); this skill applies these rules to the testing context.
- **`deckle-workflow`** — daily build doctrine (`dotnet build`, orchestration scripts) that this doctrine overlaps with.
- **`deckle-docs`** — where the project's written traces live; a structural change to testing leaves a trace here or in an ADR depending on weight.
- **`src/Deckle.Diagnostics/CLAUDE.md`** — cross-cutting EventSource convention (providers, listeners, JSONL schema, canonical observable classes) and provider-side test pattern (motivation, example, ADR-0005 link).
