$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'release-history.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { throw "$Message`nExpected: $Expected`nActual:   $Actual" }
}

function Assert-Throws([scriptblock]$Action, [string]$Message) {
    try { & $Action } catch { return }
    throw $Message
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-release-history-$([guid]::NewGuid())"
$null = New-Item -ItemType Directory -Path $root
try {
    Set-Content -LiteralPath (Join-Path $root 'release-history.json') -Encoding utf8NoBOM -Value @'
{
  "PublishedTags": ["v0.4.0", "v0.8.0"]
}
'@

    $tags = @(Get-PublishedReleaseTags -RepoRoot $root)
    Assert-Equal 2 $tags.Count 'The ledger should preserve every public release.'
    Assert-Equal 'v0.8.0' $tags[-1] 'The ledger order should be preserved.'
    Assert-Equal $true (Add-PublishedReleaseTag -RepoRoot $root -Tag 'v0.13.0') 'A newer release should be appended.'
    Assert-Equal $false (Add-PublishedReleaseTag -RepoRoot $root -Tag 'v0.13.0') 'Appending the same release should be idempotent.'

    Assert-Throws { Add-PublishedReleaseTag -RepoRoot $root -Tag 'v0.7.0' } 'An older release must be rejected.'
    Assert-Throws { Add-PublishedReleaseTag -RepoRoot $root -Tag 'release-1' } 'A malformed release tag must be rejected.'

    Write-Host 'release-history.tests.ps1 passed' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
