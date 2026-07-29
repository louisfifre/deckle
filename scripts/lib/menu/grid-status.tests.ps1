$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'chrome.ps1')
. (Join-Path $PSScriptRoot 'grid-picker.ps1')
. (Join-Path $PSScriptRoot 'grid-status.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$grid = New-GridStatusBody -Rows @(
    @{ Title = 'Run' }
    @{ Prefix = 'Build'; Cells = @( @{ Label = 'Release' }, @{ Label = 'Debug' } ) }
    @{ Cells = @( @{ Label = 'Maintenance' } ); TrailingCell = @{ Label = 'Quit' } }
)
Assert-Equal 3 $grid.Body.Count 'status keeps every command row'
Assert-Equal 'title' $grid.Body[0].Kind 'status keeps section headings'
Assert-Equal ($script:MenuCategoryWidth + $script:MenuGridGap) $grid.PrefixWidth 'status keeps the shared category track'
Assert-Equal 2 $grid.ColumnCount 'status keeps the command columns'
Assert-Equal 4 $grid.TrailingWidth 'status keeps the trailing action geometry'

function Get-MenuMetrics {
    return [pscustomobject]@{ TerminalWidth = 79; WindowHeight = 24 }
}
function Write-GridStatusRows {
    param($View, [int]$StartIndex, [string[]]$Lines, [int]$ResultOffset)
    $script:LastGridStatusStartIndex = $StartIndex
}

$view = [pscustomobject]@{
    TerminalWidth = 79
    WindowHeight = 24
    Body = @(@{ Kind = 'row' }, @{ Kind = 'result-title'; Text = 'Old' }, @{ Kind = 'result' })
    ResultTitleIndex = 1
    ResultRowCount = 1
}
$updated = Update-GridStatusView -View $view -Title 'Build running' -Lines @('latest') -Follow
Assert-Equal 'Build running' $updated.Body[1].Text 'refresh updates the summary in place'
Assert-Equal 1 $script:LastGridStatusStartIndex 'refresh redraws only dynamic rows'

function New-GridStatusView {
    return [pscustomobject]@{ Recreated = $true }
}
$view.TerminalWidth = 78
$resized = Update-GridStatusView -View $view -Title 'Build running' -Lines @('latest') -Follow
Assert-Equal $true $resized.Recreated 'terminal resize recreates the viewport deliberately'

Write-Host 'grid-status.tests.ps1: PASS' -ForegroundColor Green
