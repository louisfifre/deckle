$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'context.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$script:DeckleMenuIsCompact = $false
Assert-Equal 'Full' (Get-DeckleMenuBannerStyle) 'session starts with the full banner'
Use-DeckleCompactMenu
Assert-Equal 'Compact' (Get-DeckleMenuBannerStyle) 'first action compacts the whole session'

Write-Host 'context.tests.ps1: PASS' -ForegroundColor Green
