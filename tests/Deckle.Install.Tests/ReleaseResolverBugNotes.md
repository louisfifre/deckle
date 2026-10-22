# ReleaseResolver bug notes

## A prerelease could be chosen as the newest release

- **Trigger:** the GitHub release list holds a release flagged as a prerelease with a strict `vX.Y.Z` tag that is the highest version. Reproduced 2026-10-22 by test; latent in production, since the release script never sets the prerelease flag.
- **Observed symptom:** the installer or update check would offer the prerelease as the newest release.
- **Cause (established by running the test):** the resolver parsed `Prerelease` but filtered only `Draft`.
- **Violated invariant:** only a published, non-prerelease app release is offered for install.
- **Recurrence cue:** an update offer whose tag the maintainer did not publish as a regular release.

Regression: `ReleaseResolverRegressionTests` (two tests).

## A required asset listed twice failed with a message that named neither release nor file

- **Trigger:** the newest app release lists the same required asset name twice. Reproduced 2026-10-22 by test. Inferred, not observed: GitHub is believed to refuse two assets of one name in a release, so this is hardening and not a case seen in production.
- **Observed symptom:** the resolver already failed closed, but with the LINQ message "Sequence contains more than one matching element", which says nothing about the release or the file (confirmed by running the test against the old code).
- **Cause:** `FindRequiredAsset` used `SingleOrDefault`, whose exception carries no context.
- **Violated invariant:** a failure carries its cause, so the refusal names the release and the asset.
- **Recurrence cue:** an installer or update-check error reading "Sequence contains more than one matching element".
- **Not covered:** the listing reads only the first page of `/releases` (GitHub's default of 30). If more than 30 newer `native-v*` releases ever pushed the newest app release off that page, it would not be found. That is a product choice and is not decided here.

Regression: `ReleaseResolverRegressionTests.A_release_listing_a_required_asset_twice_is_refused_by_naming_it`.
