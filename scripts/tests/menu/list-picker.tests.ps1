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
Assert-Equal $true $rows[4].FullWidth 'worktree entries request both grid columns'

$worktreeGrid = New-GridPlan -Rows $rows -CategoryWidth 0
Assert-Equal 1 $worktreeGrid.SelectableRows[0].CellCount 'Back occupies one grid cell'
Assert-Equal 2 $worktreeGrid.ColumnCount 'worktrees preserve the shared second column without making it navigable'
Assert-Equal 0 $worktreeGrid.PrefixWidth 'worktrees do not reserve the unrelated category track'
Assert-Equal $true $worktreeGrid.Body[3].FullWidth 'the grid preserves the full-width worktree row contract'

$script:RenderedSegments = [System.Collections.Generic.List[string]]::new()
function Write-MenuLinePrefix { param([int]$Row) }
function Write-MenuContentSegment {
    param(
        [string]$Text,
        [ref]$Written,
        [int]$InnerWidth,
        [string]$ForegroundColor,
        [string]$BackgroundColor
    )
    $segment = Limit-MenuText -Text $Text -Width ($InnerWidth - $Written.Value)
    $script:RenderedSegments.Add($segment)
    $Written.Value += $segment.Length
}
function Write-MenuLineRemainder { param([int]$InnerWidth, [int]$Written) }
$columnWidths = Get-GridColumnWidths -ContentWidth 40 -PrefixWidth 0 -ColumnCount $worktreeGrid.ColumnCount
Write-GridLine `
    -Top 0 -Index 3 -Body $worktreeGrid.Body -ColW $columnWidths -PrefixW 0 `
    -InnerWidth 40 -ContentWidth 40 -ActiveBodyIndex 3 -ActiveCol 0 -TrailingColumn 2
Assert-Equal 38 $script:RenderedSegments[1].Length 'a worktree label receives the width of both columns after the row inset'

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
