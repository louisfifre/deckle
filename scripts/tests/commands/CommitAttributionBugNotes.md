# Commit-attribution bug notes

## Agent attribution survived the landing workflow

- **Trigger:** An automated commit command appended an agent `Co-Authored-By` trailer, then the workstream was merged through the normal close workflow.
- **Observed symptom:** Published Deckle history attributed five commits to an agent despite the maintainer-only identity rule.
- **Cause:** Commit messages were not mechanically validated, and the landing audit inspected subjects rather than full messages.
- **Violated invariant:** Every Deckle commit ships under the maintainer's sole identity, without agent co-author or generated-by attribution.
- **Recurrence cue:** A commit or landing workflow validates only changed files, hashes, or subjects and never inspects the full commit message.

Regression coverage: `scripts/tests/commands/install-hooks.tests.ps1` proves that the configured `commit-msg` guard rejects attribution in another repository while preserving Deckle's traditional `pre-commit` hook.
