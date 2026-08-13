# Launcher action-result bug notes

## Completed action logs reopen at the beginning

- **Trigger:** Complete an action whose transcript spans more than one result page, then return to its launcher menu.
- **Observed symptom:** The result viewport opens on the first page although the launcher documentation says action logs reopen on their latest page.
- **Cause:** `Show-Submenu` accepts a `ResultMode` but does not use it, and never forwards `ResultFollowTail` to the grid picker; every retained result therefore uses the same first-page default.
- **Violated invariant:** Retained action logs reopen on their latest page, while guidance and reports reopen at the beginning.
- **Recurrence cue:** A result kind is declared in launcher state but is not carried into the viewport's initial paging policy.

Current coverage in `scripts/tests/launcher/menus.tests.ps1` encodes the faulty first-page behavior; replace that expectation with a regression assertion when the launcher result states are redesigned.
