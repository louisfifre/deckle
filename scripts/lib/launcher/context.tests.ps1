$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'context.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 'Compact' (Get-DeckleMenuBannerStyle) 'the launcher always uses the compact banner'

Write-Host 'context.tests.ps1: PASS' -ForegroundColor Green
