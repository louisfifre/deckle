# ReleaseResolver bug notes

## A prerelease could be chosen as the newest release

- **Trigger:** the GitHub release list holds a release flagged as a prerelease with a strict `vX.Y.Z` tag that is the highest version. Reproduced 2026-10-22 by test; latent in production, since the release script never sets the prerelease flag.
- **Observed symptom:** the installer or update check would offer the prerelease as the newest release.
- **Cause (established by running the test):** the resolver parsed `Prerelease` but filtered only `Draft`.
- **Violated invariant:** only a published, non-prerelease app release is offered for install.
- **Recurrence cue:** an update offer whose tag the maintainer did not publish as a regular release.

Regression: `ReleaseResolverRegressionTests` (two tests).
