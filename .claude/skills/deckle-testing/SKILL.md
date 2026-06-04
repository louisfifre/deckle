---
name: deckle-testing
description: How the Deckle project tests its code (.NET 10 / WinUI 3) — the testable-without-deforming posture, per-module test projects, shared test support, coverage that grows per workstream. Invoke before writing a test, extending coverage, or reshaping the test projects. Triggers like deckle testing, deckle tests, add a deckle test, deckle test stack, deckle coverage, observability test.
type: skill
---

# Deckle — Testing doctrine

## Role

How Deckle tests its code. Invoked before writing a test, adding a module to coverage, choosing a fake or isolation strategy, or touching the shape of the test projects. Young doctrine — the project has barely begun testing — so it stays deliberately thin.

## Posture

Code is designed to be testable, but a test must never **deform the public interface**. The drift to avoid is "testable but unusable": over-abstraction, dependency injection for its own sake, fakes everywhere where a pure function would do. A seam is created only when a test genuinely needs it — never by anticipation.

Coverage grows **per workstream**: start simple (unit tests on pure leaf modules), extend layer by layer, test what we touch rather than the past. Each added layer or dimension is a tracked decision, not a wholesale migration; the frozen choices below are revisited only on observed, discussed drift.

## Stack and placement

The root `Deckle.Tests.sln` aggregates the automatic test suite so `dotnet test -c Debug -p:Platform=x64` can run every module test project from the repository root. Tests live under `tests/` with **one test project per tested module**, named `Deckle.<Module>.Tests` and referencing only the module under test plus explicit supporting modules needed by those tests. The project name mirrors the production module boundary: `Deckle.Audio.Tests` tests `Deckle.Audio`, `Deckle.Lighting.Ambient.Tests` tests `Deckle.Lighting.Ambient`, and so on. This keeps each test assembly aligned with one module owner and makes `InternalsVisibleTo` narrow instead of granting visibility to a monolithic test assembly.

Shared test plumbing lives in `tests/Deckle.TestSupport/`. It is a support library, not a test project: reusable EventSource assertions, event-args helpers, and the Windows App SDK bootstrap live there so every module test project can reuse the same initialization path without duplicating WinUI boot code.

Common test project shape lives in `tests/Directory.Build.props`: xUnit v3 on the Microsoft Testing Platform, `OutputType=Exe`, `IsTestProject=true`, the VSTest bridge, and the reference to `Deckle.TestSupport`. Package versions live in root `Directory.Packages.props` via CPM, not in individual test `.csproj` files. Assertions stay on native `Assert` (no FluentAssertions or Shouldly), and fakes stay hand-written (no mock framework — a real seam is written by hand, which keeps the interface honest). Test naming follows `deckle-nomenclature`.

## Layers

Four run automatically (`dotnet test`, agent-driven, CI-ready), each tagged `[Trait("Category", …)]`: **unit**, **integration**, **observability** (the EventSource emission→collection chain), **regression** (written against a fixed bug to pin it). Two stay manual, Louis-driven: **system** (the integrated app under real conditions) and **interactive** (visual and sensory verification).

## Pointers

- **`tdd`** — general TDD philosophy and testability design; read when a design question emerges.
- **`deckle-nomenclature`** — naming rules, applied here to test projects, test classes, and methods.
- **`deckle-modularite`** — production/test project boundaries and the support-library distinction.
- **`src/Deckle.Diagnostics/CLAUDE.md`** — the observability test pattern (`TestEventListener`, provider-side example, ADR-0003 link); the listener's native gotchas live as comments in `TestEventListener.cs`.
- **`deckle-workflow`** — the daily build doctrine this overlaps with.
- **`session-save-context`** — route a structural testing change through the cascade for a durable trace.
