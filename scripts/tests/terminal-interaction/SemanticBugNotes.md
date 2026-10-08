# Terminal-interaction semantic bug notes

## Compact Section prefixes expanded into full headings

- **Trigger:** Render a nested Action Menu whose Sections group independent operations, such as Maintenance statistics, cleanup, and AI-session actions.
- **Observed symptom:** Each compact prefix became an uppercase full-width Section rule followed by a separate option row, multiplying whitespace and breaking the original command-column rhythm.
- **Cause:** The renderer had only one visual projection for a semantic Section: a full heading and separator. It could not preserve Section semantics while presenting the label as a compact row prefix.
- **Violated invariant:** A nested Section may project as a compact Action Group without changing the independent Action intents it contains into Action Variants.
- **Recurrence cue:** A nested group of independent Actions emits `SectionSeparator`, or a caller creates an Action Row solely to recover the compact row shape.

Regression coverage: `scripts/tests/terminal-interaction/layout.tests.ps1` verifies compact group roles, adjacent rows, stable option tracks, and independent Action intents; `theme.tests.ps1` verifies that the group label takes the `ActionLabel` role of an Action Row label while its Actions keep the `Action` role.

## Independent Actions were declared as Action Variants

- **Trigger:** Describe a submenu with two independent operations that should appear near each other, such as README pulse and Changelog.
- **Observed symptom:** The catalog declared the Section label as an Action subject and the independent operations as its variants.
- **Cause:** A visual row shape was exposed as the workflow declaration, so sharing a row determined the semantic relationship between targets.
- **Violated invariant:** Sections contain semantic Actions, Accesses, and Action Rows; rows and columns are renderer results, and Action Variants are only alternate ways to carry out one shared Action.
- **Recurrence cue:** A caller creates an Action Row solely to place unrelated targets on the same line or changes object types to obtain a preferred column.

Regression coverage: `scripts/tests/terminal-interaction/contracts.tests.ps1` validates Section items, while `layout.tests.ps1` and `theme.tests.ps1` prove that independent Actions can share responsive layout without becoming Action Variants.

## Filter labels inherited the Action subject color

- **Trigger:** Render a Preparation with named Filters under the default Deckle theme.
- **Observed symptom:** Filter labels such as `Scope`, `Files`, `Measures`, and `Grouping` rendered cyan like Action subjects.
- **Cause:** The Preparation layout projected Filter labels through the `Action` presentation role even though they are non-interactive labels, not Actions.
- **Violated invariant:** Filter labels take the body presentation, while the labels of Action Rows and compact Action Groups alone use the teal `ActionLabel` color.
- **Recurrence cue:** A Preparation Filter label is emitted with the `Action` or `ActionLabel` presentation role, or resolves to the `ActionLabel` color.

Regression coverage: `scripts/tests/terminal-interaction/preparation.tests.ps1` verifies that a rendered Filter label uses the neutral body presentation while Selector targets retain their adjustment role.
