# Backend provider-store bug notes

## Migration publication raced across Windows sessions

- **Trigger:** Two Deckle sessions for the same user started before the legacy provider had been copied into the external version store.
- **Observed symptom:** Both sessions staged the same legacy bundle; one published the version directory, while the other failed its complete Anytype runtime when its competing directory move or activation lost the race.
- **Cause:** Unique staging was safe, but the version publish and activation switch had no current-user, cross-session serialization.
- **Violated invariant:** Concurrent migration is idempotent. Every contender either publishes or observes the same complete immutable version, and activation always names that direct version executable.
- **Recurrence cue:** Version-directory publication and activation appear outside `BackendProviderPublicationLease`, or a test coordinates concurrent migration with timing delays instead of arrival gates.
- **Regression coverage:** `BackendProviderStoreTests.Concurrent_legacy_migrations_publish_one_complete_active_version` holds both migrations at the publication boundary, releases them together, and asserts that both succeed on one active version.

## Legacy fallback could become a new spawn

- **Trigger:** The activation manifest was absent or invalid while the legacy payload executable still existed.
- **Observed symptom:** Provider resolution returned the replaceable legacy executable as the next serve specification instead of limiting it to adoption during migration.
- **Cause:** The same catalog method treated trusted adoption paths and the one activated spawn path as interchangeable.
- **Violated invariant:** A legacy image may be adopted while it survives, but every new spawn comes from `versions/<version>/anytype.exe` selected by a valid activation manifest.
- **Recurrence cue:** `ResolveActiveSpec` falls back to a trusted path, or activation accepts a nested/staging executable merely because its filename matches.
- **Regression coverage:** `BackendProviderStoreTests.Legacy_provider_is_adoptable_but_never_a_spawn_spec` and `Activation_manifest_selects_only_its_direct_version_executable` pin the separation.
