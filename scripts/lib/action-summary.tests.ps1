$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'action-summary.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$output = @(& {
    Write-DeckleActionSummary `
        -Workflow 'Show context stats' `
        -Result Success `
        -Sentence 'Inventory complete.' `
        -Details ([ordered]@{
            Documents = 175
            'Added in last 30 days' = 37
        })
} 6>&1 | ForEach-Object { $_.ToString() })

$rows = @($output | Where-Object { $_ -match '^  \S' })
$colonColumns = @($rows | ForEach-Object { $_.IndexOf(':') } | Select-Object -Unique)
Assert-Equal 1 $colonColumns.Count 'summary label alignment'
Assert-Equal 23 $colonColumns[0] 'longest label determines separator column'

Write-Host 'action-summary.tests.ps1: PASS' -ForegroundColor Green
