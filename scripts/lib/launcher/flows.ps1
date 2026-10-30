# Intent handling of the launcher: every Intent Request becomes a Transition
# Decision, or an Execute decision that the interaction core runs through the
# runner it carries. Repository work happens only inside that runner.

function New-LauncherContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.ScriptDiscovery.Level')][object]$Catalog,
        [Parameter(Mandatory)][string]$Banner,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$ScriptsRoot
    )

    return [pscustomobject]@{
        PSTypeName = 'DeckleScripts.Launcher.Context'
        Catalog = $Catalog
        Banner = $Banner
        RepositoryRoot = $RepositoryRoot
        ScriptsRoot = $ScriptsRoot
        Logs = New-LauncherLogsRegister
        NextSequence = 1
    }
}

function Get-LauncherRootView {
    param([Parameter(Mandatory)][PSTypeName('DeckleScripts.Launcher.Context')][object]$Context)

    return ConvertTo-LauncherDiscoveredView -Level $Context.Catalog -Banner $Context.Banner -IsRoot
}

function New-LauncherExecuteDecision {
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.Launcher.Context')][object]$Context,
        [Parameter(Mandatory)][object]$Action,
        [Parameter(Mandatory)][string]$OwnerActionMenuId,
        [Parameter(Mandatory)][ValidateSet('Open', 'Replace')][string]$Mode
    )

    $record = New-LauncherExecutionRecord -Action $Action -OwnerActionMenuId $OwnerActionMenuId -Sequence $Context.NextSequence
    $Context.NextSequence++
    Add-LauncherLogsRecord -Register $Context.Logs -Record $record
    $banner = $Context.Banner
    $scriptsRoot = $Context.ScriptsRoot
    $repositoryRoot = $Context.RepositoryRoot
    return [pscustomobject]@{
        Kind = 'Execute'
        Mode = $Mode
        View = ConvertTo-LauncherExecutionView -Record $record -Banner $banner
        # The core calls the runner with a paint callback that takes a View;
        # the runner answers with the final View.
        Runner = {
            param([scriptblock]$Paint)
            # GetNewClosure copies the local scope only, so the inner
            # closure gets its own copy of what the outer one captured.
            $paintBanner = $banner
            $publish = { param($current) & $Paint (ConvertTo-LauncherExecutionView -Record $current -Banner $paintBanner) }.GetNewClosure()
            $final = Invoke-LauncherExecution -Record $record -ScriptsRoot $scriptsRoot -RepositoryRoot $repositoryRoot -Publish $publish
            return ConvertTo-LauncherExecutionView -Record $final -Banner $banner
        }.GetNewClosure()
    }
}

function Resolve-LauncherIntent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Request,
        [Parameter(Mandatory)][object]$SourceView,
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.Launcher.Context')][object]$Context
    )

    if ($Request.IntentKind -eq 'Command' -and $Request.TargetId -eq 'command.quit') {
        return [pscustomobject]@{ Kind = 'Exit' }
    }

    if ($Request.IntentKind -eq 'Access') {
        $accessId = [string]$Request.Payload.AccessId
        if ($accessId -eq 'logs') {
            $sequenceProperty = $Request.Payload.PSObject.Properties['Sequence']
            if ($null -eq $sequenceProperty) {
                return [pscustomobject]@{
                    Kind = 'OpenView'
                    View = ConvertTo-LauncherLogsView -Register $Context.Logs -Banner $Context.Banner -OwnerActionMenuId $SourceView.ViewId
                }
            }
            # A run reopened from the Logs sits under the root menu, where the
            # Logs live, whatever folder menu started it.
            $record = Get-LauncherLogsRecord -Register $Context.Logs -Sequence ([int]$sequenceProperty.Value)
            return [pscustomobject]@{
                Kind = 'OpenView'
                View = ConvertTo-LauncherExecutionView -Record $record -Banner $Context.Banner -OwnerActionMenuId $SourceView.OwnerActionMenuId
            }
        }
        $level = Resolve-LauncherDiscoveredAccess -Catalog $Context.Catalog -AccessId $accessId
        if ($null -eq $level) { throw "Unknown Access '$accessId'." }
        return [pscustomobject]@{
            Kind = 'OpenView'
            View = ConvertTo-LauncherDiscoveredView -Level $level -Banner $Context.Banner
        }
    }

    if ($Request.IntentKind -eq 'Action') {
        # An Action with a confirmation sentence opens its Confirmation step
        # first; the step's Danger choice then replaces it with the Execution,
        # so Back after the run returns to the owning menu.
        if ($SourceView.Kind -eq 'ActionMenu') {
            if ($Request.Payload.Confirmation) {
                return [pscustomobject]@{
                    Kind = 'OpenView'
                    View = New-LauncherConfirmationView -Action $Request.Payload -Banner $Context.Banner -OwnerActionMenuId $SourceView.ViewId
                }
            }
            return New-LauncherExecuteDecision -Context $Context -Action $Request.Payload -OwnerActionMenuId $SourceView.ViewId -Mode Open
        }
        if (Test-LauncherConfirmationView -View $SourceView) {
            return New-LauncherExecuteDecision -Context $Context -Action $Request.Payload -OwnerActionMenuId $SourceView.OwnerActionMenuId -Mode Replace
        }
    }

    return [pscustomobject]@{ Kind = 'Stay' }
}
