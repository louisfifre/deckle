---
name: deckle-testing
description: Use when you write a Deckle test or extend coverage, whatever its category, to place it in the suite that owns it with the category the taxonomy gives it.
metadata:
  version: "1.1"
---

## Why

How to test, what the code does through the seams the design already has, holds across projects and lives in the person's testing convention. What stays here is what only Deckle decides: its categories, its suites and its shared plumbing.

## Statement

Every test class carries a Category trait whose value comes from the taxonomy in `tests/GLOSSARY.md`. A module's tests live in the suite that owns it, `Deckle.<Module>.Tests` for the application and `scripts/tests` for the scripts, and plumbing that several C# suites share lives in `Deckle.TestSupport`.

A fixed bug gets a regression test when its failure can be reproduced through a seam the design already has. The test carries the `regression` category, recreates the failing conditions and is named for the behavior that must remain true. It fails on the faulty behavior and passes on the fixed one.
