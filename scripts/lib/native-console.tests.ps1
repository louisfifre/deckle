$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'native-console.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$pwsh = Join-Path $PSHOME 'pwsh.exe'
Assert-Equal 0 (Invoke-DeckleConsoleProcess -FilePath $pwsh -ArgumentList @('-NoProfile', '-Command', 'exit 0')) 'native console process reports success'
Assert-Equal 7 (Invoke-DeckleConsoleProcess -FilePath $pwsh -ArgumentList @('-NoProfile', '-Command', 'exit 7')) 'native console process preserves a failing exit code'

Write-Host 'native-console.tests.ps1: PASS' -ForegroundColor Green
