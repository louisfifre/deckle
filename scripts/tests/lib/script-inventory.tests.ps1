$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$RepoRoot = Split-Path -Parent $ScriptsDir

# The pre-commit hook regenerates scripts/INVENTORY.md, but a checkout whose
# hook predates the generator, or a commit made with --no-verify, leaves it
# stale. Agents choose scripts from it, so a stale list fails here, before a
# merge, rather than misleading the next agent.
$output = @(& pwsh -NoProfile -File (Join-Path $ScriptsDir 'setup\update-inventory.ps1') -Target $RepoRoot 2>&1 6>&1) -join "`n"
if ($LASTEXITCODE -ne 0) { throw "update-inventory.ps1 failed:`n$output" }
if ($output -notmatch 'Changed\s*:\s*no') {
    throw "scripts/INVENTORY.md is stale. Run: pwsh -NoProfile -File scripts/setup/update-inventory.ps1 -Apply, then commit it."
}

Write-Host 'script-inventory.tests.ps1: PASS' -ForegroundColor Green
