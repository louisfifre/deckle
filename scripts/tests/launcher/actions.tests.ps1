$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'
$LauncherDir = Join-Path $LibDir 'launcher'
$MenuDir = Join-Path $LibDir 'menu'
. (Join-Path $MenuDir 'session.ps1')
. (Join-Path $LauncherDir 'actions.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$script:PreflightSequence = [System.Collections.Generic.List[string]]::new()
$script:LibDir = $LibDir
$script:CommandDir = $CommandDir

function git {
    param([Parameter(ValueFromRemainingArguments)]$Arguments)
    $script:PreflightSequence.Add('fetch')
    $global:LASTEXITCODE = 0
}

function Import-Module {
    param($Name, [switch]$Force)
}

function Assert-DeckleReleaseRepositorySource {
    param([string]$RepoRoot, [switch]$AllowAhead)
    $script:PreflightSequence.Add('validate')
    Assert-Equal $true ([bool]$AllowAhead) 'release preflight permits unpublished commits ahead of origin/main'
    return [pscustomobject]@{ HeadSha = '1234567890abcdef'; OwnerRepo = 'owner/deckle' }
}

function Invoke-DeckleMenuAction {
    param($Header, $Label, $Source, $MenuRows, [scriptblock]$Action)
    try {
        & $Action
        return [pscustomobject]@{ Succeeded = $true; Title = $Label; Lines = @() }
    } catch {
        return [pscustomobject]@{ Succeeded = $false; Title = $Label; Lines = @($_.Exception.Message) }
    }
}

$preflight = Invoke-ReleaseSourcePreflight -Worktree 'D:\repo' -MenuRows @(@{ Cells = @(@{ Label = 'Publish' }) })
Assert-Equal $true $preflight.Succeeded 'valid release source passes preflight'
Assert-Equal 'fetch validate' ($script:PreflightSequence -join ' ') 'release source is fetched before validation'

function Get-WorktreeOrReturn { return 'D:\feature-worktree' }
function Get-CsprojVersion { return '0.28.7' }
function Invoke-ReleaseSourcePreflight {
    return [pscustomobject]@{ Succeeded = $false; Title = 'Invalid source'; Lines = @('Use main.') }
}

$blocked = Invoke-PublishRelease -MenuRows @(@{ Cells = @(@{ Label = 'Publish' }) })
Assert-Equal $false $blocked.Succeeded 'invalid release source stops publication'
Assert-Equal 'Invalid source' $blocked.Title 'preflight failure returns to the release menu'

function Select-Action { throw 'Cancelled' }
Assert-Equal $null (Select-VersionBump -Current '0.28.7') 'ordinary version cancellation returns to the caller'

function Select-Action { throw [System.OperationCanceledException]::new('global quit') }
$quitReachedRoot = $false
try {
    Select-VersionBump -Current '0.28.7' | Out-Null
} catch {
    $quitReachedRoot = $_.Exception.GetType().Name -eq 'OperationCanceledException'
}
Assert-Equal $true $quitReachedRoot 'version selection propagates non-local cancellation such as Ctrl+C'

$dispatchRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-command-dispatch-$([guid]::NewGuid())"
$null = New-Item -ItemType Directory -Path $dispatchRoot
$previousCommandDir = $script:CommandDir
try {
    $probePath = Join-Path $dispatchRoot 'probe.ps1'
    $markerPath = Join-Path $dispatchRoot 'target.txt'
    Set-Content -LiteralPath $probePath -Encoding utf8NoBOM -Value 'param([string]$Target, [string]$MarkerPath) Set-Content -LiteralPath $MarkerPath -Value $Target -Encoding utf8NoBOM'
    $script:CommandDir = $dispatchRoot

    $dispatch = Invoke-WorktreeScript `
        -Script 'probe.ps1' `
        -Label 'Probe command' `
        -Source Test `
        -MenuRows @([pscustomobject]@{ Cells = @() }) `
        -ScriptParameters @{ MarkerPath = $markerPath }

    Assert-Equal $true $dispatch.Succeeded 'launcher dispatches a standalone command'
    Assert-Equal 'D:\feature-worktree' (Get-Content -Raw $markerPath).Trim() 'launcher passes the selected worktree to the command'
} finally {
    $script:CommandDir = $previousCommandDir
    if (Test-Path -LiteralPath $dispatchRoot) { Remove-Item -LiteralPath $dispatchRoot -Recurse -Force }
}
Write-Host 'actions.tests.ps1: PASS' -ForegroundColor Green
