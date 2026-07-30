$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'actions.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$script:PreflightSequence = [System.Collections.Generic.List[string]]::new()
$script:LibDir = Split-Path -Parent $PSScriptRoot

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

Write-Host 'actions.tests.ps1: PASS' -ForegroundColor Green
