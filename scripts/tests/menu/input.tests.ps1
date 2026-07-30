$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$MenuDir = Join-Path $ScriptsDir 'lib\menu'
. (Join-Path $MenuDir 'input.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 'Previous' (Get-MenuWheelPageDirection -Delta 120) 'wheel up shows the previous page'
Assert-Equal 'Next' (Get-MenuWheelPageDirection -Delta -120) 'wheel down shows the next page'
Assert-Equal 'Current' (Get-MenuWheelPageDirection -Delta 0) 'stationary wheel keeps the current page'

Write-Host 'input.tests.ps1: PASS' -ForegroundColor Green
