$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'chrome.ps1')
. (Join-Path $PSScriptRoot 'grid-picker.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$fixedPrefixWidth = $script:MenuCategoryWidth + $script:MenuGridGap
$widths = Get-GridColumnWidths -ContentWidth 74 -PrefixWidth $fixedPrefixWidth -ColumnCount 2
Assert-Equal 17 $fixedPrefixWidth 'fixed category track preserves the main menu geometry'
Assert-Equal 28 $widths[0] 'first column receives remainder'
Assert-Equal 27 $widths[1] 'second column width'
Assert-Equal 55 ($widths[0] + $widths[1]) 'columns fill available content width'

Assert-Equal 0 (Get-GridResultOffset -Current 0 -PageSize 5 -LineCount 12 -Direction Previous) 'result stays at first page'
Assert-Equal 5 (Get-GridResultOffset -Current 0 -PageSize 5 -LineCount 12 -Direction Next) 'result advances one page'
Assert-Equal 7 (Get-GridResultOffset -Current 5 -PageSize 5 -LineCount 12 -Direction Next) 'result clamps to final page'
Assert-Equal 7 (Get-GridResultOffset -Current 9 -PageSize 5 -LineCount 12 -Direction Current) 'current result offset clamps after resize'

Assert-Equal 1 (Get-GridColumnForRow -CurrentColumn 0 -ColumnOffset 1 -CellCount 1) 'down to offset row keeps its visual column'
Assert-Equal 1 (Get-GridColumnForRow -CurrentColumn 1 -ColumnOffset 0 -CellCount 2) 'up from offset row returns to cell above'
Assert-Equal 2 (Get-GridColumnForRow -CurrentColumn 2 -ColumnOffset 0 -CellCount 2 -HasTrailing $true -TrailingColumn 2) 'trailing action remains reachable on its row'
Assert-Equal 1 (Get-GridColumnForRow -CurrentColumn 2 -ColumnOffset 0 -CellCount 2 -TrailingColumn 2) 'leaving trailing action returns to the nearest regular column'

$compactLayout = New-GridBodyLayout -CommandBody @(@{ Kind = 'row' }) -ResultTitle 'Results' -BannerStyle Compact
Assert-Equal 16 $compactLayout.Body.Count 'result layout consumes available compact body'
Assert-Equal 13 $compactLayout.ResultRowCount 'result layout reserves breathing room, title, and commands'
Assert-Equal 'blank' $compactLayout.Body[1].Kind 'result layout separates commands from results'

Write-Host 'grid-picker.tests.ps1: PASS' -ForegroundColor Green
