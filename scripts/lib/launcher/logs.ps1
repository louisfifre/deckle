# The Logs of one launcher session: every Execution since it started, kept
# in memory, listed as a plain View, each run reopening at its Journal.

function New-LauncherLogsRegister {
    return [pscustomobject]@{
        PSTypeName = 'DeckleScripts.Launcher.LogsRegister'
        Records = [System.Collections.Generic.List[object]]::new()
    }
}

function Add-LauncherLogsRecord {
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.Launcher.LogsRegister')][object]$Register,
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.Launcher.ExecutionRecord')][object]$Record
    )

    $Register.Records.Add($Record)
}

function Get-LauncherLogsRecord {
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.Launcher.LogsRegister')][object]$Register,
        [Parameter(Mandatory)][int]$Sequence
    )

    foreach ($record in $Register.Records) {
        if ($record.Sequence -eq $Sequence) { return $record }
    }
    throw "No run with sequence $Sequence in the Logs."
}

function New-LauncherLogsAccessTarget {
    return New-TerminalTarget `
        -TargetId 'access.logs' `
        -Label ('Logs' + [char]0x2026) `
        -IntentKind Access `
        -Payload ([pscustomobject]@{ AccessId = 'logs' })
}

function ConvertTo-LauncherLogsView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.Launcher.LogsRegister')][object]$Register,
        [Parameter(Mandatory)][string]$Banner,
        [Parameter(Mandatory)][string]$OwnerActionMenuId
    )

    $lines = if ($Register.Records.Count -eq 0) {
        @('No run yet in this session.')
    } else {
        @("$($Register.Records.Count) run(s) since the launcher started, newest last.")
    }
    $targets = foreach ($record in $Register.Records) {
        $result = if ($record.Result) { $record.Result } else { $record.State }
        New-TerminalTarget `
            -TargetId "logs.$($record.Sequence)" `
            -Label ("{0:HH:mm:ss}  {1}  [{2}]{3}" -f $record.StartedAt, $record.Context, $result, [char]0x2026) `
            -IntentKind Access `
            -Payload ([pscustomobject]@{ AccessId = 'logs'; Sequence = $record.Sequence })
    }
    return New-TerminalContentView `
        -ViewId 'logs' `
        -Banner $Banner `
        -Context 'Logs' `
        -Lines @($lines) `
        -Targets @($targets) `
        -BackTarget (New-LauncherBackTarget) `
        -OwnerActionMenuId $OwnerActionMenuId
}
