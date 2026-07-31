$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$MenuDir = Join-Path $ScriptsDir 'lib\menu'
. (Join-Path $MenuDir 'chrome.ps1')
. (Join-Path $MenuDir 'grid-layout.ps1')
. (Join-Path $MenuDir 'grid-picker.ps1')
. (Join-Path $MenuDir 'list-picker.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$entries = @(
    [pscustomobject]@{ Branch = 'main'; Path = 'D:\projects\ai\deckle' }
    [pscustomobject]@{ Branch = 'fix/a-branch-name-that-needs-truncation'; Path = 'D:\worktrees\deckle\menu-polish' }
)
$rows = @(New-WorktreeGridRows -Entries $entries)

Assert-Equal 5 $rows.Count 'worktree picker includes navigation and section rows'
Assert-Equal '< Back' $rows[0].Cells[0].Label 'worktree picker starts with back'
Assert-Equal 'Available' $rows[2].Title 'worktree picker labels the list once'
Assert-Equal 'main  ·  deckle' $rows[3].Cells[0].Label 'main worktree keeps branch and directory together'
Assert-Equal 'fix/a-branch-name-that-needs-truncation  ·  menu-polish' $rows[4].Cells[0].Label 'branch label reaches layout intact before column truncation'

$worktreeGrid = New-GridPlan -Rows $rows
Assert-Equal 1 $worktreeGrid.SelectableRows[0].CellCount 'Back occupies one grid cell'
Assert-Equal 2 $worktreeGrid.ColumnCount 'worktrees preserve the shared second column without making it navigable'

$confirmationRows = @(New-YesNoGridRows -ConfirmLabel 'Delete outputs' -CancelLabel 'Keep files' -ContextLines @('Deletes generated files.') -Destructive)
Assert-Equal 'Before you continue' $confirmationRows[0].Title 'confirmation keeps its consequence in the same surface'
Assert-Equal 'Deletes generated files.' $confirmationRows[1].Text 'confirmation context appears before the buttons'
Assert-Equal 'Delete outputs' $confirmationRows[-1].Cells[0].Label 'confirmation keeps the explicit action label'
Assert-Equal 'danger' $confirmationRows[-1].Cells[0].Role 'destructive confirmation keeps its danger role'
Assert-Equal 'Keep files' $confirmationRows[-1].Cells[1].Label 'confirmation keeps the safe alternative'
Assert-Equal $entries[1].Path $rows[4].Cells[0].Value 'selection preserves the full worktree path'

$matching = Get-WorktreeMenuLabel -Branch 'feat/menu-polish' -Path 'D:\worktrees\deckle\menu-polish'
Assert-Equal 'feat/menu-polish' $matching 'matching branch and directory names are not repeated'

Write-Host 'list-picker.tests.ps1: PASS' -ForegroundColor Green
