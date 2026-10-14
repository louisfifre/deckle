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

## Test fixtures committed under a throwaway identity

- **Trigger:** The maintainer ran `changelog.tests.ps1` from his own terminal.
- **Observed symptom:** The first fixture commit failed with exit code 1 and no reason, since the test discarded git's output.
- **Cause:** The fixture configured `Deckle Tests <deckle-tests@example.invalid>`, which the global commit guard rejects in every repository. The agent shell that wrote the test carries `GIT_AUTHOR_NAME` and `GIT_COMMITTER_NAME` set to the maintainer, and those variables outrank repository config, so the test passed there.
- **Violated invariant:** A fixture commit records the maintainer's identity like any other commit.
- **Recurrence cue:** A test passes in an agent shell and fails in the maintainer's terminal; a test discards git's stderr on failure.

Regression coverage: `scripts/tests/commands/changelog.tests.ps1` and `versioning-workflow.tests.ps1` commit their fixtures under the canonical identity and surface git's output when a command fails.
