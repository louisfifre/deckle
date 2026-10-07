$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandPath = Join-Path $ScriptsDir 'commands\check-script-headers.ps1'
$FixtureDir = Join-Path $ScriptsDir 'tests\fixtures\script-discovery'

function Assert-True([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "${Case}: condition was false" }
}

$root = Join-Path ([IO.Path]::GetTempPath()) "check-script-headers-$([guid]::NewGuid())"
try {
    $commandsCopy = Join-Path $root 'scripts\commands'
    $null = New-Item -ItemType Directory -Path $commandsCopy
    Copy-Item -Path (Join-Path $FixtureDir '*') -Destination $commandsCopy -Recurse

    $faultyOutput = @(& pwsh -NoProfile -File $CommandPath -Target $root 2>&1 6>&1) -join "`n"
    Assert-True ($LASTEXITCODE -eq 1) 'voided and invalid headers fail the check'
    foreach ($name in @('glued-requires.ps1', 'dot-word.ps1', 'two-axes.ps1')) {
        Assert-True ($faultyOutput -match [regex]::Escape("scripts/commands/$name")) "the faulty list names $name"
    }
    Assert-True ($faultyOutput -match 'commands/no-header\.ps1') 'the hidden-by-choice list names the script without a Header'
    Assert-True ($faultyOutput -notmatch 'scripts/commands/no-header\.ps1 \[') 'a script without a Header is not a fault'
    Assert-True ($faultyOutput -match 'Faulty\s*:\s*3') 'the summary counts the faulty headers'

    foreach ($name in @('glued-requires.ps1', 'dot-word.ps1', 'two-axes.ps1')) {
        Remove-Item -LiteralPath (Join-Path $commandsCopy $name) -Force
    }
    $cleanOutput = @(& pwsh -NoProfile -File $CommandPath -Target $root 2>&1 6>&1) -join "`n"
    Assert-True ($LASTEXITCODE -eq 0) 'scripts hidden by choice do not fail the check'
    Assert-True ($cleanOutput -match 'commands/no-header\.ps1') 'hidden scripts are still listed when the check passes'
    Assert-True ($cleanOutput -match 'Hidden\s*:\s*1') 'the summary counts the hidden scripts'

    Write-Host 'check-script-headers.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
