$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $ScriptsDir 'lib\terminal-interaction.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

function Assert-Throws([scriptblock]$Action, [string]$Pattern, [string]$Case) {
    try {
        & $Action
        throw "${Case}: expected an exception"
    } catch {
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "${Case}: unexpected exception '$($_.Exception.Message)'"
        }
    }
}

Assert-Throws `
    -Action { New-TerminalTarget -TargetId disabled -Label Disabled -IntentKind Action -Enabled $false } `
    -Pattern 'requires a reason' `
    -Case 'disabled targets explain why activation is unavailable'

$access = New-TerminalTarget -TargetId access.sample -Label Sample -IntentKind Access
Assert-Equal Access $access.PresentationRole 'an Access receives its semantic presentation role without renderer hints'
Assert-Throws `
    -Action { New-TerminalActionRow -Label Invalid -Variants @($access) } `
    -Pattern 'only Action targets' `
    -Case 'Action Row variants remain semantic Actions'

$firstAction = New-TerminalTarget -TargetId action.first -Label First -IntentKind Action
$secondAction = New-TerminalTarget -TargetId action.second -Label Second -IntentKind Action
$independentSection = New-TerminalSection -Label Independent -Items @($firstAction, $secondAction)
Assert-Equal 2 $independentSection.Items.Count 'a Section accepts independent Actions without inventing an Action Row'
Assert-Equal Action $independentSection.Items[0].PresentationRole 'an Action carries Action presentation independently from its layout'

$danger = New-TerminalTarget -TargetId action.danger -Label Reset -IntentKind Action -PresentationRole Danger
Assert-Equal Action $danger.IntentKind 'danger presentation does not replace Action intent'
Assert-Equal Danger $danger.PresentationRole 'danger is an independent presentation responsibility'

$duplicate = New-TerminalTarget -TargetId action.same -Label Same -IntentKind Action
$duplicateSection = New-TerminalSection -Label Duplicate -Items @(
    New-TerminalActionRow -Label One -Variants @($duplicate)
    New-TerminalActionRow -Label Two -Variants @($duplicate)
)
Assert-Throws `
    -Action { New-TerminalActionMenuView -ViewId duplicate -Banner Duplicate -Sections @($duplicateSection) } `
    -Pattern 'more than once' `
    -Case 'target identity is unique inside one View'

$back = New-TerminalTarget `
    -TargetId navigation.back `
    -Label Back `
    -IntentKind Navigation `
    -Payload ([pscustomobject]@{ Command = 'Back' }) `
    -PresentationRole Navigation
$running = New-TerminalExecutionView `
    -ViewId execution.running `
    -Banner Running `
    -State Running `
    -JournalLines @('waiting') `
    -TrackingSteps @([pscustomobject]@{ Label = 'Wait'; State = 'Running' }) `
    -BackTarget $back `
    -OwnerActionMenuId menu.root
Assert-Equal $false $running.BackTarget.Enabled 'Back is unavailable while Execution is Running'
Assert-Equal 'Wait for the execution to finish.' $running.BackTarget.DisabledReason 'Running Back explains the wait'

Write-Host 'contracts.tests.ps1: PASS' -ForegroundColor Green
