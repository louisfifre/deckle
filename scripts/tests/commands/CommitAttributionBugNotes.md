# Commit-attribution bug notes

## Agent attribution survived the landing workflow

- **Trigger:** An automated commit command appended an agent `Co-Authored-By` trailer, then the workstream was merged through the normal close workflow.
- **Observed symptom:** Published Deckle history attributed five commits to an agent despite the maintainer-only identity rule.
- **Cause:** Commit messages were not mechanically validated, and the landing audit inspected subjects rather than full messages.
- **Violated invariant:** Every Deckle commit ships under the maintainer's sole identity, without agent co-author or generated-by attribution.
- **Recurrence cue:** A commit or landing workflow validates only changed files, hashes, or subjects and never inspects the full commit message.

Regression coverage: `scripts/tests/commands/install-hooks.tests.ps1` proves that the configured `commit-msg` guard rejects attribution in another repository while preserving Deckle's traditional `pre-commit` hook.

## Git configuration silently changed the recorded maintainer name

- **Trigger:** A normal commit ran while the effective global Git identity was `PelopeeNoire <git@louisfifre.com>`.
- **Observed symptom:** Hundreds of commits recorded the maintainer's former profile name even though the current GitHub profile and repository remote did not use it.
- **Cause:** Git reads author and committer identity from effective Git configuration, independently of the signed-in GitHub account, and the commit path enforced no exact identity.
- **Violated invariant:** Every Deckle commit records both author and committer exactly as `Louis <git@louisfifre.com>`.
- **Recurrence cue:** A clone, worktree, laptop, automation, or temporary `git -c` override can commit without first proving the effective author and committer identities.

Regression coverage: `scripts/tests/commands/install-hooks.tests.ps1` proves that the configured guard accepts the canonical identity and rejects wrong author or committer names and emails.
