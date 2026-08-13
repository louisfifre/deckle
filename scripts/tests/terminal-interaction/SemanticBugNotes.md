# Terminal-interaction semantic bug notes

## Independent Actions were declared as Action Variants

- **Trigger:** Describe a submenu with two independent operations that should appear near each other, such as README pulse and Changelog.
- **Observed symptom:** The catalog declared the Section label as an Action subject and the independent operations as its variants.
- **Cause:** A visual row shape was exposed as the workflow declaration, so sharing a row determined the semantic relationship between targets.
- **Violated invariant:** Sections contain semantic Actions, Accesses, and Action Rows; rows and columns are renderer results, and Action Variants are only alternate ways to carry out one shared Action.
- **Recurrence cue:** A caller creates an Action Row solely to place unrelated targets on the same line or changes object types to obtain a preferred column.

Regression coverage: `scripts/tests/terminal-interaction/contracts.tests.ps1` validates Section items, while `layout.tests.ps1` and `theme.tests.ps1` prove that independent Actions can share responsive layout without becoming Action Variants.
