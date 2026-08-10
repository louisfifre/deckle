$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'
$LauncherDir = Join-Path $LibDir 'launcher'
$MenuDir = Join-Path $LibDir 'menu'
. (Join-Path $LauncherDir 'context.ps1')
. (Join-Path $LauncherDir 'menus.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

function Select-Grid {
    param($Header, $Rows, $Footer, $StartSel, [switch]$ClearScreen, $BannerStyle, $ResultTitle, $ResultLines, [switch]$ResultFollowTail, $Interaction, $SelectionState)
    $script:LastSubmenuBannerStyle = $BannerStyle
    $script:LastSubmenuRows = @($Rows)
    $script:LastSubmenuHeader = $Header
    $script:SubmenuFollowTailCalls += [bool]$ResultFollowTail
    $script:SubmenuSelectionStates += $SelectionState
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
$script:SubmenuFollowTailCalls = @()
$script:SubmenuSelectionStates = @()
Show-Submenu -Header 'Deckle > Test' -Rows $rows | Out-Null
Assert-Equal 'Compact' $script:LastSubmenuBannerStyle 'submenus always use the compact banner'

$script:WorktreeActionCount = 0
$script:MenuSelections = @('readme-stats', '__back__')
$script:SubmenuFollowTailCalls = @()
$script:SubmenuSelectionStates = @()
Show-ProjectMenu
Assert-Equal 1 $script:WorktreeActionCount 'project action runs once before the submenu resumes'
Assert-Equal $true $script:LastWorktreeScriptParameters.Commit 'README update requests a local commit'
Assert-Equal $false $script:SubmenuFollowTailCalls[0] 'project guidance starts on its first page'
Assert-Equal $false $script:SubmenuFollowTailCalls[1] 'project action logs preserve their opening context on the first page'
Assert-Equal $true ([object]::ReferenceEquals($script:SubmenuSelectionStates[0], $script:SubmenuSelectionStates[1])) 'project menu keeps one selection state across action redraws'
Assert-Equal 'Deckle > Project' $script:LastSubmenuHeader 'submenu header contains only the breadcrumb'

$script:WorktreeActionCount = 0
$script:MenuSelections = @('changelog', '__back__')
Show-ProjectMenu
Assert-Equal 1 $script:WorktreeActionCount 'changelog action runs once before the submenu resumes'
Assert-Equal $true $script:LastWorktreeScriptParameters.Commit 'changelog update requests a local commit'

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

$script:MenuSelections = @('__back__')
Show-MaintenanceMenu
$maintenanceRows = @($script:LastSubmenuRows | Where-Object { $_.ContainsKey('Prefix') })
Assert-Equal 3 $maintenanceRows.Count 'maintenance includes statistics, cleanup, and AI session rows'
Assert-Equal 'AI sessions' $maintenanceRows[2].Prefix 'AI session maintenance is machine-wide'
Assert-Equal 'Inspect AI session state' $maintenanceRows[2].Cells[0].Label 'safe session preview comes first'
Assert-Equal 'Reset AI session state' $maintenanceRows[2].Cells[1].Label 'destructive session reset is adjacent'
Assert-Equal 'danger' $maintenanceRows[2].Cells[1].Role 'destructive session reset uses the shared danger color'

Write-Host 'menus.tests.ps1: PASS' -ForegroundColor Green
