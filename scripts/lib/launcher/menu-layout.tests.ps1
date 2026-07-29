$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'menu-layout.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$rows = @(ConvertTo-MenuRows -Sections @(
    @{ Prefix = 'Inspect'; Items = @(
        @{ Label = 'One'; Value = 1 }
        @{ Label = 'Two'; Value = 2 }
        @{ Label = 'Three'; Value = 3 }
    ) }
))

Assert-Equal 2 $rows.Count 'three items wrap to two rows'
Assert-Equal 2 $rows[0].Cells.Count 'first row column count'
Assert-Equal 1 $rows[1].Cells.Count 'second row column count'
Assert-Equal 'Inspect' $rows[0].Prefix 'first row prefix'
Assert-Equal '' $rows[1].Prefix 'wrapped row prefix'
Assert-Equal 3 $rows[1].Cells[0].Value 'item order'

$singleRow = @(ConvertTo-MenuRows -Sections @(
    @{ Prefix = 'Repo'; Items = @( @{ Label = 'Install git hooks'; Value = 'hooks' } ) }
))
Assert-Equal 1 $singleRow.Count 'single item creates one row'
Assert-Equal 1 $singleRow[0].Cells.Count 'single item creates one cell'
Assert-Equal 'hooks' $singleRow[0].Cells[0].Value 'single item is preserved'

$mainRows = @(Get-DeckleMainMenuRows)
$titles = @($mainRows | Where-Object { $_.ContainsKey('Title') } | ForEach-Object { $_.Title })
Assert-Equal 'Run Project Release More' ($titles -join ' ') 'main section order'

$projectTitleIndex = [Array]::FindIndex($mainRows, [Predicate[object]]{ param($row) $row.ContainsKey('Title') -and $row.Title -eq 'Project' })
Assert-Equal 'Update README pulse' $mainRows[$projectTitleIndex + 1].Cells[0].Label 'project first action'
Assert-Equal 'Update changelog' $mainRows[$projectTitleIndex + 1].Cells[1].Label 'project paired update'
Assert-Equal 'Record version' $mainRows[$projectTitleIndex + 2].Cells[0].Label 'record version wraps last'

$releaseTitleIndex = [Array]::FindIndex($mainRows, [Predicate[object]]{ param($row) $row.ContainsKey('Title') -and $row.Title -eq 'Release' })
Assert-Equal 2 $mainRows[$releaseTitleIndex + 1].Cells.Count 'release first row columns'
Assert-Equal 'Prepare native runtime' $mainRows[$releaseTitleIndex + 2].Cells[0].Label 'release third action wraps'

$moreTitleIndex = [Array]::FindIndex($mainRows, [Predicate[object]]{ param($row) $row.ContainsKey('Title') -and $row.Title -eq 'More' })
Assert-Equal 'Maintenance…' $mainRows[$moreTitleIndex + 1].Cells[0].Label 'maintenance submenu'
Assert-Equal 'Setup…' $mainRows[$moreTitleIndex + 1].Cells[1].Label 'setup submenu'
Assert-Equal 1 $mainRows[$moreTitleIndex + 2].ColumnOffset 'quit uses trailing column'
Assert-Equal 'Quit' $mainRows[$moreTitleIndex + 2].Cells[0].Label 'quit wraps last'

Write-Host 'menu-layout.tests.ps1: PASS' -ForegroundColor Green
