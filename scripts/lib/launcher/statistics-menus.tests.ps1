$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'statistics-menus.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 'Repository statistics' (Get-MaintenanceScanLabel -Kind Repository) 'repository label matches the parent menu action'
Assert-Equal 'Deckle > Maintenance > Repository statistics > Overview' `
    (Get-MaintenanceScanHeader -Kind Repository -Segments 'Overview') `
    'scan breadcrumb preserves the real parent and current goal'
Assert-Equal 'Deckle > Maintenance > Context statistics > Custom > Measures' `
    (Get-MaintenanceScanHeader -Kind Context -Segments @('Custom', 'Measures')) `
    'nested editor breadcrumb preserves every hierarchy level'

function Show-Submenu {
    param($Header, $Rows, $ResultTitle, $ResultLines, $ResultMode, $Interaction, [switch]$PreparedRows, $BannerStyle)
    $script:CapturedResultHeader = $Header
    $script:CapturedResultRows = @($Rows)
    $script:CapturedResultMode = $ResultMode
    return $null
}

$specification = [pscustomobject]@{ Kind = 'Repository'; GoalLabel = 'Overview'; ScopePath = '' }
$result = [pscustomobject]@{ Title = 'Repository · Overview'; Lines = @('42 files') }
Show-MaintenanceScanResult -Specification $specification -Result $result | Out-Null
Assert-Equal 'Deckle > Maintenance > Repository statistics > Overview' $script:CapturedResultHeader 'result remains under its statistics parent'
Assert-Equal '< Back' $script:CapturedResultRows[0].Cells[0].Label 'result exposes one explicit Back action'
Assert-Equal 'Report' $script:CapturedResultMode 'result keeps report paging behavior'

function Select-MaintenanceScanGoal {
    param($Kind)
    $script:GoalCallCount++
    $next = $script:GoalSelections[0]
    $script:GoalSelections = @($script:GoalSelections | Select-Object -Skip 1)
    if ($next -eq '__back__') { return $null }
    return $next
}
function New-MaintenanceScanSpecification {
    param($Kind, $Goal)
    return [pscustomobject]@{ Kind = $Kind; GoalLabel = 'Overview'; ScopePath = ''; Goal = $Goal }
}
function Get-WorktreeOrReturn { return 'D:\repo' }
function Resolve-MaintenanceScanSpecification { param($Specification, $Worktree) return $Specification }
function Show-MaintenanceScanReview {
    param($Specification, $Worktree)
    $script:ReviewCallCount++
    $next = $script:ReviewSelections[0]
    $script:ReviewSelections = @($script:ReviewSelections | Select-Object -Skip 1)
    if ($next -eq '__back__') { return $null }
    return $next
}
function Copy-MaintenanceScanSpecification { param($Specification) return $Specification }
function Show-CustomScanEditor { param($Specification, $ResultLines) $script:EditorCallCount++; return $null }
function Show-MenuStatus { param($Header, $Title, $Lines) }
function Get-MaintenanceScopeLabel { param($ScopePath) return 'Whole tracked repository' }
function Invoke-MaintenanceTargetedScan {
    param($Specification, $Worktree, $LibDir)
    $script:ScanCallCount++
    return [pscustomobject]@{ Title = 'Repository · Overview'; Lines = @('42 files') }
}
function Show-MaintenanceScanResult { param($Specification, $Result) $script:ResultCallCount++ }

function Reset-FlowState {
    param([string[]]$Goals, [string[]]$Reviews)
    $script:GoalSelections = @($Goals)
    $script:ReviewSelections = @($Reviews)
    $script:GoalCallCount = 0
    $script:ReviewCallCount = 0
    $script:EditorCallCount = 0
    $script:ScanCallCount = 0
    $script:ResultCallCount = 0
    $script:LibDir = 'D:\lib'
}

Reset-FlowState -Goals @('overview', '__back__') -Reviews @('run')
Invoke-MaintenanceScanFlow -Kind Repository
Assert-Equal 2 $script:GoalCallCount 'Back from a completed overview returns to repository statistics'
Assert-Equal 1 $script:ResultCallCount 'completed overview is shown in its child result surface'
Assert-Equal 1 $script:ScanCallCount 'returning to repository statistics does not rerun the scan'

Reset-FlowState -Goals @('overview', '__back__') -Reviews @('__back__')
Invoke-MaintenanceScanFlow -Kind Repository
Assert-Equal 2 $script:GoalCallCount 'Back from review returns to repository statistics'
Assert-Equal 0 $script:ScanCallCount 'Back from review does not run the scan'

Reset-FlowState -Goals @('overview', '__back__') -Reviews @('edit', '__back__')
Invoke-MaintenanceScanFlow -Kind Repository
Assert-Equal 2 $script:ReviewCallCount 'Back from Edit scan returns to review'
Assert-Equal 1 $script:EditorCallCount 'Edit scan opens the custom editor once'
Assert-Equal 2 $script:GoalCallCount 'Back from review still returns to repository statistics after editing'

Write-Host 'statistics-menus.tests.ps1: PASS' -ForegroundColor Green
