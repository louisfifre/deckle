$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'chrome.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 2 @(Get-MenuBanner -Style Compact).Count 'compact banner line count'
Assert-Equal 6 @(Get-MenuBanner -Style Full).Count 'full banner line count'
Assert-Equal 12 (New-MenuRule -MaxWidth 12).Length 'rule uses requested width'
Assert-Equal 11 (New-MenuRule -MaxWidth 11 -Style Section).Length 'section rule uses requested width'
Assert-Equal 16 (Get-MenuBodyCapacity -BannerStyle Compact -WindowHeight 24) 'compact body capacity'
Assert-Equal 12 (Get-MenuBodyCapacity -BannerStyle Full -WindowHeight 24) 'full body capacity'
Assert-Equal 0 (Get-MenuBodyCapacity -BannerStyle Compact -WindowHeight 6) 'undersized terminal has no body capacity'

$fits = [pscustomobject]@{ ContentWidth = 74; WindowHeight = 24 }
$tooNarrow = [pscustomobject]@{ ContentWidth = 39; WindowHeight = 24 }
$tooShort = [pscustomobject]@{ ContentWidth = 74; WindowHeight = 19 }
Assert-Equal $true (Test-MenuViewportFits -BodyCount 13 -BannerStyle Compact -Metrics $fits) 'main menu fits supported terminal'
Assert-Equal $false (Test-MenuViewportFits -BodyCount 13 -BannerStyle Compact -Metrics $tooNarrow) 'minimum width is enforced'
Assert-Equal $false (Test-MenuViewportFits -BodyCount 13 -BannerStyle Compact -Metrics $tooShort) 'minimum height is enforced'

Write-Host 'chrome.tests.ps1: PASS' -ForegroundColor Green
