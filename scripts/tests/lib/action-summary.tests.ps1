$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
. (Join-Path $LibDir 'action-summary.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$captured = @(& {
    Write-DeckleActionSummary `
        -Workflow 'Show context stats' `
        -Result Success `
        -Sentence 'Inventory complete.' `
        -Details ([ordered]@{
            Documents = 175
            'Added in last 30 days' = 37
        })
} 6>&1)
$output = @($captured | ForEach-Object { $_.ToString() })

$rows = @($output | Where-Object { $_ -match '^  \S' })
$colonColumns = @($rows | ForEach-Object { $_.IndexOf(':') } | Select-Object -Unique)
Assert-Equal 1 $colonColumns.Count 'summary label alignment'
Assert-Equal 23 $colonColumns[0] 'longest label determines separator column'

$summaryCategory = @($captured | Where-Object { [string]$_.MessageData.Message -eq '[summary] ' })[0]
$summaryBody = @($captured | Where-Object { [string]$_.MessageData.Message -eq 'Inventory complete.' })[0]
Assert-Equal (Get-DeckleOutputColor -Role Category) $summaryCategory.MessageData.ForegroundColor 'summary category uses the shared category role'
Assert-Equal ([Console]::ForegroundColor) $summaryBody.MessageData.ForegroundColor 'summary sentence remains normal body text'
$resultValue = @($captured | Where-Object { [string]$_.MessageData.Message -eq 'Success' })[0]
Assert-Equal (Get-DeckleOutputColor -Role Success) $resultValue.MessageData.ForegroundColor 'only the summary result receives the success color'

Write-Host 'action-summary.tests.ps1: PASS' -ForegroundColor Green
