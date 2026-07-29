$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'chrome.ps1')
. (Join-Path $PSScriptRoot 'grid-picker.ps1')
. (Join-Path $PSScriptRoot 'list-picker.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$entries = @(
    [pscustomobject]@{ Branch = 'main'; Path = 'D:\projects\ai\deckle' }
    [pscustomobject]@{ Branch = 'fix/a-branch-name-that-needs-truncation'; Path = 'D:\worktrees\deckle\menu-polish' }
)
$rows = @(New-WorktreeGridRows -Entries $entries -BranchWidth 12)

Assert-Equal 5 $rows.Count 'worktree picker includes navigation and section rows'
Assert-Equal '< Back' $rows[0].Cells[0].Label 'worktree picker starts with back'
Assert-Equal 'Available' $rows[2].Title 'worktree picker labels the list once'
Assert-Equal 'main' $rows[3].Prefix 'worktree branch has its own column'
Assert-Equal 'deckle' $rows[3].Cells[0].Label 'worktree directory has its own column'
Assert-Equal 12 $rows[4].Prefix.Length 'long branch is constrained before layout'
Assert-Equal 'menu-polish' $rows[4].Cells[0].Label 'directory stays readable when branch is long'
Assert-Equal $entries[1].Path $rows[4].Cells[0].Value 'selection preserves the full worktree path'

Write-Host 'list-picker.tests.ps1: PASS' -ForegroundColor Green
