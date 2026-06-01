---
name: deckle-testing
description: How the Deckle project tests its code (.NET 10 / WinUI 3) — the testable-without-deforming posture, a frozen minimal stack, coverage that grows per workstream. Invoke before writing a test, extending coverage, or reshaping the test project. Triggers like deckle testing, deckle tests, add a deckle test, deckle test stack, deckle coverage, observability test.
type: skill
---

# Deckle — Testing doctrine

## Role

How Deckle tests its code. Invoked before writing a test, adding a module to coverage, choosing a fake or isolation strategy, or touching the shape of the test project. Young doctrine — the project has barely begun testing — so it stays deliberately thin.

## Posture

Code is designed to be testable, but a test must never **deform the public interface**. The drift to avoid is "testable but unusable": over-abstraction, dependency injection for its own sake, fakes everywhere where a pure function would do. A seam is created only when a test genuinely needs it — never by anticipation.

Coverage grows **per workstream**: start simple (unit tests on pure leaf modules), extend layer by layer, test what we touch rather than the past. Each added layer or dimension is a tracked decision, not a wholesale migration; the frozen choices below are revisited only on observed, discussed drift.

## Stack and placement

A single test project, `tests/Deckle.Tests/`, sibling of `src/` and mirroring its folders. Frozen, minimal stack: **xUnit v3** on the **Microsoft Testing Platform**, **native `Assert`** (no FluentAssertions or Shouldly), **hand-written fakes** (no mock framework — a real seam is written by hand, which keeps the interface honest). Exact package versions live in the `.csproj`, not here. Test naming follows `deckle-nomenclature`.

## Layers

Four run automatically (`dotnet test`, agent-driven, CI-ready), each tagged `[Trait("Category", …)]`: **unit**, **integration**, **observability** (the EventSource emission→collection chain), **regression** (written against a fixed bug to pin it). Two stay manual, Louis-driven: **system** (the integrated app under real conditions) and **interactive** (visual and sensory verification).

## Pointers

- **`tdd`** — general TDD philosophy and testability design; read when a design question emerges.
- **`deckle-nomenclature`** — naming rules, applied here to test classes and methods.
- **`src/Deckle.Diagnostics/CLAUDE.md`** — the observability test pattern (`TestEventListener`, provider-side example, ADR-0005 link); the listener's native gotchas live as comments in `TestEventListener.cs`.
- **[ADR-0012](../../../docs/adr/0012-adoption-de-dotnet-build-et-dotnet-test.md)** — the `dotnet build` / `dotnet test` decision and the reapplicable XamlCompiler `MSB3073` workaround if the historic bug returns.
- **`deckle-workflow`** — the daily build doctrine this overlaps with.
- **`session-save-context`** — route a structural testing change through the cascade for a durable trace.
