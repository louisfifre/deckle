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

Write-Host 'grid-status.tests.ps1: PASS' -ForegroundColor Green
