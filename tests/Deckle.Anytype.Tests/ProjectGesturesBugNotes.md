# Project gesture bug notes

## Epic creation omitted the available template

- **Trigger:** `create_epic` created a new Epic after the live Dev space gained the `EPIC Par défaut` template.
- **Observed symptom:** the Epic opened as a bare collection with no configured properties or views, so its project membership could not be usefully displayed or sorted.
- **Cause:** `CreateEpicAsync` omitted `template_id`, and its regression test explicitly required that omission based on an older measurement that no Epic template existed.
- **Violated invariant:** every creation gesture for a templated planning type must pass that type's measured template id because Anytype does not apply the default template implicitly through the REST API.
- **Recurrence cue:** a planning type's template changes in the live space while `DevSpace.Templates` or its creation-payload regression test remains absent or stale.

Regression: `ProjectGesturesTests.CreateEpicPassesTheEpicTemplateIdSoTheEpicIsBornWithItsViews`.
