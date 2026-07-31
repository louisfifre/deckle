$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$MenuDir = Join-Path $ScriptsDir 'lib\menu'
. (Join-Path $MenuDir 'chrome.ps1')
. (Join-Path $MenuDir 'grid-layout.ps1')
. (Join-Path $MenuDir 'grid-picker.ps1')
. (Join-Path $MenuDir 'grid-status.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$grid = New-GridPlan -Rows @(
    @{ Title = 'Run' }
    @{ Prefix = 'Build'; Cells = @( @{ Label = 'Release' }, @{ Label = 'Debug' } ) }
    @{ Cells = @( @{ Label = 'Maintenance' }, @{ Label = 'Setup' } ); TrailingCell = @{ Label = 'Quit' } }
)
Assert-Equal 3 $grid.Body.Count 'status keeps every command row'
Assert-Equal 'title' $grid.Body[0].Kind 'status keeps section headings'
Assert-Equal ($script:MenuCategoryWidth + $script:MenuGridGap) $grid.PrefixWidth 'status keeps the shared category track'
Assert-Equal 2 $grid.ColumnCount 'status keeps the command columns'
Assert-Equal 4 $grid.TrailingWidth 'status keeps the trailing action geometry'

$singleCellGrid = New-GridPlan -Rows @(@{ Cells = @( @{ Label = '< Back' } ) })
Assert-Equal ($script:MenuCategoryWidth + $script:MenuGridGap) $singleCellGrid.PrefixWidth 'status and picker share the fixed category track'
Assert-Equal 2 $singleCellGrid.ColumnCount 'status and picker share the two-column minimum'

$statusLinesParameter = (Get-Command New-GridStatusView).Parameters['Lines']
$acceptsEmptyLines = @($statusLinesParameter.Attributes | Where-Object { $_ -is [System.Management.Automation.AllowEmptyCollectionAttribute] }).Count -eq 1
Assert-Equal $true $acceptsEmptyLines 'a running status accepts an empty transcript before the first log line'

function Get-MenuMetrics {
    return [pscustomobject]@{ TerminalWidth = 79; WindowHeight = 24 }
}
function Write-GridStatusRows {
    param($View, [int]$StartIndex, [object[]]$Lines, [int]$ResultOffset)
    $script:LastGridStatusStartIndex = $StartIndex
}
function Set-GridStatusCursorParking {
    param($View)
    $script:ParkedGridStatusCursor = $true
}

$view = [pscustomobject]@{
    TerminalWidth = 79
    WindowHeight = 24
    Body = @(@{ Kind = 'row' }, @{ Kind = 'result-title'; Text = 'Old' }, @{ Kind = 'result' })
    ResultTitleIndex = 1
    ResultRowCount = 1
    Header = 'Deckle'
    Rows = @()
    Footer = ''
    HeaderCommands = 'Ctrl+C quit'
    BannerStyle = 'Compact'
    RestoreCursorVisible = $true
}
$updated = Update-GridStatusView -View $view -Title 'Build running' -Lines @('latest') -Follow
Assert-Equal 'Build running' $updated.Body[1].Text 'refresh updates the summary in place'
Assert-Equal 1 $script:LastGridStatusStartIndex 'refresh redraws only dynamic rows'
Assert-Equal $true $script:ParkedGridStatusCursor 'refresh parks the hidden cursor inside the status viewport'

function New-GridStatusView {
    param([Nullable[bool]]$RestoreCursorVisible)
    return [pscustomobject]@{ Recreated = $true; RestoreCursorVisible = $RestoreCursorVisible }
}
$view.TerminalWidth = 78
$resized = Update-GridStatusView -View $view -Title 'Build running' -Lines @('latest') -Follow
Assert-Equal $true $resized.Recreated 'terminal resize recreates the viewport deliberately'
Assert-Equal $true $resized.RestoreCursorVisible 'terminal resize preserves the original cursor state'

function Set-GridStatusCursorVisibility {
    param([bool]$Visible)
    $script:ClosedCursorVisibility = $Visible
}
Close-GridStatusView -View $view
Assert-Equal $true $script:ClosedCursorVisibility 'closing the status view restores the previous cursor state'

Write-Host 'grid-status.tests.ps1: PASS' -ForegroundColor Green
