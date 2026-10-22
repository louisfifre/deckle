---
name: deckle-testing
description: Use when you write a Deckle test or extend coverage, whatever its category, to test what the code does through the seams the design already has.
metadata:
  version: "1.0"
---

## Why

A test is worth keeping when it survives a refactor and follows a re-tune. Code bent only to be tested is harder to use, and that drift costs more than the test is worth.

## Statement

Test what the code does, so the test survives a change in how it does it.

Compare against the value the production code itself uses, so the test follows a re-tune. Write a fixed value in the test only when that value is the promise.

Reach the code through a seam the design already has. When a test would need a seam the design does not want, change the test.

Coverage grows with each workstream. A workstream tests the code it touches and leaves the rest for the workstream that reaches it.

Every test class carries a Category trait whose value comes from the taxonomy in `tests/GLOSSARY.md`. A module's tests live in the suite that owns it, `Deckle.<Module>.Tests` for the application and `scripts/tests` for the scripts, and plumbing that several C# suites share lives in `Deckle.TestSupport`.

A fixed bug gets a regression test when its failure can be reproduced through a seam the design already has. The test carries the `regression` category, recreates the failing conditions and is named for the behavior that must remain true. It fails on the faulty behavior and passes on the fixed one.
