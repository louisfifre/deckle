$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'status-view.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$short = Get-MenuStatusLayout -LineCount 2 -BodyCapacity 16
Assert-Equal 15 $short.VisibleLineCount 'status viewport reserves every available log row'
Assert-Equal 0 $short.LineOffset 'short output starts at its first line'

$live = Get-MenuStatusLayout -LineCount 40 -BodyCapacity 16 -Follow
Assert-Equal 15 $live.VisibleLineCount 'live viewport keeps a fixed height'
Assert-Equal 25 $live.LineOffset 'live viewport follows its latest line'

Write-Host 'status-view.tests.ps1: PASS' -ForegroundColor Green
