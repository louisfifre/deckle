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

Write-Host 'menu-layout.tests.ps1: PASS' -ForegroundColor Green
