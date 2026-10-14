# Changelog-generator bug notes

## A single published release tag broke the generator

- **Trigger:** `changelog.ps1` ran against a repository whose `release-history.json` listed exactly one published tag.
- **Observed symptom:** The run reported the latest public release as `0` and failed on `git log 0..HEAD`.
- **Cause:** `Get-VersionTags` returned a `[string[]]` through the pipeline, which unrolls a one-element array into a bare string; `$tags[-1]` then read the tag's last character.
- **Violated invariant:** The published-tag list is an array whatever its length, so `$tags[-1]` is always a tag.
- **Recurrence cue:** A PowerShell function returns a collection without the leading comma, and every fixture it was tested with holds two or more elements.

Regression coverage: `scripts/tests/commands/changelog.tests.ps1` regenerates a changelog from a ledger holding one published tag.
