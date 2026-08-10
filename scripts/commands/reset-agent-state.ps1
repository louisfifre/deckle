[CmdletBinding()]
param(
    [ValidateSet('Codex', 'Claude')]
    [string[]]$Scope = @('Codex', 'Claude'),
    [switch]$Apply,
    [string]$Confirmation
)

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'agent-state-maintenance.psm1') -Force
$strings = Get-AgentStateStrings
$workflow = $strings.Workflow
$output = New-DeckleWorkflowOutput -Category 'ai-sessions'

try {
    Write-DeckleWorkflowStep -Output $output -Message 'Build the local session cleanup plan'
    Write-DeckleWorkflowMessage -Output $output -Message "Scope: $($Scope -join ', ')" -Role Muted
    $plan = Get-AgentStateCleanupPlan -Scope $Scope
    Write-DeckleWorkflowMessage -Output $output -Message "$($plan.Files) files / $(Format-AgentStateByteCount -Bytes $plan.Bytes)" -Role Muted

    $operation = if ($Apply) { 'Validate and reset local session state' } else { 'Validate the cleanup plan without changing local state' }
    Write-DeckleWorkflowStep -Output $output -Message $operation
    $result = Invoke-AgentStateCleanupPlan -Plan $plan -Apply:$Apply -Confirmation $Confirmation
    $operationResult = if ($Apply) { 'Local session state reset completed' } else { 'Cleanup plan validated without changes' }
    Write-DeckleWorkflowResult -Output $output -Message $operationResult
    $sentence = if ($Apply) { $strings.ResetSentence } else { $strings.AuditSentence }
    $details = [ordered]@{
        Mode = $(if ($Apply) { 'Apply' } else { 'Preview' })
        Scope = ($plan.Scope -join ', ')
        'Plan ID' = $plan.PlanId
        'Session files' = $plan.Files
        'Session data' = Format-AgentStateByteCount -Bytes $plan.Bytes
        'Mixed state files' = $result.ChangedStateFiles
        'Mixed databases' = $plan.Databases.Count
        'Removed targets' = $result.RemovedTargets
    }
    $summary = @{
        Workflow = $workflow
        Result = 'Success'
        Sentence = $sentence
        Details = $details
        Next = $plan.Warnings
    }
    Write-DeckleActionSummary @summary
} catch {
    $next = if ($_.Exception.Message -match '^Close the active AI tools') { @($strings.CloseApps) } else { @() }
    $summary = @{
        Workflow = $workflow
        Result = 'Failed'
        Sentence = $strings.FailureSentence
        Details = [ordered]@{ Error = $_.Exception.Message }
        Next = $next
    }
    Write-DeckleActionSummary @summary
    throw
}
