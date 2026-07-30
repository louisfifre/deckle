$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'chrome.ps1')
. (Join-Path $PSScriptRoot 'grid-layout.ps1')
. (Join-Path $PSScriptRoot 'grid-picker.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$fixedPrefixWidth = $script:MenuCategoryWidth + $script:MenuGridGap
$sharedColumnCount = Get-GridColumnCount -OccupiedColumnCount 1
$widths = Get-GridColumnWidths -ContentWidth 74 -PrefixWidth $fixedPrefixWidth -ColumnCount $sharedColumnCount
Assert-Equal 17 $fixedPrefixWidth 'fixed category track preserves the main menu geometry'
Assert-Equal 2 $sharedColumnCount 'a single-cell picker preserves the shared two-column grid'
Assert-Equal 28 $widths[0] 'first column receives remainder after the action-row inset'
Assert-Equal 27 $widths[1] 'second column width'
Assert-Equal 55 ($widths[0] + $widths[1]) 'columns fill the action track after its internal inset'

$worktreeWidths = Get-GridColumnWidths -ContentWidth 40 -PrefixWidth $fixedPrefixWidth -ColumnCount $sharedColumnCount
Assert-Equal 11 $worktreeWidths[0] 'worktree back occupies only the first action column at minimum width'
Assert-Equal 10 $worktreeWidths[1] 'worktree picker reserves the second action column at minimum width'

Assert-Equal 0 (Get-GridResultOffset -Current 0 -PageSize 5 -LineCount 12 -Direction Previous) 'result stays at first page'
Assert-Equal 5 (Get-GridResultOffset -Current 0 -PageSize 5 -LineCount 12 -Direction Next) 'result advances one page'
Assert-Equal 10 (Get-GridResultOffset -Current 5 -PageSize 5 -LineCount 12 -Direction Next) 'result advances to a non-overlapping final page'
Assert-Equal 5 (Get-GridResultOffset -Current 9 -PageSize 5 -LineCount 12 -Direction Current) 'current result offset snaps to its page after resize'
Assert-Equal 5 (Get-GridResultOffset -Current 10 -PageSize 5 -LineCount 12 -Direction Previous) 'result returns to the previous page'
Assert-Equal 0 (Get-GridResultOffset -Current 10 -PageSize 5 -LineCount 12 -Direction First) 'home returns to the first result page'
Assert-Equal 10 (Get-GridResultOffset -Current 0 -PageSize 5 -LineCount 12 -Direction Last) 'end follows the latest result page'

$lastPage = Get-GridResultPage -Offset 10 -PageSize 5 -LineCount 12
Assert-Equal 3 $lastPage.Number 'page indicator reports the current result page'
Assert-Equal 3 $lastPage.Count 'page indicator reports the total result pages'

Assert-Equal 1 (Get-GridColumnForRow -PreferredColumn 0 -ColumnOffset 1 -CellCount 1) 'down to offset row keeps its visual column'
Assert-Equal 1 (Get-GridColumnForRow -PreferredColumn 1 -ColumnOffset 0 -CellCount 2) 'up from offset row returns to cell above'
Assert-Equal 2 (Get-GridColumnForRow -PreferredColumn 2 -ColumnOffset 0 -CellCount 2 -HasTrailing $true -TrailingColumn 2) 'trailing action remains reachable on its row'
Assert-Equal 1 (Get-GridColumnForRow -PreferredColumn 2 -ColumnOffset 0 -CellCount 2 -TrailingColumn 2) 'leaving trailing action returns to the nearest regular column'

$preferredColumn = 1
$backColumn = Get-GridColumnForRow -PreferredColumn $preferredColumn -ColumnOffset 0 -CellCount 1
$restoredColumn = Get-GridColumnForRow -PreferredColumn $preferredColumn -ColumnOffset 0 -CellCount 2
Assert-Equal 0 $backColumn 'a sparse Back row clamps only the active cell'
Assert-Equal 1 $restoredColumn 'leaving Back restores the preferred column'

$stateRows = (New-GridPlan -Rows @(
    @{ Cells = @( @{ Label = '< Back' } ) }
    @{ Cells = @( @{ Label = 'Left' }, @{ Label = 'Right' } ) }
)).SelectableRows
$navigationState = @{}
Set-GridSelectionState -State $navigationState -Index 1 -PreferredColumn 1
$restoredPosition = Get-GridSelectionPosition -SelectableRows $stateRows -TrailingColumn 2 -State $navigationState
Assert-Equal 1 $restoredPosition.Index 'selection state restores the selected action across redraws'
Assert-Equal 1 $restoredPosition.ActiveColumn 'selection state restores the selected column across redraws'

Assert-Equal '↑↓←→ move   Enter run   Ctrl+C quit' (Get-GridNavigationCommands -EscapeAction Ignore) 'main navigation wording is centralized'
Assert-Equal '↑↓←→ move   Enter select   Esc back' (Get-GridNavigationCommands -Interaction Select) 'selection navigation uses visible arrow keys'
Assert-Equal '←→ move   Enter confirm   Esc cancel' (Get-GridNavigationCommands -Interaction Confirm) 'confirmation navigation exposes Escape consistently'
Assert-Equal 'Wheel/PgUp/PgDn pages   Home/End first/latest' (Get-GridPagingFooter -HasPages) 'paged results keep their contextual controls in the footer'
Assert-Equal '' (Get-GridPagingFooter) 'menus without pages do not duplicate header commands in the footer'

$compactLayout = New-GridBodyLayout -CommandBody @(@{ Kind = 'row' }) -ResultTitle 'Results' -BannerStyle Compact
Assert-Equal 16 $compactLayout.Body.Count 'result layout consumes available compact body'
Assert-Equal 13 $compactLayout.ResultRowCount 'result layout reserves breathing room and title'
Assert-Equal 'blank' $compactLayout.Body[1].Kind 'result layout separates commands from results'
Assert-Equal 'result-title' $compactLayout.Body[2].Kind 'result heading can display page state'

Write-Host 'grid-picker.tests.ps1: PASS' -ForegroundColor Green
