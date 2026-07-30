$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'context.ps1')
. (Join-Path $PSScriptRoot 'menus.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

function Select-Grid {
    param($Header, $Rows, $Footer, $StartSel, [switch]$ClearScreen, $BannerStyle, $ResultTitle, $ResultLines)
    $script:LastSubmenuBannerStyle = $BannerStyle
    $script:LastSubmenuRows = @($Rows)
    if ($script:MenuSelections.Count -gt 0) {
        $next = $script:MenuSelections[0]
        $script:MenuSelections = @($script:MenuSelections | Select-Object -Skip 1)
        return $next
    }
    return $script:NextMenuSelection
}
function Invoke-WorktreeScript {
    param($Script, $Label, $Source, $MenuRows, $ScriptParameters)
    $script:WorktreeActionCount++
    $script:LastWorktreeScriptParameters = $ScriptParameters
    return [pscustomobject]@{ Title = 'Updated'; Lines = @('done') }
}

$rows = @(@{ Prefix = 'Section'; Items = @( @{ Label = 'Action'; Value = 'action' } ) })
$script:NextMenuSelection = '__back__'
$script:MenuSelections = @()
Show-Submenu -Header 'Deckle > Test' -Rows $rows | Out-Null
Assert-Equal 'Compact' $script:LastSubmenuBannerStyle 'submenus always use the compact banner'

$script:WorktreeActionCount = 0
$script:MenuSelections = @('readme-stats', '__back__')
Show-ProjectMenu
Assert-Equal 'Compact' (Get-DeckleMenuBannerStyle) 'running a command keeps the compact banner'
Assert-Equal 1 $script:WorktreeActionCount 'project action runs once before the submenu resumes'
Assert-Equal $true $script:LastWorktreeScriptParameters.Commit 'README update requests a local commit'

$script:MenuSelections = @('__back__')
Show-ReleaseMenu
$releaseRows = @($script:LastSubmenuRows | Where-Object { $_.ContainsKey('Prefix') })
Assert-Equal 2 $releaseRows.Count 'release choices form a two-by-two matrix'
Assert-Equal 'Publish' $releaseRows[0].Prefix 'publish row comes first'
Assert-Equal 'App release' $releaseRows[0].Cells[0].Label 'app publish stays in the left column'
Assert-Equal 'Native runtime' $releaseRows[0].Cells[1].Label 'native publish stays in the right column'
Assert-Equal 'Prepare' $releaseRows[1].Prefix 'prepare row comes second'
Assert-Equal 'App artifacts' $releaseRows[1].Cells[0].Label 'app prepare stays in the left column'
Assert-Equal 'Native runtime' $releaseRows[1].Cells[1].Label 'native prepare stays in the right column'

Write-Host 'menus.tests.ps1: PASS' -ForegroundColor Green
