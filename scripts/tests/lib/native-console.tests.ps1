$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
. (Join-Path $LibDir 'native-console.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$pwsh = Join-Path $PSHOME 'pwsh.exe'
Assert-Equal 0 (Invoke-DeckleConsoleProcess -FilePath $pwsh -ArgumentList @('-NoProfile', '-Command', 'exit 0')) 'native console process reports success'
Assert-Equal 7 (Invoke-DeckleConsoleProcess -FilePath $pwsh -ArgumentList @('-NoProfile', '-Command', 'exit 7')) 'native console process preserves a failing exit code'

$capturedRecords = @(& {
    $exitCode = Invoke-DeckleConsoleProcess -FilePath $pwsh -ArgumentList @(
        '-NoProfile',
        '-Command',
        "[Console]::Out.WriteLine('native stdout'); [Console]::Error.WriteLine('native stderr'); exit 3"
    )
    Write-Output "exit:$exitCode"
} 6>&1)
$captured = @($capturedRecords | ForEach-Object { [string]$_ })
Assert-Equal $true ($captured -contains 'native stdout') 'native stdout is retained as launcher information'
Assert-Equal $true ($captured -contains 'native stderr') 'native stderr is retained as launcher information'
Assert-Equal $true ($captured -contains 'exit:3') 'capturing native lines does not contaminate the exit code'
$nativeRecords = @($capturedRecords | Where-Object { $_ -is [System.Management.Automation.InformationRecord] })
Assert-Equal 'StdOut' $nativeRecords[0].MessageData.DeckleStream 'native stdout keeps its diagnostic stream identity'
Assert-Equal 'StdErr' $nativeRecords[1].MessageData.DeckleStream 'native stderr keeps its diagnostic stream identity'

Write-Host 'native-console.tests.ps1: PASS' -ForegroundColor Green
