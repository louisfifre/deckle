# Home layout bug notes

## A lone project in no category hidden in a closed fold

- **Trigger:** a store with no category and a project in none, the state every store has after "Add a project…" on an empty Home (seen 2026-11-25 on the maintainer's store: one project, no category).
- **Observed symptom:** Home showed only the header "Without a category 1", closed; the project's card was behind it.
- **Cause:** `HomePage.Build` drew the projects in no category in `WithoutCategoryFold` whatever the store held, and the fold is closed by default (spec, Home, item 8). The spec describes the fold below categories; the case of a Home with no category was not written down, and the code took the fold for it.
- **Violated invariant:** the fold sets loose projects apart from categories; with no category it has nothing to set them apart from, so it is not drawn (`HomeLayout.FoldsLooseProjects`, comment there).
- **Recurrence cue:** a Home that shows a header and no card while the store holds projects; a new grouping on Home that is closed by default.

Regression: `HomeLayoutTests.WithNoCategoryTheProjectsInNoCategoryShowOnHomeWithoutAFold`. It guards the rule, not the drawing: the fold's visibility is a WinUI control the unit suite does not instantiate.
