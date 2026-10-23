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

Regression: `ReleaseResolverRegressionTests.A_release_listing_a_required_asset_twice_is_refused_by_naming_it`.

## The newest app release was not found once 30 newer native releases pushed it off the first page

- **Trigger:** the `/releases` listing holds 30 or more `native-v*` releases newer than the newest app release. The resolver read one request, which GitHub serves as 30 releases by default, so the app release was on page 2. Reproduced 2026-10-22 by test with a fed listing; not seen in production, where the native runtime has far fewer releases.
- **Observed symptom:** the installer and the update check failed with "No published Deckle app release found on GitHub." although a published app release existed.
- **Cause (established by running the test against the old code):** `ResolveLatestAsync` made a single unpaged GET and treated the first page as the whole listing.
- **Violated invariant:** an existing published app release is found whatever the number of other releases around it.
- **Recurrence cue:** that refusal message while the releases page of the repository shows a recent `vX.Y.Z` release.
- **Fix:** the listing is read in pages of 100 until a page holds a published app release, the listing runs out (a page shorter than 100) or 10 pages have been read.
- **Limit kept:** the search stops at the first page holding an app release and so relies on the API listing newest first. A lower version created after a higher one could hide it on a later page; the release flow does not produce that, and reading every page to rule it out would cost up to ten requests for each check.

Regression: `ReleaseResolverRegressionTests` (`The_newest_app_release_is_found_on_a_later_page`, and three tests on where the search stops).
