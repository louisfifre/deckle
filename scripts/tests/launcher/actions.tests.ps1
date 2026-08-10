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
    $script:LastActionHeader = $Header
    $script:LastActionLabel = $Label
    $script:LastActionSource = $Source
    try {
        & $Action
        return [pscustomobject]@{ Succeeded = $true; Title = $Label; Lines = @() }
    } catch {
        return [pscustomobject]@{ Succeeded = $false; Title = $Label; Lines = @($_.Exception.Message) }
    }
}

function Get-AgentStateStrings {
    return Import-PowerShellDataFile (Join-Path $script:LibDir 'agent-state-maintenance.strings.psd1')
}

function Read-YesNo {
    param($Question, $Default, $ConfirmLabel, $CancelLabel, $ContextLines, [switch]$Destructive)
    $script:LastAgentStateQuestion = [pscustomobject]@{
        Question = $Question
        Default = $Default
        ConfirmLabel = $ConfirmLabel
        CancelLabel = $CancelLabel
        ContextLines = @($ContextLines)
        Destructive = [bool]$Destructive
    }
    return $script:AgentStateConsent
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

$agentDispatchRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-dispatch-$([guid]::NewGuid())"
$null = New-Item -ItemType Directory -Path $agentDispatchRoot
$previousCommandDir = $script:CommandDir
$previousMarker = $env:DECKLE_AGENT_STATE_TEST_MARKER
try {
    $agentScript = Join-Path $agentDispatchRoot 'reset-agent-state.ps1'
    $agentMarker = Join-Path $agentDispatchRoot 'agent-state.txt'
    $env:DECKLE_AGENT_STATE_TEST_MARKER = $agentMarker
    Set-Content -LiteralPath $agentScript -Encoding utf8NoBOM -Value 'param([switch]$Apply, [string]$Confirmation) Set-Content -LiteralPath $env:DECKLE_AGENT_STATE_TEST_MARKER -Value "$([bool]$Apply)|$Confirmation" -Encoding utf8NoBOM'
    $script:CommandDir = $agentDispatchRoot
    $rows = @([pscustomobject]@{ Cells = @() })

    $script:AgentStateConsent = $false
    $cancelled = Invoke-ResetAgentState -MenuRows $rows
    Assert-Equal $null $cancelled 'cancelled AI session reset returns without dispatch'
    Assert-Equal $false (Test-Path -LiteralPath $agentMarker) 'cancelled AI session reset does not run the command'
    Assert-Equal $false $script:LastAgentStateQuestion.Default 'AI session reset keeps the safe choice as default'
    Assert-Equal $true $script:LastAgentStateQuestion.Destructive 'AI session reset uses the shared destructive confirmation'
    Assert-Equal $true ($script:LastAgentStateQuestion.ContextLines -match 'local storage' -as [bool]) 'AI session reset discloses the Claude Desktop local-storage reset before consent'
    Assert-Equal $true ($script:LastAgentStateQuestion.ContextLines -match 'sign-in' -as [bool]) 'AI session reset discloses possible Claude Desktop sign-in loss before consent'

    $script:AgentStateConsent = $true
    $resetResult = Invoke-ResetAgentState -MenuRows $rows
    $strings = Get-AgentStateStrings
    Assert-Equal $true $resetResult.Succeeded 'confirmed AI session reset returns its launcher result'
    Assert-Equal "True|$($strings.ConfirmationPhrase)" (Get-Content -Raw -LiteralPath $agentMarker).Trim() 'confirmed AI session reset passes apply and the exact guard phrase'
    Assert-Equal $strings.MenuResetHeader $script:LastActionHeader 'reset action uses its own breadcrumb'
    Assert-Equal 'Maintenance' $script:LastActionSource 'reset action uses the maintenance transcript source'

    $inspectResult = Invoke-InspectAgentState -MenuRows $rows
    Assert-Equal $true $inspectResult.Succeeded 'AI session inspection returns its launcher result'
    Assert-Equal 'False|' (Get-Content -Raw -LiteralPath $agentMarker).Trim() 'AI session inspection keeps preview mode'
    Assert-Equal $strings.MenuInspectHeader $script:LastActionHeader 'inspect action uses its own breadcrumb'
} finally {
    $script:CommandDir = $previousCommandDir
    $env:DECKLE_AGENT_STATE_TEST_MARKER = $previousMarker
    if (Test-Path -LiteralPath $agentDispatchRoot) { Remove-Item -LiteralPath $agentDispatchRoot -Recurse -Force }
}
Write-Host 'actions.tests.ps1: PASS' -ForegroundColor Green
