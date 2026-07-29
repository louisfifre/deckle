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

$separatedRows = @(ConvertTo-MenuRows -SeparateSections -Sections @(
    @{ Prefix = 'One'; Items = @( @{ Label = 'First'; Value = 1 } ) }
    @{ Prefix = 'Two'; Items = @( @{ Label = 'Second'; Value = 2 } ) }
))
Assert-Equal 3 $separatedRows.Count 'separated sections add one breathing row'
Assert-Equal $true $separatedRows[1].Blank 'section separator is blank'

$mainRows = @(Get-DeckleMainMenuRows)
$titles = @($mainRows | Where-Object { $_.ContainsKey('Title') } | ForEach-Object { $_.Title })
Assert-Equal 'Run Workspace' ($titles -join ' ') 'main section order'

$workspaceTitleIndex = [Array]::FindIndex($mainRows, [Predicate[object]]{ param($row) $row.ContainsKey('Title') -and $row.Title -eq 'Workspace' })
Assert-Equal 'Project…' $mainRows[$workspaceTitleIndex + 1].Cells[0].Label 'project submenu'
Assert-Equal 'Release…' $mainRows[$workspaceTitleIndex + 1].Cells[1].Label 'release submenu'
Assert-Equal 'Maintenance…' $mainRows[$workspaceTitleIndex + 2].Cells[0].Label 'maintenance submenu'
Assert-Equal 'Setup…' $mainRows[$workspaceTitleIndex + 2].Cells[1].Label 'setup submenu'

$quitRow = $mainRows[-1]
Assert-Equal 1 $quitRow.ColumnOffset 'quit uses trailing column'
Assert-Equal 'Quit' $quitRow.Cells[0].Label 'quit stays last'
Assert-Equal 'quit' $quitRow.Cells[0].Role 'quit has a distinct visual role'
Assert-Equal 'Right' $quitRow.Cells[0].Align 'quit is aligned to the outer edge'

Write-Host 'menu-layout.tests.ps1: PASS' -ForegroundColor Green
