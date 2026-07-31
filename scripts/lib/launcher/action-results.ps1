# Launcher action orchestration over one retained transcript.

. (Join-Path $PSScriptRoot 'action-log.ps1')

function Get-DeckleActionResultTitle {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][ValidateSet('Running', 'Succeeded', 'Failed', 'Partial', 'Skipped')][string]$State,
        [Parameter(Mandatory)][timespan]$Elapsed
    )

    $seconds = $Elapsed.TotalSeconds.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture)
    return '{0} {1} · {2} s' -f $Label, $State.ToLowerInvariant(), $seconds
}

function Invoke-DeckleMenuAction {
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][object[]]$MenuRows,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    $collector = New-DeckleActionLogCollector -Source $Source
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $refreshInterval = [timespan]::FromMilliseconds(100)
    $lastRefresh = $null
    $succeeded = $true
    $view = $null
    $result = $null
    $state = $null

    $view = New-GridStatusView `
        -Header "$Header · Running…" `
        -HeaderCommands 'Ctrl+C quit' `
        -Rows $MenuRows `
        -Title (Get-DeckleActionResultTitle -Label $Label -State Running -Elapsed $timer.Elapsed) `
        -Lines @() `
        -Follow

    try {
        & $Action *>&1 | ForEach-Object {
            $completedRecords = @(Add-DeckleActionLogItem -Collector $collector -InputObject $_)
            $refreshDue = $null -eq $lastRefresh -or ($timer.Elapsed - $lastRefresh) -ge $refreshInterval
            if ($completedRecords.Count -gt 0 -and $refreshDue) {
                $view = Update-GridStatusView `
                    -View $view `
                    -Title (Get-DeckleActionResultTitle -Label $Label -State Running -Elapsed $timer.Elapsed) `
                    -Lines @(ConvertTo-DeckleActionLogDisplayLines -Records @($collector.Records)) `
                    -Follow
                $lastRefresh = $timer.Elapsed
            }
        }
    } catch {
        $succeeded = $false
        Add-DeckleActionLogItem -Collector $collector -InputObject $_ | Out-Null
    } finally {
        $timer.Stop()
        try {
            Complete-DeckleActionLog -Collector $collector | Out-Null
            if ($collector.Records.Count -eq 0) {
                Add-DeckleActionLogItem `
                    -Collector $collector `
                    -InputObject 'The action produced no console output.' | Out-Null
            }

            $result = if (-not $succeeded) {
                'Failed'
            } elseif ($collector.ReportedResult) {
                $collector.ReportedResult
            } else {
                'Success'
            }
            $state = if ($result -ceq 'Success') { 'Succeeded' } else { $result }
            $view = Update-GridStatusView `
                -View $view `
                -Title (Get-DeckleActionResultTitle -Label $Label -State $state -Elapsed $timer.Elapsed) `
                -Lines @(ConvertTo-DeckleActionLogDisplayLines -Records @($collector.Records)) `
                -Follow
        } finally {
            Close-GridStatusView -View $view
        }
    }

    return [pscustomobject]@{
        Result    = $result
        Succeeded = $result -ceq 'Success'
        Title     = Get-DeckleActionResultTitle -Label $Label -State $state -Elapsed $timer.Elapsed
        Lines     = @(ConvertTo-DeckleActionLogDisplayLines -Records @($collector.Records))
        LogRecords = @($collector.Records)
    }
}
