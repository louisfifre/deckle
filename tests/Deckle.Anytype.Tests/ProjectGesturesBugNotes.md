# Project gesture bug notes

## Epic creation omitted the available template

- **Trigger:** `create_epic` created a new Epic after the live Dev space gained the `EPIC Par défaut` template.
- **Observed symptom:** the Epic opened as a bare collection with no configured properties or views, so its project membership could not be usefully displayed or sorted.
- **Cause:** `CreateEpicAsync` omitted `template_id`, and its regression test explicitly required that omission based on an older measurement that no Epic template existed.
- **Violated invariant:** every creation gesture for a templated planning type must pass that type's measured template id because Anytype does not apply the default template implicitly through the REST API.
- **Recurrence cue:** a planning type's template changes in the live space while `DevSpace.Templates` or its creation-payload regression test remains absent or stale.

Regression: `ProjectGesturesTests.CreateEpicPassesTheEpicTemplateIdSoTheEpicIsBornWithItsViews`.

## Project overview omitted tasks past the first search page

- **Trigger:** `project_overview` on a project whose linked tasks included one not modified for a long time, once the space held more tasks than one search page (2026-09-10).
- **Observed symptom:** the task was absent from the overview although its `relation_projet` named the project (`link` reported it already present, 0 additions). Writing the task (an `archive:false` PATCH) made it appear at the top of the overview, and an older task of the same project vanished at the same moment. The same single-page window applied to the project list, to the report join of `project_overview` and `session_start`, and to the schema surface's collection read.
- **Cause:** every exhaustive listing read one search page and filtered it client-side. `SearchAsync` sent `limit` in the JSON body, a field the 2025-11-08 `SearchRequest` schema does not carry; paging (`offset`, `limit`, maximum 1000) lives in the query string, and the response announces the rest through `pagination.has_more`. The gestures' `limit: 200` therefore never reached the wire. Uncertain: that the server then served its default page of 100 is read from the schema, not measured on the wire — the symptom is consistent with a page of either size.
- **Violated invariant:** a listing that feeds a client-side filter reads the complete set — every page until `has_more` is false — whatever the number of objects in the space.
- **Recurrence cue:** a `SearchAsync` call with an empty query used as an inventory, or any paged endpoint read without a `has_more` loop; an object that "reappears" in a digest right after being written, while another one drops out.

Regression: `ProjectGesturesTests.OverviewListsALinkedTaskBeyondTheFirstSearchPage`.
