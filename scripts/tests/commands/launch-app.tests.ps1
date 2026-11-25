$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Script = Join-Path $ScriptsDir 'launch-app.ps1'

function Assert-True([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "${Case}: condition was false" }
}

# Regression: launch-app.ps1 -App folders with no Release build ended on
# "ScriptHalted": the trap re-raised with a bare throw, which hides the real
# message. The executable is also resolved before the running app is stopped,
# so this run, on an empty target, stops no process.
$target = Join-Path ([IO.Path]::GetTempPath()) "deckle-launch-app-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path $target
    $text = @(& pwsh -NoProfile -File $Script -App folders -Target $target 2>&1 6>&1) -join "`n"
    Assert-True ($LASTEXITCODE -ne 0) 'a launch with no build fails'
    Assert-True ($text -match 'No Release build of DeckleFolders\.exe') 'the failure names the missing build'
    Assert-True ($text -notmatch 'ScriptHalted') 'the trap re-raises the real error, not ScriptHalted'
    Assert-True ($text -notmatch 'Stop running') 'nothing is stopped when there is nothing to launch'

    Write-Host 'launch-app.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
